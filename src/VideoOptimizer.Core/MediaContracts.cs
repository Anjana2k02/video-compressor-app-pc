namespace VideoOptimizer.Core;

public interface IMediaService
{
    Task<VideoInfo> AnalyzeAsync(string inputPath, CancellationToken cancellationToken = default);
}

public interface IVideoPicker
{
    Task<string?> PickAsync();
}

public interface IAppLog
{
    void Write(string eventName, string? errorType = null);
}

public sealed class MediaException(string message, Exception? inner = null) : Exception(message, inner);

public static class VideoFilePolicy
{
    public static IReadOnlyList<string> Extensions { get; } = Array.AsReadOnly(new[] { ".mp4", ".mov", ".mkv", ".webm" });

    public static void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new MediaException("Choose a video file from a local drive.");
        if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new MediaException("Choose an MP4, MOV, MKV, or WebM video.");
        // Prevent media tools from reading network locations in this local-only application.
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || new Uri(path).IsUnc)
            throw new MediaException("Copy the video to a local drive before importing it.");
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length == 0) throw new MediaException("This file is empty. Choose another video.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new MediaException("The video cannot be read. Check that it exists and that you have access, then try again.", ex);
        }
    }
}
