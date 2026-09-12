param(
    [string]$KeystorePath = '',
    [string]$KeyAlias = '',
    [string]$StorePassword = '',
    [string]$KeyPassword = '',
    [switch]$SkipFlutterBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$preferredKeystorePath = 'D:\Download\backup\infinitycm.bks'
$preferredJavaHome = 'D:\jdk17'
$gradleRoot = Join-Path $repoRoot 'android\android'
$keyPropertiesPath = Join-Path $gradleRoot 'key.properties'

# Match the local, ignored key.properties convention used by the Review
# project. Values are kept in memory only; never print passwords or write them
# to the repository. Environment variables/parameters remain available for
# CI and for machines that intentionally do not keep a local config file.
$signingProperties = @{}
if (Test-Path -LiteralPath $keyPropertiesPath -PathType Leaf) {
    foreach ($line in [System.IO.File]::ReadAllLines($keyPropertiesPath)) {
        $trimmedLine = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmedLine) -or $trimmedLine.StartsWith('#')) {
            continue
        }
        $separator = $trimmedLine.IndexOf('=')
        if ($separator -le 0) {
            continue
        }
        $propertyName = $trimmedLine.Substring(0, $separator).Trim()
        $propertyValue = $trimmedLine.Substring($separator + 1).Trim()
        $signingProperties[$propertyName] = $propertyValue
    }
}

if ([string]::IsNullOrWhiteSpace($KeystorePath) -and $signingProperties.ContainsKey('storeFile')) {
    $configuredStoreFile = $signingProperties['storeFile']
    $KeystorePath = if ([System.IO.Path]::IsPathRooted($configuredStoreFile)) {
        $configuredStoreFile
    } else {
        Join-Path $gradleRoot $configuredStoreFile
    }
}
if ([string]::IsNullOrWhiteSpace($KeyAlias) -and $signingProperties.ContainsKey('keyAlias')) {
    $KeyAlias = $signingProperties['keyAlias']
}
if ([string]::IsNullOrWhiteSpace($StorePassword) -and $signingProperties.ContainsKey('storePassword')) {
    $StorePassword = $signingProperties['storePassword']
}
if ([string]::IsNullOrWhiteSpace($KeyPassword) -and $signingProperties.ContainsKey('keyPassword')) {
    $KeyPassword = $signingProperties['keyPassword']
}

# Explicit parameters win, then the local Review-style file, then the legacy
# environment-variable fallback used by CI jobs.
if ([string]::IsNullOrWhiteSpace($KeystorePath) -and
    -not [string]::IsNullOrWhiteSpace($env:HINGE_KEYSTORE_PATH)) {
    $KeystorePath = $env:HINGE_KEYSTORE_PATH
}
if ([string]::IsNullOrWhiteSpace($KeyAlias) -and
    -not [string]::IsNullOrWhiteSpace($env:HINGE_KEY_ALIAS)) {
    $KeyAlias = $env:HINGE_KEY_ALIAS
}
if ([string]::IsNullOrWhiteSpace($StorePassword) -and
    -not [string]::IsNullOrWhiteSpace($env:HINGE_KEYSTORE_PASSWORD)) {
    $StorePassword = $env:HINGE_KEYSTORE_PASSWORD
}
if ([string]::IsNullOrWhiteSpace($KeyPassword) -and
    -not [string]::IsNullOrWhiteSpace($env:HINGE_KEY_PASSWORD)) {
    $KeyPassword = $env:HINGE_KEY_PASSWORD
}
$sourceStoreType = if ($signingProperties.ContainsKey('storeType') -and
    -not [string]::IsNullOrWhiteSpace($signingProperties['storeType'])) {
    $signingProperties['storeType']
} else {
    'BKS'
}

# Flutter/Gradle must not inherit the machine-wide Java 26 shim. Prefer the
# verified local JDK 17, then fall back to a valid caller-provided JAVA_HOME.
$androidJavaHome = if (Test-Path -LiteralPath (Join-Path $preferredJavaHome 'bin\java.exe') -PathType Leaf) {
    $preferredJavaHome
} elseif (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME) -and
    (Test-Path -LiteralPath (Join-Path $env:JAVA_HOME 'bin\java.exe') -PathType Leaf)) {
    $env:JAVA_HOME
} else {
    $null
}
if ($null -eq $androidJavaHome) {
    throw '未找到可用的 JDK 17。请安装 JDK 17，或通过 JAVA_HOME 指定包含 bin\java.exe 的 JDK。'
}

# Use the local key.properties storeFile when it is present. Otherwise use the
# developer's fixed release key, with HINGE_KEYSTORE_PATH as the portable
# fallback for another machine.
if ([string]::IsNullOrWhiteSpace($KeystorePath)) {
    if (Test-Path -LiteralPath $preferredKeystorePath -PathType Leaf) {
        $KeystorePath = $preferredKeystorePath
    } else {
        $KeystorePath = $env:HINGE_KEYSTORE_PATH
    }
}

if ([string]::IsNullOrWhiteSpace($KeystorePath)) {
    throw '未找到 Android 签名文件路径。默认路径不存在时，请通过 HINGE_KEYSTORE_PATH 指定。'
}
if (-not (Test-Path -LiteralPath $KeystorePath -PathType Leaf)) {
    throw "找不到 Android 签名文件：$KeystorePath"
}
if ([string]::IsNullOrWhiteSpace($KeyAlias)) {
    throw '未提供 Android 签名别名，请填写 android/android/key.properties，或设置 HINGE_KEY_ALIAS。'
}
if ([string]::IsNullOrWhiteSpace($StorePassword)) {
    throw '未提供 Android 签名库密码，请填写 android/android/key.properties，或设置 HINGE_KEYSTORE_PASSWORD。'
}
if ([string]::IsNullOrWhiteSpace($KeyPassword)) {
    $KeyPassword = $StorePassword
}

# Build Android Release APK
Write-Host "=== Building Hinge Android Release APK ===" -ForegroundColor Cyan

$apkPath = Join-Path $repoRoot 'android/build/app/outputs/flutter-apk/app-release.apk'
$buildTempDir = Join-Path $repoRoot 'tmp'
New-Item -ItemType Directory -Path $buildTempDir -Force | Out-Null
if ($SkipFlutterBuild -and -not (Test-Path -LiteralPath $apkPath)) {
    $apkPath = Join-Path $repoRoot 'publish/Hinge.apk'
}
if (-not $SkipFlutterBuild) {
    $oldJavaHome = [Environment]::GetEnvironmentVariable('JAVA_HOME', 'Process')
    $oldTemp = [Environment]::GetEnvironmentVariable('TEMP', 'Process')
    $oldTmp = [Environment]::GetEnvironmentVariable('TMP', 'Process')
    $oldPath = [Environment]::GetEnvironmentVariable('Path', 'Process')
    $oldGradleOpts = [Environment]::GetEnvironmentVariable('GRADLE_OPTS', 'Process')
    $env:JAVA_HOME = $androidJavaHome
    $env:TEMP = $buildTempDir
    $env:TMP = $buildTempDir
    $env:Path = "$androidJavaHome\bin;$oldPath"
    $env:GRADLE_OPTS = if ([string]::IsNullOrWhiteSpace($oldGradleOpts)) {
        '-Dorg.gradle.daemon=false'
    } else {
        "$oldGradleOpts -Dorg.gradle.daemon=false"
    }
    Push-Location (Join-Path $repoRoot 'android')
    try {
        & "D:\flutter_sdk\bin\flutter.bat" build apk --release --target-platform android-arm64
        $flutterExit = $LASTEXITCODE
    } finally {
        Pop-Location
        if ($null -eq $oldJavaHome) { Remove-Item Env:JAVA_HOME -ErrorAction SilentlyContinue } else { $env:JAVA_HOME = $oldJavaHome }
        if ($null -eq $oldTemp) { Remove-Item Env:TEMP -ErrorAction SilentlyContinue } else { $env:TEMP = $oldTemp }
        if ($null -eq $oldTmp) { Remove-Item Env:TMP -ErrorAction SilentlyContinue } else { $env:TMP = $oldTmp }
        if ($null -eq $oldPath) { Remove-Item Env:Path -ErrorAction SilentlyContinue } else { $env:Path = $oldPath }
        if ($null -eq $oldGradleOpts) { Remove-Item Env:GRADLE_OPTS -ErrorAction SilentlyContinue } else { $env:GRADLE_OPTS = $oldGradleOpts }
    }

    if ($flutterExit -ne 0) {
        Write-Error "Android Release build failed!"
        exit $flutterExit
    }
}

if (-not (Test-Path -LiteralPath $apkPath)) {
    Write-Error "Android release APK 未找到：$apkPath"
    exit 1
}

# Do not silently sign a stale multi-ABI APK when -SkipFlutterBuild is used.
# Hinge's supported Android delivery target is arm64-v8a only.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$apkArchive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $apkPath))
try {
    $nativeEntries = @($apkArchive.Entries | Where-Object { $_.FullName -like 'lib/*/*.so' })
    $unsupportedAbis = @(
        $nativeEntries |
            ForEach-Object { ($_.FullName -split '/')[1] } |
            Where-Object { $_ -ne 'arm64-v8a' } |
            Sort-Object -Unique
    )
    if ($nativeEntries.Count -eq 0 -or $unsupportedAbis.Count -gt 0) {
        $details = if ($unsupportedAbis.Count -gt 0) {
            "检测到不支持的 ABI：$($unsupportedAbis -join ', ')"
        } else {
            'APK 中没有找到 arm64-v8a 原生库'
        }
        throw "Android APK 架构校验失败。$details 请使用 --target-platform android-arm64 重新构建。"
    }
} finally {
    $apkArchive.Dispose()
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

$signingDirectory = $buildTempDir
New-Item -ItemType Directory -Path $signingDirectory -Force | Out-Null

Write-Host "Validating Android signing key: $KeystorePath"
$bcprov = $null
if ($sourceStoreType -ieq 'BKS') {
    $bcprovRoot = Join-Path $env:USERPROFILE '.gradle\caches\modules-2\files-2.1\org.bouncycastle'
    $bcprov = Get-ChildItem -LiteralPath $bcprovRoot -Recurse -File -Filter 'bcprov-jdk18on-*.jar' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $bcprov) {
        Write-Error '未找到 Bouncy Castle provider，无法读取 BKS 签名库。'
        exit 1
    }
}

$storePasswordEnv = 'HINGE_BUILD_STORE_PASSWORD'
$keyPasswordEnv = 'HINGE_BUILD_KEY_PASSWORD'
$oldStorePasswordEnv = [Environment]::GetEnvironmentVariable($storePasswordEnv, 'Process')
$oldKeyPasswordEnv = [Environment]::GetEnvironmentVariable($keyPasswordEnv, 'Process')
$sourceKeytoolArguments = @(
    '-list',
    '-storetype', $sourceStoreType,
    '-keystore', $KeystorePath,
    "-storepass:env", $storePasswordEnv,
    '-alias', $KeyAlias
)
if ($sourceStoreType -ieq 'BKS') {
    $sourceKeytoolArguments += @(
        '-providerclass', 'org.bouncycastle.jce.provider.BouncyCastleProvider',
        '-providerpath', $bcprov.FullName
    )
}

[Environment]::SetEnvironmentVariable($storePasswordEnv, $StorePassword, 'Process')
[Environment]::SetEnvironmentVariable($keyPasswordEnv, $KeyPassword, 'Process')

& $keytool @sourceKeytoolArguments | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Error 'Android 签名库验证失败，请检查签名文件、别名或密码。'
    exit $LASTEXITCODE
}

$signedApkPath = Join-Path $signingDirectory 'Hinge-release-signed.apk'
$convertedKeystore = Join-Path ([System.IO.Path]::GetTempPath()) "Hinge-$([guid]::NewGuid().ToString('N')).p12"
try {
    # apksigner does not bundle a BKS provider. Convert only this temporary
    # copy to PKCS12; the certificate and private key remain unchanged.
    $importKeytoolArguments = @(
        '-importkeystore', '-noprompt',
        '-srckeystore', $KeystorePath, '-srcstoretype', $sourceStoreType, '-srcalias', $KeyAlias,
        "-srcstorepass:env", $storePasswordEnv, "-srckeypass:env", $keyPasswordEnv,
        '-destkeystore', $convertedKeystore, '-deststoretype', 'PKCS12', '-destalias', $KeyAlias,
        "-deststorepass:env", $storePasswordEnv, "-destkeypass:env", $keyPasswordEnv
    )
    if ($sourceStoreType -ieq 'BKS') {
        $importKeytoolArguments += @(
            '-providerclass', 'org.bouncycastle.jce.provider.BouncyCastleProvider',
            '-providerpath', $bcprov.FullName
        )
    }
    & $keytool @importKeytoolArguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw '无法将签名库转换为临时 PKCS12 签名库。'
    }

    if (Test-Path -LiteralPath $signedApkPath) {
        Remove-Item -LiteralPath $signedApkPath -Force
    }
    & $apksigner.FullName sign --ks $convertedKeystore --ks-type PKCS12 `
        --ks-key-alias $KeyAlias --ks-pass "env:$storePasswordEnv" `
        --key-pass "env:$keyPasswordEnv" --out $signedApkPath $apkPath
    if ($LASTEXITCODE -ne 0) {
        throw 'Android release APK 签名失败。'
    }

    $certificateOutput = & $apksigner.FullName verify --print-certs --verbose $signedApkPath 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw 'Android release APK 签名校验失败。'
    }
    if ($certificateOutput -notmatch '(?im)certificate SHA-256 digest:\s*[0-9a-f:]+') {
        throw 'Android release APK 未检测到有效的 SHA-256 签名证书。'
    }
}
finally {
    if (Test-Path -LiteralPath $convertedKeystore) {
        Remove-Item -LiteralPath $convertedKeystore -Force -ErrorAction SilentlyContinue
    }
    if ($null -eq $oldStorePasswordEnv) {
        Remove-Item -LiteralPath "Env:$storePasswordEnv" -ErrorAction SilentlyContinue
    } else {
        [Environment]::SetEnvironmentVariable($storePasswordEnv, $oldStorePasswordEnv, 'Process')
    }
    if ($null -eq $oldKeyPasswordEnv) {
        Remove-Item -LiteralPath "Env:$keyPasswordEnv" -ErrorAction SilentlyContinue
    } else {
        [Environment]::SetEnvironmentVariable($keyPasswordEnv, $oldKeyPasswordEnv, 'Process')
    }
}

$publishDir = Join-Path $repoRoot 'publish'
if (-not (Test-Path $publishDir)) {
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
}
$publishApkPath = Join-Path $publishDir 'Hinge.apk'
Copy-Item -Path $signedApkPath -Destination $publishApkPath -Force

# Remove only the known legacy Android output after the new signed artifact
# has been copied successfully. Never remove it before the replacement exists.
$legacyPublishDir = Join-Path $publishDir 'android'
$legacyApkPath = Join-Path $legacyPublishDir 'Hinge.apk'
if (Test-Path -LiteralPath $legacyApkPath -PathType Leaf) {
    Remove-Item -LiteralPath $legacyApkPath -Force
    if ((Test-Path -LiteralPath $legacyPublishDir -PathType Container) -and
        (-not (Get-ChildItem -LiteralPath $legacyPublishDir -Force))) {
        Remove-Item -LiteralPath $legacyPublishDir -Force
    }
}

Write-Host "=== Android Release Build Successful! Artifact copied to $publishApkPath ===" -ForegroundColor Green
