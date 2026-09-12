using VideoOptimizer.Core;

namespace VideoOptimizer.Media;

// Detects whether the bundled FFmpeg exposes the libvmaf filter. Measured optimization is disabled cleanly
// when it is not available.
public sealed class VmafProbe(BundledToolLocator locator, MediaProcessRunner runner) : IVmafProbe
{
    private bool? cached;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (cached is { } value) return value;
        try
        {
            var tools = locator.Locate();
            var result = await runner.RunAsync(tools.Ffmpeg, ["-hide_banner", "-filters"], cancellationToken).ConfigureAwait(false);
            cached = result.ExitCode == 0 && result.StandardOutput.Contains("libvmaf", StringComparison.Ordinal);
        }
        catch (MediaException)
        {
            cached = false;
        }
        return cached.Value;
    }
}
