$ErrorActionPreference = 'Stop'
$taskRepo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$taskClasses = Join-Path $taskRepo 'android/build/app/tmp/kotlin-classes/release'
if (-not (Test-Path (Join-Path $taskClasses 'com/hinge/office/DesktopDecodeState.class'))) {
    throw '先构建 Android Release，以验证实际编译的解码状态类。'
}
$taskGradle = if ($env:GRADLE_USER_HOME) { $env:GRADLE_USER_HOME } else { Join-Path $env:USERPROFILE '.gradle' }
$taskRuntimeRoot = Join-Path $taskGradle 'caches/modules-2/files-2.1/org.jetbrains.kotlin/kotlin-stdlib'
$taskRuntimeVersion = Get-ChildItem $taskRuntimeRoot -Directory |
    Where-Object Name -Match '^\d+\.\d+\.\d+$' |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $taskRuntimeVersion) { throw '未找到本机已缓存的 Kotlin 标准库，不自动下载测试依赖。' }
$taskRuntime = Get-ChildItem $taskRuntimeVersion.FullName -Recurse -Filter "kotlin-stdlib-$($taskRuntimeVersion.Name).jar" |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $taskRuntime) { throw 'Kotlin 标准库缓存不完整。' }
$taskOutput = Join-Path $taskRepo 'tmp/desktop-decode-checks'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
& 'D:/jdk17/bin/javac.exe' -cp "$taskClasses;$taskRuntime" -d $taskOutput (Join-Path $taskRepo 'android/android/app/src/test/java/com/hinge/office/DesktopDecodeStateCheck.java')
if ($LASTEXITCODE -ne 0) { throw '解码状态自检编译失败。' }
& 'D:/jdk17/bin/java.exe' -ea -cp "$taskOutput;$taskClasses;$taskRuntime" com.hinge.office.DesktopDecodeStateCheck
if ($LASTEXITCODE -ne 0) { throw '解码状态自检失败。' }
