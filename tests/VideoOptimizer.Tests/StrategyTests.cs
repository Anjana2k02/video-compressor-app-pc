using VideoOptimizer.Core;
using VideoOptimizer.Media;
using Xunit;

namespace VideoOptimizer.Tests;

public sealed class StrategyTests
{
    private static readonly ExportPreset Instagram = PresetCatalog.LoadFromDirectory(TestPaths.PresetsDirectory()).Find("instagram-story")!;

    // A source that already matches Instagram Story (1080x1920 H.264 SDR yuv420p + AAC): stream-copy eligible.
    private static VideoInfo Compatible(string codec = "h264", string pix = "yuv420p", int? depth = 8,
        ColorKind color = ColorKind.Sdr, AudioInfo? audio = null) =>
        new("clip.mp4", TimeSpan.FromSeconds(10), 10_000_000, 1080, 1920, 0, 30, 30, false,
            codec, pix, depth, 5_000_000, audio ?? new AudioInfo("aac", 2, 48000, 128000),
            "bt709", "bt709", "bt709", "tv", color, 0);

    private static (ExportGeometry geo, VideoExportRequest req) Setup(VideoInfo source, FramingMode framing = FramingMode.CropToFill,
        TimeSpan? start = null, TimeSpan? end = null, PerformanceMode perf = PerformanceMode.Automatic)
    {
        var geo = ExportPlanner.Plan(source, Instagram, framing, allowUpscaling: false);
        var req = new VideoExportRequest(source.InputPath, "out.mp4", start, end, Instagram.Id, ExportQuality.Recommended, framing,
            PreserveSourceFps: true, AllowUpscaling: false, CropAnchor: null, Performance: perf);
        return (geo, req);
    }

    private static readonly EncoderCapabilities WithNvenc = new([new VideoEncoder("h264_nvenc", EncoderKind.Nvenc, "NVIDIA NVENC")]);

    [Fact]
    public void CompatibleNoTrimIsStreamCopy()
    {
        var source = Compatible();
        var (geo, req) = Setup(source);
        Assert.True(geo.IsPassthrough);
        var d = ExportStrategyEngine.Decide(source, Instagram, req, geo, EncoderCapabilities.CpuOnly);
        Assert.Equal(ExportStrategy.StreamCopy, d.Strategy);
        Assert.True(d.FrameAccurate);
        Assert.Contains("Stream copy", d.Explanation);
    }

    [Fact]
    public void CompatibleWithTrimIsSmartTrimUnderFastModes()
    {
        var source = Compatible();
        var (geo, req) = Setup(source, start: TimeSpan.FromSeconds(1), end: TimeSpan.FromSeconds(3), perf: PerformanceMode.Fast);
        var d = ExportStrategyEngine.Decide(source, Instagram, req, geo, EncoderCapabilities.CpuOnly);
        Assert.Equal(ExportStrategy.SmartTrim, d.Strategy);
        Assert.False(d.FrameAccurate);
        Assert.Contains(d.Warnings, w => w.Contains("keyframe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompatibleWithTrimReencodesForFrameAccuracyUnderCompressionModes()
    {
        var source = Compatible();
        var (geo, req) = Setup(source, start: TimeSpan.FromSeconds(1), end: TimeSpan.FromSeconds(3), perf: PerformanceMode.MaximumCompression);
        var d = ExportStrategyEngine.Decide(source, Instagram, req, geo, EncoderCapabilities.CpuOnly);
        Assert.Equal(ExportStrategy.Reencode, d.Strategy);
        Assert.True(d.FrameAccurate);
    }

    [Theory]
    [InlineData("hevc", "yuv420p", 8, ColorKind.Sdr)]      // codec
    [InlineData("h264", "yuv420p10le", 10, ColorKind.Hdr)] // HDR + 10-bit
    public void IncompatibleSourcesReencode(string codec, string pix, int depth, ColorKind color)
    {
        var source = Compatible(codec, pix, depth, color);
        var (geo, req) = Setup(source);
        var d = ExportStrategyEngine.Decide(source, Instagram, req, geo, EncoderCapabilities.CpuOnly);
        Assert.Equal(ExportStrategy.Reencode, d.Strategy);
    }

    [Fact]
    public void HdrSourceConvertsToSdrWithNotice()
    {
        var source = Compatible("h264", "yuv420p10le", 10, ColorKind.Hdr);
        var (geo, req) = Setup(source);
        var d = ExportStrategyEngine.Decide(source, Instagram, req, geo, EncoderCapabilities.CpuOnly);
        Assert.True(d.ConvertsHdrToSdr);
        Assert.Contains(d.Warnings, w => w.Contains("HDR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NonAacAudioCannotStreamCopy()
    {
        var source = Compatible(audio: new AudioInfo("opus", 2, 48000, 128000));
        var (geo, req) = Setup(source);
        var report = CompatibilityChecker.Check(source, Instagram, geo);
        Assert.False(report.AudioOk);
        Assert.False(report.CanStreamCopy);
    }

    [Fact]
    public void FramingThatScalesForcesReencode()
    {
        // Landscape source into a portrait preset requires cropping/scaling -> not passthrough.
        var landscape = Compatible() with { Width = 1920, Height = 1080 };
        var (geo, req) = Setup(landscape);
        Assert.False(geo.IsPassthrough);
        var d = ExportStrategyEngine.Decide(landscape, Instagram, req, geo, EncoderCapabilities.CpuOnly);
        Assert.Equal(ExportStrategy.Reencode, d.Strategy);
    }

    [Theory]
    [InlineData(PerformanceMode.Automatic, EncoderKind.Nvenc)]
    [InlineData(PerformanceMode.Fast, EncoderKind.Nvenc)]
    [InlineData(PerformanceMode.Balanced, EncoderKind.Cpu)]
    [InlineData(PerformanceMode.MaximumCompression, EncoderKind.Cpu)]
    public void EncoderSelectionHonorsPerformanceAndHardware(PerformanceMode mode, EncoderKind expected)
    {
        var (encoder, _) = ExportStrategyEngine.ChooseEncoder(WithNvenc, mode);
        Assert.Equal(expected, encoder.Kind);
    }

    [Fact]
    public void WithoutHardwareEverythingFallsBackToCpu()
    {
        foreach (var mode in Enum.GetValues<PerformanceMode>())
        {
            var (encoder, _) = ExportStrategyEngine.ChooseEncoder(EncoderCapabilities.CpuOnly, mode);
            Assert.Equal(EncoderKind.Cpu, encoder.Kind);
        }
    }
}

public sealed class EncoderProbeTests
{
    private const string SampleEncoders = """
         V....D libx264              libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (codec h264)
         V....D h264_amf             AMD AMF H.264 Encoder (codec h264)
         V....D h264_nvenc           NVIDIA NVENC H.264 encoder (codec h264)
         V..... h264_qsv             H.264 / AVC (Intel Quick Sync Video acceleration) (codec h264)
        """;

    [Fact]
    public void ParsesAllHardwareEncodersPlusCpuBaseline()
    {
        var caps = EncoderProbe.Parse(SampleEncoders);
        Assert.True(caps.Has(EncoderKind.Nvenc));
        Assert.True(caps.Has(EncoderKind.Qsv));
        Assert.True(caps.Has(EncoderKind.Amf));
        Assert.True(caps.Has(EncoderKind.Cpu));
        Assert.Equal(EncoderKind.Nvenc, caps.PreferredHardware!.Kind); // NVENC preferred first
    }

    [Fact]
    public void EmptyOutputYieldsCpuOnly()
    {
        var caps = EncoderProbe.Parse("");
        Assert.False(caps.HasHardware);
        Assert.True(caps.Has(EncoderKind.Cpu));
        Assert.Single(caps.Available);
    }
}
