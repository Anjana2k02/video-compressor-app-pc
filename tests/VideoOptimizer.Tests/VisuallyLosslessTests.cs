using VideoOptimizer.Core;
using VideoOptimizer.Media;
using Xunit;

namespace VideoOptimizer.Tests;

public sealed class SampleWindowsTests
{
    [Fact]
    public void LongVideoProducesFiveWindowsWithinBounds()
    {
        var windows = SampleWindows.For(TimeSpan.FromSeconds(100), sampleSeconds: 2);
        Assert.Equal(5, windows.Count);
        Assert.All(windows, w =>
        {
            Assert.True(w.Start >= TimeSpan.Zero);
            Assert.True(w.Start + w.Length <= TimeSpan.FromSeconds(100) + TimeSpan.FromMilliseconds(1));
            Assert.Equal(2, w.Length.TotalSeconds, 3);
        });
    }

    [Fact]
    public void ShortVideoCollapsesToFewerNonDuplicateWindows()
    {
        var windows = SampleWindows.For(TimeSpan.FromSeconds(1), sampleSeconds: 2);
        Assert.True(windows.Count >= 1 && windows.Count < 5);
        Assert.All(windows, w => Assert.True(w.Length.TotalSeconds <= 1.001));
    }

    [Fact]
    public void ZeroDurationHasNoWindows() => Assert.Empty(SampleWindows.For(TimeSpan.Zero));
}

public sealed class QualitySearchTests
{
    // Synthetic monotonic model: VMAF falls by 1 point per CRF step above 16.
    private static Task<double> Model(int crf) => Task.FromResult(100.0 - (crf - 16));

    [Fact]
    public async Task FindsLargestCrfMeetingTarget()
    {
        var result = await QualitySearch.SearchAsync(target: 96, minCrf: 16, maxCrf: 34, maxTrials: 8, Model);
        Assert.True(result.Converged);
        Assert.Equal(20, result.Crf);          // 100-(20-16)=96 meets; 21 -> 95 fails
        Assert.True(result.Vmaf >= 96);
    }

    [Fact]
    public async Task UnreachableTargetReturnsBestEffortAtHighestQuality()
    {
        var result = await QualitySearch.SearchAsync(target: 101, minCrf: 16, maxCrf: 34, maxTrials: 8, Model);
        Assert.False(result.Converged);
        Assert.Equal(16, result.Crf);
    }

    [Fact]
    public async Task RespectsTrialBudget()
    {
        int calls = 0;
        var result = await QualitySearch.SearchAsync(96, 16, 34, maxTrials: 3, crf => { calls++; return Model(crf); });
        Assert.True(calls <= 3, $"expected <= 3 measurements, got {calls}");
        Assert.True(result.Trials <= 3);
    }
}

public sealed class VmafAggregateTests
{
    [Fact]
    public void ConservativeUsesTheWorstSample()
    {
        var scores = new[] { new VmafScore(98, 95), new VmafScore(91, 88), new VmafScore(96, 90) };
        Assert.Equal(91, VmafAggregate.Conservative(scores));
    }

    [Fact]
    public void EmptyIsZero() => Assert.Equal(0, VmafAggregate.Conservative([]));
}

public sealed class AnalysisCacheTests
{
    private static AnalysisCacheKey Key(int target = 96, FramingMode framing = FramingMode.CropToFill) =>
        new("fp", "instagram-story", framing, target, -1, -1, 0.5, 0.5);

    [Fact]
    public void StoresAndRetrievesByKey()
    {
        var cache = new MemoryAnalysisCache();
        var outcome = new OptimizationOutcome(22, 96.5, true, true, []);
        cache.Set(Key(), outcome);
        Assert.True(cache.TryGet(Key(), out var got));
        Assert.Equal(22, got.Crf);
    }

    [Fact]
    public void DifferentSettingsInvalidateTheEntry()
    {
        var cache = new MemoryAnalysisCache();
        cache.Set(Key(target: 96), new OptimizationOutcome(22, 96.5, true, true, []));
        Assert.False(cache.TryGet(Key(target: 90), out _));               // different target
        Assert.False(cache.TryGet(Key(framing: FramingMode.FitBlur), out _)); // different framing
    }
}

public sealed class VmafParseTests
{
    [Fact]
    public void ParsesPooledMeanAndMin()
    {
        const string json = """
            { "pooled_metrics": { "vmaf": { "min": 92.8, "max": 95.5, "mean": 94.2, "harmonic_mean": 94.1 } } }
            """;
        var score = VmafMeasurer.Parse(json);
        Assert.Equal(94.2, score.Mean, 3);
        Assert.Equal(92.8, score.Min, 3);
    }
}

public sealed class OptimizerFallbackTests
{
    private static readonly ExportPreset Instagram = PresetCatalog.LoadFromDirectory(TestPaths.PresetsDirectory()).Find("instagram-story")!;

    private sealed class UnavailableVmaf : IVmafProbe
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    [Fact]
    public async Task WhenVmafUnavailableTheOptimizerFallsBackToThePresetQuality()
    {
        var source = new VideoInfo("clip.mp4", TimeSpan.FromSeconds(10), 10_000_000, 1080, 1920, 0, 30, 30, false,
            "h264", "yuv420p", 8, 5_000_000, new AudioInfo("aac", 2, 48000, 128000), "bt709", "bt709", "bt709", "tv", ColorKind.Sdr, 0);
        var geometry = ExportPlanner.Plan(source, Instagram, FramingMode.CropToFill, false);
        var request = new VideoExportRequest(source.InputPath, "out.mp4", null, null, Instagram.Id, ExportQuality.Recommended,
            FramingMode.CropToFill, TargetVmaf: 96);

        var optimizer = new VisuallyLosslessOptimizer(
            new BundledToolLocator(TestPaths.ToolsRoot()), new MediaProcessRunner(),
            new UnavailableVmaf(), new VmafMeasurer(new MediaProcessRunner()), new MemoryAnalysisCache());

        var outcome = await optimizer.OptimizeAsync(request, source, Instagram, geometry);
        Assert.False(outcome.Measured);
        Assert.Equal(Instagram.CrfFor(ExportQuality.Recommended), outcome.Crf);
        Assert.NotEmpty(outcome.Warnings);
    }
}
