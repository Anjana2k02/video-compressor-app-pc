using VideoOptimizer.Core;
using Xunit;

namespace VideoOptimizer.Tests;

public sealed class PresetTests
{
    [Fact]
    public void ShippedPresetsLoadWithoutErrors()
    {
        var catalog = PresetCatalog.LoadFromDirectory(TestPaths.PresetsDirectory());
        Assert.Empty(catalog.Errors);
        Assert.Contains(catalog.Presets, p => p.Id == "instagram-story");
        Assert.Contains(catalog.Presets, p => p.Id == "tiktok");
        Assert.Contains(catalog.Presets, p => p.Id == "whatsapp-status");
        Assert.Contains(catalog.Presets, p => p.Id == "whatsapp-status-hd");
    }

    [Fact]
    public void WhatsAppOptimizedTargetsSevenTwentyAt128k()
    {
        var preset = PresetCatalog.LoadFromDirectory(TestPaths.PresetsDirectory()).Find("whatsapp-status");
        Assert.NotNull(preset);
        Assert.Equal((720, 1280), (preset!.Video.PreferredWidth, preset.Video.PreferredHeight));
        Assert.Equal(128000, preset.Audio.Bitrate);
    }

    [Fact]
    public void QualityMapsToConfiguredCrf()
    {
        var preset = PresetCatalog.LoadFromDirectory(TestPaths.PresetsDirectory()).Find("instagram-story")!;
        Assert.Equal(23, preset.CrfFor(ExportQuality.Small));
        Assert.Equal(20, preset.CrfFor(ExportQuality.Recommended));
        Assert.Equal(17, preset.CrfFor(ExportQuality.Maximum));
    }

    [Theory]
    [InlineData("{ not json ")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2,\"id\":\"x\",\"name\":\"X\",\"video\":{\"preferredWidth\":1080,\"preferredHeight\":1920,\"aspectRatio\":\"9:16\",\"codec\":\"h264\",\"pixelFormat\":\"yuv420p\"},\"audio\":{\"codec\":\"aac\",\"sampleRate\":48000,\"bitrate\":128000},\"quality\":{\"smallCrf\":23,\"recommendedCrf\":20,\"maximumCrf\":17}}")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"x\",\"name\":\"X\",\"video\":{\"preferredWidth\":1081,\"preferredHeight\":1920,\"aspectRatio\":\"9:16\",\"codec\":\"h264\",\"pixelFormat\":\"yuv420p\"},\"audio\":{\"codec\":\"aac\",\"sampleRate\":48000,\"bitrate\":128000},\"quality\":{\"smallCrf\":23,\"recommendedCrf\":20,\"maximumCrf\":17}}")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"x\",\"name\":\"X\",\"video\":{\"preferredWidth\":1080,\"preferredHeight\":1920,\"aspectRatio\":\"9:16\",\"codec\":\"vp9\",\"pixelFormat\":\"yuv420p\"},\"audio\":{\"codec\":\"aac\",\"sampleRate\":48000,\"bitrate\":128000},\"quality\":{\"smallCrf\":23,\"recommendedCrf\":20,\"maximumCrf\":17}}")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"x\",\"name\":\"X\",\"video\":{\"preferredWidth\":1080,\"preferredHeight\":1920,\"aspectRatio\":\"9:16\",\"codec\":\"h264\",\"pixelFormat\":\"yuv420p\"},\"audio\":{\"codec\":\"aac\",\"sampleRate\":48000,\"bitrate\":128000},\"quality\":{\"smallCrf\":80,\"recommendedCrf\":20,\"maximumCrf\":17}}")]
    public void InvalidPresetsAreRejected(string json) =>
        Assert.Throws<PresetValidationException>(() => PresetCatalog.Parse(json));

    [Fact]
    public void MissingDirectoryIsReportedNotThrown()
    {
        var catalog = PresetCatalog.LoadFromDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Assert.Empty(catalog.Presets);
        Assert.Single(catalog.Errors);
    }

    [Fact]
    public void InvalidFileIsSkippedButValidOnesLoad()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VideoOptimizer.Presets." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Combine(TestPaths.PresetsDirectory(), "tiktok.json"), Path.Combine(directory, "tiktok.json"));
            File.WriteAllText(Path.Combine(directory, "broken.json"), "{ not valid");
            var catalog = PresetCatalog.LoadFromDirectory(directory);
            Assert.Single(catalog.Presets);
            Assert.Single(catalog.Errors);
            Assert.Equal("broken.json", catalog.Errors[0].File);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
