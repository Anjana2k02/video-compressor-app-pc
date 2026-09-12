using VideoOptimizer.Core;
using VideoOptimizer.Media;
using Xunit;

namespace VideoOptimizer.Tests;

public sealed class StrategyCommandTests
{
    private static readonly ExportPreset Instagram = PresetCatalog.LoadFromDirectory(TestPaths.PresetsDirectory()).Find("instagram-story")!;

    private static VideoInfo Source(bool audio = true) =>
        new(@"C:\videos\clip.mp4", TimeSpan.FromSeconds(12), 20_000_000, 1080, 1920, 0, 30, 30, false,
            "h264", "yuv420p", 8, 6_000_000, audio ? new AudioInfo("aac", 2, 48000, 128000) : null,
            "bt709", "bt709", "bt709", "tv", ColorKind.Sdr, 0);

    private static IReadOnlyList<string> Build(StrategyDecision decision, VideoInfo source, TimeSpan? start = null, TimeSpan? end = null)
    {
        var request = new VideoExportRequest(source.InputPath, @"D:\out\clip.tmp.mp4", start, end, Instagram.Id, ExportQuality.Recommended, FramingMode.CropToFill);
        var geometry = ExportPlanner.Plan(source, Instagram, FramingMode.CropToFill, allowUpscaling: false);
        return FfmpegCommandBuilder.Build(request, source, Instagram, geometry, decision, @"D:\out\clip.tmp.mp4");
    }

    [Fact]
    public void StreamCopyRemuxesWithoutFiltersOrReencode()
    {
        var d = new StrategyDecision(ExportStrategy.StreamCopy, null, "", true, false, "", []);
        var args = Build(d, Source());
        Assert.Contains("copy", args);
        Assert.DoesNotContain("-filter_complex", args);
        Assert.DoesNotContain("libx264", args);
        Assert.Contains("+faststart", args);
        Assert.Contains("0:v:0", args);
    }

    [Fact]
    public void SmartTrimCopiesWithKeyframeSeekAndTimestampFix()
    {
        var d = new StrategyDecision(ExportStrategy.SmartTrim, null, "", false, false, "", []);
        var list = Build(d, Source(), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)).ToList();
        Assert.Contains("copy", list);
        Assert.True(list.IndexOf("-ss") < list.IndexOf("-i"));
        Assert.Contains("-avoid_negative_ts", list);
        Assert.Equal("2", list[list.IndexOf("-t") + 1]); // 3 - 1 = 2 seconds
    }

    [Fact]
    public void HardwareReencodeUsesNvencQualityArguments()
    {
        var encoder = new VideoEncoder("h264_nvenc", EncoderKind.Nvenc, "NVIDIA NVENC");
        var d = new StrategyDecision(ExportStrategy.Reencode, encoder, "", true, false, "", []);
        var args = Build(d, Source());
        Assert.Contains("h264_nvenc", args);
        Assert.Contains("-cq", args);
        Assert.DoesNotContain("libx264", args);
    }

    [Fact]
    public void HdrReencodeInsertsToneMapFilter()
    {
        var d = new StrategyDecision(ExportStrategy.Reencode, EncoderCapabilities.Cpu, "", true, true, "", []);
        var args = Build(d, Source());
        var filter = args[args.ToList().IndexOf("-filter_complex") + 1];
        Assert.Contains("tonemap", filter);
        Assert.Contains("zscale", filter);
        Assert.EndsWith("[v]", filter);
    }

    [Fact]
    public void CpuReencodeHonorsExplicitSpeedPreset()
    {
        var d = new StrategyDecision(ExportStrategy.Reencode, EncoderCapabilities.Cpu, "slow", true, false, "", []);
        var args = Build(d, Source()).ToList();
        Assert.Equal("slow", args[args.IndexOf("-preset") + 1]);
        Assert.Contains("libx264", args);
    }
}
