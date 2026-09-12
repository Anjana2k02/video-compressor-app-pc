using System.Diagnostics;
using VideoOptimizer.Core;
using VideoOptimizer.Media;
using Xunit;

namespace VideoOptimizer.Tests;

// These tests deliberately fail when the bundle is missing; setup is part of the build contract.
[Collection("ffmpeg-integration")]
public sealed class MediaIntegrationTests : IDisposable
{
    private readonly string root = FindRoot();
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "VideoOptimizer.Tests", Guid.NewGuid().ToString("N"));
    private readonly MediaProcessRunner runner = new();

    public MediaIntegrationTests() => Directory.CreateDirectory(scratch);

    [Theory]
    [InlineData("mp4", "libx264", "aac")]
    [InlineData("mov", "libx264", "aac")]
    [InlineData("mkv", "libx264", "aac")]
    [InlineData("webm", "libvpx-vp9", "libopus")]
    public async Task ProbesRealContainersAndSpecialPathsWithoutChangingInput(string extension, string videoCodec, string audioCodec)
    {
        var tools = new BundledToolLocator(root).Locate();
        var path = Path.Combine(scratch, $"clip space සිංහල & # % ' [{extension}].{extension}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await runner.RunAsync(tools.Ffmpeg,
            ["-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=25",
             "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "1.2", "-c:v", videoCodec,
             "-pix_fmt", "yuv420p", "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-c:a", audioCodec, path], timeout.Token);
        Assert.Equal(0, result.ExitCode);
        var before = await File.ReadAllBytesAsync(path);
        var info = await Service().AnalyzeAsync(path, timeout.Token);
        Assert.Equal((160, 90), (info.DisplayWidth, info.DisplayHeight));
        Assert.InRange(info.Duration.TotalSeconds, 1.15, 1.3);
        Assert.Equal(25, info.FramesPerSecond);
        Assert.NotNull(info.Audio);
        Assert.Equal(ColorKind.Sdr, info.Color);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ProbeRejectsRenamedNonVideoAndEmptyFile()
    {
        var path = Path.Combine(scratch, "fake.mp4");
        await File.WriteAllTextAsync(path, "this is not a video");
        await Assert.ThrowsAsync<MediaException>(() => Service().AnalyzeAsync(path));
        await File.WriteAllTextAsync(path, "");
        await Assert.ThrowsAsync<MediaException>(() => Service().AnalyzeAsync(path));
    }

    [Fact]
    public async Task CancelsRunningFfmpegAndLeavesNoOwnedProcess()
    {
        var tools = new BundledToolLocator(root).Locate();
        using var cancellation = new CancellationTokenSource();
        var running = runner.RunAsync(tools.Ffmpeg,
            ["-nostdin", "-v", "error", "-re", "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=25", "-t", "60", "-f", "null", "-"], cancellation.Token);
        // Poll for the process owned by this test's exact executable, without touching other FFmpeg instances.
        var deadline = Stopwatch.StartNew();
        Process? owned = null;
        while (deadline.Elapsed < TimeSpan.FromSeconds(5) && owned is null)
        {
            foreach (var p in Process.GetProcessesByName("ffmpeg"))
            {
                try
                {
                    if (string.Equals(p.MainModule?.FileName, tools.Ffmpeg, StringComparison.OrdinalIgnoreCase)) { owned = p; break; }
                }
                catch (System.ComponentModel.Win32Exception) { }
                if (p != owned) p.Dispose();
            }
            if (owned is null) await Task.Delay(25);
        }
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.NotNull(owned);
        using (owned) Assert.True(owned.HasExited);
    }

    [Fact]
    public async Task MissingExecutableFailsRecoverably()
    {
        await Assert.ThrowsAsync<MediaException>(() => runner.RunAsync(Path.Combine(scratch, "missing.exe"), [], CancellationToken.None));
    }

    private MediaService Service() => new(new BundledToolLocator(root), runner, new FfprobeParser());
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "VideoOptimizer.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Run integration tests from the repository checkout.");
    }
    public void Dispose()
    {
        // Only this test's unique directory is removed; never a user-supplied location.
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }
}
