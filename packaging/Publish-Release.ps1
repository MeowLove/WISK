[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version = '3.0.0',
    [string]$CertificateThumbprint = '',
    [string]$TimestampServer = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'src/WindowsInitializer.App/WindowsInitializer.App.csproj'
$outputRoot = Join-Path $repoRoot "artifacts/publish/Release/SelfContained/$Runtime"
$manifestPath = Join-Path $outputRoot 'release-manifest.json'
$settingsPath = Join-Path $outputRoot 'WindowsInitializer.json'
$preservedSettings = if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
    [IO.File]::ReadAllBytes($settingsPath)
} else {
    $null
}

if (-not (Test-Path $project -PathType Leaf)) { throw "App project not found: $project" }
if ($Configuration -ne 'Release') { throw 'WISK release publishing requires Release configuration.' }
if ($Runtime -ne 'win-x64') { throw 'WISK release runtime must be win-x64.' }
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw 'Version must be a semantic version.' }
$normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
if ($normalizedThumbprint -and $normalizedThumbprint -notmatch '^[0-9A-F]{40,64}$') { throw 'Certificate thumbprint is invalid.' }
if ($TimestampServer) {
    $timestampUri = [Uri]$TimestampServer
    if (-not $timestampUri.IsAbsoluteUri -or $timestampUri.Scheme -ne 'https') { throw 'Timestamp server must be an absolute HTTPS URI.' }
}
if (git -C $repoRoot status --porcelain --untracked-files=no) { throw 'Release publishing requires a clean tracked source tree.' }
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$resolvedOutput = [IO.Path]::GetFullPath($outputRoot)
if (-not $resolvedOutput.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish output escaped the repository artifacts directory.'
}
if (Test-Path -LiteralPath $resolvedOutput) { Remove-Item -LiteralPath $resolvedOutput -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$sourceCommit = (git -C $repoRoot rev-parse HEAD).Trim()
$generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
$embeddedSignatureStatus = if ($normalizedThumbprint) { 'SignedRelease' } else { 'NotSignedInLocalBuild' }

$buildScript = Join-Path $repoRoot 'Build-Wisk.ps1'
& $buildScript -Target Publish -Profile Release -RuntimeMode SelfContained -Component App -Runtime $Runtime -Version $Version -SignatureStatus $embeddedSignatureStatus
if ($LASTEXITCODE -ne 0) { throw "WISK publish failed with exit code $LASTEXITCODE" }

$exe = @(Get-ChildItem -LiteralPath $outputRoot -Filter 'WindowsInitializer.exe' -File)
if ($exe.Count -ne 1) { throw "Expected exactly one WindowsInitializer.exe, found $($exe.Count)" }
$payloadFiles = @(Get-ChildItem -LiteralPath $outputRoot -File | Where-Object Name -ne 'release-manifest.json')
if ($payloadFiles.Count -ne 1 -or $payloadFiles[0].Name -ne 'WindowsInitializer.exe') { throw "Single-file publish contains unexpected payload files: $($payloadFiles.Name -join ', ')" }
$signatureStatus = 'NotSignedInLocalBuild'
if ($normalizedThumbprint) {
    $certificates = @(Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert |
        Where-Object { $_.Thumbprint -eq $normalizedThumbprint -and $_.HasPrivateKey })
    if ($certificates.Count -ne 1) { throw "Expected exactly one code-signing certificate with private key for thumbprint $normalizedThumbprint." }
    $signing = @{ FilePath = $exe[0].FullName; Certificate = $certificates[0]; HashAlgorithm = 'SHA256' }
    if ($TimestampServer) { $signing['TimestampServer'] = $TimestampServer }
    $signed = Set-AuthenticodeSignature @signing
    if ($signed.Status -ne 'Valid') { throw "Authenticode signing failed: $($signed.Status) - $($signed.StatusMessage)" }
    $verified = Get-AuthenticodeSignature -LiteralPath $exe[0].FullName
    if ($verified.Status -ne 'Valid' -or $verified.SignerCertificate.Thumbprint -ne $normalizedThumbprint) {
        throw 'Authenticode signature verification failed after signing.'
    }
    $signatureStatus = 'Valid'
}
$files = @(Get-ChildItem -LiteralPath $outputRoot -File | Where-Object Name -ne 'release-manifest.json' | ForEach-Object {
    [ordered]@{ path = $_.Name; length = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$runtimeVersion = (dotnet --version).Trim()
$manifest = [ordered]@{
    product = 'WISK'
    version = $Version
    sourceCommit = $sourceCommit
    runtime = $runtimeVersion
    rid = $Runtime
    architecture = 'x64'
    selfContained = $true
    publishSingleFile = $true
    publishTrimmed = $false
    publishAot = $false
    signatureStatus = $signatureStatus
    certificateThumbprint = $normalizedThumbprint
    timestampServer = $TimestampServer
    files = $files
    generatedAt = $generatedAt
}
$tempManifest = "$manifestPath.tmp"
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $tempManifest -Encoding utf8
Move-Item -LiteralPath $tempManifest -Destination $manifestPath -Force
if ($null -ne $preservedSettings) {
    [IO.File]::WriteAllBytes($settingsPath, $preservedSettings)
}
Write-Host "Published $($exe.FullName)"
Write-Host "Manifest $manifestPath"
