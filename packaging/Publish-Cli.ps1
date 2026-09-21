[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "3.0.0",
    [string]$CertificateThumbprint = "",
    [string]$TimestampServer = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repoRoot "src\WindowsInitializer.Cli\WindowsInitializer.Cli.csproj"
$output = Join-Path $repoRoot "artifacts\publish\cli\$Runtime"
$manifestPath = Join-Path $output "release-manifest.json"

if ($Runtime -ne "win-x64") { throw "V2 release runtime must be win-x64." }
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw "Version must be a semantic version." }
$normalizedThumbprint = $CertificateThumbprint.Replace(" ", "").ToUpperInvariant()
if ($normalizedThumbprint -and $normalizedThumbprint -notmatch '^[0-9A-F]{40,64}$') { throw "Certificate thumbprint is invalid." }
if ($TimestampServer) {
    $timestampUri = [Uri]$TimestampServer
    if (-not $timestampUri.IsAbsoluteUri -or $timestampUri.Scheme -ne "https") { throw "Timestamp server must be an absolute HTTPS URI." }
}
if (git -C $repoRoot status --porcelain --untracked-files=no) { throw "Release publishing requires a clean tracked source tree." }
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$resolvedOutput = [IO.Path]::GetFullPath($output)
if (-not $resolvedOutput.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish output escaped the repository artifacts directory."
}
if (Test-Path -LiteralPath $resolvedOutput) { Remove-Item -LiteralPath $resolvedOutput -Recurse -Force }
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

dotnet restore (Join-Path $repoRoot "WindowsInitializer.sln")
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }
dotnet publish $project --configuration $Configuration --runtime $Runtime --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:PublishAot=false -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version --output $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$payload = @(Get-ChildItem -LiteralPath $output -File | Where-Object Name -ne "release-manifest.json")
if ($payload.Count -ne 1 -or $payload[0].Name -ne "WindowsInitializer.Cli.exe") { throw "CLI publish output must contain exactly WindowsInitializer.Cli.exe." }
$signatureStatus = "NotSignedInLocalBuild"
if ($normalizedThumbprint) {
    $certificates = @(Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert |
        Where-Object { $_.Thumbprint -eq $normalizedThumbprint -and $_.HasPrivateKey })
    if ($certificates.Count -ne 1) { throw "Expected exactly one code-signing certificate with private key for thumbprint $normalizedThumbprint." }
    $signing = @{ FilePath = $payload[0].FullName; Certificate = $certificates[0]; HashAlgorithm = "SHA256" }
    if ($TimestampServer) { $signing["TimestampServer"] = $TimestampServer }
    $signed = Set-AuthenticodeSignature @signing
    if ($signed.Status -ne "Valid") { throw "Authenticode signing failed: $($signed.Status) - $($signed.StatusMessage)" }
    $verified = Get-AuthenticodeSignature -LiteralPath $payload[0].FullName
    if ($verified.Status -ne "Valid" -or $verified.SignerCertificate.Thumbprint -ne $normalizedThumbprint) {
        throw "Authenticode signature verification failed after signing."
    }
    $signatureStatus = "Valid"
}
$sourceCommit = (git -C $repoRoot rev-parse HEAD).Trim()
$runtimeVersion = (dotnet --version).Trim()
$manifest = [ordered]@{
    product = "WISK CLI"
    version = $Version
    sourceCommit = $sourceCommit
    runtime = $runtimeVersion
    rid = $Runtime
    architecture = "x64"
    selfContained = $true
    publishSingleFile = $true
    publishTrimmed = $false
    publishAot = $false
    signatureStatus = $signatureStatus
    certificateThumbprint = $normalizedThumbprint
    timestampServer = $TimestampServer
    files = @(@{ path = $payload[0].Name; length = $payload[0].Length; sha256 = (Get-FileHash -LiteralPath $payload[0].FullName -Algorithm SHA256).Hash })
    generatedAt = [DateTimeOffset]::UtcNow.ToString('O')
}
$temp = "$manifestPath.tmp"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $temp -Encoding UTF8
Move-Item -LiteralPath $temp -Destination $manifestPath -Force
Write-Host "Published $($payload[0].FullName)"
Write-Host "Manifest $manifestPath"
