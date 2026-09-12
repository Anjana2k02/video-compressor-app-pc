using System.Globalization;
using VideoOptimizer.Core;

namespace VideoOptimizer.Media;

// Deterministic FFmpeg argument construction. Every argument is a discrete list entry (never a concatenated
// shell string), so paths with spaces, Unicode, and shell metacharacters pass verbatim. The strategy decides
// whether this is a stream copy, a keyframe-limited smart trim, or a full re-encode (CPU or hardware).
public static class FfmpegCommandBuilder
{
    // Fallback x264 speed preset when a decision does not specify one.
    public const string DefaultX264Preset = "medium";

    // HDR (PQ/HLG) to SDR BT.709 tone-mapping chain using zimg + tonemap.
    private const string ToneMapChain =
        "zscale=t=linear:npl=100,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:p=bt709:r=tv,format=yuv420p";

    public static IReadOnlyList<string> Build(VideoExportRequest request, VideoInfo source,
        ExportPreset preset, ExportGeometry geometry, StrategyDecision decision, string outputPath) =>
        decision.Strategy switch
        {
            ExportStrategy.StreamCopy => BuildCopy(request, source, outputPath, trimmed: false),
            ExportStrategy.SmartTrim => BuildCopy(request, source, outputPath, trimmed: true),
            _ => BuildReencode(request, source, preset, geometry, decision, outputPath),
        };

    private static List<string> Common() => new() { "-hide_banner", "-loglevel", "error", "-nostdin", "-y" };

    private static void AppendTrimInput(List<string> args, VideoExportRequest request, VideoInfo source)
    {
        if (request.TrimStart is { } start) { args.Add("-ss"); args.Add(Seconds(start)); }
        args.Add("-i");
        args.Add(source.InputPath);
        if (request.TrimEnd is { } end)
        {
            var from = request.TrimStart ?? TimeSpan.Zero;
            args.Add("-t");
            args.Add(Seconds(end - from));
        }
    }

    private static void AppendTail(List<string> args, string outputPath)
    {
        args.Add("-movflags");
        args.Add("+faststart");
        args.Add("-progress");
        args.Add("pipe:1");
        args.Add("-nostats");
        args.Add(outputPath);
    }

    // Stream copy (whole file) or smart trim (keyframe-limited copy). No filters, no re-encode.
    private static List<string> BuildCopy(VideoExportRequest request, VideoInfo source, string outputPath, bool trimmed)
    {
        var args = Common();
        if (trimmed) AppendTrimInput(args, request, source);
        else { args.Add("-i"); args.Add(source.InputPath); }

        args.Add("-map"); args.Add("0:v:0");
        if (source.Audio is not null) { args.Add("-map"); args.Add("0:a:0"); }
        args.Add("-c"); args.Add("copy");
        // Keeps a copied trim from starting with negative timestamps.
        if (trimmed) { args.Add("-avoid_negative_ts"); args.Add("make_zero"); }
        AppendTail(args, outputPath);
        return args;
    }

    private static List<string> BuildReencode(VideoExportRequest request, VideoInfo source,
        ExportPreset preset, ExportGeometry geometry, StrategyDecision decision, string outputPath)
    {
        var args = Common();
        AppendTrimInput(args, request, source);

        args.Add("-filter_complex");
        args.Add(BuildFilter(geometry, decision.ConvertsHdrToSdr, FpsCap(preset, source)));
        args.Add("-map"); args.Add("[v]");

        bool hasAudio = source.Audio is not null;
        if (hasAudio) { args.Add("-map"); args.Add("0:a:0"); }

        AppendVideoEncoder(args, decision, preset, request.Quality);

        if (hasAudio)
        {
            args.Add("-c:a"); args.Add("aac");
            args.Add("-b:a"); args.Add(preset.Audio.Bitrate.ToString(CultureInfo.InvariantCulture));
            args.Add("-ar"); args.Add(preset.Audio.SampleRate.ToString(CultureInfo.InvariantCulture));
            args.Add("-ac"); args.Add("2");
        }
        else { args.Add("-an"); }

        AppendTail(args, outputPath);
        return args;
    }

    private static void AppendVideoEncoder(List<string> args, StrategyDecision decision, ExportPreset preset, ExportQuality quality)
    {
        var encoder = decision.Encoder ?? EncoderCapabilities.Cpu;
        var q = (decision.CrfOverride ?? preset.CrfFor(quality)).ToString(CultureInfo.InvariantCulture);
        args.Add("-c:v");
        args.Add(encoder.FfmpegName);
        switch (encoder.Kind)
        {
            case EncoderKind.Nvenc:
                args.AddRange(["-preset", "p5", "-rc", "vbr", "-cq", q, "-b:v", "0"]);
                break;
            case EncoderKind.Qsv:
                args.AddRange(["-global_quality", q, "-preset", "medium"]);
                break;
            case EncoderKind.Amf:
                args.AddRange(["-rc", "cqp", "-qp_i", q, "-qp_p", q, "-qp_b", q]);
                break;
            default: // Cpu / libx264
                args.AddRange(["-preset", string.IsNullOrEmpty(decision.EncoderPreset) ? DefaultX264Preset : decision.EncoderPreset, "-crf", q]);
                break;
        }
        // Bitrate ceiling (VBV): keeps peaks inside the destination's upload envelope so the platform's own
        // transcoder re-compresses lightly or not at all.
        if (preset.Video.MaxBitrate is { } maxRate)
        {
            args.Add("-maxrate"); args.Add(maxRate.ToString(CultureInfo.InvariantCulture));
            args.Add("-bufsize"); args.Add((maxRate * 2).ToString(CultureInfo.InvariantCulture));
        }
        args.Add("-pix_fmt"); args.Add(preset.Video.PixelFormat);
        args.Add("-color_primaries"); args.Add("bt709");
        args.Add("-color_trc"); args.Add("bt709");
        args.Add("-colorspace"); args.Add("bt709");
    }

    // The preset's FPS cap applies only when the source is known to exceed it; FPS is never reduced silently
    // (the strategy explanation names the reduction).
    public static double? FpsCap(ExportPreset preset, VideoInfo source) =>
        preset.Video.MaxFps is { } cap && source.FramesPerSecond is { } fps && fps > cap + 0.01 ? cap : null;

    public static string BuildFilter(ExportGeometry g, bool toneMap, double? fpsCap = null)
    {
        var tone = toneMap ? "," + ToneMapChain : "";
        if (fpsCap is { } cap) tone += ",fps=" + cap.ToString("0.###", CultureInfo.InvariantCulture);
        return g.Framing switch
        {
            FramingMode.CropToFill =>
                $"[0:v:0]crop={g.CropWidth}:{g.CropHeight}:{g.CropX}:{g.CropY},scale={g.OutputWidth}:{g.OutputHeight}:flags=lanczos{tone},setsar=1[v]",
            FramingMode.FitBlack =>
                $"[0:v:0]scale={g.ContentWidth}:{g.ContentHeight}:flags=lanczos,pad={g.OutputWidth}:{g.OutputHeight}:{(g.OutputWidth - g.ContentWidth) / 2}:{(g.OutputHeight - g.ContentHeight) / 2}:color=black{tone},setsar=1[v]",
            FramingMode.FitBlur =>
                $"[0:v:0]split=2[bg][fg];[bg]scale={g.OutputWidth}:{g.OutputHeight}:force_original_aspect_ratio=increase,crop={g.OutputWidth}:{g.OutputHeight},gblur=sigma=20[bgb];[fg]scale={g.ContentWidth}:{g.ContentHeight}:flags=lanczos[fgs];[bgb][fgs]overlay=(W-w)/2:(H-h)/2{tone},setsar=1[v]",
            _ => throw new ArgumentOutOfRangeException(nameof(g)),
        };
    }

    private static string Seconds(TimeSpan value) =>
        value.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture);
}
