$executablePath = Join-Path $PSScriptRoot 'Hinge.exe'
if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "Hinge.exe was not found: $executablePath"
}

# Use netsh rather than the NetSecurity PowerShell module. netsh ships with
# Windows editions where the NetSecurity module may not be available.
$rules = @(
    @{ Name = 'Hinge LAN - Discovery UDP'; Protocol = 'UDP'; LocalPort = '52830' },
    @{ Name = 'Hinge LAN - Session TCP'; Protocol = 'TCP'; LocalPort = '52831' },
    @{ Name = 'Hinge LAN - Session TCP Dynamic'; Protocol = 'TCP'; LocalPort = 'any' }
)

# Remove rules from both the current and the previous product name. Deleting
# a missing rule is harmless, so its exit code is intentionally ignored.
foreach ($name in @(
    'Office Suite LAN - Discovery UDP',
    'Office Suite LAN - Session TCP',
    'Hinge LAN - Discovery UDP',
    'Hinge LAN - Session TCP',
    'Hinge LAN - Session TCP Dynamic'
)) {
    & netsh.exe advfirewall firewall delete rule "name=$name" | Out-Null
}

foreach ($rule in $rules) {
    $arguments = @(
        'advfirewall', 'firewall', 'add', 'rule',
        "name=$($rule.Name)",
        'dir=in', 'action=allow',
        "program=$executablePath",
        "protocol=$($rule.Protocol)",
        "localport=$($rule.LocalPort)",
        'remoteip=localsubnet',
        'profile=any', 'enable=yes'
    )
    & netsh.exe @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to create Windows Firewall rule: $($rule.Name) (exit code $LASTEXITCODE)."
    }
}

Write-Host 'Hinge firewall rules configured.' -ForegroundColor Green
