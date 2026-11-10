param(
    [string]$KeystorePath = '',
    [string]$KeyAlias = '',
    [string]$StorePassword = '',
    [string]$KeyPassword = '',
    [string]$OutputPath = '',
    [Alias('SkipFlutterBuild')]
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $PSBoundParameters['OutputPath'] = Join-Path $PSScriptRoot '../publish/Hinge.apk'
}
& (Join-Path $PSScriptRoot '../build-release.ps1') @PSBoundParameters
