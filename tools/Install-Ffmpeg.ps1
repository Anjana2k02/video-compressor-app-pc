$ErrorActionPreference = 'Stop'
$version = '8.1.2'
$expectedHash = 'db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec'
$archiveDirectory = Join-Path $PSScriptRoot 'downloads'
$destination = Join-Path $PSScriptRoot 'ffmpeg'
$archivePath = Join-Path $archiveDirectory "ffmpeg-$version-essentials_build.zip"
$null = New-Item -ItemType Directory -Force -Path $archiveDirectory, $destination
if (!(Test-Path -LiteralPath $archivePath) -or (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) {
    & curl.exe --location --fail --retry 2 --output $archivePath "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-$version-essentials_build.zip"
    if ($LASTEXITCODE -ne 0) { throw 'Download failed. Retry this script when connected.' }
}
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Checksum mismatch. No tools were installed.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($name in @('ffmpeg.exe', 'ffprobe.exe', 'LICENSE', 'README.txt')) {
        $entry = $archive.Entries | Where-Object { $_.Name -eq $name } | Select-Object -First 1
        if ($null -eq $entry) { throw "Archive is missing $name" }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $destination $name), $true)
    }
} finally { $archive.Dispose() }
& (Join-Path $destination 'ffmpeg.exe') -version | Select-Object -First 1
& (Join-Path $destination 'ffprobe.exe') -version | Select-Object -First 1
Write-Host 'Verified FFmpeg tools installed. Rebuild the app to copy them to its output folder.'
