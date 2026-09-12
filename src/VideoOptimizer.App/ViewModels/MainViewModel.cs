using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VideoOptimizer.Core;

namespace VideoOptimizer.App.ViewModels;

public sealed class MainViewModel(IMediaService media, IVideoPicker picker, IAppLog log,
    IExportService? export = null, PresetCatalog? presets = null, IOutputPicker? outputPicker = null,
    IEncoderProbe? encoderProbe = null, IVmafProbe? vmafProbe = null)
    : INotifyPropertyChanged, IDisposable
{
    private CancellationTokenSource? importCancellation;
    private CancellationTokenSource? exportCancellation;
    private VideoInfo? video;
    private TrimRange? trim;
    private bool busy;
    private bool pickerOpen;
    private string? error;
    private string? trimError;
    private string trimStartText = "0";
    private string trimEndText = "0";
    private bool disposed;

    private ExportPreset? selectedPreset = presets?.Presets.FirstOrDefault();
    private ExportQuality selectedQuality = ExportQuality.Recommended;
    private FramingMode selectedFraming = FramingMode.CropToFill;
    private PerformanceMode selectedPerformance = PerformanceMode.Automatic;
    private CropAnchor cropAnchor = CropAnchor.Center;
    private EncoderCapabilities capabilities = EncoderCapabilities.CpuOnly;
    private bool capabilitiesRequested;
    private string strategyText = "";
    private bool measureEnabled;
    private bool measureAvailable;
    private bool measureRequested;
    private bool exporting;
    private double exportFraction;
    private string? exportError;
    private string exportStatus = "";
    private ExportResult? result;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<VideoInfo> RecentSelections { get; } = [];
    public VideoInfo? Video { get => video; private set { video = value; Changed(); Changed(nameof(HasVideo)); Changed(nameof(FileName)); Changed(nameof(Metadata)); Changed(nameof(ColorNotice)); Changed(nameof(CanExport)); Changed(nameof(ExportBlockedReason)); RefreshExportPreview(); } }
    public TrimRange? Trim { get => trim; private set { trim = value; Changed(); Changed(nameof(SelectedDuration)); RefreshExportPreview(); } }
    public bool HasVideo => Video is not null;
    public bool IsBusy { get => busy; private set { busy = value; Changed(); Changed(nameof(CanPick)); Changed(nameof(Status)); Changed(nameof(CanExport)); Changed(nameof(ExportBlockedReason)); } }
    public bool CanPick => !IsBusy && !pickerOpen && !IsExporting;
    public string Status => IsBusy ? "Analyzing video…" : HasVideo ? "Ready to preview and trim" : "Everything stays on your device";
    public string? Error { get => error; private set { error = value; Changed(); Changed(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(Error);
    public string? TrimError { get => trimError; private set { trimError = value; Changed(); } }
    public string TrimStartText { get => trimStartText; set { trimStartText = value; Changed(); } }
    public string TrimEndText { get => trimEndText; set { trimEndText = value; Changed(); } }
    public string SelectedDuration => Trim is null ? "No selection" : $"Selected duration · {TrimRange.FormatDisplay(Trim.Duration)}";
    public string FileName => Video?.FileName ?? "Your video, ready for a closer look";
    public string Metadata => Video is not { } v ? "Import a video to see its details."
        : $"{TrimRange.FormatDisplay(v.Duration)}  ·  {v.SizeBytes / 1048576d:0.##} MB  ·  {v.DisplayWidth} × {v.DisplayHeight}\n"
        + $"{(v.FramesPerSecond is { } fps ? $"{fps:0.###} fps" : "FPS unknown")}{(v.MayBeVariableFrameRate ? " (possibly variable)" : "")}  ·  {v.VideoCodec.ToUpperInvariant()}  ·  {(v.Audio is null ? "No audio" : v.Audio.Codec.ToUpperInvariant())}\n"
        + $"{v.PixelFormat}  ·  {(v.BitDepth is { } depth ? $"{depth}-bit" : "Bit depth unknown")}  ·  {v.Color switch { ColorKind.Hdr => "HDR", ColorKind.Sdr => "SDR", _ => "Color tags unknown" }}";
    public string ColorNotice => Video?.Color == ColorKind.Hdr
        ? "HDR detected. Preview appearance depends on Windows, your display, and installed codecs. No color conversion is applied."
        : "Preview uses Windows media codecs. If a codec is unavailable, analysis and trim selection still work.";

    public async Task PickAsync()
    {
        if (!CanPick || disposed) return;
        pickerOpen = true;
        Changed(nameof(CanPick));
        try
        {
            var path = await picker.PickAsync();
            // Cancelled picker must leave the previous selection and trim untouched.
            if (path is not null && !disposed) await ImportAsync(path);
        }
        catch (Exception ex)
        {
            if (!disposed) ReportError("The file picker could not open. Try dragging a video into the window.", ex);
        }
        finally { pickerOpen = false; Changed(nameof(CanPick)); }
    }

    public async Task ImportAsync(string path)
    {
        if (disposed) return;
        importCancellation?.Cancel();
        using var operation = new CancellationTokenSource();
        importCancellation = operation;
        IsBusy = true;
        Error = null;
        log.Write("import_started");
        try
        {
            var result = await media.AnalyzeAsync(path, operation.Token);
            if (disposed || operation.IsCancellationRequested || importCancellation != operation) return;
            if (!TrimRange.TryCreate(TimeSpan.Zero, result.Duration, result.Duration, out var initialTrim, out _))
                throw new MediaException("The video does not have a usable duration.");
            Result = null;
            ExportError = null;
            Video = result;
            Trim = initialTrim;
            ResetTrimFields();
            var previous = RecentSelections.FirstOrDefault(v => string.Equals(v.InputPath, result.InputPath, StringComparison.OrdinalIgnoreCase));
            if (previous is not null) RecentSelections.Remove(previous);
            RecentSelections.Insert(0, result);
            while (RecentSelections.Count > 5) RecentSelections.RemoveAt(RecentSelections.Count - 1);
            Changed(nameof(Status));
            log.Write("import_succeeded");
        }
        catch (OperationCanceledException) { log.Write("import_cancelled"); }
        catch (Exception ex)
        {
            if (!disposed && importCancellation == operation)
                ReportError(ex is MediaException ? ex.Message : "This video could not be opened. Check the file and try again.", ex);
        }
        finally
        {
            if (importCancellation == operation) { importCancellation = null; IsBusy = false; }
        }
    }

    public void CancelImport() => importCancellation?.Cancel();
    public void DismissError() => Error = null;
    public void ReportError(string message, Exception? exception = null)
    {
        Error = message;
        log.Write("recoverable_error", exception?.GetType().Name);
    }

    public bool ApplyTrim()
    {
        if (Video is null) return false;
        if (!TrimRange.TryParseTime(TrimStartText, out var start) || !TrimRange.TryParseTime(TrimEndText, out var end))
        {
            ResetTrimFields();
            TrimError = "Enter seconds (for example 2.5) or hh:mm:ss.fff. The previous selection was kept.";
            return false;
        }
        return SetTrim(start, end);
    }

    public bool SetTrim(TimeSpan start, TimeSpan end)
    {
        if (Video is null) return false;
        if (!TrimRange.TryCreate(start, end, Video.Duration, out var range, out var validationError))
        {
            ResetTrimFields();
            TrimError = validationError + " The previous selection was kept.";
            return false;
        }
        Trim = range;
        ResetTrimFields();
        return true;
    }

    public void ResetTrim() { if (Video is { } v) SetTrim(TimeSpan.Zero, v.Duration); }
    private void ResetTrimFields()
    {
        TrimStartText = TrimRange.FormatInput(Trim?.Start ?? TimeSpan.Zero);
        TrimEndText = TrimRange.FormatInput(Trim?.End ?? TimeSpan.Zero);
        TrimError = null;
    }
    // ---- Export (Phase 2) ----

    public IReadOnlyList<ExportPreset> Presets { get; } = presets?.Presets ?? [];
    public IReadOnlyList<PresetLoadError> PresetWarnings { get; } = presets?.Errors ?? [];
    public bool HasPresetWarnings => PresetWarnings.Count > 0;
    public string PresetWarningMessage => HasPresetWarnings
        ? "Some presets could not be loaded and were skipped: " + string.Join("; ", PresetWarnings.Select(e => $"{e.File} — {e.Message}"))
        : "";

    public ExportPreset? SelectedPreset { get => selectedPreset; set { selectedPreset = value; Changed(); Changed(nameof(CanExport)); Changed(nameof(ExportBlockedReason)); Changed(nameof(PresetNote)); RefreshExportPreview(); } }
    // Destination-specific upload guidance (e.g. WhatsApp's HD toggle / send-as-document tip) from preset data.
    public string PresetNote => SelectedPreset?.Notes ?? "";
    public ExportQuality SelectedQuality { get => selectedQuality; set { selectedQuality = value; Changed(); Changed(nameof(SelectedQualityIndex)); RefreshExportPreview(); } }
    public FramingMode SelectedFraming { get => selectedFraming; set { selectedFraming = value; Changed(); Changed(nameof(SelectedFramingIndex)); Changed(nameof(SupportsCropAnchor)); RefreshExportPreview(); } }
    public CropAnchor CropAnchor { get => cropAnchor; private set { cropAnchor = value; Changed(); RefreshExportPreview(); } }
    public bool SupportsCropAnchor => SelectedFraming == FramingMode.CropToFill;

    // Index-based bindings so the combo boxes can use ItemsSource + SelectedIndex without code-behind.
    public IReadOnlyList<string> QualityOptions { get; } = ["Smaller file", "Recommended", "Best quality"];
    public IReadOnlyList<string> FramingOptions { get; } = ["Crop to fill", "Fit with black bars", "Fit with blurred background"];
    public IReadOnlyList<string> CropHorizontalOptions { get; } = ["Left", "Center", "Right"];
    public IReadOnlyList<string> CropVerticalOptions { get; } = ["Top", "Center", "Bottom"];
    public int SelectedQualityIndex { get => (int)selectedQuality; set { SelectedQuality = (ExportQuality)Math.Clamp(value, 0, 2); } }
    public int SelectedFramingIndex { get => (int)selectedFraming; set { SelectedFraming = (FramingMode)Math.Clamp(value, 0, 2); } }
    private int cropHorizontalIndex = 1;
    private int cropVerticalIndex = 1;
    public int CropHorizontalIndex { get => cropHorizontalIndex; set { cropHorizontalIndex = Math.Clamp(value, 0, 2); Changed(); UpdateAnchor(); } }
    public int CropVerticalIndex { get => cropVerticalIndex; set { cropVerticalIndex = Math.Clamp(value, 0, 2); Changed(); UpdateAnchor(); } }
    private void UpdateAnchor() => CropAnchor = new CropAnchor(
        cropHorizontalIndex switch { 0 => 0, 2 => 1, _ => 0.5 },
        cropVerticalIndex switch { 0 => 0, 2 => 1, _ => 0.5 });

    public IReadOnlyList<string> PerformanceOptions { get; } = ["Automatic", "Fast (hardware if available)", "Balanced", "Maximum compression"];
    public int SelectedPerformanceIndex { get => (int)selectedPerformance; set { selectedPerformance = (PerformanceMode)Math.Clamp(value, 0, 3); Changed(); RefreshExportPreview(); } }
    // Plain-language description of the chosen export strategy plus any warnings (e.g. HDR conversion, keyframe trim).
    public string StrategyText { get => strategyText; private set { strategyText = value; Changed(); Changed(nameof(HasStrategy)); } }
    public bool HasStrategy => !string.IsNullOrEmpty(StrategyText);

    // Measured "visually lossless" optimization (VMAF). Enabled only when the FFmpeg build supports it.
    public bool MeasureAvailable { get => measureAvailable; private set { measureAvailable = value; Changed(); Changed(nameof(MeasureNote)); } }
    public bool MeasureEnabled { get => measureEnabled; set { measureEnabled = value && MeasureAvailable; Changed(); RefreshExportPreview(); } }
    public string MeasureNote => MeasureAvailable
        ? "Measures quality (VMAF) to find the smallest visually-lossless file. Slower — it encodes short samples first, then encodes once."
        : "Measured optimization is unavailable in this FFmpeg build; the selected quality is used.";

    public bool CanExport => !disposed && export is not null && outputPicker is not null && HasVideo && !IsBusy && !IsExporting && SelectedPreset is not null;
    // Explains why export is unavailable so the disabled button is never a mystery. Empty when export is ready or already running.
    public string ExportBlockedReason =>
        CanExport || IsExporting ? ""
        : export is null || outputPicker is null ? "Export is unavailable in this build."
        : !HasVideo ? "Import a video first to enable export."
        : IsBusy ? "Analyzing the video… export will be available in a moment."
        : SelectedPreset is null ? "No destination presets are available. Check the presets folder."
        : "Export is not available right now.";
    public bool IsExporting { get => exporting; private set { exporting = value; Changed(); Changed(nameof(CanExport)); Changed(nameof(ExportBlockedReason)); Changed(nameof(CanPick)); } }
    public double ExportFraction { get => exportFraction; private set { exportFraction = value; Changed(); Changed(nameof(ExportPercentText)); } }
    public string ExportPercentText => $"{ExportFraction * 100:0}%";
    public string ExportStatus { get => exportStatus; private set { exportStatus = value; Changed(); } }
    public string? ExportError { get => exportError; private set { exportError = value; Changed(); Changed(nameof(HasExportError)); } }
    public bool HasExportError => !string.IsNullOrEmpty(ExportError);

    private string exportSummary = "Import a video, then choose a destination.";
    public string ExportSummary { get => exportSummary; private set { exportSummary = value; Changed(); } }

    public ExportResult? Result { get => result; private set { result = value; Changed(); Changed(nameof(HasResult)); Changed(nameof(ResultHeadline)); Changed(nameof(ResultDetails)); Changed(nameof(ResultStrategy)); Changed(nameof(ResultNotices)); Changed(nameof(HasResultNotices)); Changed(nameof(ResultVmaf)); } }
    public bool HasResult => Result is not null;
    public string? ResultPath => Result?.OutputPath;
    // Reports measured perceptual quality without ever claiming "100%" or "lossless" for a lossy re-encode.
    public string ResultVmaf => Result?.AchievedVmaf is { } v
        ? $"Estimated VMAF {v:0.#} — visually lossless target met (not identical to the original)."
        : "";
    public string ResultStrategy => Result?.StrategyExplanation ?? "";
    public string ResultNotices => Result is { } r && r.AllNotices.Count > 0 ? string.Join("\n", r.AllNotices) : "";
    public bool HasResultNotices => Result is { } r && r.AllNotices.Count > 0;
    public string ResultHeadline => Result is not { } r ? "" :
        r.ReductionPercent >= 0 ? $"Export complete · {r.ReductionPercent:0.#}% smaller"
                                : $"Export complete · {Math.Abs(r.ReductionPercent):0.#}% larger";
    public string ResultDetails => Result is not { } r ? "" :
        $"{FormatSize(r.OriginalSize)} → {FormatSize(r.OutputSize)}\n"
        + $"{r.Output.DisplayWidth} × {r.Output.DisplayHeight} · "
        + $"{(r.Output.FramesPerSecond is { } f ? $"{f:0.###} fps" : "fps unknown")} · "
        + $"{r.Output.VideoCodec.ToUpperInvariant()} / {(r.Output.Audio?.Codec.ToUpperInvariant() ?? "no audio")}";

    public async Task ExportAsync()
    {
        if (!CanExport || export is null || outputPicker is null || Video is not { } v || SelectedPreset is not { } preset) return;
        string? destination;
        try { destination = await outputPicker.PickSaveAsync(SuggestedName(v, preset)); }
        catch (Exception ex) { ReportExportError("The save dialog could not open. Please try again.", ex); return; }
        if (destination is null || disposed) return;

        using var operation = new CancellationTokenSource();
        exportCancellation = operation;
        IsExporting = true;
        Result = null;
        ExportError = null;
        ExportFraction = 0;
        ExportStatus = "Preparing…";
        log.Write("export_started");
        // Ignore progress that arrives after cancellation so it cannot overwrite the final status.
        var progress = new Progress<ExportProgress>(p => { if (!operation.IsCancellationRequested) OnExportProgress(p); });
        try
        {
            int? targetVmaf = MeasureEnabled && MeasureAvailable ? 96 : null;
            var request = new VideoExportRequest(v.InputPath, destination, Trim?.Start, Trim?.End,
                preset.Id, SelectedQuality, SelectedFraming, PreserveSourceFps: true, AllowUpscaling: false, CropAnchor, selectedPerformance, targetVmaf);
            var completed = await export.ExportAsync(request, v, progress, operation.Token);
            if (disposed || exportCancellation != operation) return;
            Result = completed;
            ExportStatus = "";
            log.Write("export_succeeded");
        }
        catch (OperationCanceledException)
        {
            if (!disposed) ExportStatus = "Export cancelled.";
            log.Write("export_cancelled");
        }
        catch (MediaException ex) { if (!disposed) ReportExportError(ex.Message, ex); }
        catch (Exception ex) { if (!disposed) ReportExportError("The export could not be completed. Please try again.", ex); }
        finally { if (exportCancellation == operation) { exportCancellation = null; IsExporting = false; } }
    }

    public void CancelExport() => exportCancellation?.Cancel();
    public void StartNewExport() { Result = null; ExportError = null; ExportStatus = ""; ExportFraction = 0; }
    public void DismissExportError() => ExportError = null;

    private void OnExportProgress(ExportProgress p)
    {
        if (disposed) return;
        if (p.Fraction is { } fraction)
        {
            ExportFraction = fraction;
            ExportStatus = p.Remaining is { } remaining
                ? $"Encoding · {fraction * 100:0}% · about {FormatRemaining(remaining)} left"
                : $"Encoding · {fraction * 100:0}%";
        }
        else
        {
            ExportStatus = "Working…";
        }
    }

    private void RefreshExportPreview()
    {
        EnsureCapabilities();
        if (Video is not { } v || SelectedPreset is not { } preset)
        {
            ExportSummary = "Import a video, then choose a destination.";
            StrategyText = "";
            return;
        }
        var geometry = ExportPlanner.Plan(v, preset, SelectedFraming, allowUpscaling: false, CropAnchor);
        var seconds = (Trim?.Duration ?? v.Duration).TotalSeconds;
        var estimate = ExportEstimator.EstimateBytes(geometry, v.FramesPerSecond, seconds, preset, SelectedQuality);
        var upscaleNote = geometry.Upscaled ? " · upscaled" : "";
        ExportSummary = $"Output {geometry.OutputWidth} × {geometry.OutputHeight} · {preset.Video.AspectRatio} · "
            + $"H.264 / AAC · about {FormatSize(estimate)} (estimate){upscaleNote}";

        var probe = new VideoExportRequest(v.InputPath, "preview.mp4", Trim?.Start, Trim?.End, preset.Id,
            SelectedQuality, SelectedFraming, PreserveSourceFps: true, AllowUpscaling: false, CropAnchor, selectedPerformance);
        if (MeasureEnabled)
        {
            StrategyText = "Visually lossless (measured) — finds the smallest size meeting a VMAF 96 target, then encodes once. This takes longer than a normal export.";
            return;
        }
        var decision = ExportStrategyEngine.Decide(v, preset, probe, geometry, capabilities);
        StrategyText = decision.Warnings.Count > 0
            ? decision.Explanation + "\n" + string.Join("\n", decision.Warnings)
            : decision.Explanation;
    }

    private void EnsureCapabilities()
    {
        if (encoderProbe is not null && !capabilitiesRequested) { capabilitiesRequested = true; _ = DetectCapabilitiesAsync(); }
        if (vmafProbe is not null && !measureRequested) { measureRequested = true; _ = DetectMeasureAsync(); }
    }

    private async Task DetectCapabilitiesAsync()
    {
        try
        {
            var detected = await encoderProbe!.DetectAsync().ConfigureAwait(true);
            if (!disposed) { capabilities = detected; RefreshExportPreview(); }
        }
        catch (Exception ex) { log.Write("encoder_probe_failed", ex.GetType().Name); }
    }

    private async Task DetectMeasureAsync()
    {
        try
        {
            var available = await vmafProbe!.IsAvailableAsync().ConfigureAwait(true);
            if (!disposed) MeasureAvailable = available;
        }
        catch (Exception ex) { log.Write("vmaf_probe_failed", ex.GetType().Name); }
    }

    private void ReportExportError(string message, Exception? exception)
    {
        ExportError = message;
        ExportStatus = "";
        log.Write("export_error", exception?.GetType().Name);
    }

    private static string SuggestedName(VideoInfo v, ExportPreset preset) =>
        $"{Path.GetFileNameWithoutExtension(v.FileName)}-{preset.Id}.mp4";
    private static string FormatSize(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576d:0.#} MB" : $"{bytes / 1024d:0.#} KB";
    private static string FormatRemaining(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds}s" : $"{Math.Max(1, t.Seconds)}s";

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void Dispose() { disposed = true; importCancellation?.Cancel(); exportCancellation?.Cancel(); }
}
