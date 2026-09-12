using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VideoOptimizer.App.Services;
using VideoOptimizer.App.ViewModels;
using VideoOptimizer.Core;
using VideoOptimizer.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;

namespace VideoOptimizer.App;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly MediaPlayer player = new() { AutoPlay = false };
    private readonly DispatcherTimer selectionTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool closed;
    private int previewGeneration;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 1000));
        viewModel = new MainViewModel(new MediaService(new BundledToolLocator(AppContext.BaseDirectory),
            new MediaProcessRunner(), new FfprobeParser()), new NativeVideoPicker(this), new FileAppLog());
        Root.DataContext = viewModel;
        Preview.SetMediaPlayer(player);
        player.MediaFailed += OnMediaFailed;
        viewModel.PropertyChanged += OnViewModelChanged;
        selectionTimer.Tick += OnSelectionTick;
        Root.SizeChanged += (_, e) => Preview.Height = Math.Clamp(e.NewSize.Width * 0.4, 200, 420);
        Closed += (_, _) =>
        {
            closed = true;
            previewGeneration++;
            selectionTimer.Stop();
            viewModel.PropertyChanged -= OnViewModelChanged;
            viewModel.Dispose();
            player.MediaFailed -= OnMediaFailed;
            (player.Source as IDisposable)?.Dispose();
            player.Dispose();
        };
    }

    private async void OnImport(object sender, RoutedEventArgs e) => await viewModel.PickAsync();
    private void OnCancel(object sender, RoutedEventArgs e) => viewModel.CancelImport();
    private void OnErrorClosed(InfoBar sender, InfoBarClosedEventArgs args) => viewModel.DismissError();
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.DragUIOverride.Caption = "Open video";
        e.Handled = true;
    }
    private async void OnDrop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count != 1 || items[0] is not StorageFile file)
            {
                viewModel.ReportError("Drop one MP4, MOV, MKV, or WebM file at a time.");
                return;
            }
            await viewModel.ImportAsync(file.Path);
        }
        catch (Exception ex) { if (!closed) viewModel.ReportError("The dropped file could not be opened. Try the Import video button.", ex); }
        finally { deferral.Complete(); }
    }
    private async void OnRecentSelection(object sender, SelectionChangedEventArgs e)
    {
        if (RecentBox.SelectedItem is not VideoInfo selected) return;
        RecentBox.SelectedItem = null;
        await viewModel.ImportAsync(selected.InputPath);
    }
    private async void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Video) || viewModel.Video is not { } video) return;
        var generation = ++previewGeneration;
        selectionTimer.Stop();
        player.Pause();
        var previous = player.Source;
        player.Source = null;
        (previous as IDisposable)?.Dispose();
        PreviewError.IsOpen = false;
        try
        {
            // StorageFile avoids URI escaping problems with #, %, spaces and Unicode filenames.
            var file = await StorageFile.GetFileFromPathAsync(video.InputPath);
            if (closed || generation != previewGeneration) return;
            player.Source = MediaSource.CreateFromStorageFile(file);
        }
        catch (Exception) { if (!closed && generation == previewGeneration) PreviewError.IsOpen = true; }
    }
    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!closed) { selectionTimer.Stop(); PreviewError.IsOpen = true; }
    });
    private void OnApplyTrim(object sender, RoutedEventArgs e) { selectionTimer.Stop(); viewModel.ApplyTrim(); }
    private void OnTrimKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { selectionTimer.Stop(); viewModel.ApplyTrim(); e.Handled = true; }
    }
    private void OnMarkStart(object sender, RoutedEventArgs e)
    {
        selectionTimer.Stop();
        if (viewModel.Trim is { } trim) viewModel.SetTrim(player.PlaybackSession.Position, trim.End);
    }
    private void OnMarkEnd(object sender, RoutedEventArgs e)
    {
        selectionTimer.Stop();
        if (viewModel.Trim is { } trim) viewModel.SetTrim(trim.Start, player.PlaybackSession.Position);
    }
    private void OnResetTrim(object sender, RoutedEventArgs e) { selectionTimer.Stop(); viewModel.ResetTrim(); }
    private void OnPlaySelection(object sender, RoutedEventArgs e)
    {
        if (!viewModel.ApplyTrim() || viewModel.Trim is not { } trim || player.Source is null) return;
        player.PlaybackSession.Position = trim.Start;
        player.Play();
        selectionTimer.Start();
    }
    private void OnSelectionTick(object? sender, object e)
    {
        if (viewModel.Trim is not { } trim || player.PlaybackSession.Position >= trim.End)
        {
            player.Pause();
            selectionTimer.Stop();
        }
    }
}
