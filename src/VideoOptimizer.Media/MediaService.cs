using VideoOptimizer.Core;

namespace VideoOptimizer.Media;

public sealed class MediaService(BundledToolLocator locator, MediaProcessRunner runner, FfprobeParser parser) : IMediaService
{
    public async Task<VideoInfo> AnalyzeAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => VideoFilePolicy.Validate(inputPath), cancellationToken).ConfigureAwait(false);
        var path = Path.GetFullPath(inputPath);
        var tools = locator.Locate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            // Only the local file protocol is allowed, including demuxer references.
            string[] arguments = ["-v", "error", "-protocol_whitelist", "file", "-show_streams", "-show_format", "-of", "json", "-i", path];
            var result = await runner.RunAsync(tools.Ffprobe, arguments, timeout.Token).ConfigureAwait(false);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
                throw new MediaException("This file could not be analyzed. It may be damaged, unsupported, or no longer readable.");
            return parser.Parse(result.StandardOutput, path, new FileInfo(path).Length);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MediaException("Analysis took too long. Try a different video or copy it to a faster local drive.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MediaException("The video became unreadable during analysis. Check the file and try again.", ex);
        }
    }
}
