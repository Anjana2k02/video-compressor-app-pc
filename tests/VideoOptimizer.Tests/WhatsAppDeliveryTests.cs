using VideoOptimizer.Core;
using VideoOptimizer.Media;
using Xunit;

namespace VideoOptimizer.Tests;

// WhatsApp re-compresses uploads that exceed its limits, so the WhatsApp presets cap FPS and bitrate to
// deliver files WhatsApp barely touches. These tests pin that delivery-cap behavior.
public sealed class WhatsAppDeliveryTests
{
    private static readonly PresetCatalog Catalog = PresetCatalog.LoadFromDirectory(TestPaths.PresetsDirectory());
    private static readonly ExportPreset WhatsApp = Catalog.Find("whatsapp-status")!;

    private static VideoInfo Source(double fps = 60, long bitrate = 12_000_000, int width = 1080, int height = 1920) =>
        new(@"C:\videos\phone.mp4", TimeSpan.FromSeconds(10), 20_000_000, width, height, 0, fps, fps, false,
            "h264", "yuv420p", 8, bitrate, new AudioInfo("aac", 2, 48000, 128000),
            "bt709", "bt709", "bt709", "tv", ColorKind.Sdr, 0);

    private static IReadOnlyList<string> Build(VideoInfo source, ExportPreset preset)
    {
        var request = new VideoExportRequest(source.InputPath, @"D:\out\o.mp4", null, null, preset.Id, ExportQuality.Maximum, FramingMode.CropToFill);
        var geometry = ExportPlanner.Plan(source, preset, FramingMode.CropToFill, allowUpscaling: false);
        var decision = new StrategyDecision(ExportStrategy.Reencode, EncoderCapabilities.Cpu, "", true, false, "", []);
        return FfmpegCommandBuilder.Build(request, source, preset, geometry, decision, @"D:\out\o.mp4");
    }

    [Fact]
    public void WhatsAppPresetsCarryDeliveryCaps()
    {
        Assert.Equal(30, WhatsApp.Video.MaxFps);
        Assert.Equal(2_000_000, WhatsApp.Video.MaxBitrate);
        var hd = Catalog.Find("whatsapp-status-hd")!;
        Assert.Equal(30, hd.Video.MaxFps);
        Assert.Equal(4_000_000, hd.Video.MaxBitrate);
        // Other destinations remain uncapped.
        Assert.Null(Catalog.Find("instagram-story")!.Video.MaxFps);
        Assert.Null(Catalog.Find("tiktok")!.Video.MaxBitrate);
    }

    [Fact]
    public void SixtyFpsSourceGetsFpsFilterAndBitrateCeiling()
    {
        var list = Build(Source(fps: 60), WhatsApp).ToList();
        var filter = list[list.IndexOf("-filter_complex") + 1];
        Assert.Contains(",fps=30", filter);
        Assert.Equal("2000000", list[list.IndexOf("-maxrate") + 1]);
        Assert.Equal("4000000", list[list.IndexOf("-bufsize") + 1]);
    }

    [Fact]
    public void ThirtyFpsSourceIsNotResampled()
    {
        var list = Build(Source(fps: 30), WhatsApp).ToList();
        var filter = list[list.IndexOf("-filter_complex") + 1];
        Assert.DoesNotContain("fps=", filter);
        Assert.Contains("-maxrate", list); // ceiling still applies
    }

    [Fact]
    public void UncappedPresetAddsNeitherFpsFilterNorCeiling()
    {
        var instagram = Catalog.Find("instagram-story")!;
        var list = Build(Source(fps: 60), instagram).ToList();
        Assert.DoesNotContain("-maxrate", list);
        Assert.DoesNotContain("fps=", list[list.IndexOf("-filter_complex") + 1]);
    }

    [Fact]
    public void HighFpsOrHighBitrateSourceCannotStreamCopyToWhatsApp()
    {
        // 720x1280 passthrough geometry, everything compatible except the caps.
        var matching = Source(fps: 60, bitrate: 12_000_000, width: 720, height: 1280);
        var geometry = ExportPlanner.Plan(matching, WhatsApp, FramingMode.CropToFill, false);
        Assert.True(geometry.IsPassthrough);

        var report = CompatibilityChecker.Check(matching, WhatsApp, geometry);
        Assert.False(report.FpsOk);
        Assert.False(report.BitrateOk);
        Assert.False(report.CanStreamCopy);
        Assert.Contains(report.Notes, n => n.Contains("30"));
        Assert.Contains(report.Notes, n => n.Contains("itrate"));

        // Within the caps, stream copy is allowed again.
        var compliant = Source(fps: 30, bitrate: 1_800_000, width: 720, height: 1280);
        Assert.True(CompatibilityChecker.Check(compliant, WhatsApp,
            ExportPlanner.Plan(compliant, WhatsApp, FramingMode.CropToFill, false)).CanStreamCopy);
    }

    [Fact]
    public void StrategyExplanationNamesTheFpsReduction()
    {
        var source = Source(fps: 60, width: 720, height: 1280);
        var geometry = ExportPlanner.Plan(source, WhatsApp, FramingMode.CropToFill, false);
        var request = new VideoExportRequest(source.InputPath, "o.mp4", null, null, WhatsApp.Id, ExportQuality.Maximum, FramingMode.CropToFill);
        var decision = ExportStrategyEngine.Decide(source, WhatsApp, request, geometry, EncoderCapabilities.CpuOnly);
        Assert.Equal(ExportStrategy.Reencode, decision.Strategy);
        Assert.Contains("30", decision.Explanation);
    }

    [Theory]
    [InlineData("\"maxFps\": 0")]
    [InlineData("\"maxFps\": -5")]
    [InlineData("\"maxBitrate\": 0")]
    public void InvalidCapsAreRejected(string bad)
    {
        var json = "{\"schemaVersion\":1,\"id\":\"x\",\"name\":\"X\",\"video\":{\"preferredWidth\":720,\"preferredHeight\":1280,"
            + "\"aspectRatio\":\"9:16\",\"codec\":\"h264\",\"pixelFormat\":\"yuv420p\"," + bad + "},"
            + "\"audio\":{\"codec\":\"aac\",\"sampleRate\":48000,\"bitrate\":128000},"
            + "\"quality\":{\"smallCrf\":23,\"recommendedCrf\":20,\"maximumCrf\":17}}";
        Assert.Throws<PresetValidationException>(() => PresetCatalog.Parse(json));
    }

    [Fact]
    public void EstimateHonorsFpsAndBitrateCaps()
    {
        var source = Source(fps: 60);
        var geometry = ExportPlanner.Plan(source, WhatsApp, FramingMode.CropToFill, false);
        var capped = ExportEstimator.EstimateBytes(geometry, 60, 10, WhatsApp, ExportQuality.Maximum);
        // 10 s at a 2 Mbps video ceiling + 128 kbps audio ≈ 2.66 MB upper bound.
        Assert.True(capped <= (long)((2_000_000 + 128_000) * 10 / 8.0 * 1.01), $"estimate {capped} exceeds the capped envelope");
    }
}
