using VideoOptimizer.Core;
using VideoOptimizer.Media;
using Xunit;

namespace VideoOptimizer.Tests;

// Phase 3 end-to-end: strategy selection, HDR tone mapping, silent handling, and hardware fallback, all
// using the bundled FFmpeg. Serialized with the other FFmpeg tests via the shared collection.
[Collection("ffmpeg-integration")]
public sealed class ExportStrategyIntegrationTests : IDisposable
{
    private readonly string root = TestPaths.ToolsRoot();
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "VideoOptimizer.Strategy", Guid.NewGuid().ToString("N"));
    private readonly MediaProcessRunner runner = new();
    private readonly PresetCatalog catalog;

    public ExportStrategyIntegrationTests()
    {
        Directory.CreateDirectory(scratch);
        catalog = PresetCatalog.LoadFromDirectory(TestPaths.PresetsDirectory());
    }

    private VideoExportService Service() =>
        new(new BundledToolLocator(root), runner, new FfprobeParser(), catalog, new EncoderProbe(new BundledToolLocator(root), runner));
    private MediaService Analyzer() => new(new BundledToolLocator(root), runner, new FfprobeParser());

    private async Task<VideoInfo> EncodeAsync(string name, IEnumerable<string> extra, string size, double seconds, bool audio)
    {
        var tools = new BundledToolLocator(root).Locate();
        var path = Path.Combine(scratch, name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-f", "lavfi", "-i", $"testsrc2=size={size}:rate=25",
        };
        if (audio) args.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000"]);
        args.AddRange(["-t", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "-c:v", "libx264"]);
        args.AddRange(extra);
        if (audio) args.AddRange(["-c:a", "aac"]);
        args.Add(path);
        Assert.Equal(0, await runner.RunProgressAsync(tools.Ffmpeg, args, _ => { }, timeout.Token));
        return await Analyzer().AnalyzeAsync(path, timeout.Token);
    }

    private VideoExportRequest Request(VideoInfo source, string preset, FramingMode framing, PerformanceMode perf,
        TimeSpan? start = null, TimeSpan? end = null) =>
        new(source.InputPath, Path.Combine(scratch, $"out {Guid.NewGuid():N}.mp4"), start, end, preset,
            ExportQuality.Recommended, framing, PreserveSourceFps: true, AllowUpscaling: false, CropAnchor: null, Performance: perf);

    [Fact]
    public async Task PortraitSourceMatchingPresetStreamCopiesWithoutReencoding()
    {
        var source = await EncodeAsync("copy src.mp4", ["-pix_fmt", "yuv420p"], "1080x1920", 1.5, audio: true);
        var request = Request(source, "instagram-story", FramingMode.CropToFill, PerformanceMode.Automatic);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var result = await Service().ExportAsync(request, source, null, timeout.Token);
        Assert.Contains("Stream copy", result.StrategyExplanation);
        Assert.Equal("h264", result.Output.VideoCodec);
        Assert.Equal((1080, 1920), (result.Output.DisplayWidth, result.Output.DisplayHeight));
    }

    [Fact]
    public async Task HdrSourceIsToneMappedToSdrBt709()
    {
        var source = await EncodeAsync("hdr src.mp4",
            ["-pix_fmt", "yuv420p10le", "-profile:v", "high10", "-color_primaries", "bt2020", "-color_trc", "smpte2084", "-colorspace", "bt2020nc",
             "-x264-params", "colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc"],
            "320x568", 1.2, audio: true);
        Assert.Equal(ColorKind.Hdr, source.Color); // sanity: our probe flagged it HDR

        var request = Request(source, "instagram-story", FramingMode.FitBlack, PerformanceMode.Balanced);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var result = await Service().ExportAsync(request, source, null, timeout.Token);

        Assert.NotEqual(ColorKind.Hdr, result.Output.Color);
        Assert.Equal("yuv420p", result.Output.PixelFormat);
        Assert.Equal(8, result.Output.BitDepth);
        Assert.True(result.ConvertsHdrNoticePresent(), "expected an HDR conversion notice");
    }

    [Fact]
    public async Task SilentSourceReencodesWithoutAudio()
    {
        // Landscape -> portrait forces a re-encode; source has no audio, so output must have none.
        var source = await EncodeAsync("silent src.mp4", ["-pix_fmt", "yuv420p"], "640x360", 1.2, audio: false);
        Assert.Null(source.Audio);
        var request = Request(source, "tiktok", FramingMode.CropToFill, PerformanceMode.Balanced);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var result = await Service().ExportAsync(request, source, null, timeout.Token);
        Assert.Null(result.Output.Audio);
        Assert.Equal("h264", result.Output.VideoCodec);
    }

    [Fact]
    public async Task HardwarePreferredModeProducesPlayableOutputEvenWhenGpuIsAbsent()
    {
        // Fast prefers a hardware encoder. If the GPU is missing the service falls back to libx264;
        // either way a valid H.264 file must result.
        var source = await EncodeAsync("hw src.mp4", ["-pix_fmt", "yuv420p"], "1280x720", 1.2, audio: true);
        var request = Request(source, "instagram-story", FramingMode.CropToFill, PerformanceMode.Fast);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var result = await Service().ExportAsync(request, source, null, timeout.Token);
        Assert.True(File.Exists(result.OutputPath));
        Assert.Equal("h264", result.Output.VideoCodec);
        Assert.Empty(Directory.GetFiles(scratch, "*.part.mp4"));
    }

    public void Dispose()
    {
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }
}

internal static class ExportResultTestExtensions
{
    public static bool ConvertsHdrNoticePresent(this ExportResult result) =>
        result.AllNotices.Any(n => n.Contains("HDR", StringComparison.OrdinalIgnoreCase));
}
