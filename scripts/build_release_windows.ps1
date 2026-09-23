param([switch]$TransferDiagnostics)

$diagnosticBuild = [bool]$TransferDiagnostics
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publishDir = Join-Path $repoRoot $(if ($diagnosticBuild) { 'publish\windows\diagnostics' } else { 'publish\windows' })
$bundleDir = Join-Path $repoRoot $(if ($diagnosticBuild) { 'tmp\Hinge-win-bundle-diagnostics' } else { 'tmp\Hinge-win-bundle' })
$nativeOutput = Join-Path $repoRoot $(if ($diagnosticBuild) { 'tmp\Hinge-winui-publish-diagnostics' } else { 'tmp\Hinge-winui-publish' })
$shellOutput = Join-Path $repoRoot $(if ($diagnosticBuild) { 'tmp\Hinge-shell-publish-diagnostics' } else { 'tmp\Hinge-shell-publish' })
$sparseStage = Join-Path $repoRoot $(if ($diagnosticBuild) { 'tmp\Hinge-sparse-package-diagnostics' } else { 'tmp\Hinge-sparse-package' })

Write-Host '=== Building Hinge WinUI 3 Windows Release Bundle ===' -ForegroundColor Cyan

# The sparse identity package is what makes the Explorer command visible in
# Windows 11's first-level menu. A stale package version can leave an older
# extension registered even when the desktop executable was updated, so fail
# the build instead of producing a mixed bundle.
$versionFiles = @(
    (Join-Path $repoRoot 'windows\Hinge.App\Hinge.App.csproj'),
    (Join-Path $repoRoot 'installer\Hinge.Setup.csproj')
)
$productVersion = $null
foreach ($versionFile in $versionFiles) {
    $versionMatch = [regex]::Match(
        (Get-Content -LiteralPath $versionFile -Raw),
        '<Version>([^<]+)</Version>')
    if (-not $versionMatch.Success) {
        throw "未能从版本文件读取产品版本：$versionFile"
    }

    $fileVersion = $versionMatch.Groups[1].Value.Trim()
    if ($null -eq $productVersion) {
        $productVersion = $fileVersion
    } elseif ($productVersion -ne $fileVersion) {
        throw "Windows 应用和安装器版本不一致：$productVersion / $fileVersion"
    }
}

$sparseManifestPath = Join-Path $repoRoot 'windows\Hinge.SparsePackage\AppxManifest.xml'
$sparseVersion = "${productVersion}.0"
$sparseManifest = Get-Content -LiteralPath $sparseManifestPath -Raw
$sparseVersionPattern = 'Version="' + [regex]::Escape($sparseVersion) + '"'
if ($sparseManifest -notmatch $sparseVersionPattern) {
    throw "稀疏身份包版本与 Windows 产品版本不一致，应为 $sparseVersion：$sparseManifestPath"
}

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
    '-p:WindowsPackageType=None',
    '-p:PublishSingleFile=false'
)
if ($diagnosticBuild) {
    $publishArgs += '-p:HingeTransferDiagnostics=true'
}
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error 'WinUI 3 Windows 发布失败。请确认已安装 .NET 8 SDK、Windows App SDK 和 Windows 11 SDK。'
    exit $LASTEXITCODE
}

if (Test-Path -LiteralPath $shellOutput) {
    Remove-Item -LiteralPath $shellOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $shellOutput -Force | Out-Null
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuild)) {
    throw "未找到 Visual Studio Build Tools：$msbuild"
}
$shellProject = Join-Path $repoRoot 'windows\Hinge.ShellExtension\Hinge.ShellExtension.vcxproj'
& $msbuild $shellProject /p:Configuration=Release /p:Platform=x64 "/p:OutDir=$shellOutput\" /m
if ($LASTEXITCODE -ne 0) {
    throw 'Windows 11 右键菜单原生组件构建失败。'
}
$shellDll = Join-Path $shellOutput 'Hinge.ShellExtension.dll'
if (-not (Test-Path -LiteralPath $shellDll)) {
    throw "未找到 Windows 11 右键菜单组件：$shellDll"
}

# Do not reuse a fixed DLL filename across releases. Explorer's COM surrogate
# can keep the previous shell extension loaded while the installer is
# replacing the application directory. A release-specific filename lets the
# new package be installed without trying to overwrite that locked DLL.
$versionedShellName = "Hinge.ShellExtension.v$productVersion.dll"

$sdkBin = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64'
$makeAppx = Join-Path $sdkBin 'makeappx.exe'
$signTool = Join-Path $sdkBin 'signtool.exe'
if (-not (Test-Path -LiteralPath $makeAppx) -or -not (Test-Path -LiteralPath $signTool)) {
    throw "未找到 Windows SDK 打包工具：$sdkBin"
}

$executablePath = Join-Path $nativeOutput 'Hinge.exe'
if (-not (Test-Path -LiteralPath $executablePath)) {
    Write-Error "WinUI 3 输出中未找到 Hinge.exe：$nativeOutput"
    exit 1
}

$artifactSuffix = $(if ($diagnosticBuild) { '-AB-send-timing' } else { '' })
if (Test-Path -LiteralPath $publishDir) {
    try {
        Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction Stop
    }
    catch {
        # A previously launched installer can keep its own EXE open while a
        # new release is being built. Keep the old artifact intact and emit a
        # versioned pair beside it instead of aborting the build.
        $artifactSuffix = "-v$productVersion"
        Write-Warning "旧发布文件仍被占用，将生成版本化文件名：$artifactSuffix"
    }
}
if (Test-Path -LiteralPath $bundleDir) {
    Remove-Item -LiteralPath $bundleDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null
Copy-Item -Path (Join-Path $nativeOutput '*') -Destination $bundleDir -Recurse -Force
Copy-Item -LiteralPath $shellDll -Destination (Join-Path $bundleDir $versionedShellName) -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\allow_hinge_firewall.ps1') -Destination $bundleDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\Uninstall-Hinge.ps1') -Destination $bundleDir -Force

# Windows 11's first-level context menu requires package identity even for an
# unpackaged Win32 app. The sparse identity remains an internal installer
# component; the public deliverables stay EXE + portable ZIP.
if (Test-Path -LiteralPath $sparseStage) {
    Remove-Item -LiteralPath $sparseStage -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $sparseStage 'Assets') -Force | Out-Null
$stagedManifestPath = Join-Path $sparseStage 'AppxManifest.xml'
$stagedManifest = $sparseManifest.Replace(
    'Path="Hinge.ShellExtension.dll"',
    ('Path="' + $versionedShellName + '"'))
if ($stagedManifest -eq $sparseManifest) {
    throw "稀疏身份包清单未找到 Shell 扩展 DLL 路径：$sparseManifestPath"
}
[System.IO.File]::WriteAllText(
    $stagedManifestPath,
    $stagedManifest,
    [System.Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath $shellDll -Destination (Join-Path $sparseStage $versionedShellName) -Force

Add-Type -AssemblyName System.Drawing.Common

function Write-PackageLogo {
    param(
        [Parameter(Mandatory = $true)] [string] $Source,
        [Parameter(Mandatory = $true)] [string] $Destination,
        [Parameter(Mandatory = $true)] [int] $Size
    )

    $sourceImage = [System.Drawing.Image]::FromFile($Source)
    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($sourceImage, 0, 0, $Size, $Size)
        $bitmap.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
        $sourceImage.Dispose()
    }
}

$packageIconSource = Join-Path $repoRoot 'windows\Hinge.App\Assets\app_icon.png'
$packageAssets = Join-Path $sparseStage 'Assets'
Write-PackageLogo -Source $packageIconSource -Destination (Join-Path $packageAssets 'StoreLogo.png') -Size 50
Write-PackageLogo -Source $packageIconSource -Destination (Join-Path $packageAssets 'Square44x44Logo.png') -Size 44
Write-PackageLogo -Source $packageIconSource -Destination (Join-Path $packageAssets 'Square150x150Logo.png') -Size 150

foreach ($targetSize in @(16, 20, 24, 32, 40, 48, 64, 256)) {
    Write-PackageLogo `
        -Source $packageIconSource `
        -Destination (Join-Path $packageAssets "Square44x44Logo.targetsize-$targetSize.png") `
        -Size $targetSize
    Write-PackageLogo `
        -Source $packageIconSource `
        -Destination (Join-Path $packageAssets "Square44x44Logo.targetsize-$($targetSize)_altform-unplated.png") `
        -Size $targetSize
}

$certificateSubject = 'CN=Hinge Package Identity'
$signingCertificate = Get-ChildItem -Path Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $certificateSubject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddMonths(3) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1
if ($null -eq $signingCertificate) {
    $signingCertificate = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $certificateSubject `
        -FriendlyName 'Hinge sparse package signing' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyAlgorithm RSA `
        -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3') `
        -NotAfter (Get-Date).AddYears(10)
}
$publicCertificate = Join-Path $bundleDir 'Hinge.Identity.cer'
Export-Certificate -Cert $signingCertificate -FilePath $publicCertificate -Force | Out-Null

# Sign the in-process shell extension with the same certificate used by the
# sparse identity package. This does not claim public CA trust, but it avoids
# shipping an unsigned COM DLL when the installer has already established the
# local Hinge package certificate as trusted.
& $signTool sign /fd SHA256 /sha1 $signingCertificate.Thumbprint /s My $shellDll
if ($LASTEXITCODE -ne 0) {
    throw 'Windows 11 右键菜单组件签名失败。'
}
Copy-Item -LiteralPath $shellDll -Destination (Join-Path $bundleDir $versionedShellName) -Force
Copy-Item -LiteralPath $shellDll -Destination (Join-Path $sparseStage $versionedShellName) -Force

$identityPackage = Join-Path $bundleDir 'Hinge.Identity.msix'
& $makeAppx pack /d $sparseStage /p $identityPackage /nv /o
if ($LASTEXITCODE -ne 0) {
    throw 'Hinge 稀疏身份包构建失败。'
}
& $signTool sign /fd SHA256 /sha1 $signingCertificate.Thumbprint /s My $identityPackage
if ($LASTEXITCODE -ne 0) {
    throw 'Hinge 稀疏身份包签名失败。'
}

$zipPath = Join-Path $publishDir ("Hinge-Windows$artifactSuffix.zip")
# Put the runnable files at the archive root so extracting the ZIP does not
# require users to guess which nested folder contains the actual application.
Compress-Archive -Path (Join-Path $bundleDir '*') -DestinationPath $zipPath -Force

# Build a certificate-free EXE installer. The small WinForms bootstrapper is
# self-contained and carries the runnable ZIP as an appended payload. It asks
# for the destination folder at install time, so it is not tied to MSIX/AppX
# deployment rules or the system C: drive.
$installerOutput = Join-Path $repoRoot $(if ($diagnosticBuild) { 'tmp\Hinge-setup-publish-diagnostics' } else { 'tmp\Hinge-setup-publish' })
$installerProject = Join-Path $repoRoot 'installer\Hinge.Setup.csproj'
$setupPath = Join-Path $publishDir ("Hinge-Setup$artifactSuffix.exe")
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
    '-p:IncludeNativeLibrariesForSelfExtract=false',
    '-p:EnableCompressionInSingleFile=true',
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
