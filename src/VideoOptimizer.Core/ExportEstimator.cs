namespace VideoOptimizer.Core;

// A deliberately rough pre-encode size estimate. CRF encoding is content-adaptive, so this is a
// bits-per-pixel heuristic only and must always be presented to the user as an estimate, never a promise.
public static class ExportEstimator
{
    public static long EstimateBytes(ExportGeometry geometry, double? fps, double durationSeconds,
        ExportPreset preset, ExportQuality quality)
    {
        double bitsPerPixel = quality switch
        {
            ExportQuality.Small => 0.06,
            ExportQuality.Maximum => 0.14,
            _ => 0.09,
        };
        double frameRate = Math.Clamp(fps ?? 30, 1, 120);
        if (preset.Video.MaxFps is { } fpsCap) frameRate = Math.Min(frameRate, fpsCap);
        double pixels = (double)geometry.OutputWidth * geometry.OutputHeight;
        double videoBitsPerSecond = pixels * frameRate * bitsPerPixel;
        if (preset.Video.MaxBitrate is { } maxRate) videoBitsPerSecond = Math.Min(videoBitsPerSecond, maxRate);
        double audioBitsPerSecond = preset.Audio.Bitrate;
        double seconds = Math.Max(0.1, durationSeconds);
        return (long)((videoBitsPerSecond + audioBitsPerSecond) * seconds / 8.0);
    }
}
