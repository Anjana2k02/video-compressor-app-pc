using VideoOptimizer.Core;
using VideoOptimizer.Media;
using Xunit;

namespace VideoOptimizer.Tests;

// Real measured "visually lossless" pipeline using the bundled FFmpeg + libvmaf: sample encodes, VMAF
// measurement, CRF search, then one full encode. Serialized with the other FFmpeg tests.
[Collection("ffmpeg-integration")]
public sealed class OptimizerIntegrationTests : IDisposable
{
    private readonly string root = TestPaths.ToolsRoot();
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "VideoOptimizer.Opt", Guid.NewGuid().ToString("N"));
    private readonly MediaProcessRunner runner = new();
    private readonly PresetCatalog catalog;

    public OptimizerIntegrationTests()
    {
        Directory.CreateDirectory(scratch);
        catalog = PresetCatalog.LoadFromDirectory(TestPaths.PresetsDirectory());
    }

    private VideoExportService Service()
    {
        var locator = new BundledToolLocator(root);
        var vmaf = new VmafProbe(locator, runner);
        var optimizer = new VisuallyLosslessOptimizer(locator, runner, vmaf, new VmafMeasurer(runner), new MemoryAnalysisCache());
        return new VideoExportService(locator, runner, new FfprobeParser(), catalog, new EncoderProbe(locator, runner), optimizer);
    }

    [Fact]
    public async Task MeasuredExportChoosesACrfAndReportsAchievedVmaf()
    {
        var tools = new BundledToolLocator(root).Locate();
        var input = Path.Combine(scratch, "opt src.mp4");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(240));
        Assert.Equal(0, await runner.RunProgressAsync(tools.Ffmpeg,
            ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc2=size=360x640:rate=25",
             "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "3", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", input],
            _ => { }, timeout.Token));

        var source = await new MediaService(new BundledToolLocator(root), runner, new FfprobeParser()).AnalyzeAsync(input, timeout.Token);
        var request = new VideoExportRequest(input, Path.Combine(scratch, "opt out.mp4"), null, null,
            "instagram-story", ExportQuality.Recommended, FramingMode.CropToFill,
            PreserveSourceFps: true, AllowUpscaling: false, CropAnchor: null, Performance: PerformanceMode.Balanced, TargetVmaf: 96);

        var result = await Service().ExportAsync(request, source, null, timeout.Token);

        Assert.True(File.Exists(result.OutputPath));
        Assert.Equal("h264", result.Output.VideoCodec);
        Assert.NotNull(result.AchievedVmaf);
        Assert.True(result.AchievedVmaf >= 85, $"expected a high VMAF, got {result.AchievedVmaf}");
        Assert.Contains("isually lossless", result.StrategyExplanation);
        Assert.InRange(result.Output.Duration.TotalSeconds, 2.7, 3.3); // full clip encoded
    }

    public void Dispose()
    {
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }
}
