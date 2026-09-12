# VideoOptimizer benchmark

This document defines how VideoOptimizer's quality, size, and speed are measured, so the "Visually Lossless"
wording is defensible. Numbers are hardware- and content-dependent — regenerate them on your own machine and
fixture set before quoting them in marketing.

## What "Visually Lossless" means here

- Target: **VMAF ≈ 96** on a conservative aggregate (the worst sampled segment, not the average).
- VMAF (Netflix's Video Multimethod Assessment Fusion) approximates human-perceived quality on a 0–100 scale.
  ~96 is widely used as a "transparent / visually lossless" threshold for H.264 delivery.
- The app never claims "100%", "lossless", or "identical to the original" for a lossy re-encode. Results say
  *"Estimated VMAF N — visually lossless target met (not identical to the original)."*

## Method

1. Build a small, diverse fixture set (each 10–30 s), one clip per category:
   - Talking head (low motion), high motion (sport/action), noise/grain (low light),
     animation / screen recording (flat areas + text), SDR and HDR, landscape and portrait.
2. For each clip and destination preset, export at **Recommended** and at **Measured (VMAF 96)**.
3. Record: source size, output size, reduction %, achieved VMAF (measured mode), encoder, strategy, wall-clock
   time, and CPU vs GPU.

## Reproduce

```powershell
# 1. quality of a single encode vs the source (video-only reference at the output geometry)
$ff = "tools/ffmpeg/ffmpeg.exe"
& $ff -y -i source.mp4 -vf "scale=1080:1920:flags=lanczos,setsar=1" -c:v libx264 -crf 0 ref.mp4
& $ff -y -i source.mp4 -vf "scale=1080:1920:flags=lanczos,setsar=1" -c:v libx264 -crf 20 dist.mp4
# 2. measure (run from the folder holding the files so the log path is relative)
& $ff -i dist.mp4 -i ref.mp4 -lavfi "[0:v][1:v]libvmaf=log_fmt=json:log_path=vmaf.json" -f null -
```

The app performs the same reference/candidate comparison automatically over five sampled windows
(≈10/30/50/70/90% of the clip) and binary-searches the CRF.

## Results (example — regenerate per machine)

| Category | Preset | Mode | Reduction | Achieved VMAF | Encoder | Strategy |
|----------|--------|------|-----------|---------------|---------|----------|
| Talking head | Instagram | Measured | _fill_ | _~96_ | libx264 | Re-encode |
| High motion | TikTok | Recommended | _fill_ | — | libx264/NVENC | Re-encode |
| Screen recording | WhatsApp | Measured | _fill_ | _~96_ | libx264 | Re-encode |
| Already-compliant H.264 | Instagram | Recommended | ~0% | — | (none) | Stream copy |

## Limitations

- VMAF is a model, not ground truth; extreme grain/animation can score differently from subjective opinion.
- Measured mode encodes sample segments first, so it is slower than a fixed-quality export.
- Hardware encoders (NVENC/QSV/AMF) trade some compression efficiency for speed; measured mode uses libx264
  (CPU) for predictable, comparable quality.
- Synthetic sources (e.g. `testsrc2`) are not representative of camera footage; always benchmark real clips.
