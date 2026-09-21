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
$repoRoot = (Resolve-Path $PSScriptRoot).Path
$solution = Join-Path $repoRoot 'WindowsInitializer.sln'
$appProject = Join-Path $repoRoot 'src/WindowsInitializer.App/WindowsInitializer.App.csproj'
$appOutput = Join-Path $repoRoot "src/WindowsInitializer.App/bin/$Configuration/net10.0-windows/WindowsInitializer.exe"
$sourceCommit = (git -C $repoRoot rev-parse HEAD).Trim()
$buildTimestamp = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
$buildProperties = @("-p:SourceCommit=$sourceCommit", "-p:BuildTimestampUtc=$buildTimestamp", '-p:SignatureStatus=NotSignedInLocalBuild')

function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

$noRestore = if ($Restore) { @() } else { @('--no-restore') }

switch ($Mode) {
    'Build' {
        $arguments = @('build', $appProject, '--configuration', $Configuration) + $noRestore + $buildProperties
        Invoke-Dotnet $arguments
        Write-Host "Built $appOutput"
    }
    'Test' {
        $arguments = @('test', $solution, '--configuration', $Configuration) + $noRestore + $buildProperties
        Invoke-Dotnet $arguments
    }
    'Run' {
        $arguments = @('build', $appProject, '--configuration', $Configuration) + $noRestore + $buildProperties
        Invoke-Dotnet $arguments
        if (-not (Test-Path -LiteralPath $appOutput -PathType Leaf)) { throw "Application output not found: $appOutput" }
        $process = Start-Process -FilePath $appOutput -WorkingDirectory (Split-Path $appOutput) -PassThru
Write-Host "Started WISK (PID $($process.Id))"
        if ($Wait) { $process.WaitForExit(); exit $process.ExitCode }
    }
}
