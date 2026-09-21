[CmdletBinding()]
param(
    [ValidateSet('Build', 'Test', 'Run')]
    [string]$Mode = 'Build',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Restore,
    [switch]$Wait
)

$ErrorActionPreference = 'Stop'
$profile = if ($Mode -eq 'Test') { 'Test' } elseif ($Configuration -eq 'Release') { 'Release' } else { 'Development' }
$buildScript = Join-Path $PSScriptRoot 'Build-Wisk.ps1'
& $buildScript -Target $Mode -Profile $profile -RuntimeMode FrameworkDependent -Restore:$Restore -Wait:$Wait
exit $LASTEXITCODE
