#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TaskId,
    [Parameter(Mandatory)][string]$ContextPath,
    [Parameter(Mandatory)][string]$StatePath,
    [Parameter(Mandatory)][string]$LogPath,
    [Parameter(Mandatory)][string]$CancelPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$modulePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'modules\WindowsInitializer.psm1'
Import-Module -Name $modulePath -Force
Set-InitializerLogPath -Path $LogPath

function Set-WorkerState {
    param([string]$Status, [string]$Error)
    $state = [ordered]@{
        Id = $TaskId
        Status = $Status
        LastHeartbeat = (Get-Date).ToString('o')
        RestartRequired = Get-InitializerRestartRequired
    }
    if ($Error) { $state.Error = $Error }
    Write-InitializerJsonFile -Path $StatePath -Value $state
}

try {
    if (Test-Path -LiteralPath $CancelPath) { Set-WorkerState -Status 'Cancelled'; exit 0 }
    $context = Import-Clixml -LiteralPath $ContextPath
    $item = Get-InitializerTaskDefinition -Id $TaskId
    Set-WorkerState -Status 'Running'
    Invoke-InitializerTask -Item $item -Context $context
    if (Test-Path -LiteralPath $CancelPath) { Set-WorkerState -Status 'Cancelled'; exit 0 }
    Set-WorkerState -Status 'Completed'
}
catch {
    Set-WorkerState -Status 'Failed' -Error $_.Exception.Message
    exit 1
}
