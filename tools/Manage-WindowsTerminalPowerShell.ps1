[CmdletBinding()]
param(
    [ValidateSet('Menu', 'Status', 'PS5', 'PS7', 'InstallPS7', 'OpenSettings', 'OpenSettingsFile', 'Help')]
    [string]$Action = 'Menu'
)

$ErrorActionPreference = 'Stop'

function Get-TerminalSettingsPath {
    $terminal = Get-AppxPackage -Name Microsoft.WindowsTerminal `
        -ErrorAction SilentlyContinue |
        Sort-Object Version -Descending |
        Select-Object -First 1

    if ($null -eq $terminal) {
        throw 'Microsoft Store Windows Terminal was not found.'
    }

    $settings = Join-Path $env:LOCALAPPDATA `
        ("Packages\{0}\LocalState\settings.json" -f $terminal.PackageFamilyName)

    if (-not (Test-Path -LiteralPath $settings)) {
        throw "Windows Terminal settings file was not found: $settings"
    }

    return $settings
}

function Read-TerminalData {
    param(
        [Parameter(Mandatory)]
        [string]$SettingsPath
    )

    $raw = [System.IO.File]::ReadAllText(
        $SettingsPath,
        [System.Text.Encoding]::UTF8
    )

    try {
        $config = $raw | ConvertFrom-Json
    }
    catch {
        throw @"
settings.json could not be parsed as standard JSON.

This script does not modify settings files containing JSONC comments
or trailing commas. Use Windows Terminal Settings UI instead.
"@
    }

    return [PSCustomObject]@{
        Raw    = $raw
        Config = $config
    }
}

function Get-PowerShellProfiles {
    param(
        [Parameter(Mandatory)]
        $Config
    )

    $profiles = @($Config.profiles.list)

    $ps5 = $profiles |
        Where-Object {
            $_.guid -and
            $_.commandline -match '(?i)WindowsPowerShell\\v1\.0\\powershell\.exe'
        } |
        Select-Object -First 1

    # Exact source check: never matches Azure Cloud Shell.
    $ps7 = $profiles |
        Where-Object {
            $_.guid -and
            $_.source -eq 'Windows.Terminal.PowershellCore' -and
            $_.hidden -ne $true
        } |
        Select-Object -First 1

    return [PSCustomObject]@{
        PS5 = $ps5
        PS7 = $ps7
    }
}

function Get-CurrentDefaultProfile {
    param(
        [Parameter(Mandatory)]
        $Config
    )

    return @($Config.profiles.list) |
        Where-Object { $_.guid -eq $Config.defaultProfile } |
        Select-Object -First 1
}

function Get-ProfileDisplayName {
    param($Profile)

    if ($null -eq $Profile) {
        return '<not found>'
    }

    return $Profile.name
}

function Get-ProfileGuid {
    param($Profile)

    if ($null -eq $Profile) {
        return '<not found>'
    }

    return $Profile.guid
}

function Show-Status {
    $settings = Get-TerminalSettingsPath
    $data = Read-TerminalData -SettingsPath $settings
    $profiles = Get-PowerShellProfiles -Config $data.Config
    $current = Get-CurrentDefaultProfile -Config $data.Config

    Write-Host ''
    Write-Host 'Windows Terminal PowerShell status' -ForegroundColor Cyan
    Write-Host "Settings file: $settings"
    Write-Host "Current default: $(Get-ProfileDisplayName $current)"
    Write-Host "Current GUID: $($data.Config.defaultProfile)"
    Write-Host ''
    Write-Host "PS5 profile: $(Get-ProfileDisplayName $profiles.PS5)"
    Write-Host "PS5 GUID: $(Get-ProfileGuid $profiles.PS5)"
    Write-Host ''
    Write-Host "PS7 profile: $(Get-ProfileDisplayName $profiles.PS7)"
    Write-Host "PS7 GUID: $(Get-ProfileGuid $profiles.PS7)"
    Write-Host ''
}

function Set-DefaultProfile {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('PS5', 'PS7')]
        [string]$Target
    )

    $settings = Get-TerminalSettingsPath
    $data = Read-TerminalData -SettingsPath $settings
    $profiles = Get-PowerShellProfiles -Config $data.Config
    $profile = $profiles.$Target

    if ($null -eq $profile) {
        throw "$Target profile was not found in Windows Terminal."
    }

    $oldGuid = $data.Config.defaultProfile
    $newGuid = $profile.guid

    if ($oldGuid -eq $newGuid) {
        Write-Host ''
        Write-Host "No change needed. $($profile.name) is already the default." `
            -ForegroundColor Green
        Write-Host ''
        return
    }

    $replacementGuid = $newGuid
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator]{
        param($match)
        return $match.Groups[1].Value + '"' + $replacementGuid + '"'
    }

    $updated = [regex]::Replace(
        $data.Raw,
        '(?m)("defaultProfile"\s*:\s*)"[^"]*"',
        $evaluator,
        1
    )

    if ($updated -eq $data.Raw) {
        throw 'defaultProfile was not found. No changes were made.'
    }

    try {
        $updatedConfig = $updated | ConvertFrom-Json
    }
    catch {
        throw 'The updated settings content is not valid JSON. No changes were made.'
    }

    if ($updatedConfig.defaultProfile -ne $newGuid) {
        throw 'The updated settings content does not contain the requested default profile. No changes were made.'
    }

    $directory = Split-Path -Parent $settings
    $temporary = Join-Path $directory ('.settings.json.tmp-{0}' -f [guid]::NewGuid())
    $backup = "$settings.bak-$(Get-Date -Format 'yyyyMMdd-HHmmssfff')"

    try {
        [System.IO.File]::WriteAllText(
            $temporary,
            $updated,
            [System.Text.UTF8Encoding]::new($false)
        )

        [System.IO.File]::Replace($temporary, $settings, $backup)
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Host ''
    Write-Host "Default profile changed to: $($profile.name)" -ForegroundColor Green
    Write-Host "Old GUID: $oldGuid"
    Write-Host "New GUID: $newGuid"
    Write-Host "Backup: $backup"
    Write-Host 'Close this Terminal window and open a new one to apply the change.'
    Write-Host ''
}

function Install-PS7 {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($null -eq $winget) {
        throw 'winget was not found. Install App Installer from Microsoft Store, then try again.'
    }

    Write-Host ''
    Write-Host 'Installing PowerShell 7 from Microsoft Store...' -ForegroundColor Cyan

    & winget install -e --id 9MZ1SNWT0N5D --source msstore `
        --accept-source-agreements --accept-package-agreements

    if ($LASTEXITCODE -ne 0) {
        throw "winget install failed. Exit code: $LASTEXITCODE"
    }

    Write-Host ''
    Write-Host 'PowerShell 7 installation completed.' -ForegroundColor Green
    Write-Host 'Open Windows Terminal once, then choose option 3 to set PS7 as default.'
    Write-Host ''
}

function Open-TerminalSettingsUI {
    Write-Host ''
    Write-Host 'Attempting to open Windows Terminal Settings UI...' -ForegroundColor Cyan

    try {
        Add-Type -AssemblyName System.Windows.Forms
        Start-Sleep -Milliseconds 300
        [System.Windows.Forms.SendKeys]::SendWait('^,')

        Write-Host 'If Settings did not open, keep this Terminal window active and press Ctrl+, manually.'
    }
    catch {
        Write-Host 'Could not simulate Ctrl+,.' -ForegroundColor Yellow
        Write-Host 'Keep this Terminal window active and press Ctrl+, manually.'
    }

    Write-Host ''
}

function Open-TerminalSettingsFile {
    $settings = Get-TerminalSettingsPath
    $code = Get-Command code -ErrorAction SilentlyContinue

    if ($null -ne $code) {
        Start-Process -FilePath $code.Source -ArgumentList @(
            '--reuse-window',
            $settings
        )
        Write-Host 'Opened settings.json in VS Code.' -ForegroundColor Green
    }
    else {
        Start-Process -FilePath 'notepad.exe' -ArgumentList @($settings)
        Write-Host 'Opened settings.json in Notepad.' -ForegroundColor Green
    }
}

function Show-Help {
    Write-Host @'

Manage Windows Terminal PowerShell

Menu options:
  1  Show current default profile and detected PS5 / PS7 profiles.
  2  Set Windows PowerShell 5.1 as the default Terminal profile.
  3  Set local PowerShell 7 as the default Terminal profile.
  4  Install or update PowerShell 7 from Microsoft Store with winget.
  5  Open Windows Terminal Settings UI (simulates Ctrl+,).
  6  Open Windows Terminal settings.json in VS Code or Notepad.
  H  Show this help.
  Q  Exit.

Command-line usage:
  powershell -ExecutionPolicy Bypass -File .\Manage-WindowsTerminalPowerShell.ps1
  powershell -ExecutionPolicy Bypass -File .\Manage-WindowsTerminalPowerShell.ps1 -Action Status
  powershell -ExecutionPolicy Bypass -File .\Manage-WindowsTerminalPowerShell.ps1 -Action PS5
  powershell -ExecutionPolicy Bypass -File .\Manage-WindowsTerminalPowerShell.ps1 -Action PS7
  powershell -ExecutionPolicy Bypass -File .\Manage-WindowsTerminalPowerShell.ps1 -Action InstallPS7
  powershell -ExecutionPolicy Bypass -File .\Manage-WindowsTerminalPowerShell.ps1 -Action OpenSettings
  powershell -ExecutionPolicy Bypass -File .\Manage-WindowsTerminalPowerShell.ps1 -Action OpenSettingsFile
  powershell -ExecutionPolicy Bypass -File .\Manage-WindowsTerminalPowerShell.ps1 -Action Help

Notes:
  - Every PS5 / PS7 switch backs up settings.json first.
  - PS7 is identified only through:
      Windows.Terminal.PowershellCore
  - Azure Cloud Shell uses:
      Windows.Terminal.Azure
    and cannot be selected by this script.
  - Option 5 requires the current Windows Terminal window to remain active.
  - After changing the default profile, close the current Terminal and open a new one.

'@
}

function Start-Menu {
    do {
        Write-Host '========================================'
        Write-Host ' Windows Terminal PowerShell Manager'
        Write-Host '========================================'
        Write-Host '1. Show status'
        Write-Host '2. Set default to Windows PowerShell 5.1'
        Write-Host '3. Set default to PowerShell 7'
        Write-Host '4. Install PowerShell 7 from Microsoft Store'
        Write-Host '5. Open Windows Terminal Settings UI'
        Write-Host '6. Open Windows Terminal settings.json'
        Write-Host 'H. Help'
        Write-Host 'Q. Exit'
        Write-Host ''

        $choice = (Read-Host 'Select an option').Trim().ToUpperInvariant()

        try {
            switch ($choice) {
                '1' { Show-Status }
                '2' { Set-DefaultProfile -Target PS5 }
                '3' { Set-DefaultProfile -Target PS7 }
                '4' { Install-PS7 }
                '5' { Open-TerminalSettingsUI }
                '6' { Open-TerminalSettingsFile }
                'H' { Show-Help }
                'Q' { break }
                default {
                    Write-Host 'Invalid option.' -ForegroundColor Yellow
                }
            }
        }
        catch {
            Write-Host ''
            Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host ''
        }

        if ($choice -ne 'Q') {
            Read-Host 'Press Enter to continue' | Out-Null
            Write-Host ''
        }
    }
    while ($choice -ne 'Q')
}

switch ($Action) {
    'Menu' {
        Start-Menu
    }
    'Status' {
        Show-Status
    }
    'PS5' {
        Set-DefaultProfile -Target PS5
    }
    'PS7' {
        Set-DefaultProfile -Target PS7
    }
    'InstallPS7' {
        Install-PS7
    }
    'OpenSettings' {
        Open-TerminalSettingsUI
    }
    'OpenSettingsFile' {
        Open-TerminalSettingsFile
    }
    'Help' {
        Show-Help
    }
}
