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

$makeAppx = Get-ChildItem -LiteralPath 'C:\Program Files (x86)\Windows Kits\10\bin' -Filter 'makeappx.exe' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } | Sort-Object FullName -Descending | Select-Object -First 1
$signTool = Get-ChildItem -LiteralPath 'C:\Program Files (x86)\Windows Kits\10\bin' -Filter 'signtool.exe' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } | Sort-Object FullName -Descending | Select-Object -First 1

if ($null -eq $makeAppx -or $null -eq $signTool) {
    Write-Warning 'Windows SDK makeappx/signtool 未找到，跳过 MSIX；EXE 安装包和便携包仍已生成。'
} else {

$msixStage = Join-Path $repoRoot 'tmp\Hinge-msix'
$msixPath = Join-Path $publishDir 'Hinge-Installer.msix'
$certificatePath = Join-Path $publishDir 'Hinge-Installer.cer'
if (Test-Path -LiteralPath $msixStage) {
    Remove-Item -LiteralPath $msixStage -Recurse -Force
}
New-Item -ItemType Directory -Path $msixStage -Force | Out-Null
Copy-Item -Path (Join-Path $bundleDir '*') -Destination $msixStage -Recurse -Force

$assetDir = Join-Path $msixStage 'Assets'
New-Item -ItemType Directory -Path $assetDir -Force | Out-Null
$iconPath = Join-Path $repoRoot 'windows\Hinge.App\Assets\app_icon.png'
foreach ($assetName in @('StoreLogo.png', 'Square44x44Logo.png', 'Square150x150Logo.png')) {
    Copy-Item -LiteralPath $iconPath -Destination (Join-Path $assetDir $assetName) -Force
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'windows\Hinge.AppxManifest.xml') -Destination (Join-Path $msixStage 'AppxManifest.xml') -Force

$manifestPath = Join-Path $msixStage 'AppxManifest.xml'
$manifestText = [System.IO.File]::ReadAllText($manifestPath)
$versionMatch = [regex]::Match($manifestText, 'Version="([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)"')
if ($versionMatch.Success) {
    $packageVersion = [version]$versionMatch.Groups[1].Value
    $installedVersion = $null
    try {
        $installedVersionText = Get-AppxPackage -AllUsers -Name 'Hinge' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Version
        if (-not [string]::IsNullOrWhiteSpace($installedVersionText)) {
            $installedVersion = [version]$installedVersionText
        }
    } catch {
        $installedVersion = $null
    }
    if ($null -ne $installedVersion -and $packageVersion -le $installedVersion) {
        $nextRevision = $installedVersion.Revision + 1
        if ($nextRevision -gt 65535) {
            throw "Hinge 的 AppX 修订版本号已达到上限：$installedVersion"
        }
        $nextVersion = [version]::new($installedVersion.Major, $installedVersion.Minor, $installedVersion.Build, $nextRevision)
        $manifestText = $manifestText.Replace($versionMatch.Groups[0].Value, ('Version="{0}"' -f $nextVersion))
        [System.IO.File]::WriteAllText($manifestPath, $manifestText, [System.Text.UTF8Encoding]::new($false))
        Write-Host "Detected installed AppX $installedVersion; using package version $nextVersion." -ForegroundColor Yellow
    }
}

& $makeAppx.FullName pack /d $msixStage /p $msixPath /o
if ($LASTEXITCODE -ne 0) {
    Write-Error 'MSIX packaging failed.'
    exit $LASTEXITCODE
}

$certificate = Get-ChildItem -Path 'Cert:\CurrentUser\My' -ErrorAction SilentlyContinue | Where-Object {
    if ($_.Subject -ne 'CN=Hinge Test' -or -not $_.HasPrivateKey -or $_.NotAfter -le (Get-Date)) {
        return $false
    }
    $basicConstraints = $_.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' } | Select-Object -First 1
    return $null -eq $basicConstraints -or -not $basicConstraints.CertificateAuthority
} | Sort-Object NotAfter -Descending | Select-Object -First 1
if ($null -eq $certificate) {
    $certificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=Hinge Test' -CertStoreLocation 'Cert:\CurrentUser\My' -HashAlgorithm SHA256 -KeyAlgorithm RSA -KeyLength 2048 -KeyUsage DigitalSignature -TextExtension '2.5.29.37={text}1.3.6.1.5.5.7.3.3' -NotAfter (Get-Date).AddYears(3)
}
Export-Certificate -Cert $certificate -FilePath $certificatePath -Type CERT | Out-Null
& $signTool.FullName sign /fd SHA256 /sha1 $certificate.Thumbprint $msixPath
if ($LASTEXITCODE -ne 0) {
    Write-Error 'MSIX signing failed.'
    exit $LASTEXITCODE
}
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts\Install-Hinge.ps1') -Destination (Join-Path $publishDir 'Install-Hinge.ps1') -Force

Write-Host "=== WinUI 3 Windows release bundle successful: $publishDir ===" -ForegroundColor Green
Write-Host "EXE installer: $setupPath" -ForegroundColor Green
Write-Host "MSIX installer: $msixPath" -ForegroundColor Green
Write-Host "Installer certificate: $certificatePath" -ForegroundColor Yellow
