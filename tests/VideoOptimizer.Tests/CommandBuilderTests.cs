using VideoOptimizer.Core;
using VideoOptimizer.Media;
using Xunit;

namespace VideoOptimizer.Tests;

public sealed class CommandBuilderTests
{
    private static readonly PresetCatalog Catalog = PresetCatalog.LoadFromDirectory(TestPaths.PresetsDirectory());

    private static VideoInfo Source(int width, int height, bool audio = true, string path = @"C:\videos\clip.mp4") =>
        new(path, TimeSpan.FromSeconds(12), 20_000_000, width, height, 0, 30, 30, false,
            "h264", "yuv420p", 8, 6_000_000,
            audio ? new AudioInfo("aac", 2, 48000, 128000) : null,
            "bt709", "bt709", "bt709", "tv", ColorKind.Sdr, 0);

    private static IReadOnlyList<string> Build(string presetId, ExportQuality quality, FramingMode framing,
        VideoInfo source, TimeSpan? start = null, TimeSpan? end = null, string output = @"D:\out\clip.tmp.mp4")
    {
        var preset = Catalog.Find(presetId)!;
        var request = new VideoExportRequest(source.InputPath, output, start, end, presetId, quality, framing);
        var geometry = ExportPlanner.Plan(source, preset, framing, allowUpscaling: false);
        var decision = new StrategyDecision(ExportStrategy.Reencode, EncoderCapabilities.Cpu, "", true, false, "", []);
        return FfmpegCommandBuilder.Build(request, source, preset, geometry, decision, output);
    }

    [Fact]
    public void GoldenInstagramRecommendedCropLandscape()
    {
        var args = Build("instagram-story", ExportQuality.Recommended, FramingMode.CropToFill, Source(1920, 1080));
        Assert.Equal(new[]
        {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-i", @"C:\videos\clip.mp4",
            "-filter_complex", "[0:v:0]crop=608:1080:656:0,scale=608:1080:flags=lanczos,setsar=1[v]",
            "-map", "[v]",
            "-map", "0:a:0",
            "-c:v", "libx264",
            "-preset", "medium",
            "-crf", "20",
            "-pix_fmt", "yuv420p",
            "-color_primaries", "bt709",
            "-color_trc", "bt709",
            "-colorspace", "bt709",
            "-c:a", "aac",
            "-b:a", "160000",
            "-ar", "48000",
            "-ac", "2",
            "-movflags", "+faststart",
            "-progress", "pipe:1",
            "-nostats",
            @"D:\out\clip.tmp.mp4",
        }, args);
    }

    [Fact]
    public void GoldenWhatsAppSmallFitBlackPortrait()
    {
        var args = Build("whatsapp-status", ExportQuality.Small, FramingMode.FitBlack, Source(1080, 1920));
        Assert.Contains("[0:v:0]scale=720:1280:flags=lanczos,pad=720:1280:0:0:color=black,setsar=1[v]", args);
        Assert.Equal("23", ValueAfter(args, "-crf"));
        Assert.Equal("128000", ValueAfter(args, "-b:a"));
    }

    [Fact]
    public void GoldenTikTokMaximumBlur()
    {
        var args = Build("tiktok", ExportQuality.Maximum, FramingMode.FitBlur, Source(1280, 720));
        Assert.Contains(args, a => a.StartsWith("[0:v:0]split=2[bg][fg];") && a.Contains("gblur=sigma=20") && a.EndsWith("[v]"));
        Assert.Equal("17", ValueAfter(args, "-crf"));
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void EveryCombinationProducesAWellFormedCommand(string presetId, ExportQuality quality, FramingMode framing)
    {
        var preset = Catalog.Find(presetId)!;
        var args = Build(presetId, quality, framing, Source(1920, 1080));

        Assert.Equal(@"D:\out\clip.tmp.mp4", args[^1]);           // output is last
        Assert.Contains("libx264", args);
        Assert.Equal("yuv420p", ValueAfter(args, "-pix_fmt"));
        Assert.Equal(preset.CrfFor(quality).ToString(), ValueAfter(args, "-crf"));
        Assert.Equal(preset.Audio.Bitrate.ToString(), ValueAfter(args, "-b:a"));
        Assert.Equal("48000", ValueAfter(args, "-ar"));
        Assert.Equal("+faststart", ValueAfter(args, "-movflags"));
        Assert.Equal("pipe:1", ValueAfter(args, "-progress"));
        Assert.Equal("[v]", ValueAfter(args, "-map"));
        Assert.Single(args, a => a.StartsWith("[0:v:0]") && a.EndsWith("[v]"));   // exactly one filter graph
        Assert.DoesNotContain(args, a => a.Contains("&&") || a.Contains(" | "));  // never a shell string
    }

    public static IEnumerable<object[]> AllCombinations()
    {
        foreach (var preset in new[] { "instagram-story", "tiktok", "whatsapp-status", "whatsapp-status-hd" })
            foreach (var quality in Enum.GetValues<ExportQuality>())
                foreach (var framing in Enum.GetValues<FramingMode>())
                    yield return [preset, quality, framing];
    }

    [Fact]
    public void SpecialCharacterPathsArePassedAsSingleUnescapedArguments()
    {
        const string input = @"C:\videos\clip space සිංහල & # % '.mp4";
        const string output = @"D:\out\clip space සිංහල & # % '.tmp.mp4";
        var args = Build("tiktok", ExportQuality.Recommended, FramingMode.CropToFill, Source(1920, 1080, path: input), output: output);
        Assert.Equal(input, ValueAfter(args, "-i"));
        Assert.Equal(output, args[^1]);
    }

    [Fact]
    public void TrimUsesAccurateSeekBeforeInputAndDurationAfter()
    {
        var list = Build("tiktok", ExportQuality.Recommended, FramingMode.CropToFill, Source(1920, 1080),
            start: TimeSpan.FromSeconds(1.5), end: TimeSpan.FromSeconds(4)).ToList();
        var args = (IReadOnlyList<string>)list;
        Assert.True(list.IndexOf("-ss") < list.IndexOf("-i"));
        Assert.Equal("1.5", ValueAfter(args, "-ss"));
        Assert.Equal("2.5", ValueAfter(args, "-t"));
    }

    [Fact]
    public void MissingAudioDisablesAudioStream()
    {
        var args = Build("tiktok", ExportQuality.Recommended, FramingMode.CropToFill, Source(1920, 1080, audio: false));
        Assert.Contains("-an", args);
        Assert.DoesNotContain("-c:a", args);
        Assert.DoesNotContain("0:a:0", args);
    }

    private static string ValueAfter(IReadOnlyList<string> args, string flag)
    {
        var index = args.ToList().IndexOf(flag);
        return index >= 0 && index + 1 < args.Count ? args[index + 1] : "";
    }
}
