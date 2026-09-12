using VideoOptimizer.Core;
using Xunit;

namespace VideoOptimizer.Tests;

public sealed class GeometryTests
{
    private static readonly ExportPreset Instagram = PresetCatalog.LoadFromDirectory(TestPaths.PresetsDirectory()).Find("instagram-story")!;

    private static VideoInfo Source(int width, int height, double rotation = 0, double fps = 30) =>
        new("clip.mp4", TimeSpan.FromSeconds(10), 10_000_000, width, height, rotation, fps, fps, false,
            "h264", "yuv420p", 8, 5_000_000, new AudioInfo("aac", 2, 48000, 128000),
            "bt709", "bt709", "bt709", "tv", ColorKind.Sdr, 0);

    [Fact]
    public void LandscapeCropToPortraitReachesTargetAspectWithoutUpscaling()
    {
        var g = ExportPlanner.Plan(Source(1920, 1080), Instagram, FramingMode.CropToFill, allowUpscaling: false);
        Assert.False(g.Upscaled);
        Assert.Equal(Instagram.TargetAspect, g.OutputAspect, 2);
        Assert.True(g.OutputWidth <= 1920 && g.OutputHeight <= 1080);
        Assert.Equal((608, 1080), (g.OutputWidth, g.OutputHeight));
        Assert.Equal(0, g.CropWidth % 2);
        Assert.Equal(0, g.CropHeight % 2);
    }

    [Fact]
    public void PortraitPassthroughMatchesPreset()
    {
        var g = ExportPlanner.Plan(Source(1080, 1920), Instagram, FramingMode.CropToFill, allowUpscaling: false);
        Assert.Equal((1080, 1920), (g.OutputWidth, g.OutputHeight));
        Assert.False(g.Upscaled);
    }

    [Fact]
    public void SquareInputCropsToTargetAspect()
    {
        var g = ExportPlanner.Plan(Source(1000, 1000), Instagram, FramingMode.CropToFill, allowUpscaling: false);
        Assert.Equal(Instagram.TargetAspect, g.OutputAspect, 2);
        Assert.False(g.Upscaled);
        Assert.True(g.OutputWidth <= 1000 && g.OutputHeight <= 1000);
    }

    [Fact]
    public void LowResolutionCropDoesNotUpscale()
    {
        var g = ExportPlanner.Plan(Source(640, 360), Instagram, FramingMode.CropToFill, allowUpscaling: false);
        Assert.False(g.Upscaled);
        Assert.Equal((202, 360), (g.OutputWidth, g.OutputHeight));
    }

    [Fact]
    public void LowResolutionFitBlackKeepsContentNativeAndCanvasAtPreset()
    {
        var g = ExportPlanner.Plan(Source(640, 360), Instagram, FramingMode.FitBlack, allowUpscaling: false);
        Assert.False(g.Upscaled);
        Assert.Equal((640, 360), (g.ContentWidth, g.ContentHeight));
        Assert.Equal((1080, 1920), (g.OutputWidth, g.OutputHeight));
    }

    [Fact]
    public void FitBlurUsesSameGeometryAsFitBlack()
    {
        var black = ExportPlanner.Plan(Source(1280, 720), Instagram, FramingMode.FitBlack, allowUpscaling: false);
        var blur = ExportPlanner.Plan(Source(1280, 720), Instagram, FramingMode.FitBlur, allowUpscaling: false);
        Assert.Equal((black.ContentWidth, black.ContentHeight), (blur.ContentWidth, blur.ContentHeight));
        Assert.Equal((black.OutputWidth, black.OutputHeight), (blur.OutputWidth, blur.OutputHeight));
    }

    [Fact]
    public void UpscalingWhenExplicitlyAllowedReachesPresetSize()
    {
        var g = ExportPlanner.Plan(Source(640, 360), Instagram, FramingMode.FitBlack, allowUpscaling: true);
        Assert.True(g.Upscaled);
        Assert.Equal((1080, 1920), (g.OutputWidth, g.OutputHeight));
        Assert.True(g.ContentWidth > 640);
    }

    [Fact]
    public void RotationCorrectedDimensionsDriveGeometry()
    {
        // A 1920x1080 file rotated 90 degrees displays as 1080x1920 and should pass through as portrait.
        var g = ExportPlanner.Plan(Source(1920, 1080, rotation: 90), Instagram, FramingMode.CropToFill, allowUpscaling: false);
        Assert.Equal((1080, 1920), (g.OutputWidth, g.OutputHeight));
        Assert.False(g.Upscaled);
    }

    [Fact]
    public void CropAnchorMovesTheCropWindow()
    {
        var source = Source(1920, 1080);
        var centre = ExportPlanner.Plan(source, Instagram, FramingMode.CropToFill, false, CropAnchor.Center);
        var left = ExportPlanner.Plan(source, Instagram, FramingMode.CropToFill, false, new CropAnchor(0, 0.5));
        var right = ExportPlanner.Plan(source, Instagram, FramingMode.CropToFill, false, new CropAnchor(1, 0.5));
        Assert.Equal(0, left.CropX);
        Assert.True(centre.CropX > left.CropX);
        Assert.True(right.CropX > centre.CropX);
    }
}
