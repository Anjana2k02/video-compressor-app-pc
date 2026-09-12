using VideoOptimizer.App.ViewModels;
using VideoOptimizer.Core;
using VideoOptimizer.Media;
using Xunit;

namespace VideoOptimizer.Tests;

public sealed class ImportTests
{
    private static VideoInfo Sample(string path = @"C:\test\clip.mp4") => new FfprobeParser().Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "h264-sdr.json")), path, 123456);

    [Fact]
    public async Task CancelledPickerPreservesSelectionAndTrim()
    {
        using var vm = Create();
        await vm.ImportAsync(@"C:\test\clip.mp4");
        vm.SetTrim(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var before = vm.Video;
        var trim = vm.Trim;
        await vm.PickAsync();
        Assert.Same(before, vm.Video);
        Assert.Same(trim, vm.Trim);
        Assert.False(vm.HasError);
    }
    [Fact]
    public async Task InvalidTrimDraftRestoresValidFields()
    {
        using var vm = Create();
        await vm.ImportAsync(@"C:\test\clip.mp4");
        var before = vm.Trim;
        vm.TrimStartText = "50";
        vm.TrimEndText = "1";
        Assert.False(vm.ApplyTrim());
        Assert.Same(before, vm.Trim);
        Assert.Equal("0", vm.TrimStartText);
        Assert.NotNull(vm.TrimError);
        vm.TrimStartText = "invalid";
        Assert.False(vm.ApplyTrim());
        Assert.Same(before, vm.Trim);
    }
    [Fact]
    public async Task FailedImportPreservesWorkingVideoAndRecovers()
    {
        var service = new FakeMedia();
        using var vm = new MainViewModel(service, new CancelledPicker(), new QuietLog());
        await vm.ImportAsync(@"C:\test\clip.mp4");
        var before = vm.Video;
        service.Fail = true;
        await vm.ImportAsync(@"C:\private\bad.mp4");
        Assert.Same(before, vm.Video);
        Assert.True(vm.HasError);
        Assert.DoesNotContain("private", vm.Error);
        Assert.False(vm.IsBusy);
        service.Fail = false;
        await vm.ImportAsync(@"C:\test\other.mp4");
        Assert.False(vm.HasError);
        Assert.Equal("other.mp4", vm.FileName);
        Assert.Equal(2, vm.RecentSelections.Count);
    }
    [Fact]
    public async Task LatestImportWinsEvenIfOldServiceIgnoresCancellation()
    {
        var service = new DeferredMedia();
        using var vm = new MainViewModel(service, new CancelledPicker(), new QuietLog());
        var old = vm.ImportAsync("old");
        var current = vm.ImportAsync("new");
        service.New.SetResult(Sample(@"C:\test\new.mp4"));
        await current;
        service.Old.SetResult(Sample(@"C:\test\old.mp4"));
        await old;
        Assert.Equal("new.mp4", vm.FileName);
        Assert.Single(vm.RecentSelections);
        Assert.False(vm.IsBusy);
    }
    [Fact]
    public async Task CancelAnalysisLeavesNoPartialSelection()
    {
        using var vm = new MainViewModel(new WaitingMedia(), new CancelledPicker(), new QuietLog());
        var task = vm.ImportAsync("clip");
        vm.CancelImport();
        await task;
        Assert.Null(vm.Video);
        Assert.Null(vm.Trim);
        Assert.False(vm.HasError);
        Assert.False(vm.IsBusy);
    }
    [Fact]
    public void MissingToolsGivesActionableMessage()
    {
        var ex = Assert.Throws<MediaException>(() => new BundledToolLocator(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())).Locate());
        Assert.Contains("Install-Ffmpeg.ps1", ex.Message);
    }
    [Theory]
    [InlineData(@"C:\test\document.txt")]
    [InlineData(@"C:\test\picture.png")]
    [InlineData("https://example.com/video.mp4")]
    [InlineData(@"\\server\share\video.mp4")]
    public void UnsupportedAndNetworkPathsAreRejected(string path) => Assert.Throws<MediaException>(() => VideoFilePolicy.Validate(path));
    [Fact]
    public void MissingVideoIsRecoverable() => Assert.Throws<MediaException>(() => VideoFilePolicy.Validate(@"C:\missing-" + Guid.NewGuid() + ".mp4"));

    private static MainViewModel Create() => new(new FakeMedia(), new CancelledPicker(), new QuietLog());
    private sealed class FakeMedia : IMediaService
    {
        public bool Fail { get; set; }
        public Task<VideoInfo> AnalyzeAsync(string path, CancellationToken cancellationToken = default) =>
            Fail ? throw new IOException("sensitive path") : Task.FromResult(Sample(path));
    }
    private sealed class CancelledPicker : IVideoPicker { public Task<string?> PickAsync() => Task.FromResult<string?>(null); }
    private sealed class QuietLog : IAppLog { public void Write(string eventName, string? errorType = null) { } }
    private sealed class DeferredMedia : IMediaService
    {
        public TaskCompletionSource<VideoInfo> Old { get; } = new();
        public TaskCompletionSource<VideoInfo> New { get; } = new();
        public Task<VideoInfo> AnalyzeAsync(string inputPath, CancellationToken cancellationToken = default) => inputPath == "old" ? Old.Task : New.Task;
    }
    private sealed class WaitingMedia : IMediaService
    {
        public async Task<VideoInfo> AnalyzeAsync(string inputPath, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}
