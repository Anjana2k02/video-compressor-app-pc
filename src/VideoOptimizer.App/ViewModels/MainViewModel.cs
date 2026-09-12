using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VideoOptimizer.Core;

namespace VideoOptimizer.App.ViewModels;

public sealed class MainViewModel(IMediaService media, IVideoPicker picker, IAppLog log) : INotifyPropertyChanged, IDisposable
{
    private CancellationTokenSource? importCancellation;
    private VideoInfo? video;
    private TrimRange? trim;
    private bool busy;
    private bool pickerOpen;
    private string? error;
    private string? trimError;
    private string trimStartText = "0";
    private string trimEndText = "0";
    private bool disposed;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<VideoInfo> RecentSelections { get; } = [];
    public VideoInfo? Video { get => video; private set { video = value; Changed(); Changed(nameof(HasVideo)); Changed(nameof(FileName)); Changed(nameof(Metadata)); Changed(nameof(ColorNotice)); } }
    public TrimRange? Trim { get => trim; private set { trim = value; Changed(); Changed(nameof(SelectedDuration)); } }
    public bool HasVideo => Video is not null;
    public bool IsBusy { get => busy; private set { busy = value; Changed(); Changed(nameof(CanPick)); Changed(nameof(Status)); } }
    public bool CanPick => !IsBusy && !pickerOpen;
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
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void Dispose() { disposed = true; importCancellation?.Cancel(); }
}
