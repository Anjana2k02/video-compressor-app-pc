namespace VideoOptimizer.Core;

public enum EncoderKind { Cpu, Nvenc, Qsv, Amf }

public sealed record VideoEncoder(string FfmpegName, EncoderKind Kind, string DisplayName);

// The set of H.264 encoders the bundled FFmpeg was built with. An encoder being listed does not guarantee
// the matching GPU is present at runtime, so hardware paths must always be able to fall back to the CPU.
public sealed class EncoderCapabilities
{
    public static readonly VideoEncoder Cpu = new("libx264", EncoderKind.Cpu, "CPU (libx264)");

    public IReadOnlyList<VideoEncoder> Available { get; }

    public EncoderCapabilities(IEnumerable<VideoEncoder> available)
    {
        var list = available.ToList();
        if (list.All(e => e.Kind != EncoderKind.Cpu)) list.Insert(0, Cpu); // libx264 is the guaranteed baseline
        Available = list;
    }

    public bool Has(EncoderKind kind) => Available.Any(e => e.Kind == kind);
    public VideoEncoder? Find(EncoderKind kind) => Available.FirstOrDefault(e => e.Kind == kind);
    // Preference order for hardware acceleration.
    public VideoEncoder? PreferredHardware =>
        Find(EncoderKind.Nvenc) ?? Find(EncoderKind.Qsv) ?? Find(EncoderKind.Amf);
    public bool HasHardware => PreferredHardware is not null;

    public static EncoderCapabilities CpuOnly { get; } = new([]);
}

public interface IEncoderProbe
{
    Task<EncoderCapabilities> DetectAsync(CancellationToken cancellationToken = default);
}
