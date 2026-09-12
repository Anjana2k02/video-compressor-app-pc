using VideoOptimizer.Core;
using VideoOptimizer.Media;
using Xunit;

namespace VideoOptimizer.Tests;

public sealed class ParserTests
{
    private static VideoInfo Parse(string fixture) => new FfprobeParser().Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture + ".json")), @"C:\test\clip.mp4", 123456);

    [Fact]
    public void H264SdrIncludesMetadata()
    {
        var v = Parse("h264-sdr");
        Assert.Equal("h264", v.VideoCodec);
        Assert.Equal((1920, 1080), (v.DisplayWidth, v.DisplayHeight));
        Assert.Equal(29.97003, v.FramesPerSecond!.Value, 5);
        Assert.Equal(12.5125, v.Duration.TotalSeconds, 4);
        Assert.Equal(123456, v.SizeBytes);
        Assert.Equal(4000000, v.Bitrate);
        Assert.Equal(ColorKind.Sdr, v.Color);
        Assert.Equal(8, v.BitDepth);
        Assert.Equal("bt709", v.ColorMatrix);
        Assert.Equal("tv", v.ColorRange);
        Assert.Equal(new AudioInfo("aac", 2, 48000, 160000), v.Audio);
    }
    [Fact]
    public void HevcTenBitHdrPortrait()
    {
        var v = Parse("hevc-hdr");
        Assert.Equal("hevc", v.VideoCodec);
        Assert.Equal(ColorKind.Hdr, v.Color);
        Assert.Equal(10, v.BitDepth);
        Assert.Equal("bt2020", v.ColorPrimaries);
        Assert.Equal("smpte2084", v.ColorTransfer);
        Assert.Equal((1080, 1920), (v.DisplayWidth, v.DisplayHeight));
    }
    [Fact]
    public void RotationUsesDisplayMatrixOverLegacyTags()
    {
        var v = Parse("rotated-phone");
        Assert.Equal(270, v.Rotation);
        Assert.Equal((1080, 1920), (v.DisplayWidth, v.DisplayHeight));
        Assert.Equal((1920, 1080), (v.Width, v.Height));
    }
    [Fact]
    public void VfrIsReportedAsPossibleAndReadsDurationTag()
    {
        var v = Parse("vfr");
        Assert.True(v.MayBeVariableFrameRate);
        Assert.Equal(28.7, v.FramesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(10), v.Duration);
        Assert.Null(v.Bitrate);
    }
    [Fact]
    public void MissingAudioAndUnknownColorAreValid()
    {
        var v = Parse("no-audio");
        Assert.Null(v.Audio);
        Assert.Equal(25, v.FramesPerSecond);
        Assert.Equal(ColorKind.Unknown, v.Color);
    }
    [Fact]
    public void MalformedJsonIsRecoverable() => Assert.Throws<MediaException>(() => Parse("malformed"));

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"streams\":[]}")]
    [InlineData("{\"streams\":[{\"codec_type\":\"audio\"}]}")]
    [InlineData("{\"streams\":[{\"codec_type\":\"video\",\"width\":0,\"height\":10,\"duration\":\"1\"}]}")]
    [InlineData("{\"streams\":[{\"codec_type\":\"video\",\"width\":10,\"height\":10,\"duration\":\"Infinity\"}]}")]
    public void InvalidStructuresDoNotEscapeAsJsonErrors(string json) =>
        Assert.Throws<MediaException>(() => new FfprobeParser().Parse(json, "clip.mp4", 1));

    [Fact]
    public void CoverArtIsIgnoredAndDefaultVideoSelected()
    {
        const string json = """
            { "streams": [
              { "index": 0, "codec_type": "video", "disposition": { "attached_pic": 1, "default": 1 } },
              { "index": 1, "codec_type": "video", "width": 640, "height": 360 },
              { "index": 2, "codec_type": "video", "width": 1280, "height": 720, "disposition": { "default": 1 } }
            ], "format": { "duration": "5" } }
            """;
        var v = new FfprobeParser().Parse(json, "clip.mp4", 1);
        Assert.Equal(2, v.VideoStreamIndex);
        Assert.Equal(1280, v.Width);
    }
}
