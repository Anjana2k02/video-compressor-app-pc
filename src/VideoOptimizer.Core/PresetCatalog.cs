using System.Text.Json;

namespace VideoOptimizer.Core;

public sealed record PresetLoadError(string File, string Message);

// Loads and validates versioned JSON presets. Invalid files are reported, not thrown, so one bad
// preset never prevents the app from starting; valid presets still load.
public sealed class PresetCatalog
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public IReadOnlyList<ExportPreset> Presets { get; }
    public IReadOnlyList<PresetLoadError> Errors { get; }

    private PresetCatalog(IReadOnlyList<ExportPreset> presets, IReadOnlyList<PresetLoadError> errors)
        => (Presets, Errors) = (presets, errors);

    public ExportPreset? Find(string id) =>
        Presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    public static PresetCatalog LoadFromDirectory(string directory)
    {
        var presets = new List<ExportPreset>();
        var errors = new List<PresetLoadError>();
        if (!Directory.Exists(directory))
            return new PresetCatalog(presets, [new PresetLoadError(directory, "The presets folder is missing.")]);

        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                var preset = Parse(File.ReadAllText(file));
                if (presets.Any(p => p.Id == preset.Id))
                    errors.Add(new PresetLoadError(Path.GetFileName(file), $"Duplicate preset id '{preset.Id}'."));
                else
                    presets.Add(preset);
            }
            catch (PresetValidationException ex)
            {
                errors.Add(new PresetLoadError(Path.GetFileName(file), ex.Message));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                errors.Add(new PresetLoadError(Path.GetFileName(file), "The preset file could not be read as valid JSON."));
            }
        }
        return new PresetCatalog(presets, errors);
    }

    public static ExportPreset Parse(string json)
    {
        Dto? dto;
        try { dto = JsonSerializer.Deserialize<Dto>(json, SerializerOptions); }
        catch (JsonException) { throw new PresetValidationException("The preset is not valid JSON."); }
        if (dto is null) throw new PresetValidationException("The preset is empty.");

        if (dto.SchemaVersion is not SupportedSchemaVersion)
            throw new PresetValidationException($"Unsupported schemaVersion; expected {SupportedSchemaVersion}.");
        Require(!string.IsNullOrWhiteSpace(dto.Id), "id is required.");
        Require(!string.IsNullOrWhiteSpace(dto.Name), "name is required.");

        var v = dto.Video ?? throw new PresetValidationException("video is required.");
        Require(v.PreferredWidth is > 0 and <= 7680, "video.preferredWidth is out of range.");
        Require(v.PreferredHeight is > 0 and <= 7680, "video.preferredHeight is out of range.");
        Require((v.PreferredWidth & 1) == 0 && (v.PreferredHeight & 1) == 0, "video dimensions must be even.");
        Require(string.Equals(v.Codec, "h264", StringComparison.OrdinalIgnoreCase), "video.codec must be h264 in this release.");
        Require(string.Equals(v.PixelFormat, "yuv420p", StringComparison.OrdinalIgnoreCase), "video.pixelFormat must be yuv420p.");
        Require(!string.IsNullOrWhiteSpace(v.AspectRatio), "video.aspectRatio is required.");
        Require(v.MaxFps is null or > 0 and <= 240, "video.maxFps is out of range.");
        Require(v.MaxBitrate is null or > 0, "video.maxBitrate must be positive.");

        var a = dto.Audio ?? throw new PresetValidationException("audio is required.");
        Require(string.Equals(a.Codec, "aac", StringComparison.OrdinalIgnoreCase), "audio.codec must be aac.");
        Require(a.SampleRate is > 0, "audio.sampleRate must be positive.");
        Require(a.Bitrate is > 0, "audio.bitrate must be positive.");

        var q = dto.Quality ?? throw new PresetValidationException("quality is required.");
        foreach (var crf in new[] { q.SmallCrf, q.RecommendedCrf, q.MaximumCrf })
            Require(crf is >= 0 and <= 51, "quality CRF values must be between 0 and 51.");

        var r = dto.Rules ?? new RulesDto { PreventUpscaling = true, PreserveSourceFps = true };

        return new ExportPreset(
            dto.SchemaVersion!.Value, dto.Id!, dto.Name!,
            new PresetVideo(v.PreferredWidth!.Value, v.PreferredHeight!.Value, v.AspectRatio!, v.Codec!.ToLowerInvariant(), v.PixelFormat!.ToLowerInvariant(), v.MaxFps, v.MaxBitrate),
            new PresetAudio(a.Codec!.ToLowerInvariant(), a.SampleRate!.Value, a.Bitrate!.Value),
            new PresetQuality(q.SmallCrf!.Value, q.RecommendedCrf!.Value, q.MaximumCrf!.Value),
            new PresetRules(r.PreventUpscaling ?? true, r.PreserveSourceFps ?? true),
            dto.LastReviewed, dto.Notes);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new PresetValidationException(message);
    }

    private sealed class Dto
    {
        public int? SchemaVersion { get; set; }
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? LastReviewed { get; set; }
        public string? Notes { get; set; }
        public VideoDto? Video { get; set; }
        public AudioDto? Audio { get; set; }
        public QualityDto? Quality { get; set; }
        public RulesDto? Rules { get; set; }
    }
    private sealed class VideoDto
    {
        public int? PreferredWidth { get; set; }
        public int? PreferredHeight { get; set; }
        public string? AspectRatio { get; set; }
        public string? Codec { get; set; }
        public string? PixelFormat { get; set; }
        public double? MaxFps { get; set; }
        public long? MaxBitrate { get; set; }
    }
    private sealed class AudioDto
    {
        public string? Codec { get; set; }
        public int? SampleRate { get; set; }
        public int? Bitrate { get; set; }
    }
    private sealed class QualityDto
    {
        public int? SmallCrf { get; set; }
        public int? RecommendedCrf { get; set; }
        public int? MaximumCrf { get; set; }
    }
    private sealed class RulesDto
    {
        public bool? PreventUpscaling { get; set; }
        public bool? PreserveSourceFps { get; set; }
    }
}

public sealed class PresetValidationException(string message) : Exception(message);
