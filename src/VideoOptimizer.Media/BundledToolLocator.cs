using VideoOptimizer.Core;

namespace VideoOptimizer.Media;

public sealed record MediaTools(string Ffmpeg, string Ffprobe);

public sealed class BundledToolLocator(string baseDirectory)
{
    public MediaTools Locate()
    {
        var directory = Path.Combine(baseDirectory, "tools", "ffmpeg");
        var ffmpeg = Path.Combine(directory, "ffmpeg.exe");
        var ffprobe = Path.Combine(directory, "ffprobe.exe");
        if (!File.Exists(ffmpeg) || !File.Exists(ffprobe))
            throw new MediaException("Bundled FFmpeg 8 tools are missing. Run tools/Install-Ffmpeg.ps1 from the project folder, then rebuild and restart VideoOptimizer.");
        return new MediaTools(ffmpeg, ffprobe);
    }
}
