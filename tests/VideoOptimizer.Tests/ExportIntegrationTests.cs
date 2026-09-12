using VideoOptimizer.Core;
using VideoOptimizer.Media;
using Xunit;

namespace VideoOptimizer.Tests;

// End-to-end export tests using the bundled FFmpeg. They encode a small source, export it, and probe the
// result. They fail deliberately when the tool bundle is missing; setup is part of the build contract.
[Collection("ffmpeg-integration")]
public sealed class ExportIntegrationTests : IDisposable
{
    private readonly string root = TestPaths.ToolsRoot();
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "VideoOptimizer.Export", Guid.NewGuid().ToString("N"));
    private readonly MediaProcessRunner runner = new();
    private readonly PresetCatalog catalog;

    public ExportIntegrationTests()
    {
        Directory.CreateDirectory(scratch);
        catalog = PresetCatalog.LoadFromDirectory(TestPaths.PresetsDirectory());
    }

    private VideoExportService Service() =>
        new(new BundledToolLocator(root), runner, new FfprobeParser(), catalog, new EncoderProbe(new BundledToolLocator(root), runner));
    private MediaService Analyzer() =>
        new(new BundledToolLocator(root), runner, new FfprobeParser());

    private async Task<VideoInfo> MakeSourceAsync(string name, string size = "320x180", double seconds = 1.2, bool audio = true)
    {
        var tools = new BundledToolLocator(root).Locate();
        var path = Path.Combine(scratch, name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "lavfi", "-i", $"testsrc2=size={size}:rate=25",
        };
        if (audio) { args.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000"]); }
        args.AddRange(["-t", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-c:v", "libx264", "-pix_fmt", "yuv420p"]);
        if (audio) args.AddRange(["-c:a", "aac"]);
        args.Add(path);
        Assert.Equal(0, await runner.RunProgressAsync(tools.Ffmpeg, args, _ => { }, timeout.Token));
        return await Analyzer().AnalyzeAsync(path, timeout.Token);
    }

    [Theory]
    [InlineData("instagram-story", FramingMode.CropToFill)]
    [InlineData("whatsapp-status", FramingMode.FitBlack)]
    [InlineData("tiktok", FramingMode.FitBlur)]
    public async Task ExportsPlayableMp4WithExpectedGeometryAndLeavesInputUntouched(string presetId, FramingMode framing)
    {
        var source = await MakeSourceAsync($"src {presetId}.mp4");
        var before = await File.ReadAllBytesAsync(source.InputPath);
        var output = Path.Combine(scratch, $"out {presetId}.mp4");
        var request = new VideoExportRequest(source.InputPath, output, null, null, presetId, ExportQuality.Recommended, framing);
        var preset = catalog.Find(presetId)!;
        var expected = ExportPlanner.Plan(source, preset, framing, allowUpscaling: false);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var result = await Service().ExportAsync(request, source, null, timeout.Token);

        Assert.True(File.Exists(result.OutputPath));
        Assert.NotEqual(Path.GetFullPath(source.InputPath), Path.GetFullPath(result.OutputPath));
        Assert.Equal(before, await File.ReadAllBytesAsync(source.InputPath));      // input untouched
        Assert.Equal("h264", result.Output.VideoCodec);
        Assert.Equal("yuv420p", result.Output.PixelFormat);
        Assert.NotNull(result.Output.Audio);
        Assert.Equal("aac", result.Output.Audio!.Codec);
        Assert.Equal((expected.OutputWidth, expected.OutputHeight), (result.Output.DisplayWidth, result.Output.DisplayHeight));
        Assert.InRange(result.Output.Duration.TotalSeconds, 1.0, 1.5);
    }

    [Fact]
    public async Task TrimmedExportHasTrimmedDurationWithinTolerance()
    {
        var source = await MakeSourceAsync("src trim.mp4", seconds: 3.0);
        var output = Path.Combine(scratch, "out trim.mp4");
        var request = new VideoExportRequest(source.InputPath, output, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(2.0),
            "tiktok", ExportQuality.Small, FramingMode.CropToFill);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var result = await Service().ExportAsync(request, source, null, timeout.Token);
        Assert.InRange(result.Output.Duration.TotalSeconds, 1.3, 1.7);              // ~1.5s +/- tolerance
    }

    [Fact]
    public async Task ProgressReachesCompletion()
    {
        var source = await MakeSourceAsync("src progress.mp4");
        var output = Path.Combine(scratch, "out progress.mp4");
        var request = new VideoExportRequest(source.InputPath, output, null, null, "instagram-story", ExportQuality.Small, FramingMode.CropToFill);
        double last = 0;
        var progress = new Progress<ExportProgress>(p => { if (p.Fraction is { } f) last = Math.Max(last, f); });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await Service().ExportAsync(request, source, progress, timeout.Token);
        // Progress is marshaled; allow the last callback to arrive.
        await Task.Delay(100);
        Assert.True(last > 0);
    }

    [Fact]
    public async Task SpecialCharacterPathsExportSuccessfully()
    {
        var source = await MakeSourceAsync("src space සිංහල & # %.mp4");
        var output = Path.Combine(scratch, "out space සිංහල & # %.mp4");
        var request = new VideoExportRequest(source.InputPath, output, null, null, "tiktok", ExportQuality.Small, FramingMode.CropToFill);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var result = await Service().ExportAsync(request, source, null, timeout.Token);
        Assert.True(File.Exists(result.OutputPath));
    }

    [Fact]
    public async Task CollisionAvoidanceNeverOverwritesExistingOutputOrInput()
    {
        var source = await MakeSourceAsync("src collide.mp4");
        var output = Path.Combine(scratch, "out collide.mp4");
        var request = new VideoExportRequest(source.InputPath, output, null, null, "whatsapp-status", ExportQuality.Small, FramingMode.CropToFill);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var first = await Service().ExportAsync(request, source, null, timeout.Token);
        var second = await Service().ExportAsync(request, source, null, timeout.Token);
        Assert.NotEqual(first.OutputPath, second.OutputPath);
        Assert.True(File.Exists(first.OutputPath));
        Assert.True(File.Exists(second.OutputPath));
    }

    [Fact]
    public async Task FfmpegFailureIsRecoverableAndLeavesNoPartialFile()
    {
        var badInput = Path.Combine(scratch, "not-a-video.mp4");
        await File.WriteAllTextAsync(badInput, "this is not a video");
        var source = new VideoInfo(badInput, TimeSpan.FromSeconds(5), 19, 1920, 1080, 0, 30, 30, false,
            "h264", "yuv420p", 8, null, new AudioInfo("aac", 2, 48000, 128000), "bt709", "bt709", "bt709", "tv", ColorKind.Sdr, 0);
        var output = Path.Combine(scratch, "out fail.mp4");
        var request = new VideoExportRequest(badInput, output, null, null, "tiktok", ExportQuality.Small, FramingMode.CropToFill);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await Assert.ThrowsAsync<MediaException>(() => Service().ExportAsync(request, source, null, timeout.Token));
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(scratch, "*.part.mp4"));
    }

    [Fact]
    public async Task CancellationStopsExportAndCleansTemporaryFiles()
    {
        var source = await MakeSourceAsync("src cancel.mp4", size: "1280x720", seconds: 20, audio: true);
        var output = Path.Combine(scratch, "out cancel.mp4");
        var request = new VideoExportRequest(source.InputPath, output, null, null, "instagram-story", ExportQuality.Maximum, FramingMode.FitBlur);
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        var progress = new Progress<ExportProgress>(_ => started.TrySetResult());
        var task = Service().ExportAsync(request, source, progress, cancellation.Token);
        await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(File.Exists(output));
        Assert.Empty(Directory.GetFiles(scratch, "*.part.mp4"));
    }

    [Fact]
    public void ResolveOutputNeverReturnsTheInputPath()
    {
        var input = Path.Combine(scratch, "same.mp4");
        var resolved = VideoExportService.ResolveOutput(input, Path.GetFullPath(input));
        Assert.NotEqual(Path.GetFullPath(input), Path.GetFullPath(resolved));
    }

    public void Dispose()
    {
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }
}
