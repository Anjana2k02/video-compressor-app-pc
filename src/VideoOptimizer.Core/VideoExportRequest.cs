namespace VideoOptimizer.Core;

public enum ExportQuality { Small, Recommended, Maximum }
public enum FramingMode { CropToFill, FitBlack, FitBlur }

// Reserved single export entry point; Phase 1 deliberately has no export execution.
public sealed record VideoExportRequest(
    string InputPath,
    string OutputPath,
    TimeSpan? TrimStart,
    TimeSpan? TrimEnd,
    string PresetId,
    ExportQuality Quality,
    FramingMode Framing,
    bool PreserveSourceFps = true,
    bool AllowUpscaling = false);
