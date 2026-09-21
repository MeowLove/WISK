[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dll = Join-Path $repoRoot "src/WindowsInitializer.App/bin/$Configuration/net10.0-windows/WindowsInitializer.dll"
$output = Join-Path $repoRoot 'artifacts/ui-smoke'

if ($Build) { & (Join-Path $repoRoot 'Build-Dev.ps1') -Mode Build -Configuration $Configuration }
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw "Application DLL not found: $dll" }
New-Item -ItemType Directory -Force -Path $output | Out-Null

$cases = @(
    @{ Name = 'home-light-zh-CN.png'; Language = 'zh-CN'; Dark = $false },
    @{ Name = 'home-dark-zh-CN.png'; Language = 'zh-CN'; Dark = $true },
    @{ Name = 'home-light-ar-rtl.png'; Language = 'ar'; Dark = $false },
    @{ Name = 'system-light-zh-CN.png'; Language = 'zh-CN'; Dark = $false; Workspace = 'System' },
    @{ Name = 'system-dark-zh-CN.png'; Language = 'zh-CN'; Dark = $true; Workspace = 'System' },
    @{ Name = 'software-light-zh-CN.png'; Language = 'zh-CN'; Dark = $false; Workspace = 'Software' },
    @{ Name = 'software-dark-zh-CN.png'; Language = 'zh-CN'; Dark = $true; Workspace = 'Software' }
)

Add-Type -AssemblyName System.Drawing
foreach ($case in $cases) {
    $path = Join-Path $output $case.Name
    $arguments = @($dll, '--capture-ui', $path, '--language', $case.Language)
    if ($case.Dark) { $arguments += '--dark' }
    if ($case.Workspace) { $arguments += @('--workspace', $case.Workspace) }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "UI capture failed for $($case.Name) with exit code $LASTEXITCODE." }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "UI capture was not created: $path" }

    $bitmap = [System.Drawing.Bitmap]::FromFile($path)
    try {
        if ($bitmap.Width -lt 1180 -or $bitmap.Height -lt 720) { throw "UI capture has invalid dimensions: $($bitmap.Width)x$($bitmap.Height)" }
        $colors = [Collections.Generic.HashSet[int]]::new()
        for ($x = 0; $x -lt $bitmap.Width; $x += 80) {
            for ($y = 0; $y -lt $bitmap.Height; $y += 60) { [void]$colors.Add($bitmap.GetPixel($x, $y).ToArgb()) }
        }
        if ($colors.Count -lt 8) { throw "UI capture appears blank: $path" }
    } finally {
        $bitmap.Dispose()
    }
    Write-Host "Verified $path"
}
