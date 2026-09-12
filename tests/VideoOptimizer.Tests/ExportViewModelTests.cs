using VideoOptimizer.App.ViewModels;
using VideoOptimizer.Core;
using VideoOptimizer.Media;
using Xunit;

namespace VideoOptimizer.Tests;

public sealed class ExportViewModelTests
{
    private static readonly PresetCatalog Catalog = PresetCatalog.LoadFromDirectory(TestPaths.PresetsDirectory());

    private static VideoInfo Sample(string path = @"C:\test\clip.mp4") => new FfprobeParser().Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "h264-sdr.json")), path, 8_000_000);

    private static MainViewModel Create(IExportService export, IOutputPicker picker) =>
        new(new FakeMedia(), new NullPicker(), new NullLog(), export, Catalog, picker);

    private static async Task<MainViewModel> Imported(IExportService export, IOutputPicker picker)
    {
        var vm = Create(export, picker);
        await vm.ImportAsync(@"C:\test\clip.mp4");
        return vm;
    }

    [Fact]
    public void PresetsAreLoadedAndCannotExportWithoutVideo()
    {
        using var vm = Create(new FakeExport(), new FakeOutput(@"D:\out\clip.mp4"));
        Assert.NotEmpty(vm.Presets);
        Assert.NotNull(vm.SelectedPreset);
        Assert.False(vm.CanExport);
    }

    [Fact]
    public async Task SuccessfulExportProducesResultAndClearsBusyState()
    {
        var export = new FakeExport();
        using var vm = await Imported(export, new FakeOutput(@"D:\out\clip.mp4"));
        Assert.True(vm.CanExport);
        await vm.ExportAsync();
        Assert.True(vm.HasResult);
        Assert.False(vm.IsExporting);
        Assert.False(vm.HasExportError);
        Assert.Contains("smaller", vm.ResultHeadline);
        Assert.Equal("instagram-story", export.LastRequest!.PresetId);
    }

    [Fact]
    public async Task CancelledSavePickerDoesNothing()
    {
        var export = new FakeExport();
        using var vm = await Imported(export, new FakeOutput(null));
        await vm.ExportAsync();
        Assert.False(vm.HasResult);
        Assert.False(vm.IsExporting);
        Assert.Null(export.LastRequest);
    }

    [Fact]
    public async Task ExportFailureIsReportedAndRecoverable()
    {
        var export = new FakeExport { Fail = true };
        using var vm = await Imported(export, new FakeOutput(@"D:\out\clip.mp4"));
        await vm.ExportAsync();
        Assert.True(vm.HasExportError);
        Assert.False(vm.HasResult);
        Assert.False(vm.IsExporting);
        // Recovers on a subsequent successful export.
        export.Fail = false;
        await vm.ExportAsync();
        Assert.True(vm.HasResult);
        Assert.False(vm.HasExportError);
    }

    [Fact]
    public async Task CancellationSetsCancelledStatusAndLeavesNoResult()
    {
        using var vm = await Imported(new BlockingExport(), new FakeOutput(@"D:\out\clip.mp4"));
        var task = vm.ExportAsync();
        vm.CancelExport();
        await task;
        Assert.False(vm.HasResult);
        Assert.False(vm.IsExporting);
        Assert.Equal("Export cancelled.", vm.ExportStatus);
    }

    [Fact]
    public async Task RequestCarriesTrimSelectionAndFraming()
    {
        var export = new FakeExport();
        using var vm = await Imported(export, new FakeOutput(@"D:\out\clip.mp4"));
        vm.SetTrim(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        vm.SelectedFraming = FramingMode.FitBlur;
        vm.SelectedQuality = ExportQuality.Maximum;
        await vm.ExportAsync();
        Assert.Equal(TimeSpan.FromSeconds(1), export.LastRequest!.TrimStart);
        Assert.Equal(TimeSpan.FromSeconds(3), export.LastRequest.TrimEnd);
        Assert.Equal(FramingMode.FitBlur, export.LastRequest.Framing);
        Assert.Equal(ExportQuality.Maximum, export.LastRequest.Quality);
        Assert.False(export.LastRequest.AllowUpscaling);
    }

    [Fact]
    public async Task SummaryReflectsSelectedPresetAndUpdatesOnChange()
    {
        using var vm = await Imported(new FakeExport(), new FakeOutput(@"D:\out\clip.mp4"));
        // FitBlack keeps the full preset canvas, so the 720p WhatsApp target is visible in the summary.
        vm.SelectedFraming = FramingMode.FitBlack;
        vm.SelectedPreset = Catalog.Find("whatsapp-status");
        Assert.Contains("720 × 1280", vm.ExportSummary);
        Assert.Contains("estimate", vm.ExportSummary);
    }

    [Fact]
    public void CropAnchorOnlyMeaningfulForCropToFill()
    {
        using var vm = Create(new FakeExport(), new FakeOutput(@"D:\out\clip.mp4"));
        vm.SelectedFraming = FramingMode.CropToFill;
        Assert.True(vm.SupportsCropAnchor);
        vm.SelectedFraming = FramingMode.FitBlack;
        Assert.False(vm.SupportsCropAnchor);
    }

    [Fact]
    public async Task SuggestedOutputNameCombinesSourceAndPreset()
    {
        var picker = new FakeOutput(@"D:\out\clip-instagram-story.mp4");
        using var vm = await Imported(new FakeExport(), picker);
        await vm.ExportAsync();
        Assert.Equal("clip-instagram-story.mp4", picker.LastSuggested);
    }

    private sealed class FakeExport : IExportService
    {
        public bool Fail { get; set; }
        public VideoExportRequest? LastRequest { get; private set; }
        public Task<ExportResult> ExportAsync(VideoExportRequest request, VideoInfo source,
            IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (Fail) throw new MediaException("The export failed.");
            progress?.Report(new ExportProgress(0.5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
            var output = source with { InputPath = request.OutputPath, SizeBytes = source.SizeBytes / 2 };
            return Task.FromResult(new ExportResult(source, output, request.OutputPath));
        }
    }

    private sealed class BlockingExport : IExportService
    {
        public async Task<ExportResult> ExportAsync(VideoExportRequest request, VideoInfo source,
            IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            progress?.Report(new ExportProgress(0.1, TimeSpan.Zero, null));
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException();
        }
    }

    private sealed class FakeOutput(string? path) : IOutputPicker
    {
        public string? LastSuggested { get; private set; }
        public Task<string?> PickSaveAsync(string suggestedFileName)
        {
            LastSuggested = suggestedFileName;
            return Task.FromResult(path);
        }
    }

    private sealed class FakeMedia : IMediaService
    {
        public Task<VideoInfo> AnalyzeAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(Sample(path));
    }
    private sealed class NullPicker : IVideoPicker { public Task<string?> PickAsync() => Task.FromResult<string?>(null); }
    private sealed class NullLog : IAppLog { public void Write(string eventName, string? errorType = null) { } }
}
