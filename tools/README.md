# Bundled media tools

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Install-Ffmpeg.ps1` once before building. The script downloads the pinned Gyan FFmpeg **8.1.2 essentials** Windows x64 archive, verifies SHA-256, and extracts FFmpeg, FFprobe, the original license, and build README to `tools/ffmpeg/`. The app copies this folder alongside its executable. It never searches PATH or downloads tools at runtime.

Archive: https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip

SHA-256: `db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec`

Publisher and corresponding source links: https://www.gyan.dev/ffmpeg/builds/ and https://github.com/GyanD/codexffmpeg/releases/tag/8.1.2

These third-party executables are GPLv3 builds, invoked as separate processes. Preserve the original `LICENSE` and `README.txt` notices. Consult the publisher's corresponding source/build information before release distribution. Release packaging and its licensing checklist belong to Phase 4. Downloads and executables are ignored by Git; a fresh checkout needs this setup step. The development app operates offline afterward.
