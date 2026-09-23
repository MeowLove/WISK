[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '3.0.0',
    [switch]$ReplaceExisting
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourceCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve the current Git commit.' }

$outputRoot = Join-Path $repoRoot "artifacts/preview/$Version"
if (-not (Test-Path -LiteralPath $outputRoot)) {
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
}

$specs = @(
    @{
        Mode = 'FrameworkDependent'
        SelfContained = $false
        Source = 'artifacts/publish/Release/FrameworkDependent/win-x64'
        Archive = "WISK-$Version-FrameworkDependent-win-x64.zip"
    },
    @{
        Mode = 'SelfContained'
        SelfContained = $true
        Source = 'artifacts/publish/Release/SelfContained/win-x64'
        Archive = "WISK-$Version-SelfContained-win-x64.zip"
    }
)

$knownOutputs = @($specs.Archive) + 'SHA256SUMS.txt'
$unknownFiles = @(Get-ChildItem -LiteralPath $outputRoot -File -Force | Where-Object Name -notin $knownOutputs)
if ($unknownFiles.Count -ne 0) {
    throw "Preview output contains unrelated files that will not be modified: $($unknownFiles.Name -join ', ')"
}
$existingOutputs = @(Get-ChildItem -LiteralPath $outputRoot -File -Force | Where-Object Name -in $knownOutputs)
if ($existingOutputs.Count -ne 0) {
    if (-not $ReplaceExisting) { throw "Preview package files already exist. Use -ReplaceExisting to preserve them and create replacements." }
    $backupRoot = Join-Path $outputRoot ".superseded-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))"
    New-Item -ItemType Directory -Path $backupRoot | Out-Null
    foreach ($file in $existingOutputs) {
        Move-Item -LiteralPath $file.FullName -Destination (Join-Path $backupRoot $file.Name)
    }
    Write-Output "Previous preview ZIPs and checksums were preserved under $backupRoot"
}

$verifier = Join-Path $PSScriptRoot 'Verify-PreviewPackage.ps1'
$guide = Join-Path $repoRoot 'docs/Preview-Testing.md'
$license = Join-Path $repoRoot 'LICENSE'
$stageRoot = Join-Path $outputRoot ".staging-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $stageRoot | Out-Null
$builtPackages = [System.Collections.Generic.List[object]]::new()

try {
    foreach ($spec in $specs) {
        $sourceRoot = Join-Path $repoRoot $spec.Source
        $manifestPath = Join-Path $sourceRoot 'release-manifest.json'
        $payloadPath = Join-Path $sourceRoot 'WISK.exe'
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $payloadPath -PathType Leaf)) {
            throw "Missing release payload or manifest under $sourceRoot"
        }

        $sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -File -Force)
        $sourceDirectories = @(Get-ChildItem -LiteralPath $sourceRoot -Directory -Force)
        if ($sourceFiles.Count -ne 2 -or $sourceDirectories.Count -ne 0 -or
            @($sourceFiles.Name | Where-Object { $_ -notin @('WISK.exe', 'release-manifest.json') }).Count -ne 0) {
            throw "Release directory does not contain exactly WISK.exe and release-manifest.json: $sourceRoot"
        }

        $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
        if ($manifest.product -cne 'WISK' -or $manifest.version -cne $Version -or
            $manifest.sourceCommit -cne $sourceCommit -or $manifest.rid -cne 'win-x64' -or
            [bool]$manifest.selfContained -ne [bool]$spec.SelfContained -or
            $manifest.signatureStatus -cne 'NotSignedInLocalBuild') {
            throw "Release manifest does not match this preview build ($($spec.Mode)): $manifestPath"
        }

        $packageRoot = Join-Path $stageRoot $spec.Mode
        New-Item -ItemType Directory -Path $packageRoot | Out-Null
        Copy-Item -LiteralPath $payloadPath -Destination (Join-Path $packageRoot 'WISK.exe')
        Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $packageRoot 'release-manifest.json')
        Copy-Item -LiteralPath $guide -Destination (Join-Path $packageRoot 'README.md')
        Copy-Item -LiteralPath $license -Destination (Join-Path $packageRoot 'LICENSE')
        Copy-Item -LiteralPath $verifier -Destination (Join-Path $packageRoot 'Verify-Package.ps1')

        & $verifier -PackageDirectory $packageRoot -ExpectedRuntimeMode $spec.Mode
        $archivePath = Join-Path $stageRoot $spec.Archive
        Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archivePath -CompressionLevel Optimal
        & $verifier -ArchivePath $archivePath -ExpectedRuntimeMode $spec.Mode
        $archiveItem = Get-Item -LiteralPath $archivePath
        $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToUpperInvariant()
        $builtPackages.Add([pscustomobject]@{
            Archive = $spec.Archive
            StagedPath = $archivePath
            Length = [int64]$archiveItem.Length
            Sha256 = $archiveHash
        })
    }

    foreach ($package in $builtPackages) {
        Move-Item -LiteralPath $package.StagedPath -Destination (Join-Path $outputRoot $package.Archive)
    }
    $checksumLines = @($builtPackages | ForEach-Object { "$($_.Sha256)  $($_.Archive)" })
    [System.IO.File]::WriteAllText(
        (Join-Path $outputRoot 'SHA256SUMS.txt'),
        (($checksumLines -join [Environment]::NewLine) + [Environment]::NewLine),
        [System.Text.UTF8Encoding]::new($false))

    foreach ($package in $builtPackages) {
        Write-Output "$($package.Archive)|$($package.Length)|$($package.Sha256)"
    }
    Write-Output "Preview packages created under $outputRoot"
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
}
