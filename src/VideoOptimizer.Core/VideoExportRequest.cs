namespace VideoOptimizer.Core;

public enum ExportQuality { Small, Recommended, Maximum }
public enum FramingMode { CropToFill, FitBlack, FitBlur }
public enum PerformanceMode { Automatic, Fast, Balanced, MaximumCompression }

// The single export entry point. CropAnchor is additive (Phase 2 crop positioning) and defaults to centre,
// so the contract's locked fields are unchanged for existing callers.
public sealed record VideoExportRequest(
    string InputPath,
    string OutputPath,
    TimeSpan? TrimStart,
    TimeSpan? TrimEnd,
    string PresetId,
    ExportQuality Quality,
    FramingMode Framing,
    bool PreserveSourceFps = true,
    bool AllowUpscaling = false,
    CropAnchor? CropAnchor = null,
    PerformanceMode Performance = PerformanceMode.Automatic,
    int? TargetVmaf = null); // when set, measure to find the smallest visually-lossless CRF
