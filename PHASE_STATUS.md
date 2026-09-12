# Phase Status

Current phase: 4
State: READY_FOR_USER_TEST

## Scope completed
- [x] Detect `libvmaf`; measured optimization is disabled cleanly (with a note) when it is unavailable.
- [x] Sample short segments near 10/30/50/70/90%, clamped and de-duplicated for short videos.
- [x] Normalized reference/candidate samples (same framing filter, FPS, colour) before comparison; near-lossless reference.
- [x] Bounded binary search over CRF; default target VMAF 96 for Visually Lossless.
- [x] Conservative aggregate (worst sampled segment), not only the average.
- [x] Analysis cache keyed by source fingerprint + preset + framing + target + trim + crop; correct invalidation.
- [x] One full encode after the CRF is chosen; results report achieved VMAF, reduction, encoder, and strategy.
- [x] Never displays "100%" or "lossless" for a lossy re-encode.
- [x] Release hardening: license notice (NOTICE.md + FFmpeg LICENSE), benchmark doc, release checklist, self-contained publish script, crash-safe temp cleanup, in-app licence footer, accessible/keyboard-navigable controls.

## Automated verification
- Command: `dotnet build VideoOptimizer.slnx`
- Result: Build succeeded, 0 warnings, 0 errors.
- Command: `dotnet test tests/VideoOptimizer.Tests/VideoOptimizer.Tests.csproj`
- Result: Passed — 167 passed, 0 failed, 0 skipped (13 new for Phase 4).
- Covered: sample-window bounds/short-video clamping, CRF search convergence + unreachable-target + trial-budget, conservative aggregate, cache store/invalidate, VMAF JSON parsing, the libvmaf-unavailable fallback, and a real measured export (sample encodes → VMAF → CRF search → one full encode) that reports an achieved VMAF and encodes the whole clip.

## Manual verification for user
1. `dotnet run --project src/VideoOptimizer.App/VideoOptimizer.App.csproj`.
2. Import a clip, tick "Find the smallest visually-lossless size (measured VMAF)", and export; confirm it analyses then encodes and shows an estimated VMAF (never "100%"/"lossless").
3. Compare the measured output size against a plain Recommended export of the same clip.
4. Repeat across diverse clips (talking head, high motion, screen recording, SDR/HDR, landscape/portrait) per BENCHMARK.md.
5. Package with `tools/publish.ps1`, then run the clean-machine smoke test in RELEASE_CHECKLIST.md (offline, no dev tools).
6. Confirm no input is overwritten and cancelling leaves no partial files.

## Known limitations / blockers
- Measured mode is slower (it encodes short samples first) and uses libx264 (CPU) for comparable, predictable quality.
- Batch queue and MSIX/Store packaging are intentionally deferred (see RELEASE_CHECKLIST.md); this release ships as a self-contained folder.
- Safe-zone overlays remain disabled by default (no verified per-platform data), as specified.
- BENCHMARK.md ships with the method + reproduction commands; the results table must be regenerated on real hardware with a real fixture set.

## User acceptance
- Accepted phase: 3
- Date: 2026-09-12
