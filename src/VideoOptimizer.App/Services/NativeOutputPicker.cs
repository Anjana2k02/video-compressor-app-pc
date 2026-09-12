using Microsoft.UI.Xaml;
using VideoOptimizer.Core;
using Windows.Storage.Pickers;

namespace VideoOptimizer.App.Services;

public sealed class NativeOutputPicker(Window owner) : IOutputPicker
{
    public async Task<string?> PickSaveAsync(string suggestedFileName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName),
        };
        picker.FileTypeChoices.Add("MP4 video", [".mp4"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
