using VideoOptimizer.Core;

namespace VideoOptimizer.Media;

public sealed class FileAppLog : IAppLog
{
    private readonly object gate = new();
    private readonly string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoOptimizer", "Logs", "app.log");

    public void Write(string eventName, string? errorType = null)
    {
        // Callers pass constant event IDs and exception type names, never messages or source paths.
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                if (File.Exists(logPath) && new FileInfo(logPath).Length > 512_000)
                    File.Move(logPath, logPath + ".previous", overwrite: true);
                File.AppendAllText(logPath, $"{DateTimeOffset.UtcNow:O} {eventName} {errorType}\n");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
