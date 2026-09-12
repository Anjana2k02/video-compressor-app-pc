using Microsoft.UI.Xaml;
using VideoOptimizer.Core;
using Windows.Storage.Pickers;

namespace VideoOptimizer.App.Services;

public sealed class NativeVideoPicker(Window owner) : IVideoPicker
{
    public async Task<string?> PickAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        foreach (var extension in VideoFilePolicy.Extensions) picker.FileTypeFilter.Add(extension);
        return (await picker.PickSingleFileAsync())?.Path;
    }
}
