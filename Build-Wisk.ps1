[CmdletBinding()]
param(
    [ValidateSet('Build', 'Test', 'Publish', 'Run')]
    [string]$Target = 'Build',
    [ValidateSet('Test', 'Development', 'Release')]
    [string]$Profile = 'Development',
    [ValidateSet('FrameworkDependent', 'SelfContained')]
    [string]$RuntimeMode = 'FrameworkDependent',
    [ValidateSet('App', 'Cli')]
    [string]$Component = 'App',
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',
    [string]$Version = '3.0.0',
    [ValidateSet('NotSignedInLocalBuild', 'SignedRelease')]
    [string]$SignatureStatus = 'NotSignedInLocalBuild',
    [switch]$Restore,
    [switch]$Coverage,
    [switch]$Wait
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path $PSScriptRoot).Path
$solution = Join-Path $repoRoot 'Wisk.sln'
$appProject = Join-Path $repoRoot 'src/Wisk.App/Wisk.App.csproj'
$cliProject = Join-Path $repoRoot 'src/Wisk.Cli/Wisk.Cli.csproj'

if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) { throw "Solution not found: $solution" }
if (-not (Test-Path -LiteralPath $appProject -PathType Leaf)) { throw "App project not found: $appProject" }
if (-not (Test-Path -LiteralPath $cliProject -PathType Leaf)) { throw "CLI project not found: $cliProject" }
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw 'Version must be a semantic version.' }

$configuration = if ($Profile -eq 'Release') { 'Release' } else { 'Debug' }
$configurationFolder = $configuration.ToLowerInvariant()
$artifactRoot = Join-Path $repoRoot 'artifacts'
$buildRoot = Join-Path $artifactRoot "build/$Profile/$RuntimeMode/$Runtime"
$binRoot = Join-Path $buildRoot 'bin'
$testRoot = Join-Path $artifactRoot "test-results/$Profile/$RuntimeMode/$Runtime"
$publishComponent = if ($Component -eq 'Cli') { 'cli/' } else { '' }
$publishRoot = Join-Path $artifactRoot "publish/$Profile/$RuntimeMode/$publishComponent$Runtime"
$publishProject = if ($Component -eq 'Cli') { $cliProject } else { $appProject }
$appOutput = Join-Path $binRoot "Wisk.App/$configurationFolder/WISK.exe"
$selfContained = $RuntimeMode -eq 'SelfContained'

function Assert-ArtifactPath {
    param([string]$Path)
    $resolvedRoot = [IO.Path]::GetFullPath($artifactRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Output path escaped the repository artifacts directory: $Path"
    }
}

function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

function Invoke-Restore {
    $arguments = @(
        'restore', $solution,
        '--artifacts-path', $buildRoot
    )
    if ($Target -eq 'Publish') { $arguments += @('--runtime', $Runtime) }
    Invoke-Dotnet $arguments
}

$sourceCommit = (git -C $repoRoot rev-parse HEAD 2>$null).Trim()
if (-not $sourceCommit) { $sourceCommit = 'local' }
$buildTimestamp = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
$buildProperties = @(
    "-p:SourceCommit=$sourceCommit",
    "-p:BuildTimestampUtc=$buildTimestamp",
    "-p:SignatureStatus=$SignatureStatus"
)

$shouldRestore = $Restore -or $Target -eq 'Publish'
if ($shouldRestore) { Invoke-Restore }
$noRestore = @('--no-restore')

switch ($Target) {
    'Build' {
        $arguments = @('build', $solution, '--configuration', $configuration, '--artifacts-path', $buildRoot) + $noRestore + $buildProperties
        Invoke-Dotnet $arguments
        Write-Host "Built $appOutput"
    }
    'Test' {
        New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
        $arguments = @('test', $solution, '--configuration', $configuration, '--artifacts-path', $buildRoot) + $noRestore + $buildProperties + @(
            '--results-directory', $testRoot
        )
        if ($Coverage) {
            $arguments += @(
                '--settings', (Join-Path $repoRoot 'coverlet.runsettings'),
                '--logger', 'trx;LogFileName=wisk-tests.trx',
                '--collect:XPlat Code Coverage'
            )
        }
        Invoke-Dotnet $arguments
    }
    'Publish' {
        Assert-ArtifactPath $publishRoot
        if (Test-Path -LiteralPath $publishRoot) { Remove-Item -LiteralPath $publishRoot -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
        $arguments = @(
            'publish', $publishProject,
            '--configuration', $configuration,
            '--runtime', $Runtime,
            '--self-contained', $selfContained.ToString().ToLowerInvariant(),
            '--output', $publishRoot,
            '--artifacts-path', $buildRoot,
            '--no-restore'
        ) + $buildProperties + @(
            "-p:Version=$Version",
            '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:DebugType=None',
            '-p:DebugSymbols=false',
            '-p:PublishTrimmed=false',
            '-p:PublishAot=false'
        )
        Invoke-Dotnet $arguments
        $payloadName = if ($Component -eq 'Cli') { 'WISK.Cli.exe' } else { 'WISK.exe' }
        $payloadFiles = @(Get-ChildItem -LiteralPath $publishRoot -File | Where-Object Name -ne 'release-manifest.json')
        if ($payloadFiles.Count -ne 1 -or $payloadFiles[0].Name -ne $payloadName) {
            throw "Single-file publish output must contain exactly $payloadName."
        }
        $manifestPath = Join-Path $publishRoot 'release-manifest.json'
        $manifest = [ordered]@{
            product = if ($Component -eq 'Cli') { 'WISK CLI' } else { 'WISK' }
            version = $Version
            sourceCommit = $sourceCommit
            runtime = (dotnet --version).Trim()
            rid = $Runtime
            architecture = 'x64'
            selfContained = $selfContained
            publishSingleFile = $true
            publishTrimmed = $false
            publishAot = $false
            signatureStatus = 'NotSignedInLocalBuild'
            certificateThumbprint = ''
            timestampServer = ''
            files = @([ordered]@{
                path = $payloadFiles[0].Name
                length = $payloadFiles[0].Length
                sha256 = (Get-FileHash -LiteralPath $payloadFiles[0].FullName -Algorithm SHA256).Hash
            })
            generatedAt = $buildTimestamp
        }
        $temporaryManifest = "$manifestPath.tmp"
        $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporaryManifest -Encoding utf8
        Move-Item -LiteralPath $temporaryManifest -Destination $manifestPath -Force
        Write-Host "Published $publishRoot"
    }
    'Run' {
        if ($Component -ne 'App') { throw 'Run target supports the App component only.' }
        if ($RuntimeMode -eq 'SelfContained') { throw 'Run target uses a framework-dependent development build; use Publish for self-contained output.' }
        $arguments = @('build', $appProject, '--configuration', $configuration, '--artifacts-path', $buildRoot) + $noRestore + $buildProperties
        Invoke-Dotnet $arguments
        if (-not (Test-Path -LiteralPath $appOutput -PathType Leaf)) { throw "Application output not found: $appOutput" }
        $process = Start-Process -FilePath $appOutput -WorkingDirectory (Split-Path $appOutput) -PassThru
        Write-Host "Started WISK (PID $($process.Id))"
        if ($Wait) { $process.WaitForExit(); exit $process.ExitCode }
    }
}
