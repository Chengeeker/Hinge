$ErrorActionPreference = 'Stop'

$executablePath = Join-Path $PSScriptRoot 'Hinge.exe'
if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "未找到 Hinge 程序：$executablePath。请从完整 Windows 包目录运行此脚本。"
}

$rulePrefix = 'Hinge LAN'
$rules = @(
    @{
        Name = "$rulePrefix - Discovery UDP"
        Protocol = 'UDP'
        Port = 52830
    },
    @{
        Name = "$rulePrefix - Session TCP"
        Protocol = 'TCP'
        Port = 52831
    }
)

foreach ($rule in $rules) {
    $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
    if ($null -eq $existing) {
        New-NetFirewallRule `
            -DisplayName $rule.Name `
            -Direction Inbound `
            -Action Allow `
            -Profile Any `
            -Program $executablePath `
            -Protocol $rule.Protocol `
            -LocalPort $rule.Port | Out-Null
    } else {
        Set-NetFirewallRule `
            -DisplayName $rule.Name `
            -Enabled True `
            -Direction Inbound `
            -Action Allow `
            -Profile Any `
            -Program $executablePath | Out-Null
    }
}

Write-Host 'Hinge 已允许在局域网接收设备发现和会话连接。' -ForegroundColor Green
