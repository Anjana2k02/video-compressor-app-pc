# Third-party notices

VideoOptimizer bundles and invokes third-party software as separate processes. Their licenses are preserved
in the distribution.

## FFmpeg / FFprobe 8

- Bundled build: Gyan FFmpeg **8.1.2 essentials** (Windows x64), used unmodified as external executables.
- License: **GPLv3** builds. The original `LICENSE` and `README.txt` are shipped in `tools/ffmpeg/`.
- Corresponding source and build information: https://www.gyan.dev/ffmpeg/builds/ and
  https://github.com/GyanD/codexffmpeg/releases/tag/8.1.2
- VideoOptimizer calls FFmpeg/FFprobe only as separate processes; it does not link against FFmpeg libraries.
- Perceptual quality is measured with FFmpeg's `libvmaf` filter (Netflix VMAF), when present in the build.

## .NET / Windows App SDK

- Microsoft .NET 10 and the Windows App SDK (WinUI 3) are used under their respective Microsoft licenses.

Before distributing a release, re-read `tools/ffmpeg/LICENSE` and confirm the corresponding-source obligations
for the exact FFmpeg build you ship. See `RELEASE_CHECKLIST.md`.
