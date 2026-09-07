$ErrorActionPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.Windows.Forms

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$answer = [System.Windows.Forms.MessageBox]::Show(
    "确定要卸载 Hinge 吗？`n`n应用文件和快捷方式都会被移除。",
    'Hinge 卸载程序',
    [System.Windows.Forms.MessageBoxButtons]::YesNo,
    [System.Windows.Forms.MessageBoxIcon]::Question)
if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) { exit 0 }

Get-Process -Name 'Hinge' -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.CloseMainWindow() | Out-Null } catch {}
}
Start-Sleep -Milliseconds 500

foreach ($ruleName in @('Hinge LAN - Discovery UDP', 'Hinge LAN - Session TCP')) {
    try { Get-NetFirewallRule -DisplayName $ruleName | Remove-NetFirewallRule } catch {}
}

$startMenuShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Hinge.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Hinge.lnk'
Remove-Item -LiteralPath $startMenuShortcut, $desktopShortcut -Force
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Hinge' -Recurse -Force

$cleanup = Join-Path $env:TEMP ("Hinge-Uninstall-{0}.ps1" -f [Guid]::NewGuid())
$cleanupScript = @"
`$ErrorActionPreference = 'SilentlyContinue'
Start-Sleep -Seconds 1
Remove-Item -LiteralPath '$root' -Recurse -Force
Remove-Item -LiteralPath '$cleanup' -Force
"@
$cleanupScript | Set-Content -LiteralPath $cleanup -Encoding UTF8
Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $cleanup) -WindowStyle Hidden
