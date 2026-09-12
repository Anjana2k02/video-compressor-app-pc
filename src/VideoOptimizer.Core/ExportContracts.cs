namespace VideoOptimizer.Core;

// Where the visible content sits when crop-to-fill discards edges. 0 = left/top, 1 = right/bottom.
public sealed record CropAnchor(double X, double Y)
{
    public static readonly CropAnchor Center = new(0.5, 0.5);
    public CropAnchor Clamped() => new(Math.Clamp(X, 0, 1), Math.Clamp(Y, 0, 1));
}

public sealed record ExportProgress(double? Fraction, TimeSpan Elapsed, TimeSpan? Remaining);

public sealed record ExportResult(VideoInfo Source, VideoInfo Output, string OutputPath,
    string StrategyExplanation = "", IReadOnlyList<string>? Notices = null, double? AchievedVmaf = null)
{
    public long OriginalSize => Source.SizeBytes;
    public long OutputSize => Output.SizeBytes;
    // Positive means smaller; negative means the export grew (possible for already-tiny inputs).
    public double ReductionPercent => OriginalSize <= 0 ? 0 : (1.0 - (double)OutputSize / OriginalSize) * 100.0;
    public IReadOnlyList<string> AllNotices => Notices ?? [];
}

public interface IExportService
{
    Task<ExportResult> ExportAsync(VideoExportRequest request, VideoInfo source,
        IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default);
}

public interface IOutputPicker
{
    Task<string?> PickSaveAsync(string suggestedFileName);
}
