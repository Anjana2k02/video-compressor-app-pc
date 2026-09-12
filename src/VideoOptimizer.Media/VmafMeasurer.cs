using System.Text.Json;
using VideoOptimizer.Core;

namespace VideoOptimizer.Media;

// Runs libvmaf to compare a distorted clip against a reference clip. FFmpeg is executed with its working
// directory set to the sample folder and a relative log filename, which avoids the filtergraph option-parsing
// problems that Windows paths (with ':' and '\') cause inside the libvmaf argument.
public sealed class VmafMeasurer(MediaProcessRunner runner)
{
    public async Task<VmafScore> MeasureAsync(string ffmpeg, string distortedFile, string referenceFile,
        string workingDirectory, CancellationToken cancellationToken)
    {
        var logName = $"vmaf_{Guid.NewGuid():N}.json";
        string[] arguments =
        [
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-i", distortedFile,
            "-i", referenceFile,
            "-lavfi", $"[0:v][1:v]libvmaf=log_fmt=json:log_path={logName}",
            "-f", "null", "-",
        ];
        var result = await runner.RunAsync(ffmpeg, arguments, cancellationToken, workingDirectory).ConfigureAwait(false);
        var logPath = Path.Combine(workingDirectory, logName);
        if (result.ExitCode != 0 || !File.Exists(logPath))
            throw new MediaException("Quality measurement failed. The video could not be compared.");
        try
        {
            var score = Parse(await File.ReadAllTextAsync(logPath, cancellationToken).ConfigureAwait(false));
            return score;
        }
        finally
        {
            try { File.Delete(logPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public static VmafScore Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("pooled_metrics", out var pooled)
            || !pooled.TryGetProperty("vmaf", out var vmaf))
            throw new MediaException("Quality measurement returned no score.");
        double mean = Read(vmaf, "mean");
        double min = vmaf.TryGetProperty("min", out _) ? Read(vmaf, "min") : mean;
        return new VmafScore(mean, min);
    }

    private static double Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
}
