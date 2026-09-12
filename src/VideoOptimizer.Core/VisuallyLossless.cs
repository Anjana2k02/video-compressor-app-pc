namespace VideoOptimizer.Core;

// A short segment of the source used to measure quality without encoding the whole video.
public sealed record SampleWindow(TimeSpan Start, TimeSpan Length);

public static class SampleWindows
{
    private static readonly double[] Positions = [0.10, 0.30, 0.50, 0.70, 0.90];

    // Windows centred near 10/30/50/70/90% of the (trimmed) duration, each `sampleSeconds` long, clamped
    // inside the clip. Very short clips collapse to fewer, non-duplicate windows.
    public static IReadOnlyList<SampleWindow> For(TimeSpan duration, double sampleSeconds = 2.0)
    {
        double total = duration.TotalSeconds;
        if (total <= 0) return [];
        double length = Math.Min(sampleSeconds, total);

        var windows = new List<SampleWindow>();
        foreach (var position in Positions)
        {
            double start = Math.Clamp(total * position - length / 2, 0, Math.Max(0, total - length));
            var window = new SampleWindow(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(length));
            // Skip a window that overlaps the previous one by more than half its length (short clips).
            if (windows.Count == 0 || start - windows[^1].Start.TotalSeconds >= length / 2)
                windows.Add(window);
        }
        return windows;
    }
}

public sealed record VmafScore(double Mean, double Min);

public static class VmafAggregate
{
    // Conservative: the worst sample governs, not the average across samples.
    public static double Conservative(IReadOnlyCollection<VmafScore> scores) =>
        scores.Count == 0 ? 0 : scores.Min(s => s.Mean);
}

public sealed record QualitySearchResult(int Crf, double Vmaf, int Trials, bool Converged);

// Binary search for the largest CRF (smallest file) whose measured VMAF still meets the target. Assumes
// VMAF is non-increasing as CRF rises. Pure control flow; the measurement delegate does the real work.
public static class QualitySearch
{
    public static async Task<QualitySearchResult> SearchAsync(double target, int minCrf, int maxCrf, int maxTrials,
        Func<int, Task<double>> measureAsync)
    {
        var measured = new Dictionary<int, double>();
        int trials = 0;
        async Task<double> Measure(int crf)
        {
            if (measured.TryGetValue(crf, out var cached)) return cached;
            var value = await measureAsync(crf).ConfigureAwait(false);
            measured[crf] = value;
            trials++;
            return value;
        }

        // Highest quality (lowest CRF) is the best case. If it cannot meet the target, report best effort.
        double best = await Measure(minCrf).ConfigureAwait(false);
        if (best < target)
            return new QualitySearchResult(minCrf, best, trials, Converged: false);

        int chosenCrf = minCrf;
        double chosenVmaf = best;
        int lo = minCrf + 1, hi = maxCrf;
        while (lo <= hi && trials < maxTrials)
        {
            int mid = lo + (hi - lo) / 2;
            double vmaf = await Measure(mid).ConfigureAwait(false);
            if (vmaf >= target)
            {
                chosenCrf = mid;
                chosenVmaf = vmaf;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return new QualitySearchResult(chosenCrf, chosenVmaf, trials, Converged: true);
    }
}

public sealed record OptimizationOutcome(int Crf, double AchievedVmaf, bool Converged, bool Measured, IReadOnlyList<string> Warnings);

public static class SourceFingerprint
{
    // Identifies a source for cache reuse without hashing the whole file: path + size + duration.
    public static string Of(VideoInfo source) =>
        $"{source.InputPath.ToLowerInvariant()}|{source.SizeBytes}|{source.Duration.Ticks}";
}

public sealed record AnalysisCacheKey(
    string Fingerprint, string PresetId, FramingMode Framing, int TargetVmaf,
    long TrimStartTicks, long TrimEndTicks, double CropX, double CropY);

public interface IAnalysisCache
{
    bool TryGet(AnalysisCacheKey key, out OptimizationOutcome outcome);
    void Set(AnalysisCacheKey key, OptimizationOutcome outcome);
}

public sealed class MemoryAnalysisCache : IAnalysisCache
{
    private readonly Dictionary<AnalysisCacheKey, OptimizationOutcome> entries = new();
    private readonly object gate = new();

    public bool TryGet(AnalysisCacheKey key, out OptimizationOutcome outcome)
    {
        lock (gate) return entries.TryGetValue(key, out outcome!);
    }

    public void Set(AnalysisCacheKey key, OptimizationOutcome outcome)
    {
        lock (gate) entries[key] = outcome;
    }
}

public interface IVmafProbe
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

public interface IVisuallyLosslessOptimizer
{
    Task<OptimizationOutcome> OptimizeAsync(VideoExportRequest request, VideoInfo source, ExportPreset preset,
        ExportGeometry geometry, IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default);
}
