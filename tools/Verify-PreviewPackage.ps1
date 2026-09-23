[CmdletBinding()]
param(
    [string]$PackageDirectory = $PSScriptRoot,
    [string]$ArchivePath,
    [string]$ExpectedRuntimeMode
)

$ErrorActionPreference = 'Stop'

if ($ExpectedRuntimeMode -and $ExpectedRuntimeMode -notin @('FrameworkDependent', 'SelfContained')) {
    throw 'ExpectedRuntimeMode must be FrameworkDependent or SelfContained.'
}

$expectedNames = @('LICENSE', 'README.md', 'Verify-Package.ps1', 'WISK.exe', 'release-manifest.json')
$manifestText = $null
$payloadLength = 0L
$payloadHash = $null
$archive = $null
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-PackageNames([string[]]$Names) {
    $actual = @($Names | Sort-Object)
    $expected = @($expectedNames | Sort-Object)
    if ($actual.Count -ne $expected.Count -or (($actual -join '|') -cne ($expected -join '|'))) {
        throw "Package contents differ from the expected files. Found: $($actual -join ', ')"
    }
}

function Read-ArchiveText($Entry) {
    $stream = $Entry.Open()
    $reader = [System.IO.StreamReader]::new($stream)
    try { return $reader.ReadToEnd() }
    finally { $reader.Dispose() }
}

function Get-ArchiveSha256($Entry) {
    $stream = $Entry.Open()
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $script:payloadLength = [int64]$Entry.Length
        return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

if ($ArchivePath) {
    $resolvedArchive = (Resolve-Path -LiteralPath $ArchivePath).Path
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedArchive)
    try {
        $entries = @($archive.Entries)
        Assert-PackageNames @($entries | ForEach-Object FullName)
        $manifestEntry = @($entries | Where-Object FullName -eq 'release-manifest.json')[0]
        $payloadEntry = @($entries | Where-Object FullName -eq 'WISK.exe')[0]
        $manifestText = Read-ArchiveText $manifestEntry
        $payloadHash = Get-ArchiveSha256 $payloadEntry
    }
    finally {
        $archive.Dispose()
    }
}
else {
    $resolvedDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
    $directories = @(Get-ChildItem -LiteralPath $resolvedDirectory -Directory -Force)
    if ($directories.Count -ne 0) { throw 'Package root must contain files only, without nested directories.' }
    $files = @(Get-ChildItem -LiteralPath $resolvedDirectory -File -Force)
    Assert-PackageNames @($files | ForEach-Object Name)
    $manifestText = Get-Content -Raw -LiteralPath (Join-Path $resolvedDirectory 'release-manifest.json')
    $payload = Get-Item -LiteralPath (Join-Path $resolvedDirectory 'WISK.exe')
    $payloadLength = [int64]$payload.Length
    $payloadHash = (Get-FileHash -LiteralPath $payload.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
}

$manifest = $manifestText | ConvertFrom-Json
if ($manifest.product -cne 'WISK') { throw 'Manifest product is not WISK.' }
if ($manifest.version -cne '3.0.0') { throw "Unexpected WISK version: $($manifest.version)" }
if ($manifest.rid -cne 'win-x64' -or $manifest.architecture -cne 'x64') { throw 'Package is not Windows x64.' }
if ($manifest.runtime -notmatch '^10\.') { throw "Unexpected .NET runtime line: $($manifest.runtime)" }
if ($manifest.publishSingleFile -ne $true) { throw 'Package is not a single-file publish.' }
if ($manifest.signatureStatus -cne 'NotSignedInLocalBuild') { throw 'Preview package must identify the unsigned local-build state.' }
if ([string]$manifest.sourceCommit -notmatch '^[0-9a-fA-F]{40}$') { throw 'Manifest sourceCommit is not a full Git commit ID.' }

$manifestFiles = @($manifest.files)
if ($manifestFiles.Count -ne 1 -or $manifestFiles[0].path -cne 'WISK.exe') {
    throw 'Manifest must describe exactly one WISK.exe payload.'
}
if ([int64]$manifestFiles[0].length -ne $payloadLength) { throw 'WISK.exe length does not match the manifest.' }
if ([string]$manifestFiles[0].sha256 -notmatch '^[0-9a-fA-F]{64}$' -or
    $manifestFiles[0].sha256.ToUpperInvariant() -cne $payloadHash) {
    throw 'WISK.exe SHA-256 does not match the manifest.'
}

$actualMode = if ($manifest.selfContained) { 'SelfContained' } else { 'FrameworkDependent' }
if ($ExpectedRuntimeMode -and $ExpectedRuntimeMode -cne $actualMode) {
    throw "Expected $ExpectedRuntimeMode but manifest describes $actualMode."
}

Write-Output "Verified $actualMode WISK 3.0.0 package payload: $payloadLength bytes, SHA-256 $payloadHash"
Write-Output "Source commit: $($manifest.sourceCommit)"
Write-Output 'This check confirms package consistency; it does not authenticate the publisher.'
