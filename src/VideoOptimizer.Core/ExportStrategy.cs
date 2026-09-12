namespace VideoOptimizer.Core;

public enum ExportStrategy
{
    StreamCopy,   // remux only, no re-encode, no filters
    SmartTrim,    // stream copy with a keyframe-limited cut (not frame-perfect)
    Reencode,     // full decode/filter/encode
}

// What can and cannot be stream-copied for this source + target. Notes explain, in user-facing terms,
// each reason a re-encode is required.
public sealed record CompatibilityReport(
    bool CodecOk, bool PixelFormatOk, bool ColorOk, bool AudioOk, bool GeometryPassthrough, bool FpsOk,
    IReadOnlyList<string> Notes, bool BitrateOk = true)
{
    public bool CanStreamCopy => CodecOk && PixelFormatOk && ColorOk && AudioOk && GeometryPassthrough && FpsOk && BitrateOk;
}

public static class CompatibilityChecker
{
    public static CompatibilityReport Check(VideoInfo source, ExportPreset preset, ExportGeometry geometry)
    {
        var notes = new List<string>();

        bool codecOk = string.Equals(source.VideoCodec, "h264", StringComparison.OrdinalIgnoreCase);
        if (!codecOk) notes.Add($"Video is {source.VideoCodec.ToUpperInvariant()}, so it is converted to H.264.");

        bool pixelOk = source.PixelFormat is "yuv420p" && source.BitDepth is null or 8;
        if (!pixelOk) notes.Add("Pixel format is converted to 8-bit yuv420p.");

        bool colorOk = source.Color != ColorKind.Hdr;
        if (!colorOk) notes.Add("HDR is tone-mapped to SDR.");

        bool audioOk = source.Audio is null || string.Equals(source.Audio.Codec, "aac", StringComparison.OrdinalIgnoreCase);
        if (!audioOk) notes.Add($"Audio is {source.Audio!.Codec.ToUpperInvariant()}, so it is converted to AAC.");

        bool geometryOk = geometry.IsPassthrough;
        if (!geometryOk) notes.Add("Framing changes the picture, so it is re-encoded.");

        // Delivery caps: some destinations (WhatsApp) re-compress uploads that exceed their limits, so a
        // preset can require an FPS cap and a bitrate ceiling. Exceeding either forces a re-encode.
        bool fpsOk = preset.Video.MaxFps is not { } fpsCap
            || source.FramesPerSecond is not { } fps || fps <= fpsCap + 0.01;
        if (!fpsOk) notes.Add($"Frame rate is reduced to {preset.Video.MaxFps:0.#} fps so this destination keeps more quality.");

        bool bitrateOk = preset.Video.MaxBitrate is not { } maxRate
            || source.Bitrate is not { } bitrate || bitrate <= maxRate * 1.15;
        if (!bitrateOk) notes.Add("Bitrate is reduced to fit this destination's upload limits.");

        return new CompatibilityReport(codecOk, pixelOk, colorOk, audioOk, geometryOk, fpsOk, notes, bitrateOk);
    }
}

public sealed record StrategyDecision(
    ExportStrategy Strategy,
    VideoEncoder? Encoder,           // null for StreamCopy / SmartTrim
    string EncoderPreset,            // x264 speed preset when CPU re-encoding; otherwise ""
    bool FrameAccurate,
    bool ConvertsHdrToSdr,
    string Explanation,
    IReadOnlyList<string> Warnings,
    int? CrfOverride = null)         // measured-optimizer CRF, overriding the preset quality
{
    public bool IsCopy => Strategy is ExportStrategy.StreamCopy or ExportStrategy.SmartTrim;
}

// Deterministic strategy selection. Given the same inputs it always returns the same decision, and every
// decision carries a plain-language explanation so the UI never changes behavior silently.
public static class ExportStrategyEngine
{
    public static StrategyDecision Decide(VideoInfo source, ExportPreset preset, VideoExportRequest request,
        ExportGeometry geometry, EncoderCapabilities capabilities)
    {
        var report = CompatibilityChecker.Check(source, preset, geometry);
        bool hasTrim = request.TrimStart is not null || request.TrimEnd is not null;
        var warnings = new List<string>();

        if (report.CanStreamCopy)
        {
            if (!hasTrim)
                return new StrategyDecision(ExportStrategy.StreamCopy, null, "", FrameAccurate: true, ConvertsHdrToSdr: false,
                    "Stream copy — your video already matches this destination, so it is repackaged without re-encoding. No quality is lost.",
                    warnings);

            if (request.Performance is PerformanceMode.Automatic or PerformanceMode.Fast)
            {
                warnings.Add("A fast trim cuts on the nearest keyframe, so the start and end can shift by a fraction of a second. It is not frame-perfect.");
                return new StrategyDecision(ExportStrategy.SmartTrim, null, "", FrameAccurate: false, ConvertsHdrToSdr: false,
                    "Smart trim — copies your video without re-encoding and cuts near your selection (keyframe-limited).",
                    warnings);
            }
            // Balanced / Maximum Compression re-encode for a frame-accurate trim.
        }

        bool hdr = source.Color == ColorKind.Hdr;
        if (hdr) warnings.Add("HDR is tone-mapped to SDR (BT.709); colors and brightness are adapted for standard displays.");

        var (encoder, encoderPreset) = ChooseEncoder(capabilities, request.Performance);
        string reason = report.CanStreamCopy
            ? "for a frame-accurate trim"
            : string.Join(" ", report.Notes);
        return new StrategyDecision(ExportStrategy.Reencode, encoder, encoderPreset, FrameAccurate: true, ConvertsHdrToSdr: hdr,
            $"Re-encode with {encoder.DisplayName} — {reason}".Trim(), warnings);
    }

    public static (VideoEncoder Encoder, string EncoderPreset) ChooseEncoder(EncoderCapabilities capabilities, PerformanceMode mode) =>
        mode switch
        {
            // Best size: always CPU with a slower preset.
            PerformanceMode.MaximumCompression => (EncoderCapabilities.Cpu, "slow"),
            // Fastest: hardware when built in, otherwise a fast CPU preset.
            PerformanceMode.Fast => capabilities.PreferredHardware is { } hw ? (hw, "") : (EncoderCapabilities.Cpu, "veryfast"),
            // Balanced: CPU medium (predictable quality/size).
            PerformanceMode.Balanced => (EncoderCapabilities.Cpu, "medium"),
            // Automatic: hardware if available, else CPU medium. Falls back to CPU at run time on hardware failure.
            _ => capabilities.PreferredHardware is { } h ? (h, "") : (EncoderCapabilities.Cpu, "medium"),
        };
}
