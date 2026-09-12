using System.Diagnostics;
using System.Globalization;
using VideoOptimizer.Core;

namespace VideoOptimizer.Media;

// Executes one export end to end: detect encoders, plan geometry, pick a deterministic strategy, run FFmpeg
// with progress, finalize atomically, and probe the result. Never overwrites the input; writes to an
// app-owned temp file and renames on success. A hardware encode that fails is retried once on the CPU.
public sealed class VideoExportService(
    BundledToolLocator locator, MediaProcessRunner runner, FfprobeParser parser, PresetCatalog presets,
    IEncoderProbe encoderProbe, IVisuallyLosslessOptimizer? optimizer = null)
    : IExportService
{
    public async Task<ExportResult> ExportAsync(VideoExportRequest request, VideoInfo source,
        IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var preset = presets.Find(request.PresetId)
            ?? throw new MediaException("The selected destination is unavailable. Choose another and try again.");

        var inputFull = Path.GetFullPath(source.InputPath);
        await Task.Run(() => VideoFilePolicy.Validate(inputFull), cancellationToken).ConfigureAwait(false);
        var sourceFull = source with { InputPath = inputFull };

        var finalPath = ResolveOutput(request.OutputPath, inputFull);
        var directory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(directory);

        var geometry = ExportPlanner.Plan(source, preset, request.Framing, request.AllowUpscaling, request.CropAnchor);
        var capabilities = await encoderProbe.DetectAsync(cancellationToken).ConfigureAwait(false);
        var decision = ExportStrategyEngine.Decide(source, preset, request, geometry, capabilities);
        var notices = new List<string>();
        double? achievedVmaf = null;

        // Measured "visually lossless" mode: find the smallest CRF meeting the VMAF target, then re-encode once
        // on the CPU at that CRF. This overrides the strategy (no stream copy) because the goal is minimum size.
        if (request.TargetVmaf is not null && optimizer is not null)
        {
            var outcome = await optimizer.OptimizeAsync(request, sourceFull, preset, geometry, progress, cancellationToken).ConfigureAwait(false);
            notices.AddRange(outcome.Warnings);
            bool hdr = source.Color == ColorKind.Hdr;
            var measuredWarnings = new List<string>();
            if (hdr) measuredWarnings.Add("HDR is tone-mapped to SDR (BT.709); colors and brightness are adapted for standard displays.");
            decision = decision with
            {
                Strategy = ExportStrategy.Reencode,
                Encoder = EncoderCapabilities.Cpu,
                EncoderPreset = "medium",
                CrfOverride = outcome.Crf,
                FrameAccurate = true,
                ConvertsHdrToSdr = hdr,
                Warnings = measuredWarnings,
                Explanation = outcome.Measured
                    ? $"Visually lossless (measured) — re-encoded on the CPU at CRF {outcome.Crf} to meet a VMAF {request.TargetVmaf} target."
                    : $"Re-encoded on the CPU at CRF {outcome.Crf}; measured quality was unavailable.",
            };
            achievedVmaf = outcome.Measured ? outcome.AchievedVmaf : null;
        }

        notices.AddRange(decision.Warnings);
        var tools = locator.Locate();

        double totalSeconds = (request.TrimStart, request.TrimEnd) switch
        {
            ({ } s, { } e) => Math.Max(0.001, (e - s).TotalSeconds),
            (null, { } e) => Math.Max(0.001, e.TotalSeconds),
            _ => Math.Max(0.001, source.Duration.TotalSeconds),
        };

        var attempts = Attempts(decision);
        MediaException? lastError = null;

        for (var index = 0; index < attempts.Count; index++)
        {
            var attempt = attempts[index];
            var tempPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(finalPath)}.{Guid.NewGuid():N}.part.mp4");
            var args = FfmpegCommandBuilder.Build(request, sourceFull, preset, geometry, attempt, tempPath);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var exit = await runner.RunProgressAsync(tools.Ffmpeg, args,
                    line => ReportProgress(line, totalSeconds, stopwatch, progress), cancellationToken).ConfigureAwait(false);
                if (exit != 0)
                    throw new MediaException("The export failed. This video may use an unsupported feature. Try a different quality or framing.");
                if (!File.Exists(tempPath) || new FileInfo(tempPath).Length == 0)
                    throw new MediaException("The export produced no output. Please try again.");
                File.Move(tempPath, finalPath);
            }
            catch (OperationCanceledException)
            {
                TryDelete(tempPath);
                throw;
            }
            catch (MediaException ex)
            {
                TryDelete(tempPath);
                lastError = ex;
                if (index + 1 < attempts.Count) continue; // fall back to the next attempt (CPU)
                throw;
            }

            if (index > 0)
                notices.Add("Hardware encoding was unavailable, so the CPU encoder was used instead.");
            progress?.Report(new ExportProgress(1.0, stopwatch.Elapsed, TimeSpan.Zero));

            VideoInfo output;
            try { output = await ProbeAsync(tools, finalPath, cancellationToken).ConfigureAwait(false); }
            catch (MediaException) { output = sourceFull with { InputPath = finalPath, SizeBytes = new FileInfo(finalPath).Length }; }
            return new ExportResult(sourceFull, output, finalPath, ExplanationFor(attempt, index), notices, achievedVmaf);
        }

        throw lastError ?? new MediaException("The export could not be completed.");
    }

    // The ordered encode attempts: a hardware re-encode is followed by a safe CPU fallback.
    private static IReadOnlyList<StrategyDecision> Attempts(StrategyDecision decision)
    {
        if (decision.Strategy != ExportStrategy.Reencode || decision.Encoder is null || decision.Encoder.Kind == EncoderKind.Cpu)
            return [decision];
        var cpu = decision with { Encoder = EncoderCapabilities.Cpu, EncoderPreset = "medium" };
        return [decision, cpu];
    }

    private static string ExplanationFor(StrategyDecision attempt, int index) =>
        index == 0
            ? attempt.Explanation
            : $"Re-encode with {EncoderCapabilities.Cpu.DisplayName} — hardware encoding was unavailable.";

    private async Task<VideoInfo> ProbeAsync(MediaTools tools, string path, CancellationToken cancellationToken)
    {
        string[] arguments =
            ["-v", "error", "-protocol_whitelist", "file", "-show_streams", "-show_format", "-of", "json", "-i", path];
        var result = await runner.RunAsync(tools.Ffprobe, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            throw new MediaException("The exported file could not be summarized.");
        return parser.Parse(result.StandardOutput, path, new FileInfo(path).Length);
    }

    private static void ReportProgress(string line, double totalSeconds, Stopwatch stopwatch,
        IProgress<ExportProgress>? progress)
    {
        if (progress is null) return;
        var separator = line.IndexOf('=');
        if (separator <= 0) return;
        var key = line[..separator];
        var value = line[(separator + 1)..];
        if (key != "out_time_us" && key != "out_time_ms") return;
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros) || micros < 0) return;

        double done = micros / 1_000_000.0;
        double fraction = Math.Clamp(done / totalSeconds, 0, 1);
        var elapsed = stopwatch.Elapsed;
        TimeSpan? remaining = fraction is > 0.01 and < 1
            ? TimeSpan.FromSeconds(Math.Max(0, elapsed.TotalSeconds / fraction - elapsed.TotalSeconds))
            : null;
        progress.Report(new ExportProgress(fraction, elapsed, remaining));
    }

    // Never returns the input path; never returns an existing file. Adds " (n)" until unique.
    public static string ResolveOutput(string requested, string inputFullPath)
    {
        var full = Path.GetFullPath(requested);
        var directory = Path.GetDirectoryName(full)
            ?? throw new MediaException("Choose an output location on a local drive.");
        var name = Path.GetFileNameWithoutExtension(full);
        var extension = Path.GetExtension(full);
        if (string.IsNullOrEmpty(name)) name = "VideoOptimizer";
        if (string.IsNullOrEmpty(extension)) extension = ".mp4";

        var candidate = Path.Combine(directory, name + extension);
        for (var index = 1; SamePath(candidate, inputFullPath) || File.Exists(candidate); index++)
            candidate = Path.Combine(directory, $"{name} ({index}){extension}");
        return candidate;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
