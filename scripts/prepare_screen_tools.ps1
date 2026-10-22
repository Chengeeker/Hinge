$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$cache = Join-Path $repoRoot 'tmp\screen-tool-cache'
$destination = Join-Path $repoRoot 'windows\Hinge.App\tools\scrcpy'
New-Item -ItemType Directory -Path $cache, $destination -Force | Out-Null

function Get-VerifiedAsset($name, $url, $sha256) {
    $path = Join-Path $cache $name
    if (-not (Test-Path -LiteralPath $path)) {
        $localArchive = Join-Path $repoRoot "publish\tools\$name"
        if (Test-Path -LiteralPath $localArchive) {
            Copy-Item -LiteralPath $localArchive -Destination $path
        } else {
            Invoke-WebRequest -Uri $url -OutFile "$path.download" -UseBasicParsing
            if ((Get-FileHash -LiteralPath "$path.download" -Algorithm SHA256).Hash -ne $sha256) {
                throw "SHA-256 mismatch: $name"
            }
            Move-Item -LiteralPath "$path.download" -Destination $path -Force
        }
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $sha256) {
        throw "SHA-256 mismatch in screen tool cache: $name"
    }
    return $path
}

$archive = Get-VerifiedAsset 'scrcpy-win64-v5.0.1.zip' `
    'https://github.com/Genymobile/scrcpy/releases/download/v5.0.1/scrcpy-win64-v5.0.1.zip' `
    'b12a2c4ee8be317422451fc7dcf8ee20a71b5ea7ef9ad73ddd825a25316227a5'
$files = @('scrcpy.exe', 'scrcpy-server', 'adb.exe', 'AdbWinApi.dll', 'AdbWinUsbApi.dll',
    'SDL3.dll', 'avcodec-63.dll', 'avformat-63.dll', 'avutil-61.dll', 'swresample-7.dll',
    'libusb-1.0.dll', 'scrcpy.png', 'disconnected.png', 'LICENSE.txt')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($name in $files) {
        $entry = $zip.GetEntry("scrcpy-win64-v5.0.1/$name")
        if ($null -eq $entry -or $entry.Length -eq 0) { throw "Missing screen runtime component: $name" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $destination $name), $true)
    }
} finally { $zip.Dispose() }

# Keep LGPL corresponding sources and all notices beside the unmodified shared libraries.
$sources = Join-Path $destination 'sources'
New-Item -ItemType Directory -Path $sources -Force | Out-Null
$ffmpeg = Get-VerifiedAsset 'ffmpeg-9.0.2.tar.xz' 'https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz' `
    '8c3850283eb25fa026482078a04051e0be17347b09ef81a0849bec15a96e002e'
$libusb = Get-VerifiedAsset 'libusb-1.0.30.tar.gz' 'https://github.com/libusb/libusb/archive/refs/tags/v1.0.30.tar.gz' `
    '2ae28adb0bb9558c86135c4e1c11b320b0805461e207a64a6e520a114094bf07'
$dav1d = Get-VerifiedAsset 'dav1d-1.5.4.tar.xz' 'https://downloads.videolan.org/pub/videolan/dav1d/1.5.4/dav1d-1.5.4.tar.xz' `
    '686616b7c69eb88d44459391ab25cac13b6647a3b288835c5784e71c1514a5c5'
Copy-Item -LiteralPath $ffmpeg, $libusb, $dav1d -Destination $sources -Force
$licenses = Join-Path $destination 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'screen-tool-licenses\*') -Destination $licenses -Force
$lgpl = & tar -xOf $ffmpeg 'ffmpeg-9.0.2/COPYING.LGPLv2.1'
if ($LASTEXITCODE -ne 0 -or -not $lgpl) { throw 'Failed to extract FFmpeg LGPL license' }
$lgpl | Set-Content -LiteralPath (Join-Path $licenses 'LGPL-2.1.txt') -Encoding utf8
$platformTools = Get-VerifiedAsset 'platform-tools_r37.0.1-win.zip' `
    'https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip' `
    '45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99'
$zip = [IO.Compression.ZipFile]::OpenRead($platformTools)
try {
    $notice = $zip.GetEntry('platform-tools/NOTICE.txt')
    if ($null -eq $notice) { throw 'Missing Android Platform-Tools notices' }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($notice, (Join-Path $licenses 'ADB-NOTICE.txt'), $true)
} finally { $zip.Dispose() }
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_LICENSES.md') -Destination $licenses -Force
Write-Host 'Bundled screen runtime ready: scrcpy 5.0.1 / ADB 37.0.1 (Windows x64).'
