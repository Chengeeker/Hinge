param(
    [string]$KeystorePath = '',
    [string]$KeyAlias = '',
    [string]$StorePassword = '',
    [string]$KeyPassword = '',
    [string]$OutputPath = '',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$preferredJavaHome = Join-Path $env:USERPROFILE '.gradle/jdks/eclipse_adoptium-21-amd64-windows.2'
$gradleRoot = $PSScriptRoot
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

# Gradle must not inherit the machine-wide Java 26 shim. Prefer the
# verified local JDK 21, then fall back to a valid caller-provided JAVA_HOME.
$androidJavaHome = if (Test-Path -LiteralPath (Join-Path $preferredJavaHome 'bin\java.exe') -PathType Leaf) {
    $preferredJavaHome
} elseif (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME) -and
    (Test-Path -LiteralPath (Join-Path $env:JAVA_HOME 'bin\java.exe') -PathType Leaf)) {
    $env:JAVA_HOME
} else {
    $null
}
if ($null -eq $androidJavaHome) {
    throw '未找到可用的 JDK 21。请安装 JDK 21，或通过 JAVA_HOME 指定包含 bin\java.exe 的 JDK。'
}

if ([string]::IsNullOrWhiteSpace($KeystorePath)) {
    throw '未找到 Android 签名文件路径。请配置被 Git 忽略的 android-compose/key.properties，或通过 HINGE_KEYSTORE_PATH 指定。'
}
if (-not (Test-Path -LiteralPath $KeystorePath -PathType Leaf)) {
    throw "找不到 Android 签名文件：$KeystorePath"
}
if ([string]::IsNullOrWhiteSpace($KeyAlias)) {
    throw '未提供 Android 签名别名，请填写 android-compose/key.properties，或设置 HINGE_KEY_ALIAS。'
}
if ([string]::IsNullOrWhiteSpace($StorePassword)) {
    throw '未提供 Android 签名库密码，请填写 android-compose/key.properties，或设置 HINGE_KEYSTORE_PASSWORD。'
}
if ([string]::IsNullOrWhiteSpace($KeyPassword)) {
    $KeyPassword = $StorePassword
}

$env:JAVA_HOME=$androidJavaHome
$buildTempDir=Join-Path $repoRoot 'tmp'
$apkPath=Join-Path $PSScriptRoot 'app/build/outputs/apk/release/app-release-unsigned.apk'
$appBuildFile = Join-Path $PSScriptRoot 'app/build.gradle.kts'
$appBuild = Get-Content -LiteralPath $appBuildFile -Raw
$versionCode = [regex]::Match($appBuild, '(?m)^\s*versionCode\s*=\s*(\d+)\s*$').Groups[1].Value
$versionName = [regex]::Match($appBuild, '(?m)^\s*versionName\s*=\s*"([\d.]+)"\s*$').Groups[1].Value
if (!$versionCode -or !$versionName) { throw '无法读取 Compose 版本号。' }
if (-not $SkipBuild) {
    $oldEnvironment=@{}
    foreach($name in @('TEMP','TMP','JAVA_TOOL_OPTIONS')) { $oldEnvironment[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
    try {
        $env:TEMP=$buildTempDir; $env:TMP=$buildTempDir
        $env:JAVA_TOOL_OPTIONS="-Djava.io.tmpdir=$buildTempDir"
        Push-Location $PSScriptRoot
        try { & .\gradlew.bat :app:testDebugUnitTest :app:lintRelease :app:assembleRelease --console=plain; if($LASTEXITCODE -ne 0){throw 'Compose checks/build failed'} }
        finally { Pop-Location }
    } finally { foreach($name in $oldEnvironment.Keys){[Environment]::SetEnvironmentVariable($name,$oldEnvironment[$name],'Process')} }
}
if (-not (Test-Path -LiteralPath $apkPath)) { throw 'Compose release APK is missing' }
$newest=(Get-ChildItem (Join-Path $PSScriptRoot 'app/src') -Recurse -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
$apkTime = (Get-Item -LiteralPath $apkPath).LastWriteTimeUtc
if ($apkTime -lt $newest -or $apkTime -lt (Get-Item -LiteralPath $appBuildFile).LastWriteTimeUtc) {
    throw 'APK predates sources or build metadata; rebuild before signing'
}
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

Write-Host 'Validating configured Android signing key'
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

$zipalign=Join-Path $apksigner.DirectoryName 'zipalign.exe'
$aapt2 = Join-Path $apksigner.DirectoryName 'aapt2.exe'
$badging = & $aapt2 dump badging $apkPath 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or
    !$badging.Contains("name='com.hinge.office' versionCode='$versionCode' versionName='$versionName'") -or
    $badging -notmatch "(?m)^native-code: 'arm64-v8a'\s*$") {
    throw 'APK 包名、版本或 ARM64 架构与 Compose 配置不符。'
}
$aligned=Join-Path $buildTempDir 'Hinge-Compose-aligned.apk'
& $zipalign -P 16 -f 4 $apkPath $aligned
if($LASTEXITCODE -ne 0){throw 'APK alignment failed'}
$apkPath=$aligned
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

$signedApkPath = Join-Path $signingDirectory 'Hinge-Compose-signed.apk'
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
    & $keytool @importKeytoolArguments 2>$null | Out-Null
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
    if ($certificateOutput -notmatch '3eb0f6708904f8ef916c6a2572e394ba981ee25d64861344cf1921ca7ea17975') { throw 'APK 签名证书与 Hinge 正式签名不符。' }
    foreach ($scheme in 2, 3) {
        if ($certificateOutput -notmatch "(?m)^Verified using v$scheme scheme .*: true\s*$") {
            throw "APK 未通过 v$scheme 签名验证。"
        }
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

$artifactDirectory=Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$target = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $artifactDirectory "Hinge-Compose-$versionName-$versionCode-arm64.apk"
} else {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
}
$certificateOutput | Set-Content -LiteralPath (Join-Path $artifactDirectory 'signature.txt') -Encoding utf8
& $zipalign -c -P 16 -v 4 $signedApkPath *> (Join-Path $artifactDirectory 'alignment.txt')
if($LASTEXITCODE -ne 0){throw 'Signed APK alignment failed'}
New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
$staged = "$target.$([guid]::NewGuid().ToString('N')).tmp"
try {
    Copy-Item -LiteralPath $signedApkPath -Destination $staged
    if (Test-Path -LiteralPath $target) {
        [IO.File]::Replace($staged, $target, [NullString]::Value)
    } else {
        [IO.File]::Move($staged, $target)
    }
} finally {
    if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged -Force }
}
Get-FileHash -LiteralPath $target -Algorithm SHA256 | Select-Object Path,Hash

