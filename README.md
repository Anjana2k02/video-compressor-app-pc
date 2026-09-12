# VideoOptimizer

Local-first Windows video preparation, built with C# / .NET 10, WinUI 3, and FFmpeg 8.

Phase 1 is complete pending user acceptance. It covers import, metadata, preview, and trim selection. Export is reserved for Phase 2.

See [the build contract](VIDEO_OPTIMIZER_GPT6_ASTRA.md) and [phase status](PHASE_STATUS.md).

## Requirements

- Windows 11 (build 19041 or newer)
- .NET SDK 10.0.201 (see [global.json](global.json))
- No Visual Studio required; a terminal is enough.

## First-time setup (once per checkout)

The FFmpeg 8 binaries are not committed. Download and verify them into `tools/ffmpeg/`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Install-Ffmpeg.ps1
```

The script downloads the pinned FFmpeg 8.1.2 build, checks its SHA-256, and extracts `ffmpeg.exe`, `ffprobe.exe`, and the license notices. Everything runs offline afterward. Details: [tools/README.md](tools/README.md).

## Build and run

From the repository root in the VS Code integrated terminal:

```powershell
dotnet build VideoOptimizer.slnx
dotnet run --project src/VideoOptimizer.App/VideoOptimizer.App.csproj
```

The app window opens with an import button and a drop zone. Import an MP4, MOV, MKV, or WebM to analyze, preview, and select a trim range.

## Test

```powershell
dotnet test tests/VideoOptimizer.Tests/VideoOptimizer.Tests.csproj
```

Unit tests run with fixtures; the integration tests encode short clips with the bundled FFmpeg, so run setup first.
