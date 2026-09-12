# Produces a self-contained, framework-independent build of VideoOptimizer in dist/VideoOptimizer.
# Run from the repository root:  powershell -NoProfile -ExecutionPolicy Bypass -File tools/publish.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/VideoOptimizer.App/VideoOptimizer.App.csproj'
$out = Join-Path $root 'dist/VideoOptimizer'

if (-not (Test-Path (Join-Path $root 'tools/ffmpeg/ffmpeg.exe'))) {
    throw "FFmpeg is not installed. Run tools/Install-Ffmpeg.ps1 first."
}

if (Test-Path $out) { Remove-Item -Recurse -Force $out }

Write-Host "Publishing self-contained win-x64 build..."
dotnet publish $project -c Release -r win-x64 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# The csproj copies tools/ffmpeg and presets to the output; verify.
foreach ($required in 'VideoOptimizer.App.exe', 'tools/ffmpeg/ffmpeg.exe', 'tools/ffmpeg/ffprobe.exe', 'presets/instagram-story.json') {
    if (-not (Test-Path (Join-Path $out $required))) { throw "Missing from publish output: $required" }
}

Copy-Item (Join-Path $root 'NOTICE.md') $out -Force
Write-Host "Done. Self-contained build is in: $out"
