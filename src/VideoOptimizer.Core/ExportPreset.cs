namespace VideoOptimizer.Core;

// MaxFps and MaxBitrate are optional per-destination delivery caps. Platforms like WhatsApp re-compress
// uploads that exceed their own limits, so delivering inside the envelope preserves more quality end to end.
public sealed record PresetVideo(int PreferredWidth, int PreferredHeight, string AspectRatio, string Codec, string PixelFormat,
    double? MaxFps = null, long? MaxBitrate = null);
public sealed record PresetAudio(string Codec, int SampleRate, int Bitrate);
public sealed record PresetQuality(int SmallCrf, int RecommendedCrf, int MaximumCrf);
public sealed record PresetRules(bool PreventUpscaling, bool PreserveSourceFps);

// A social destination preset. Data only; framing, quality, and command building live elsewhere.
public sealed record ExportPreset(
    int SchemaVersion,
    string Id,
    string Name,
    PresetVideo Video,
    PresetAudio Audio,
    PresetQuality Quality,
    PresetRules Rules,
    string? LastReviewed = null,
    string? Notes = null)
{
    public int CrfFor(ExportQuality quality) => quality switch
    {
        ExportQuality.Small => Quality.SmallCrf,
        ExportQuality.Maximum => Quality.MaximumCrf,
        _ => Quality.RecommendedCrf,
    };

    public double TargetAspect => (double)Video.PreferredWidth / Video.PreferredHeight;
}
