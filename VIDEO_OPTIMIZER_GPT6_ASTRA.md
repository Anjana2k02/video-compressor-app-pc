# VideoOptimizer — GPT-6 Astra Build Contract

> Give this file to GPT-6 Astra inside the project workspace. Work on **one phase only per session**. Do not start the next phase until the user tests the current phase and explicitly says: `Develop Phase N`.

## 0. Product and stack (locked)

Build a local-first Windows desktop app for simple video trimming and high-quality, social-ready compression.

- App: `VideoOptimizer`
- Language/runtime: C# + .NET 10
- UI: WinUI 3, Windows App SDK, MVVM
- Media engine: bundled FFmpeg 8 + FFprobe
- Development: VS Code/terminal-compatible; do not require Visual Studio for normal build/run
- Export default: MP4, H.264, AAC-LC, `yuv420p`, `+faststart`
- Processing: entirely local/offline; no server, account, telemetry, database, or cloud upload
- Platforms: WhatsApp Status, Instagram Story/Reel, TikTok
- Product wording: use **Visually Lossless**; never promise literal “100% quality” after lossy encoding

Do not replace this stack, add frameworks, or broaden the product without permission.

## 1. Operating rules for Astra

1. Read this file, the repository, `README.md`, and `PHASE_STATUS.md` before editing.
2. Inspect existing work first. Preserve working behavior and user changes.
3. Implement only the requested phase. No placeholders advertised as complete.
4. Keep UI, domain models, and FFmpeg command construction separate.
5. Prefer small, testable classes; async APIs; cancellation tokens; nullable reference types.
6. Quote all process arguments safely. Never invoke FFmpeg through a shell string.
7. Never overwrite an input file. Write to a temporary output and atomically finalize on success.
8. Kill the FFmpeg process tree on cancellation; remove only app-owned temporary files.
9. Do not silently stretch, upscale, reduce FPS, crop, or alter HDR. Follow explicit rules below.
10. At phase end: build, run automated tests, fix failures, update `PHASE_STATUS.md`, then stop.
11. Keep responses token-efficient: outcome, changed files, commands run, test results, known limits.
12. If blocked, report the exact blocker and smallest user action required. Do not invent success.

### Required phase response format

```md
## Phase N result
Status: PASS | BLOCKED
Implemented: <short list>
Verification: <commands and results>
Manual checks: <numbered steps>
Known limits: <only real limits>
Next: Waiting for user testing. Do not begin Phase N+1.
```

## 2. Architecture contract

```text
VideoOptimizer/
├─ src/VideoOptimizer.App/       # WinUI views, view-models, navigation
├─ src/VideoOptimizer.Core/      # models, decisions, presets, interfaces
├─ src/VideoOptimizer.Media/     # FFprobe/FFmpeg processes and filters
├─ tests/VideoOptimizer.Tests/   # unit/integration tests
├─ presets/                      # versioned JSON presets
├─ tools/                        # FFmpeg binaries + license/readme
├─ README.md
└─ PHASE_STATUS.md
```

Core request object must remain the single export entry point:

```csharp
public sealed record VideoExportRequest(
    string InputPath,
    string OutputPath,
    TimeSpan? TrimStart,
    TimeSpan? TrimEnd,
    string PresetId,
    ExportQuality Quality,
    FramingMode Framing,
    bool PreserveSourceFps = true,
    bool AllowUpscaling = false);
```

All FFmpeg execution goes through one media service. UI code must never assemble FFmpeg arguments.

## 3. Non-negotiable media rules

- Preserve source FPS by default; cap only when a selected preset explicitly requires it.
- Never upscale unless the user enables it.
- Preserve aspect ratio. Use `CropToFill`, `FitBlack`, or `FitBlur`; never stretch.
- Correct for rotation metadata before calculating dimensions/crop.
- Detect codec, dimensions, FPS, duration, bitrate, audio, pixel format, rotation, color primaries, transfer, matrix, bit depth, and HDR indicators with FFprobe JSON.
- Social default is SDR BT.709, 8-bit H.264/AAC. HDR conversion must use deliberate tone mapping; never perform a blind HDR-to-SDR conversion.
- Quality modes initially map to x264 CRF: Small `23`, Recommended `20`, Maximum `17`; keep mappings configurable.
- Audio: AAC-LC, stereo when appropriate, 48 kHz, 128 kbps WhatsApp and 160 kbps Instagram/TikTok.
- Smart copy is allowed only when codec/container/audio/dimensions/aspect/FPS/color are compatible and no visual filter is required.
- Fast seek/copy cuts may be keyframe-limited. Never label them frame-perfect.
- Parse `-progress pipe:1`; do not scrape localized console text.
- Treat exit code, cancellation, invalid/missing output, and zero-byte output as failure.

## 4. Preset schema

Presets are data, not hardcoded UI logic. Validate JSON at startup and show a recoverable error for invalid presets.

```json
{
  "schemaVersion": 1,
  "id": "instagram-story",
  "name": "Instagram Story",
  "video": {
    "preferredWidth": 1080,
    "preferredHeight": 1920,
    "aspectRatio": "9:16",
    "codec": "h264",
    "pixelFormat": "yuv420p"
  },
  "audio": { "codec": "aac", "sampleRate": 48000, "bitrate": 160000 },
  "quality": { "smallCrf": 23, "recommendedCrf": 20, "maximumCrf": 17 },
  "rules": { "preventUpscaling": true, "preserveSourceFps": true }
}
```

Create equivalent presets for TikTok and WhatsApp Status. WhatsApp optimized target is 720×1280/128 kbps; also permit an explicit 1080×1920 maximum option. Platform limits can change, so label preset metadata with `lastReviewed` and do not claim permanent compliance.

---

# Phase 1 — Foundation and runnable vertical slice

## Goal

A clean app that launches, imports a video, analyzes it, previews it, and exposes validated trim start/end controls. No production export yet.

## Implement

- Solution/projects and architecture above.
- WinUI shell with import/drop zone, native file picker, recent selection state, responsive error UI.
- Import MP4, MOV, MKV, and WebM; reject non-video or unreadable paths gracefully.
- Bundled-tool locator and actionable missing-FFmpeg message.
- FFprobe JSON runner and robust parser into `VideoInfo`.
- Display filename, duration, size, displayed width×height after rotation, FPS, codecs, SDR/HDR indicator.
- Video preview with play/pause, seek, mute/volume, current time/duration.
- Two trim handles or equivalent accessible controls with start/end time fields.
- Enforce `0 ≤ start < end ≤ duration`; show selected duration.
- View-model state must survive ordinary window resize/navigation.
- Basic logging without personal paths in normal UI.

## Tests

- FFprobe parser fixtures: H.264 SDR landscape; HEVC 10-bit HDR portrait; rotated phone video; variable frame rate; missing audio; malformed JSON.
- Trim-boundary validation.
- Tool-not-found, unsupported file, and cancelled picker cases.
- `dotnet build` and `dotnet test` must pass with zero errors.

## Manual acceptance gate

1. App launches from VS Code terminal instructions in `README.md`.
2. Select and drag/drop videos both work.
3. Preview controls work.
4. Shown metadata is correct for at least two different videos.
5. Trim values cannot become invalid.
6. App remains responsive and errors do not crash it.

Stop after Phase 1. Record completed items and evidence in `PHASE_STATUS.md`.

---

# Phase 2 — Social presets and reliable export

## Start condition

Only after the user says `Develop Phase 2` and Phase 1 is marked accepted.

## Goal

Trim and export an upload-ready MP4 using a selected platform, framing mode, and quality.

## Implement

- Load/version/validate JSON presets for WhatsApp, Instagram, and TikTok.
- Destination selector, quality selector, framing selector, output picker, and export summary.
- Framing modes: crop-to-fill with adjustable crop position; fit with black; fit with blurred background.
- Preserve aspect and FPS; prevent upscaling by default.
- Central deterministic command builder using `ArgumentList`, never concatenated shell commands.
- CPU baseline: libx264 CRF/preset + AAC + `yuv420p` + `faststart`.
- Asynchronous export with progress, elapsed/remaining time where reliable, cancel, clear errors, and temp-file cleanup.
- Output name avoids collisions and never overwrites input.
- Completion page: original/output size, reduction percentage, dimensions, FPS, codec, play output, open folder, export again.
- Basic estimated size clearly labeled as an estimate.

## Tests

- Golden argument tests for each preset × quality × framing combination.
- Paths containing spaces/Unicode/special characters.
- Landscape→portrait crop, portrait passthrough, square input, low-resolution no-upscale.
- Trim precision tolerance, cancellation, FFmpeg failure, disk/output failure.
- Probe exported fixtures and assert MP4/H.264/AAC, dimensions, aspect, pixel format, duration tolerance, playable output.

## Manual acceptance gate

Export one landscape and one portrait source for all three destinations. Confirm no stretching, no fake upscaling, correct trim, audible synced audio, responsive progress/cancel, and playable results.

Stop after Phase 2 and wait.

---

# Phase 3 — Smart acceleration, compatibility, and color safety

## Start condition

Only after the user says `Develop Phase 3` and Phase 2 is accepted.

## Goal

Choose the safest efficient export path and correctly handle common real-world phone videos.

## Implement

- Query actual FFmpeg encoders; support available NVENC, QSV, and AMF with tested fallback to libx264.
- Performance options: Automatic, Fast, Balanced, Maximum Compression.
- Deterministic strategy engine: StreamCopy, keyframe-limited SmartTrim, or Reencode; explain the selected strategy in UI.
- Compatibility checker for aspect, resolution, codecs, pixel format, FPS, audio, HDR, and required filtering.
- HDR/10-bit detection. Add tested HDR→SDR BT.709 tone mapping; show user-visible conversion notice.
- Rotation, variable-frame-rate, missing-audio, unusual timestamps, and multiple-stream handling.
- Safe-zone overlays for social UI areas, stored in preset data and disabled by default if data is uncertain.
- Better size/speed estimates from source complexity and selected encoder.
- Hardware failure automatically retries once with safe CPU settings after cleaning partial output.

## Tests

- Strategy decision matrix with positive and negative smart-copy cases.
- Mock encoder capability outputs and fallback paths.
- HDR fixture verifies output tags/range and avoids gross luminance/color errors.
- VFR, rotated, silent, corrupt, and odd-resolution fixtures.
- Confirm stream-copy output preserves packets where applicable and UI never claims frame-perfect cutting.

## Manual acceptance gate

Test CPU plus any available GPU, trim-only compatible input, HDR phone video, VFR screen recording, rotated portrait video, and cancellation/fallback. No washed-out output or silent strategy changes.

Stop after Phase 3 and wait.

---

# Phase 4 — Visually lossless optimizer and release hardening

## Start condition

Only after the user says `Develop Phase 4` and Phase 3 is accepted.

## Goal

Find the smallest practical file meeting a measurable perceptual-quality target, then produce a release-ready build.

## Implement

- Detect whether FFmpeg has `libvmaf`; disable measured optimization cleanly when unavailable.
- Sample short segments near 10%, 30%, 50%, 70%, and 90%; clamp safely for short videos.
- Normalize reference/distorted sample geometry, FPS, color, and timestamps before comparison.
- Binary-search encoder quality within bounded trials; default target VMAF 96 for Visually Lossless.
- Use a conservative aggregate (minimum or low percentile), not only average VMAF.
- Cache analysis by source fingerprint + edit/preset/encoder settings; invalidate correctly.
- Encode the full video once after selection; optionally verify representative output samples.
- Results: achieved/estimated VMAF, file reduction, encoder, strategy, warnings. Never display `100%` or “lossless” for a lossy re-encode.
- Optional batch queue with pause/cancel/retry only after single-job reliability is proven.
- Accessibility, keyboard navigation, localization-ready strings, crash-safe temp cleanup, license notices, packaging, release checklist.
- Benchmark document covering quality, size, speed, CPU/GPU, and limitations on a small diverse fixture set.

## Tests

- Search convergence, bounds, short-video sampling, cache invalidation, unavailable VMAF, failed trial, cancellation.
- Regression suite across talking head, high motion, noise/grain, animation/screen recording, SDR/HDR, landscape/portrait.
- Clean-machine packaged-app smoke test with no development tools installed.

## Final acceptance gate

All tests pass; package installs/launches/uninstalls; core flow works offline; failures are recoverable; benchmark supports product wording; no input is overwritten; temporary files are cleaned.

Stop and provide the final release report. Do not add AI upscaling, cloud encoding, accounts, ads, subtitles, transitions, filters, music, multi-track editing, or auto-reframe unless separately requested.

---

## 5. `PHASE_STATUS.md` template

```md
# Phase Status

Current phase: 1
State: NOT_STARTED | IN_PROGRESS | READY_FOR_USER_TEST | ACCEPTED | BLOCKED

## Scope completed
- [ ] ...

## Automated verification
- Command:
- Result:

## Manual verification for user
1. ...

## Known limitations / blockers
- None

## User acceptance
- Accepted phase: —
- Date: —
```

## 6. Commands from the user

- `Develop Phase 1` — implement only Phase 1 from the current repository state.
- `Fix Phase 1: <problem>` — reproduce and fix only that phase; rerun its checks.
- `Develop Phase 2` — first verify Phase 1 acceptance, then implement only Phase 2.
- `Develop Phase 3` — first verify Phase 2 acceptance, then implement only Phase 3.
- `Develop Phase 4` — first verify Phase 3 acceptance, then implement only Phase 4.
- `Audit current phase` — inspect and report gaps; do not implement unless asked.

If the user reports a defect while testing, treat it as part of the current phase. Fix and reissue the same acceptance checklist. Never advance automatically.
