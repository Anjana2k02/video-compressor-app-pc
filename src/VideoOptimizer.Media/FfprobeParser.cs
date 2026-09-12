using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using VideoOptimizer.Core;

namespace VideoOptimizer.Media;

public sealed partial class FfprobeParser
{
    public VideoInfo Parse(string json, string inputPath, long fileSize)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
                throw new MediaException("No readable video stream was found in this file.");
            var video = streams.EnumerateArray()
                .Where(s => Text(s, "codec_type") == "video" && Number(Child(s, "disposition"), "attached_pic") != 1)
                .OrderByDescending(s => Number(Child(s, "disposition"), "default") == 1)
                .FirstOrDefault();
            if (video.ValueKind != JsonValueKind.Object)
                throw new MediaException("No readable video stream was found in this file.");

            var format = Child(root, "format");
            var width = PositiveInt(video, "width") ?? 0;
            var height = PositiveInt(video, "height") ?? 0;
            var duration = Positive(video, "duration") ?? Positive(format, "duration");
            if (duration is null)
            {
                var tagDuration = Text(Child(video, "tags"), "DURATION");
                if (TimeSpan.TryParse(tagDuration, CultureInfo.InvariantCulture, out var parsed)) duration = parsed.TotalSeconds;
            }
            if (width <= 0 || height <= 0 || duration is null or <= 0 || duration >= TimeSpan.MaxValue.TotalSeconds)
                throw new MediaException("This video has invalid dimensions or no usable duration.");

            var audio = streams.EnumerateArray().Where(s => Text(s, "codec_type") == "audio")
                .OrderByDescending(s => Number(Child(s, "disposition"), "default") == 1).FirstOrDefault();
            var rotation = Number(Child(video, "tags"), "rotate") ?? 0;
            var sideData = Child(video, "side_data_list");
            var hdrMetadata = false;
            if (sideData.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in sideData.EnumerateArray())
                {
                    rotation = Number(entry, "rotation") ?? rotation;
                    var type = Text(entry, "side_data_type") ?? "";
                    hdrMetadata |= type.Contains("Mastering display", StringComparison.OrdinalIgnoreCase)
                        || type.Contains("Content light", StringComparison.OrdinalIgnoreCase)
                        || type.Contains("DOVI", StringComparison.OrdinalIgnoreCase)
                        || type.Contains("HDR", StringComparison.OrdinalIgnoreCase);
                }
            }
            rotation = ((rotation % 360) + 360) % 360;
            var pixelFormat = Text(video, "pix_fmt") ?? "unknown";
            var depth = PositiveInt(video, "bits_per_raw_sample") ?? InferBitDepth(pixelFormat);
            var transfer = Text(video, "color_transfer");
            var primaries = Text(video, "color_primaries");
            var matrix = Text(video, "color_space");
            var color = ClassifyColor(transfer, primaries, matrix, hdrMetadata);
            var fps = Rational(Text(video, "avg_frame_rate"));
            var nominal = Rational(Text(video, "r_frame_rate"));
            return new VideoInfo(inputPath, TimeSpan.FromSeconds(duration.Value), fileSize, width, height, rotation,
                fps ?? nominal, nominal, fps.HasValue && nominal.HasValue && Math.Abs(fps.Value - nominal.Value) > 0.01,
                Text(video, "codec_name") ?? "unknown", pixelFormat, depth,
                PositiveLong(video, "bit_rate") ?? PositiveLong(format, "bit_rate"),
                audio.ValueKind == JsonValueKind.Object ? new AudioInfo(Text(audio, "codec_name") ?? "unknown",
                    PositiveInt(audio, "channels"), PositiveInt(audio, "sample_rate"), PositiveLong(audio, "bit_rate")) : null,
                primaries, transfer, matrix, Text(video, "color_range"), color,
                (int)(Number(video, "index") ?? 0));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new MediaException("The media analysis returned invalid data. Try another video.", ex);
        }
    }

    // SDR-indicating tags. HDR is detected first from the transfer curve or explicit metadata; otherwise any
    // recognized SDR transfer, primaries, or matrix (color_space) marks the clip SDR. Freshly encoded H.264 often
    // carries only a matrix or primaries, so keying on the transfer alone would leave clearly-SDR clips Unknown.
    private static readonly HashSet<string> SdrTransfers = new(StringComparer.OrdinalIgnoreCase)
        { "bt709", "smpte170m", "bt470m", "bt470bg", "smpte240m", "iec61966-2-1", "gamma22", "gamma28", "smpte428", "bt1361e", "log100", "log316" };
    private static readonly HashSet<string> SdrPrimaries = new(StringComparer.OrdinalIgnoreCase)
        { "bt709", "smpte170m", "bt470m", "bt470bg", "smpte240m", "film", "smpte428", "smpte431", "smpte432" };
    private static readonly HashSet<string> SdrMatrices = new(StringComparer.OrdinalIgnoreCase)
        { "bt709", "smpte170m", "bt470bg", "smpte240m", "fcc", "bt601", "gbr", "rgb", "ycgco" };

    private static ColorKind ClassifyColor(string? transfer, string? primaries, string? matrix, bool hdrMetadata)
    {
        if (transfer is "smpte2084" or "arib-std-b67" || hdrMetadata) return ColorKind.Hdr;
        if ((transfer is not null && SdrTransfers.Contains(transfer))
            || (primaries is not null && SdrPrimaries.Contains(primaries))
            || (matrix is not null && SdrMatrices.Contains(matrix)))
            return ColorKind.Sdr;
        return ColorKind.Unknown;
    }

    private static JsonElement Child(JsonElement parent, string key) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out var value) ? value : default;
    private static string? Text(JsonElement parent, string key)
    {
        var value = Child(parent, key);
        return value.ValueKind is JsonValueKind.String ? value.GetString()
            : value.ValueKind is JsonValueKind.Number ? value.GetRawText() : null;
    }
    private static double? Number(JsonElement parent, string key) =>
        double.TryParse(Text(parent, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value) ? value : null;
    private static double? Positive(JsonElement parent, string key) => Number(parent, key) is > 0 and var n ? n : null;
    private static int? PositiveInt(JsonElement parent, string key) => Positive(parent, key) is <= int.MaxValue and var n ? (int)n : null;
    private static long? PositiveLong(JsonElement parent, string key) => Positive(parent, key) is < long.MaxValue and var n ? (long)n : null;
    private static double? Rational(string? text)
    {
        var parts = text?.Split('/');
        if (parts is null || parts.Length > 2 || !double.TryParse(parts[0], CultureInfo.InvariantCulture, out var numerator)) return null;
        var denominator = 1d;
        if (parts.Length == 2 && !double.TryParse(parts[1], CultureInfo.InvariantCulture, out denominator)) return null;
        var result = numerator / denominator;
        return double.IsFinite(result) && result > 0 ? result : null;
    }
    private static int? InferBitDepth(string pixelFormat)
    {
        var match = DepthPattern().Match(pixelFormat);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var depth)) return depth;
        if (pixelFormat is "p010le" or "p010be") return 10;
        if (pixelFormat is "p016le" or "p016be") return 16;
        return pixelFormat is "yuv420p" or "yuv422p" or "yuv444p" or "yuvj420p" or "nv12" or "nv21" or "rgb24" or "bgr24" or "rgba" or "bgra" ? 8 : null;
    }
    [GeneratedRegex(@"(?:p|gray)(9|10|12|14|16)(?:le|be)$")]
    private static partial Regex DepthPattern();
}
