using System.Globalization;
using VideoOptimizer.Core;

namespace VideoOptimizer.Media;

// Finds the smallest practical CRF (libx264, CPU) that meets a perceptual-quality target measured with VMAF.
// It samples short segments, encodes a near-lossless reference and candidate-CRF versions of each, measures
// VMAF, and binary-searches the CRF. Results are cached by source + edit + target so re-runs are instant.
public sealed class VisuallyLosslessOptimizer(
    BundledToolLocator locator, MediaProcessRunner runner, IVmafProbe vmafProbe, VmafMeasurer measurer, IAnalysisCache cache)
    : IVisuallyLosslessOptimizer
{
    private const int MinCrf = 16;
    private const int MaxCrf = 34;
    private const int MaxTrials = 6;
    private const string ReferencePreset = "medium"; // reference favours accuracy
    private const string SamplePreset = "veryfast";   // candidate samples favour speed

    public async Task<OptimizationOutcome> OptimizeAsync(VideoExportRequest request, VideoInfo source,
        ExportPreset preset, ExportGeometry geometry, IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int target = request.TargetVmaf ?? 96;
        int fallbackCrf = preset.CrfFor(request.Quality);

        if (!await vmafProbe.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
            return new OptimizationOutcome(fallbackCrf, double.NaN, Converged: false, Measured: false,
                ["Measured quality is unavailable (libvmaf is not in this FFmpeg build); the selected quality preset is used instead."]);

        var anchor = (request.CropAnchor ?? CropAnchor.Center).Clamped();
        var key = new AnalysisCacheKey(SourceFingerprint.Of(source), preset.Id, request.Framing, target,
            request.TrimStart?.Ticks ?? -1, request.TrimEnd?.Ticks ?? -1, anchor.X, anchor.Y);
        if (cache.TryGet(key, out var cached)) return cached;

        var tools = locator.Locate();
        var workDir = Path.Combine(Path.GetTempPath(), "VideoOptimizer.Vmaf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var trimStart = request.TrimStart ?? TimeSpan.Zero;
            var duration = (request.TrimEnd ?? source.Duration) - trimStart;
            var windows = SampleWindows.For(duration);
            if (windows.Count == 0)
                return Store(key, new OptimizationOutcome(fallbackCrf, double.NaN, false, false,
                    ["The clip is too short to measure; the selected quality preset is used instead."]));

            var filter = FfmpegCommandBuilder.BuildFilter(geometry, source.Color == ColorKind.Hdr,
                FfmpegCommandBuilder.FpsCap(preset, source));

            // Encode the near-lossless reference for each window once (absolute paths; only the VMAF log is relative).
            var references = new List<string>();
            for (int i = 0; i < windows.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var refPath = Path.Combine(workDir, $"ref_{i}.mp4");
                await EncodeSampleAsync(tools.Ffmpeg, source.InputPath, filter, trimStart + windows[i].Start, windows[i].Length,
                    crf: 0, ReferencePreset, refPath, cancellationToken).ConfigureAwait(false);
                references.Add(refPath);
                progress?.Report(new ExportProgress(null, stopwatch.Elapsed, null));
            }

            var result = await QualitySearch.SearchAsync(target, MinCrf, MaxCrf, MaxTrials, async crf =>
            {
                var scores = new List<VmafScore>();
                for (int i = 0; i < windows.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var distPath = Path.Combine(workDir, $"dist_{crf}_{i}.mp4");
                    await EncodeSampleAsync(tools.Ffmpeg, source.InputPath, filter, trimStart + windows[i].Start, windows[i].Length,
                        crf, SamplePreset, distPath, cancellationToken).ConfigureAwait(false);
                    scores.Add(await measurer.MeasureAsync(tools.Ffmpeg, distPath, references[i], workDir, cancellationToken).ConfigureAwait(false));
                    TryDelete(distPath);
                }
                progress?.Report(new ExportProgress(null, stopwatch.Elapsed, null));
                return VmafAggregate.Conservative(scores);
            }).ConfigureAwait(false);

            var warnings = new List<string>();
            if (!result.Converged)
                warnings.Add($"The target quality (VMAF {target}) could not be reached even at the highest setting; the best available quality is used.");
            return Store(key, new OptimizationOutcome(result.Crf, result.Vmaf, result.Converged, Measured: true, warnings));
        }
        finally
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private async Task EncodeSampleAsync(string ffmpeg, string input, string filter, TimeSpan start, TimeSpan length,
        int crf, string x264Preset, string outputPath, CancellationToken cancellationToken)
    {
        string[] args =
        [
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-ss", Seconds(start), "-t", Seconds(length),
            "-i", input,
            "-filter_complex", filter,
            "-map", "[v]", "-an",
            "-c:v", "libx264", "-preset", x264Preset, "-crf", crf.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p",
            outputPath,
        ];
        var exit = await runner.RunProgressAsync(ffmpeg, args, _ => { }, cancellationToken).ConfigureAwait(false);
        if (exit != 0) throw new MediaException("A quality-analysis sample could not be encoded.");
    }

    private OptimizationOutcome Store(AnalysisCacheKey key, OptimizationOutcome outcome)
    {
        cache.Set(key, outcome);
        return outcome;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture);
}
