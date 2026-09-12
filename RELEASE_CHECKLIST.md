# Release checklist

Run through this before shipping a VideoOptimizer build.

## Build & test

- [ ] `dotnet build VideoOptimizer.slnx` — 0 warnings, 0 errors.
- [ ] `dotnet test tests/VideoOptimizer.Tests/VideoOptimizer.Tests.csproj` — all pass (integration tests require the FFmpeg bundle).
- [ ] `tools/Install-Ffmpeg.ps1` runs on a clean checkout and verifies the SHA-256.

## Package

- [ ] Produce a self-contained build: `powershell -File tools/publish.ps1` → `dist/VideoOptimizer/`.
- [ ] Confirm `tools/ffmpeg/ffmpeg.exe`, `ffprobe.exe`, `LICENSE`, `README.txt`, and `presets/*.json` are present next to the executable.
- [ ] Launch `dist/VideoOptimizer/VideoOptimizer.App.exe` and run the core flow (import → trim → export) once.

## Clean-machine smoke test

- [ ] On a machine with **no .NET SDK and no dev tools**, copy `dist/VideoOptimizer/`, launch it, import a clip, export to each destination, play the result.
- [ ] Confirm everything works **offline** (disconnect the network).
- [ ] Uninstall = delete the folder; confirm no leftovers outside `%LOCALAPPDATA%\VideoOptimizer\Logs` and the OS temp folder (temp is auto-cleaned per job).

## Correctness & safety

- [ ] No input file is ever modified or overwritten (compare bytes before/after an export).
- [ ] Cancel an export mid-run; confirm no partial `.part.mp4` remains.
- [ ] HDR clip shows the tone-mapping notice and is not washed out.
- [ ] Hardware mode falls back to CPU cleanly when no GPU is present (result reports the fallback).
- [ ] Measured mode reports an estimated VMAF and never displays "100%" or "lossless".

## Legal

- [ ] `NOTICE.md` and `tools/ffmpeg/LICENSE` ship with the build.
- [ ] Re-confirm the FFmpeg GPLv3 corresponding-source obligation for the exact bundled build.

## Docs

- [ ] `BENCHMARK.md` results regenerated on the release hardware with a real, diverse fixture set.
- [ ] `README.md` and `PHASE_STATUS.md` reflect the shipped scope.

## Known deferrals (optional, post-1.0)

- Batch queue (pause/cancel/retry across multiple jobs).
- MSIX installer + Store packaging (this release ships as an unpackaged self-contained folder).
- Full localization (strings are UI-facing and centralizable, but no satellite resources ship yet).
