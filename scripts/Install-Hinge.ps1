$ErrorActionPreference = 'Stop'

$packagePath = Join-Path $PSScriptRoot 'Hinge-Installer.msix'
$certificatePath = Join-Path $PSScriptRoot 'Hinge-Installer.cer'

if (-not (Test-Path -LiteralPath $packagePath)) {
    throw "未找到安装包：$packagePath"
}
if (-not (Test-Path -LiteralPath $certificatePath)) {
    throw "未找到安装证书：$certificatePath"
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请以管理员身份运行此脚本，然后重试。'
}

# AppX sideloading trusts the package signer when it is installed in the
# local-machine Trusted People store. This is the test certificate generated
# beside the MSIX; a production/store certificate can replace it later.
Import-Certificate `
    -FilePath $certificatePath `
    -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

Add-AppxPackage -Path $packagePath -ForceApplicationShutdown

$package = Get-AppxPackage -Name 'Hinge' -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -ne $package -and $package.PackageFamilyName) {
    $firewallRules = @(
        @{ Name = 'Hinge MSIX LAN - Discovery UDP'; Protocol = 'UDP'; Port = 52830; RemoteAddress = 'LocalSubnet' },
        @{ Name = 'Hinge MSIX LAN - Session TCP'; Protocol = 'TCP'; Port = 52831; RemoteAddress = 'LocalSubnet' },
        @{ Name = 'Hinge MSIX LAN - Session TCP Dynamic'; Protocol = 'TCP'; Port = 'Any'; RemoteAddress = 'LocalSubnet' }
    )
    foreach ($rule in $firewallRules) {
        $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
        if ($null -eq $existing) {
            $firewallArgs = @{
                DisplayName = $rule.Name
                Direction = 'Inbound'
                Action = 'Allow'
                Profile = 'Any'
                PackageFamilyName = $package.PackageFamilyName
                Protocol = $rule.Protocol
                LocalPort = $rule.Port
            }
            if ($rule.ContainsKey('RemoteAddress')) {
                $firewallArgs.RemoteAddress = $rule.RemoteAddress
            }
            New-NetFirewallRule @firewallArgs | Out-Null
        } else {
            $firewallArgs = @{
                DisplayName = $rule.Name
                Enabled = 'True'
                Direction = 'Inbound'
                Action = 'Allow'
                Profile = 'Any'
                PackageFamilyName = $package.PackageFamilyName
                Protocol = $rule.Protocol
                LocalPort = $rule.Port
            }
            if ($rule.ContainsKey('RemoteAddress')) {
                $firewallArgs.RemoteAddress = $rule.RemoteAddress
            }
            Set-NetFirewallRule @firewallArgs | Out-Null
        }
    }
}

Write-Host 'Hinge 安装完成。' -ForegroundColor Green
