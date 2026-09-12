using System.Diagnostics;
using System.Text;
using VideoOptimizer.Core;

namespace VideoOptimizer.Media;

public sealed record ProcessResult(int ExitCode, string StandardOutput);

// All FFmpeg/FFprobe process execution is owned by this layer, including cancellation.
public sealed class MediaProcessRunner
{
    public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken, string? workingDirectory = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                RedirectStandardInput = true, StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = workingDirectory ?? ""
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start()) throw new MediaException("The media analysis tool could not start.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new MediaException("The bundled media tool could not start. Reinstall the tools and try again.", ex);
        }
        process.StandardInput.Close();
        using var registration = cancellationToken.Register(() => Kill(process));
        // Drain both pipes concurrently. Discard stderr because it can contain personal paths.
        var outputTask = ReadBoundedAsync(process.StandardOutput, 8 * 1024 * 1024);
        var errorTask = DrainAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await errorTask.ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new ProcessResult(process.ExitCode, output);
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            // Observe pipe tasks even when cancelled; killing the process closes its pipes.
            try { await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false); }
            catch (IOException) { }
        }
    }

    // Runs a process while streaming stdout line-by-line to onLine (used for FFmpeg `-progress pipe:1`).
    // stderr is drained and discarded because it can contain personal paths.
    public async Task<int> RunProgressAsync(string executable, IReadOnlyList<string> arguments,
        Action<string> onLine, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                RedirectStandardInput = true, StandardOutputEncoding = Encoding.UTF8
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start()) throw new MediaException("The export tool could not start.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new MediaException("The bundled media tool could not start. Reinstall the tools and try again.", ex);
        }
        process.StandardInput.Close();
        using var registration = cancellationToken.Register(() => Kill(process));
        var outputTask = ReadLinesAsync(process.StandardOutput, onLine);
        var errorTask = DrainAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false); }
            catch (IOException) { }
        }
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string> onLine)
    {
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null) onLine(line);
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        var exceeded = false;
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            if (builder.Length + count <= limit) builder.Append(buffer, 0, count);
            else exceeded = true; // Keep draining to avoid blocking the child process.
        }
        if (exceeded) throw new MediaException("This video's analysis is too large to load safely.");
        return builder.ToString();
    }
    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[8192];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) != 0) { }
    }
}
