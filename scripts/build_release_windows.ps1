$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publishDir = Join-Path $repoRoot 'publish\windows'
$bundleDir = Join-Path $repoRoot 'tmp\Hinge-win-bundle'
$nativeOutput = Join-Path $repoRoot 'tmp\Hinge-winui-publish'

Write-Host '=== Building Hinge WinUI 3 Windows Release Bundle ===' -ForegroundColor Cyan

if (Test-Path -LiteralPath $nativeOutput) {
    Remove-Item -LiteralPath $nativeOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $nativeOutput -Force | Out-Null

$projectPath = Join-Path $repoRoot 'windows\Hinge.App\Hinge.App.csproj'
$publishArgs = @(
    'publish',
    $projectPath,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--output', $nativeOutput,
    '-p:WindowsAppSDKSelfContained=true',
    '-p:WindowsPackageType=None',
    '-p:PublishSingleFile=false'
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error 'WinUI 3 Windows 发布失败。请确认已安装 .NET 8 SDK、Windows App SDK 和 Windows 11 SDK。'
    exit $LASTEXITCODE
}

$executablePath = Join-Path $nativeOutput 'Hinge.exe'
if (-not (Test-Path -LiteralPath $executablePath)) {
    Write-Error "WinUI 3 输出中未找到 Hinge.exe：$nativeOutput"
    exit 1
}

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
if (Test-Path -LiteralPath $bundleDir) {
    Remove-Item -LiteralPath $bundleDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null
Copy-Item -Path (Join-Path $nativeOutput '*') -Destination $bundleDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\allow_hinge_firewall.ps1') -Destination $bundleDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\Uninstall-Hinge.ps1') -Destination $bundleDir -Force

$zipPath = Join-Path $publishDir 'Hinge-Windows.zip'
# Put the runnable files at the archive root so extracting the ZIP does not
# require users to guess which nested folder contains the actual application.
Compress-Archive -Path (Join-Path $bundleDir '*') -DestinationPath $zipPath -Force

# Build a certificate-free EXE installer. The small WinForms bootstrapper is
# self-contained and carries the runnable ZIP as an appended payload. It asks
# for the destination folder at install time, so it is not tied to MSIX/AppX
# deployment rules or the system C: drive.
$installerOutput = Join-Path $repoRoot 'tmp\Hinge-setup-publish'
$installerProject = Join-Path $repoRoot 'installer\Hinge.Setup.csproj'
$setupPath = Join-Path $publishDir 'Hinge-Setup.exe'
if (Test-Path -LiteralPath $installerOutput) {
    Remove-Item -LiteralPath $installerOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $installerOutput -Force | Out-Null
$installerArgs = @(
    'publish',
    $installerProject,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--output', $installerOutput,
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)
& dotnet @installerArgs
if ($LASTEXITCODE -ne 0) {
    throw 'EXE 安装程序构建失败。'
}
$installerBinary = Join-Path $installerOutput 'Hinge-Setup.exe'
if (-not (Test-Path -LiteralPath $installerBinary)) {
    throw "未找到 EXE 安装程序输出：$installerBinary"
}
Copy-Item -LiteralPath $installerBinary -Destination $setupPath -Force
$payloadMarker = [System.Text.Encoding]::ASCII.GetBytes('HINGE_PAYLOAD_V1')
$setupStream = [System.IO.File]::Open(
    $setupPath,
    [System.IO.FileMode]::Append,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::Read)
try {
    $zipStream = [System.IO.File]::OpenRead($zipPath)
    try {
        $zipStream.CopyTo($setupStream, 1024 * 1024)
        $setupStream.Write($payloadMarker, 0, $payloadMarker.Length)
        $lengthBytes = [BitConverter]::GetBytes([long]$zipStream.Length)
        $setupStream.Write($lengthBytes, 0, $lengthBytes.Length)
    } finally {
        $zipStream.Dispose()
    }
} finally {
    $setupStream.Dispose()
}

Write-Host "=== WinUI 3 Windows release bundle successful: $publishDir ===" -ForegroundColor Green
Write-Host "EXE installer: $setupPath" -ForegroundColor Green
Write-Host "Portable ZIP: $zipPath" -ForegroundColor Green
