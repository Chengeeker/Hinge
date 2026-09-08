param(
    [string]$KeystorePath = $env:HINGE_KEYSTORE_PATH,
    [string]$KeyAlias = $env:HINGE_KEY_ALIAS,
    [string]$StorePassword = $env:HINGE_KEYSTORE_PASSWORD,
    [string]$KeyPassword = $env:HINGE_KEY_PASSWORD,
    [switch]$SkipFlutterBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# Build Android Release APK
Write-Host "=== Building Hinge Android Release APK ===" -ForegroundColor Cyan

$apkPath = Join-Path $repoRoot 'android/build/app/outputs/flutter-apk/app-release.apk'
if ($SkipFlutterBuild -and -not (Test-Path -LiteralPath $apkPath)) {
    $apkPath = Join-Path $repoRoot 'publish/android/Hinge.apk'
}
if (-not $SkipFlutterBuild) {
    Push-Location (Join-Path $repoRoot 'android')
    & "D:\flutter_sdk\bin\flutter.bat" build apk --release --target-platform android-arm64
    $flutterExit = $LASTEXITCODE
    Pop-Location

    if ($flutterExit -ne 0) {
        Write-Error "Android Release build failed!"
        exit $flutterExit
    }
}

if (-not (Test-Path -LiteralPath $apkPath)) {
    Write-Error "Android release APK 未找到：$apkPath"
    exit 1
}

# Flutter currently produces the intermediate APK with the project's default
# signing configuration. Re-sign it with the user's stable release key so
# future updates keep the same Android certificate.
$androidSdkRoot = if ($env:ANDROID_HOME) {
    $env:ANDROID_HOME
} elseif ($env:ANDROID_SDK_ROOT) {
    $env:ANDROID_SDK_ROOT
} else {
    Join-Path $env:LOCALAPPDATA 'Android\Sdk'
}
$apksigner = Get-ChildItem -LiteralPath (Join-Path $androidSdkRoot 'build-tools') -Filter 'apksigner.bat' -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
$keytool = Join-Path ($env:JAVA_HOME ?? 'D:\jdk17') 'bin\keytool.exe'
if ($null -eq $apksigner -or -not (Test-Path -LiteralPath $keytool)) {
    Write-Error '未找到 Android apksigner 或 JDK keytool，无法生成 release 签名 APK。'
    exit 1
}

$signingDirectory = Join-Path $repoRoot 'tmp'
New-Item -ItemType Directory -Path $signingDirectory -Force | Out-Null
if ([string]::IsNullOrWhiteSpace($KeystorePath)) {
    Write-Error '未提供 Android 签名文件，请通过 -KeystorePath 或 HINGE_KEYSTORE_PATH 指定。'
    exit 1
}
if (-not (Test-Path -LiteralPath $KeystorePath -PathType Leaf)) {
    Write-Error '找不到 Android 签名文件，请检查 -KeystorePath 或 HINGE_KEYSTORE_PATH。'
    exit 1
}
if ([string]::IsNullOrWhiteSpace($KeyAlias)) {
    Write-Error '未提供 Android 签名别名，请通过 -KeyAlias 或 HINGE_KEY_ALIAS 指定。'
    exit 1
}
if ([string]::IsNullOrWhiteSpace($StorePassword)) {
    Write-Error '未提供 Android 签名库密码，请设置 HINGE_KEYSTORE_PASSWORD。'
    exit 1
}
if ([string]::IsNullOrWhiteSpace($KeyPassword)) {
    $KeyPassword = $StorePassword
}

Write-Host "Validating Android signing key: $KeystorePath ($KeyAlias)"
$bcprovRoot = Join-Path $env:USERPROFILE '.gradle\caches\modules-2\files-2.1\org.bouncycastle'
$bcprov = Get-ChildItem -LiteralPath $bcprovRoot -Recurse -File -Filter 'bcprov-jdk18on-*.jar' -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -eq $bcprov) {
    Write-Error '未找到 Bouncy Castle provider，无法读取 BKS 签名库。'
    exit 1
}

& $keytool -list -storetype BKS -providerclass org.bouncycastle.jce.provider.BouncyCastleProvider `
    -providerpath $bcprov.FullName -keystore $KeystorePath -storepass $StorePassword -alias $KeyAlias | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error 'Android 签名库验证失败，请检查签名文件、别名或密码。'
    exit $LASTEXITCODE
}

$signedApkPath = Join-Path $signingDirectory 'Hinge-release-signed.apk'
$convertedKeystore = Join-Path ([System.IO.Path]::GetTempPath()) "Hinge-$([guid]::NewGuid().ToString('N')).p12"
try {
    # apksigner does not bundle a BKS provider. Convert only this temporary
    # copy to PKCS12; the certificate and private key remain unchanged.
    & $keytool -importkeystore -noprompt `
        -srckeystore $KeystorePath -srcstoretype BKS -srcalias $KeyAlias `
        -srcstorepass $StorePassword -srckeypass $KeyPassword `
        -destkeystore $convertedKeystore -deststoretype PKCS12 -destalias $KeyAlias `
        -deststorepass $StorePassword -destkeypass $KeyPassword `
        -providerclass org.bouncycastle.jce.provider.BouncyCastleProvider `
        -providerpath $bcprov.FullName | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw '无法将 BKS 签名库转换为临时 PKCS12 签名库。'
    }

    if (Test-Path -LiteralPath $signedApkPath) {
        Remove-Item -LiteralPath $signedApkPath -Force
    }
    & $apksigner.FullName sign --ks $convertedKeystore --ks-type PKCS12 `
        --ks-key-alias $KeyAlias --ks-pass "pass:$StorePassword" `
        --key-pass "pass:$KeyPassword" --out $signedApkPath $apkPath
    if ($LASTEXITCODE -ne 0) {
        throw 'Android release APK 签名失败。'
    }
}
finally {
    if (Test-Path -LiteralPath $convertedKeystore) {
        Remove-Item -LiteralPath $convertedKeystore -Force -ErrorAction SilentlyContinue
    }
}

$publishDir = Join-Path $repoRoot 'publish/android'
if (-not (Test-Path $publishDir)) {
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
}
Copy-Item -Path $signedApkPath -Destination "$publishDir/Hinge.apk" -Force

Write-Host "=== Android Release Build Successful! Artifact copied to $publishDir/Hinge.apk ===" -ForegroundColor Green
