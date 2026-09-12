using System.Text.RegularExpressions;
using VideoOptimizer.Core;

namespace VideoOptimizer.Media;

// Detects which H.264 encoders the bundled FFmpeg was compiled with by parsing `ffmpeg -encoders`.
// The result is cached; a listed hardware encoder still falls back to libx264 if the GPU is absent at run time.
public sealed class EncoderProbe(BundledToolLocator locator, MediaProcessRunner runner) : IEncoderProbe
{
    private EncoderCapabilities? cached;

    public async Task<EncoderCapabilities> DetectAsync(CancellationToken cancellationToken = default)
    {
        if (cached is not null) return cached;
        try
        {
            var tools = locator.Locate();
            var result = await runner.RunAsync(tools.Ffmpeg, ["-hide_banner", "-encoders"], cancellationToken).ConfigureAwait(false);
            cached = Parse(result.ExitCode == 0 ? result.StandardOutput : "");
        }
        catch (MediaException)
        {
            cached = EncoderCapabilities.CpuOnly; // tools missing -> CPU baseline only
        }
        return cached;
    }

    public static EncoderCapabilities Parse(string encodersOutput)
    {
        var found = new List<VideoEncoder>();
        void AddIfPresent(string name, EncoderKind kind, string display)
        {
            if (Regex.IsMatch(encodersOutput, $@"\b{Regex.Escape(name)}\b")) found.Add(new VideoEncoder(name, kind, display));
        }
        AddIfPresent("h264_nvenc", EncoderKind.Nvenc, "NVIDIA NVENC");
        AddIfPresent("h264_qsv", EncoderKind.Qsv, "Intel Quick Sync");
        AddIfPresent("h264_amf", EncoderKind.Amf, "AMD AMF");
        // libx264 is added as the guaranteed baseline by EncoderCapabilities.
        return new EncoderCapabilities(found);
    }
}
