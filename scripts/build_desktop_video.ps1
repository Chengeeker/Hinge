$ErrorActionPreference = 'Stop'
$taskRepo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$taskVswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $taskVswhere)) { throw '桌面采集组件需要 Visual Studio C++ Build Tools。' }
$taskMsbuild = & $taskVswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $taskMsbuild) { throw '未找到 Visual Studio MSBuild。' }
& $taskMsbuild (Join-Path $taskRepo 'windows\Hinge.DesktopVideo\Hinge.DesktopVideo.vcxproj') /p:Configuration=Release /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) { throw '桌面采集组件编译失败。' }
if (-not (Test-Path (Join-Path $taskRepo 'tmp\Hinge-desktop-video\Hinge.DesktopVideo.exe'))) { throw '桌面采集组件未生成。' }
