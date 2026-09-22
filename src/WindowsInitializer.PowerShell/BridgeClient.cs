using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsInitializer.Contracts;

namespace WindowsInitializer.PowerShell;

public sealed class BridgeFailureException(string message, ErrorCode code) : Exception(message)
{
    public ErrorCode Code { get; } = code;
}

public interface IBridgeProcessRunner
{
    Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string executablePath, string fixedCommand, string requestJson, TimeSpan timeout, CancellationToken cancellationToken);
}

public interface IBridgeInvoker
{
    Task<BridgeResponse> InvokeAsync(BridgeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed class BridgeClient : IBridgeInvoker
{
    public const string ProtocolVersion = "1.0";
    public const string ResponseMarker = "__WINDOWS_INITIALIZER_RESPONSE__";
    public const int MaxOutputCharacters = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly HashSet<string> FixedTaskIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "computer-name", "accounts-local", "language-zh-cn", "language-zh-tw", "language-en-us", "language-en-gb",
        "language-zh-sg", "language-zh-hk", "language-ui-preference", "font-supplements-cjk-indic-europe",
        "feature-wireless-display", "feature-wsl", "feature-virtual-machine-platform", "feature-sandbox", "feature-hyper-v",
        "capability-openssh-client", "feature-telnet-client", "setting-hibernation", "safety-restore-point",
        "setting-windows-update-mode", "setting-power-plan", "setting-sleep-timeouts", "device-setup-region", "legacy-god-mode"
    };

    static BridgeClient() => FixedTaskIds.UnionWith(ConfigurableRegistrySettingCatalog.All.Select(setting => setting.TaskId));

    public static bool SupportsTask(string taskId) => !string.IsNullOrWhiteSpace(taskId) && FixedTaskIds.Contains(taskId);

    private const string FixedCommandTemplate = @"
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
if ($null -eq $request.protocolVersion -or $request.protocolVersion -ne '1.0') { throw 'Unsupported bridge protocol.' }
$taskId = [string]$request.taskId
$parameterValue = if ($null -ne $request.parameters -and $null -ne $request.parameters.value) { [string]$request.parameters.value } else { '' }
$accountNames = if ($null -ne $request.parameters -and $null -ne $request.parameters.accountNames) { @([string]$request.parameters.accountNames -split ',') | Where-Object { $_ } } else { @() }
$accounts = @()
if ($null -ne $request.parameters -and $null -ne $request.parameters.accountsJson) {
  try { $accounts = @([string]$request.parameters.accountsJson | ConvertFrom-Json) } catch { $status = 'Failed'; $code = 'InvalidProfile'; $message = 'Account profile payload is invalid.' }
}
$languageTags = @{
  'language-zh-cn' = 'zh-CN'; 'language-zh-tw' = 'zh-TW'; 'language-en-us' = 'en-US'; 'language-en-gb' = 'en-GB'
}
function Get-BridgeInstalledLanguageIds {
  $installed = Get-InstalledLanguage -ErrorAction Stop
  $ids = @(
    foreach ($language in $installed) {
      $packs = @($language.LanguagePacks | Where-Object { $_ -and $_ -ne 'None' })
      if ($packs.Count -gt 0 -and $language.LanguageId) { [string]$language.LanguageId }
    }
  )
  return @($ids | Select-Object -Unique)
}
function ConvertTo-BridgeLanguageTagKey([string]$languageTag) {
  switch -Regex ($languageTag) {
    '^(?i:zh-CN|zh-Hans-CN)$' { return 'zh-Hans-CN' }
    '^(?i:zh-TW|zh-Hant-TW)$' { return 'zh-Hant-TW' }
    default { return $languageTag.ToLowerInvariant() }
  }
}
function Get-BridgeUserLanguageKeys {
  $current = Get-WinUserLanguageList -ErrorAction Stop
  $keys = @()
  for ($index = 0; $index -lt $current.Count; $index++) {
    if ($current[$index].LanguageTag) { $keys += ConvertTo-BridgeLanguageTagKey ([string]$current[$index].LanguageTag) }
  }
  return @($keys)
}
function Add-BridgeUserLanguage([string]$languageTag) {
  $targetKey = ConvertTo-BridgeLanguageTagKey $languageTag
  if (@(Get-BridgeUserLanguageKeys) -contains $targetKey) { return $false }
  $newLanguageList = New-WinUserLanguageList -Language $languageTag -ErrorAction Stop
  $entry = $newLanguageList[0]
  if ($null -eq $entry) { throw 'Could not create the requested user language entry.' }
  $current = Get-WinUserLanguageList -ErrorAction Stop
  [void]$current.Add($entry)
  Set-WinUserLanguageList -LanguageList $current -Force -ErrorAction Stop | Out-Null
  if (@(Get-BridgeUserLanguageKeys) -notcontains $targetKey) { throw 'The user language list did not retain the requested language.' }
  return $true
}
function Get-BridgeLanguageCapabilityNames([string]$languageTag) {
  $names = @(
    ('Language.Basic~~~' + $languageTag + '~0.0.1.0')
    ('Language.Handwriting~~~' + $languageTag + '~0.0.1.0')
    ('Language.OCR~~~' + $languageTag + '~0.0.1.0')
    ('Language.TextToSpeech~~~' + $languageTag + '~0.0.1.0')
    ('Language.Speech~~~' + $languageTag + '~0.0.1.0')
  )
  if ($languageTag -eq 'zh-CN') { $names += 'Language.Fonts.Hans~~~und-HANS~0.0.1.0' }
  if ($languageTag -eq 'zh-TW') { $names += 'Language.Fonts.Hant~~~und-HANT~0.0.1.0' }
  return @($names)
}
function Install-BridgeLanguageCapabilities([string]$languageTag) {
  $changedAny = $false
  $available = @(Get-WindowsCapability -Online -ErrorAction Stop)
  foreach ($name in @(Get-BridgeLanguageCapabilityNames $languageTag)) {
    $capability = @($available | Where-Object Name -eq $name) | Select-Object -First 1
    if ($null -eq $capability -or $capability.State -eq 'Installed') { continue }
    Add-WindowsCapability -Online -Name $name -ErrorAction Stop | Out-Null
    if ((Get-WindowsCapability -Online -Name $name -ErrorAction Stop).State -ne 'Installed') { throw ('Language capability verification failed: ' + $name) }
    $changedAny = $true
  }
  return $changedAny
}
function Get-BridgeSupplementalFontCapabilities {
  return @(Get-WindowsCapability -Online -ErrorAction Stop | Where-Object {
    $_.Name -like 'Language.Fonts.Jpan*' -or $_.Name -like 'Language.Fonts.Kore*' -or
    $_.Name -like 'Language.Fonts.PanEuropeanSupplementalFonts*' -or $_.Name -like 'Language.Fonts.Deva*'
  })
}
$regionalCultures = @{
  'language-zh-sg' = 'zh-SG'; 'language-zh-hk' = 'zh-HK'
}
$optionalFeatures = @{
  'feature-wsl' = 'Microsoft-Windows-Subsystem-Linux'
  'feature-virtual-machine-platform' = 'VirtualMachinePlatform'
  'feature-sandbox' = 'Containers-DisposableClientVM'
  'feature-hyper-v' = 'Microsoft-Hyper-V-All'
  'feature-telnet-client' = 'TelnetClient'
}
$optionalFeatureSets = @{
  'feature-virtual-machine-platform' = @('VirtualMachinePlatform', 'HypervisorPlatform')
}
function Get-BridgeOptionalFeatureNames([string]$taskId) {
  if ($optionalFeatureSets.ContainsKey($taskId)) { return @($optionalFeatureSets[$taskId]) }
  return @($optionalFeatures[$taskId])
}
function Get-BridgeOptionalFeatureStates([string]$taskId) {
  return @(
    foreach ($featureName in (Get-BridgeOptionalFeatureNames $taskId)) {
      $feature = Get-WindowsOptionalFeature -Online -FeatureName $featureName -ErrorAction SilentlyContinue
      [pscustomobject]@{ Name = $featureName; State = if ($null -eq $feature) { 'Unavailable' } else { [string]$feature.State } }
    }
  )
}
$capabilities = @{
  'capability-openssh-client' = 'OpenSSH.Client~~~~0.0.1.0'
}
$registrySettings = @{
  # __CONFIGURABLE_REGISTRY_SETTINGS__
}
function ConvertFrom-BridgeBoolean([string]$value) {
  if ([string]::IsNullOrWhiteSpace($value) -or $value -notin @('enabled', 'disabled', 'default')) { throw 'Registry setting state is invalid.' }
  return $value
}
function Test-BridgeRegistrySetting([hashtable]$setting, [string]$state) {
  $current = Get-ItemPropertyValue -Path $setting.Path -Name $setting.Name -ErrorAction SilentlyContinue
  if ($state -eq 'default') { return $null -eq $current }
  $desired = if ($state -eq 'enabled') { [int]$setting.Enabled } else { [int]$setting.Disabled }
  return $null -ne $current -and [int]$current -eq $desired
}
function Set-BridgeRegistrySetting([hashtable]$setting, [string]$state) {
  if ($state -eq 'default') {
    Remove-ItemProperty -Path $setting.Path -Name $setting.Name -ErrorAction SilentlyContinue
    return
  }
  $desired = if ($state -eq 'enabled') { [int]$setting.Enabled } else { [int]$setting.Disabled }
  New-Item -Path $setting.Path -Force | Out-Null
  Set-ItemProperty -Path $setting.Path -Name $setting.Name -Type DWord -Value $desired -ErrorAction Stop
}
$powerPlans = @{
  'balanced' = '381b4222-f694-41f0-9685-ff5bb260df2e'
  'high-performance' = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
  'power-saver' = 'a1841308-3541-4fab-bc81-f71556f20b4a'
  'ultimate-performance' = 'e9a42b02-d5df-448d-aa00-03f14749eb61'
}
$powercfg = Join-Path $env:SystemRoot 'System32\powercfg.exe'
$wsl = Join-Path $env:SystemRoot 'System32\wsl.exe'
function Get-BridgeWslStatus {
  if (-not (Test-Path -LiteralPath $wsl)) { return @{ Available = $false; DefaultVersion2 = $false; Output = '' } }
  $output = @(& $wsl '--status' 2>&1)
  $text = $output -join [Environment]::NewLine
  $available = $LASTEXITCODE -eq 0
  $defaultVersion2 = $available -and $text -match '(?im)(?:Default\s+Version|默认版本)[^0-9]*2\b'
  return @{ Available = $available; DefaultVersion2 = $defaultVersion2; Output = $text }
}
function Test-BridgeWsl2 {
  $status = Get-BridgeWslStatus
  return $status.Available -and $status.DefaultVersion2
}
function Invoke-BridgeWslCommand([string[]]$arguments, [string]$operation) {
  $output = @(& $wsl @arguments 2>&1)
  if ($LASTEXITCODE -ne 0) { throw ($operation + ' failed: ' + ($output -join ' ')) }
}
function Invoke-BridgeWslBootstrap {
  if (-not (Test-Path -LiteralPath $wsl)) { throw 'WSL executable is unavailable on this Windows edition.' }
  $status = Get-BridgeWslStatus
  if (-not $status.Available) { Invoke-BridgeWslCommand @('--install', '--no-distribution') 'wsl --install --no-distribution' }
  Invoke-BridgeWslCommand @('--set-default-version', '2') 'wsl --set-default-version 2'
  Invoke-BridgeWslCommand @('--update') 'wsl --update'
}
$updatePolicyPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'
function Get-BridgeUpdateMode {
  $value = Get-ItemPropertyValue -Path $updatePolicyPath -Name 'AUOptions' -ErrorAction SilentlyContinue
  if ($null -eq $value) { return 'default' }
  if ([int]$value -eq 2) { return 'notify-download' }
  if ([int]$value -eq 3) { return 'auto-notify-install' }
  return 'other'
}
function Get-BridgeActivePowerPlan {
  $output = @(& $powercfg /getactivescheme 2>&1) -join ' '
  foreach ($key in $powerPlans.Keys) { if ($output -match [regex]::Escape($powerPlans[$key])) { return $key } }
  return 'other'
}
function ConvertFrom-BridgeTimeouts([string]$value) {
  if ($value -notmatch '^ac=(\d{1,4});dc=(\d{1,4});displayAc=(\d{1,4});displayDc=(\d{1,4})$') { throw 'Sleep timeout configuration is invalid.' }
  $result = @{ Ac = [int]$Matches[1]; Dc = [int]$Matches[2]; DisplayAc = [int]$Matches[3]; DisplayDc = [int]$Matches[4] }
  if (@($result.Values | Where-Object { $_ -lt 0 -or $_ -gt 1440 }).Count -gt 0) { throw 'Sleep timeout values must be between 0 and 1440 minutes.' }
  return $result
}
function Get-BridgePowerSetting([string]$subGroup, [string]$setting) {
  $output = @(& $powercfg /query SCHEME_CURRENT $subGroup $setting 2>&1) -join ""`n""
  if ($LASTEXITCODE -ne 0) { throw ('powercfg query failed: ' + $output) }
  $values = @([regex]::Matches($output, '0x[0-9a-fA-F]+') | ForEach-Object { $_.Value })
  if ($values.Count -lt 2) { throw 'powercfg returned an unsupported timeout response.' }
  return @{ Ac = [int]([Convert]::ToUInt32($values[-2].Substring(2), 16) / 60); Dc = [int]([Convert]::ToUInt32($values[-1].Substring(2), 16) / 60) }
}
function Test-BridgeTimeouts([hashtable]$target) {
  $sleep = Get-BridgePowerSetting 'SUB_SLEEP' 'STANDBYIDLE'
  $display = Get-BridgePowerSetting 'SUB_VIDEO' 'VIDEOIDLE'
  return $sleep.Ac -eq $target.Ac -and $sleep.Dc -eq $target.Dc -and $display.Ac -eq $target.DisplayAc -and $display.Dc -eq $target.DisplayDc
}
$godModePath = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'Windows God Mode.{ED7BA470-8E54-465E-825C-99712043E01C}'
$status = 'Ready'; $code = 'None'; $message = 'Task is ready.'; $changed = $false; $reboot = $false
switch ([string]$request.operation) {
  'Check' {
    if ($languageTags.ContainsKey($taskId)) {
      $languageTag = $languageTags[$taskId]
      $installed = @(Get-BridgeInstalledLanguageIds) -contains $languageTag
      $registered = @(Get-BridgeUserLanguageKeys) -contains (ConvertTo-BridgeLanguageTagKey $languageTag)
      if ($installed -and $registered) { $status = 'Skipped'; $message = 'The complete language pack and user language entry are already installed.' } else { $message = 'Language installation or user registration is ready.' }
    } elseif ($regionalCultures.ContainsKey($taskId)) {
      if ((Get-Culture).Name -ieq $regionalCultures[$taskId]) { $status = 'Skipped'; $message = 'The regional format is already configured.' } else { $message = 'Regional format change is ready.' }
    } elseif ($optionalFeatures.ContainsKey($taskId)) {
      $features = @(Get-BridgeOptionalFeatureStates $taskId)
      $allAvailable = @($features | Where-Object State -ne 'Unavailable').Count -eq $features.Count
      $allEnabled = $allAvailable -and @($features | Where-Object State -ne 'Enabled').Count -eq 0
      if (-not $allAvailable) { $status = 'UnsupportedPrerequisite'; $code = 'UnsupportedOperatingSystem'; $message = 'This Windows feature is unavailable on the current edition.' }
      elseif ($allEnabled -and $taskId -eq 'feature-wsl' -and (Test-BridgeWsl2)) { $status = 'Skipped'; $message = 'Windows Subsystem for Linux and WSL 2 are already configured.' }
      elseif ($allEnabled -and $taskId -eq 'feature-wsl') { $message = 'The Windows Subsystem for Linux feature is enabled; the WSL 2 bootstrap is ready to run.' }
      elseif ($allEnabled) { $status = 'Skipped'; $message = 'The Windows feature is already enabled.' } else { $message = 'The Windows feature is ready to be enabled.' }
    } elseif ($capabilities.ContainsKey($taskId)) {
      $cap = Get-WindowsCapability -Online -Name $capabilities[$taskId] -ErrorAction SilentlyContinue
      if ($null -eq $cap) { $status = 'UnsupportedPrerequisite'; $code = 'UnsupportedOperatingSystem'; $message = 'This Windows capability is unavailable.' }
      elseif ($cap.State -eq 'Installed') { $status = 'Skipped'; $message = 'The Windows capability is already installed.' } else { $message = 'The Windows capability is ready to be installed.' }
    } elseif ($registrySettings.ContainsKey($taskId)) {
      $setting = $registrySettings[$taskId]
      try { $state = ConvertFrom-BridgeBoolean $parameterValue } catch { $status = 'Failed'; $code = 'InvalidProfile'; $message = $_.Exception.Message }
      if ($status -ne 'Failed') {
        if (Test-BridgeRegistrySetting $setting $state) { $status = 'Skipped'; $message = 'The Windows setting is already configured.' } else { $message = 'The Windows setting is ready.' }
      }
    } elseif ($taskId -eq 'setting-hibernation') {
      $current = Get-ItemPropertyValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Power' -Name 'HibernateEnabled' -ErrorAction SilentlyContinue
      if ($null -ne $current -and [int]$current -eq 1) { $status = 'Skipped'; $message = 'Hibernation is already enabled.' } else { $message = 'Hibernation is ready to be enabled.' }
    } elseif ($taskId -eq 'safety-restore-point') {
      if (-not (Get-Command -Name Checkpoint-Computer -ErrorAction SilentlyContinue)) { $status = 'UnsupportedPrerequisite'; $code = 'UnsupportedOperatingSystem'; $message = 'System Restore is unavailable on this Windows edition.' }
      else { $message = 'A system restore point is ready to be created.' }
    } elseif ($taskId -eq 'setting-windows-update-mode') {
      if ($parameterValue -notin @('default', 'notify-download', 'auto-notify-install')) { $status = 'Failed'; $code = 'InvalidProfile'; $message = 'Windows Update mode is invalid.' }
      elseif ((Get-BridgeUpdateMode) -eq $parameterValue) { $status = 'Skipped'; $message = 'Windows Update mode is already configured.' } else { $message = 'Windows Update mode is ready.' }
    } elseif ($taskId -eq 'setting-power-plan') {
      if (-not $powerPlans.ContainsKey($parameterValue)) { $status = 'Failed'; $code = 'InvalidProfile'; $message = 'Power plan is invalid.' }
      elseif ((Get-BridgeActivePowerPlan) -eq $parameterValue) { $status = 'Skipped'; $message = 'The requested power plan is already active.' } else { $message = 'Power plan change is ready.' }
    } elseif ($taskId -eq 'setting-sleep-timeouts') {
      try { $timeouts = ConvertFrom-BridgeTimeouts $parameterValue; if (Test-BridgeTimeouts $timeouts) { $status = 'Skipped'; $message = 'Sleep and display timeouts are already configured.' } else { $message = 'Sleep and display timeout changes are ready.' } }
      catch { $status = 'Failed'; $code = 'InvalidProfile'; $message = $_.Exception.Message }
    } elseif ($taskId -eq 'feature-wireless-display') {
      $cap = Get-WindowsCapability -Online -Name 'App.WirelessDisplay.Connect~~~~0.0.1.0' -ErrorAction SilentlyContinue
      if ($null -eq $cap) { $status = 'UnsupportedPrerequisite'; $code = 'UnsupportedOperatingSystem'; $message = 'Wireless display is unavailable on this Windows edition.' }
      elseif ($cap.State -eq 'Installed') { $status = 'Skipped'; $message = 'Wireless display is already enabled.' } else { $message = 'Wireless display capability is ready.' }
    } elseif ($taskId -eq 'font-supplements-cjk-indic-europe') {
      $fonts = @(Get-BridgeSupplementalFontCapabilities)
      $missing = @($fonts | Where-Object State -ne 'Installed')
      if ($fonts.Count -eq 0) { $status = 'UnsupportedPrerequisite'; $code = 'UnsupportedOperatingSystem'; $message = 'Supplemental font capabilities are unavailable on this Windows edition.' }
      elseif ($missing.Count -eq 0) { $status = 'Skipped'; $message = 'Supplemental fonts are already installed.' } else { $message = 'Supplemental fonts are ready.' }
    } elseif ($taskId -eq 'legacy-god-mode') {
      if (Test-Path -LiteralPath $godModePath) { $status = 'Skipped'; $message = 'God Mode folder already exists.' } else { $message = 'God Mode folder is ready.' }
    } elseif ($taskId -eq 'computer-name') {
      if ($parameterValue -notmatch '^[A-Za-z0-9-]{1,15}$') { $status = 'Failed'; $code = 'InvalidProfile'; $message = 'Computer name must be 1-15 ASCII letters, digits, or hyphens.'
      } elseif ([Environment]::MachineName -ieq $parameterValue) { $status = 'Skipped'; $message = 'Computer name is already configured.' } else { $message = 'Computer name change is ready.' }
    } elseif ($taskId -eq 'accounts-local') {
      if ($accountNames.Count -eq 0) { $status = 'Failed'; $code = 'InvalidProfile'; $message = 'At least one account is required.' }
      else { $message = 'Local account changes are ready; account policy cannot be inferred from the account name alone.' }
    } elseif ($taskId -eq 'language-ui-preference') {
      if ($parameterValue -notmatch '^[a-zA-Z]{2,3}(-[a-zA-Z]{2,4})?$') { $status = 'Failed'; $code = 'InvalidProfile'; $message = 'Display language tag is invalid.' }
      elseif (-not (Get-Command -Name Get-WinUILanguageOverride -ErrorAction SilentlyContinue)) { $status = 'Failed'; $code = 'UnsupportedPrerequisite'; $message = 'Windows display language override is unavailable on this edition.' }
      elseif (((Get-WinUILanguageOverride) | ForEach-Object { $_.Name }) -ieq $parameterValue) { $status = 'Skipped'; $message = 'Display language is already configured.' }
      else { $message = 'Display language preference change is ready.' }
    } elseif ($taskId -eq 'device-setup-region') {
      if ($parameterValue -notmatch '^\d{1,4}$') { $status = 'Failed'; $code = 'InvalidProfile'; $message = 'Device setup region GeoID is invalid.' }
      elseif ([int](Get-ItemPropertyValue -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion' -Name 'DeviceRegion' -ErrorAction SilentlyContinue) -eq [int]$parameterValue) { $status = 'Skipped'; $message = 'Device setup region is already configured.' }
      else { $message = 'Device setup region change is ready.' }
    } else {
      $status = 'NeedsManualReview'; $code = 'ManualReviewRequired'; $message = 'No fixed Windows adapter is enabled for this operation.'
    }
  }
  'Apply' {
    if ($optionalFeatures.ContainsKey($taskId)) {
      $features = @(Get-BridgeOptionalFeatureStates $taskId)
      if (@($features | Where-Object State -eq 'Unavailable').Count -gt 0) { throw 'This Windows feature is unavailable on the current edition.' }
      $featureWasEnabled = @($features | Where-Object State -ne 'Enabled').Count -eq 0
      if (-not $featureWasEnabled) {
        foreach ($feature in @($features | Where-Object State -ne 'Enabled')) {
          Enable-WindowsOptionalFeature -Online -FeatureName $feature.Name -All -NoRestart -ErrorAction Stop | Out-Null
        }
        $changed = $true; $reboot = $true
        $message = if ($taskId -eq 'feature-wsl') { 'Windows Subsystem for Linux enabled; reboot and run the task again to complete WSL 2 bootstrap.' } else { 'Windows feature enabled.' }
      } elseif ($taskId -eq 'feature-wsl' -and -not (Test-BridgeWsl2)) {
        Invoke-BridgeWslBootstrap
        $changed = $true; $message = 'WSL 2 bootstrap completed.'
      } else {
        $message = 'Windows feature is already configured.'
      }
    } elseif ($capabilities.ContainsKey($taskId)) {
      Add-WindowsCapability -Online -Name $capabilities[$taskId] -ErrorAction Stop | Out-Null; $changed = $true; $message = 'Windows capability installed.'
    } elseif ($registrySettings.ContainsKey($taskId)) {
      $setting = $registrySettings[$taskId]
      try { $state = ConvertFrom-BridgeBoolean $parameterValue } catch { $status = 'Failed'; $code = 'InvalidProfile'; $message = $_.Exception.Message }
      if ($status -ne 'Failed') {
        if ($taskId -eq 'setting-fast-startup' -and $state -eq 'enabled') {
          $hibernateOutput = @(& $powercfg /hibernate on 2>&1)
          if ($LASTEXITCODE -ne 0) { throw ('powercfg failed to enable hibernation for fast startup: ' + ($hibernateOutput -join ' ')) }
        }
        Set-BridgeRegistrySetting $setting $state
        $changed = $true; $reboot = $taskId -eq 'setting-long-paths'; $message = 'Windows setting configured.'
      }
    } elseif ($taskId -eq 'setting-hibernation') {
      $powercfgOutput = @(& ""$env:SystemRoot\System32\powercfg.exe"" /hibernate on 2>&1)
      if ($LASTEXITCODE -ne 0) { throw ('powercfg failed to enable hibernation: ' + ($powercfgOutput -join ' ')) }
      $changed = $true; $message = 'Hibernation enabled.'
    } elseif ($taskId -eq 'safety-restore-point') {
      Checkpoint-Computer -Description 'WISK pre-change checkpoint' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
      $changed = $true; $message = 'System restore point created.'
    } elseif ($taskId -eq 'setting-windows-update-mode') {
      if ($parameterValue -notin @('default', 'notify-download', 'auto-notify-install')) { throw 'Windows Update mode is invalid.' }
      New-Item -Path $updatePolicyPath -Force | Out-Null
      if ($parameterValue -eq 'default') { Remove-ItemProperty -Path $updatePolicyPath -Name 'AUOptions' -ErrorAction SilentlyContinue }
      else { Set-ItemProperty -Path $updatePolicyPath -Name 'AUOptions' -Type DWord -Value $(if ($parameterValue -eq 'notify-download') { 2 } else { 3 }) -ErrorAction Stop }
      $changed = $true; $message = 'Windows Update mode configured.'
    } elseif ($taskId -eq 'setting-power-plan') {
      if (-not $powerPlans.ContainsKey($parameterValue)) { throw 'Power plan is invalid.' }
      $targetGuid = $powerPlans[$parameterValue]
      if ($parameterValue -eq 'ultimate-performance' -and -not ((@(& $powercfg /list 2>&1) -join ' ') -match [regex]::Escape($targetGuid))) {
        $duplicateOutput = @(& $powercfg /duplicatescheme $targetGuid 2>&1); if ($LASTEXITCODE -ne 0) { throw ('Could not create the Ultimate Performance plan: ' + ($duplicateOutput -join ' ')) }
        $created = [regex]::Match(($duplicateOutput -join ' '), '[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}').Value; if ($created) { $targetGuid = $created }
      }
      $output = @(& $powercfg /setactive $targetGuid 2>&1); if ($LASTEXITCODE -ne 0) { throw ('Could not activate the power plan: ' + ($output -join ' ')) }
      $changed = $true; $message = 'Power plan activated.'
    } elseif ($taskId -eq 'setting-sleep-timeouts') {
      $timeouts = ConvertFrom-BridgeTimeouts $parameterValue
      foreach ($arguments in @(@('/change','standby-timeout-ac',[string]$timeouts.Ac), @('/change','standby-timeout-dc',[string]$timeouts.Dc), @('/change','monitor-timeout-ac',[string]$timeouts.DisplayAc), @('/change','monitor-timeout-dc',[string]$timeouts.DisplayDc))) {
        $output = @(& $powercfg @arguments 2>&1); if ($LASTEXITCODE -ne 0) { throw ('Could not configure a power timeout: ' + ($output -join ' ')) }
      }
      $changed = $true; $message = 'Sleep and display timeouts configured.'
    } elseif ($languageTags.ContainsKey($taskId)) {
      $languageTag = $languageTags[$taskId]
      if (@(Get-BridgeInstalledLanguageIds) -notcontains $languageTag) {
        Install-Language -Language $languageTag -ErrorAction Stop | Out-Null
        if (@(Get-BridgeInstalledLanguageIds) -notcontains $languageTag) { throw 'The language installation command returned without a complete language pack.' }
        $changed = $true
      }
      $featuresChanged = Install-BridgeLanguageCapabilities $languageTag
      if ($featuresChanged) { $changed = $true }
      $userLanguageChanged = Add-BridgeUserLanguage $languageTag
      if ($userLanguageChanged) { $changed = $true }
      $reboot = $changed; $message = if ($changed) { 'Language pack, features, and user language entry were installed and verified.' } else { 'The language is already completely installed.' }
    } elseif ($regionalCultures.ContainsKey($taskId)) {
      Set-Culture -CultureInfo $regionalCultures[$taskId] -ErrorAction Stop | Out-Null; $changed = $true; $message = 'Regional format configured.'
    } elseif ($taskId -eq 'feature-wireless-display') {
      Add-WindowsCapability -Online -Name 'App.WirelessDisplay.Connect~~~~0.0.1.0' -ErrorAction Stop | Out-Null; $changed = $true; $message = 'Wireless display capability enabled.'
    } elseif ($taskId -eq 'font-supplements-cjk-indic-europe') {
      $fonts = @(Get-BridgeSupplementalFontCapabilities)
      if ($fonts.Count -eq 0) { throw 'Supplemental font capabilities are unavailable on this Windows edition.' }
      $missing = @($fonts | Where-Object State -ne 'Installed')
      foreach ($font in $missing) { Add-WindowsCapability -Online -Name $font.Name -ErrorAction Stop | Out-Null }
      $changed = $missing.Count -gt 0; $message = if ($changed) { 'Supplemental fonts installed.' } else { 'Supplemental fonts are already installed.' }
    } elseif ($taskId -eq 'accounts-local') {
      if ($accounts.Count -eq 0) { $status = 'Failed'; $code = 'InvalidProfile'; $message = 'Account profile payload is required.' }
      else {
        foreach ($account in $accounts) {
          if ([string]$account.name -notmatch '^[A-Za-z0-9._-]{1,20}$') { throw 'Account name is invalid.' }
          $existing = Get-LocalUser -Name ([string]$account.name) -ErrorAction SilentlyContinue
          if ($null -eq $existing) {
            if ([string]::IsNullOrWhiteSpace([string]$account.password)) { throw 'A password is required for a new local account.' }
            $securePassword = ConvertTo-SecureString ([string]$account.password) -AsPlainText -Force
            New-LocalUser -Name ([string]$account.name) -Password $securePassword -FullName ([string]$account.fullName) -Description ([string]$account.description) -PasswordNeverExpires:$([bool]$account.passwordNeverExpires) | Out-Null
          } elseif (-not [string]::IsNullOrWhiteSpace([string]$account.password)) {
            $securePassword = ConvertTo-SecureString ([string]$account.password) -AsPlainText -Force
            Set-LocalUser -Name ([string]$account.name) -Password $securePassword
          }
          $existing = Get-LocalUser -Name ([string]$account.name) -ErrorAction Stop
          Set-LocalUser -Name $existing.Name -FullName ([string]$account.fullName) -Description ([string]$account.description) -PasswordNeverExpires:$([bool]$account.passwordNeverExpires) -ErrorAction Stop | Out-Null
          $groupSid = [string]$account.groupSid
          if ($groupSid -and $groupSid -notin @('S-1-5-32-544', 'S-1-5-32-545')) { throw 'Account group SID is invalid.' }
          $requestedGroupSids = @($groupSid) | Where-Object { $_ }
          if ([bool]$account.allowRemoteDesktop) { $requestedGroupSids += 'S-1-5-32-555' }
          foreach ($requestedGroupSid in $requestedGroupSids) {
            $group = Get-LocalGroup -SID ([System.Security.Principal.SecurityIdentifier]::new($requestedGroupSid)) -ErrorAction Stop
            $isMember = @(Get-LocalGroupMember -Group $group -ErrorAction Stop | Where-Object { $_.SID -eq $existing.SID }).Count -gt 0
            if (-not $isMember) { Add-LocalGroupMember -Group $group -Member $existing -ErrorAction Stop | Out-Null }
          }
          if ([bool]$account.hideFromSignInScreen) {
            $userListPath = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList'
            New-Item -Path $userListPath -Force | Out-Null
            Set-ItemProperty -Path $userListPath -Name $existing.Name -Type DWord -Value 0
          }
        }
        $changed = $true; $message = 'Local account changes completed.'
      }
    } elseif ($taskId -eq 'computer-name') {
      if ($parameterValue -notmatch '^[A-Za-z0-9-]{1,15}$') { $status = 'Failed'; $code = 'InvalidProfile'; $message = 'Computer name must be 1-15 ASCII letters, digits, or hyphens.' }
      else { Rename-Computer -NewName $parameterValue -Force -ErrorAction Stop | Out-Null; $changed = $true; $reboot = $true; $message = 'Computer name change completed.' }
    } elseif ($taskId -eq 'language-ui-preference') {
      if ($parameterValue -notmatch '^[a-zA-Z]{2,3}(-[a-zA-Z]{2,4})?$') { $status = 'Failed'; $code = 'InvalidProfile'; $message = 'Display language tag is invalid.' }
      else {
        if (-not (Get-Command -Name Set-WinUILanguageOverride -ErrorAction SilentlyContinue)) { throw 'Set-WinUILanguageOverride is unavailable on this Windows edition.' }
        $culture = [System.Globalization.CultureInfo]::GetCultureInfo($parameterValue)
        Set-WinUILanguageOverride -Language $culture -ErrorAction Stop | Out-Null
        $changed = $true; $reboot = $true; $message = 'Display language preference configured for ' + $culture.Name + '.'
      }
    } elseif ($taskId -eq 'device-setup-region') {
      if ($parameterValue -notmatch '^\d{1,4}$') { $status = 'Failed'; $code = 'InvalidProfile'; $message = 'Device setup region GeoID is invalid.' }
      else { New-Item -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion' -Force | Out-Null; Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion' -Name 'DeviceRegion' -Type DWord -Value ([int]$parameterValue); $changed = $true; $reboot = $true; $message = 'Device setup region configured.' }
    } elseif ($taskId -eq 'legacy-god-mode') {
      New-Item -ItemType Directory -Path $godModePath -Force | Out-Null; $changed = $true; $message = 'God Mode folder created.'
    } else {
      $status = 'NeedsManualReview'; $code = 'ManualReviewRequired'; $message = 'No fixed Windows adapter is enabled for this operation.'
    }
  }
  'Verify' {
    if ($optionalFeatures.ContainsKey($taskId)) { $features = @(Get-BridgeOptionalFeatureStates $taskId); $enabled = $features.Count -gt 0 -and @($features | Where-Object State -ne 'Enabled').Count -eq 0; $status = if ($enabled -and ($taskId -ne 'feature-wsl' -or (Test-BridgeWsl2))) { 'Succeeded' } else { 'Failed' }; $message = if ($taskId -eq 'feature-wsl') { 'Windows Subsystem for Linux and WSL 2 verification completed.' } else { 'Windows feature verification completed.' }
    } elseif ($capabilities.ContainsKey($taskId)) { $status = if ((Get-WindowsCapability -Online -Name $capabilities[$taskId] -ErrorAction SilentlyContinue).State -eq 'Installed') { 'Succeeded' } else { 'Failed' }; $message = 'Windows capability verification completed.'
    } elseif ($registrySettings.ContainsKey($taskId)) { $setting = $registrySettings[$taskId]; try { $state = ConvertFrom-BridgeBoolean $parameterValue; $status = if (Test-BridgeRegistrySetting $setting $state) { 'Succeeded' } else { 'Failed' }; $message = 'Windows setting verification completed.' } catch { $status = 'Failed'; $code = 'InvalidProfile'; $message = $_.Exception.Message }
    } elseif ($taskId -eq 'setting-hibernation') { $current = Get-ItemPropertyValue -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Power' -Name 'HibernateEnabled' -ErrorAction SilentlyContinue; $status = if ($null -ne $current -and [int]$current -eq 1) { 'Succeeded' } else { 'Failed' }; $message = 'Hibernation verification completed.'
    } elseif ($taskId -eq 'safety-restore-point') { $points = @(Get-ComputerRestorePoint -ErrorAction SilentlyContinue | Where-Object Description -eq 'WISK pre-change checkpoint'); $status = if ($points.Count -gt 0) { 'Succeeded' } else { 'Failed' }; $message = 'System restore point verification completed.'
    } elseif ($taskId -eq 'setting-windows-update-mode') { $status = if ((Get-BridgeUpdateMode) -eq $parameterValue) { 'Succeeded' } else { 'Failed' }; $message = 'Windows Update mode verification completed.'
    } elseif ($taskId -eq 'setting-power-plan') { $status = if ((Get-BridgeActivePowerPlan) -eq $parameterValue) { 'Succeeded' } else { 'Failed' }; $message = 'Power plan verification completed.'
    } elseif ($taskId -eq 'setting-sleep-timeouts') { try { $timeouts = ConvertFrom-BridgeTimeouts $parameterValue; $status = if (Test-BridgeTimeouts $timeouts) { 'Succeeded' } else { 'Failed' }; $message = 'Sleep and display timeout verification completed.' } catch { $status = 'Failed'; $message = $_.Exception.Message }
    } elseif ($languageTags.ContainsKey($taskId)) { $languageTag = $languageTags[$taskId]; $installed = @(Get-BridgeInstalledLanguageIds) -contains $languageTag; $registered = @(Get-BridgeUserLanguageKeys) -contains (ConvertTo-BridgeLanguageTagKey $languageTag); $status = if ($installed -and $registered) { 'Succeeded' } else { 'Failed' }; $code = if ($status -eq 'Failed') { 'VerificationFailed' } else { 'None' }; $message = 'Complete language pack and user language registration verification completed.'
    } elseif ($regionalCultures.ContainsKey($taskId)) { $status = if ((Get-Culture).Name -ieq $regionalCultures[$taskId]) { 'Succeeded' } else { 'Failed' }; $message = 'Regional format verification completed.'
    } elseif ($taskId -eq 'feature-wireless-display') { $status = if ((Get-WindowsCapability -Online -Name 'App.WirelessDisplay.Connect~~~~0.0.1.0' -ErrorAction SilentlyContinue).State -eq 'Installed') { 'Succeeded' } else { 'Failed' }; $message = 'Wireless display verification completed.'
    } elseif ($taskId -eq 'font-supplements-cjk-indic-europe') { $fonts = @(Get-BridgeSupplementalFontCapabilities); $status = if ($fonts.Count -gt 0 -and @($fonts | Where-Object State -ne 'Installed').Count -eq 0) { 'Succeeded' } else { 'Failed' }; $code = if ($status -eq 'Failed') { 'VerificationFailed' } else { 'None' }; $message = 'Supplemental font verification completed.'
    } elseif ($taskId -eq 'accounts-local') { $status = if ($accountNames.Count -gt 0 -and @($accountNames | Where-Object { $null -eq (Get-LocalUser -Name $_ -ErrorAction SilentlyContinue) }).Count -eq 0) { 'Succeeded' } else { 'Failed' }; $code = if ($status -eq 'Failed') { 'VerificationFailed' } else { 'None' }; $message = 'Local account verification completed.'
    } elseif ($taskId -eq 'computer-name') { $status = if ($parameterValue -and ([Environment]::MachineName -ieq $parameterValue)) { 'Succeeded' } else { 'Failed' }; $code = if ($status -eq 'Failed') { 'VerificationFailed' } else { 'None' }; $message = 'Computer name verification completed.'
    } elseif ($taskId -eq 'language-ui-preference') { $status = if ((Get-Command -Name Get-WinUILanguageOverride -ErrorAction SilentlyContinue) -and $parameterValue -and (((Get-WinUILanguageOverride) | ForEach-Object { $_.Name }) -ieq $parameterValue)) { 'Succeeded' } else { 'Failed' }; $code = if ($status -eq 'Failed') { 'VerificationFailed' } else { 'None' }; $message = 'Display language verification completed.'
    } elseif ($taskId -eq 'device-setup-region') { $status = if ($parameterValue -and ([int](Get-ItemPropertyValue -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion' -Name 'DeviceRegion' -ErrorAction SilentlyContinue) -eq [int]$parameterValue)) { 'Succeeded' } else { 'Failed' }; $code = if ($status -eq 'Failed') { 'VerificationFailed' } else { 'None' }; $message = 'Device setup region verification completed.'
    } elseif ($taskId -eq 'legacy-god-mode') { $status = if (Test-Path -LiteralPath $godModePath) { 'Succeeded' } else { 'Failed' }; $message = 'God Mode verification completed.'
    } else { $status = 'NeedsManualReview'; $code = 'ManualReviewRequired'; $message = 'No fixed Windows adapter is enabled for this operation.' }
  }
  default { $status = 'Failed'; $code = 'InvalidProfile'; $message = 'Unsupported bridge operation.'; $changed = $false; $reboot = $false }
}
if ($status -eq 'Failed' -and $code -eq 'None') { $code = 'VerificationFailed' }
$responseJson = [pscustomobject]@{ protocolVersion = '1.0'; runId = [string]$request.runId; taskId = [string]$request.taskId; status = $status; code = $code; message = $message; changed = $changed; rebootRequired = $reboot; summary = $message } | ConvertTo-Json -Compress
[Console]::Out.WriteLine('__WINDOWS_INITIALIZER_RESPONSE__' + $responseJson)
";

    private static readonly string FixedCommand = BuildFixedCommand();
    private readonly IBridgeProcessRunner _runner;

    private static string BuildFixedCommand()
    {
        var entries = ConfigurableRegistrySettingCatalog.All.Select(setting =>
        {
            var path = $"{setting.Hive}:\\{setting.Path}";
            return $"  '{PowerShellLiteral(setting.TaskId)}' = @{{ Path = '{PowerShellLiteral(path)}'; Name = '{PowerShellLiteral(setting.ValueName)}'; Enabled = {setting.EnabledValue}; Disabled = {setting.DisabledValue} }}";
        });
        return FixedCommandTemplate.Replace("  # __CONFIGURABLE_REGISTRY_SETTINGS__", string.Join(Environment.NewLine, entries), StringComparison.Ordinal);
    }

    private static string PowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    public BridgeClient(IBridgeProcessRunner? runner = null) => _runner = runner ?? new PowerShellProcessRunner();

    public string ExecutablePath => Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe");

    public async Task<BridgeResponse> InvokeAsync(BridgeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        var result = await _runner.RunAsync(ExecutablePath, FixedCommand, requestJson, timeout, cancellationToken).ConfigureAwait(false);
        if (result.Stdout.Length > MaxOutputCharacters || result.Stderr.Length > 8 * 1024)
            throw new BridgeFailureException("PowerShell bridge output exceeded the safety limit.", ErrorCode.ProcessFailed);
        if (result.ExitCode != 0)
            throw new BridgeFailureException(ProcessFailureMessage(result.Stderr), MapProcessError(result.ExitCode));
        var payloads = result.Stdout.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(line => line.Trim()).Where(line => line.StartsWith(ResponseMarker, StringComparison.Ordinal)).ToArray();
        if (payloads.Length != 1)
            throw new BridgeFailureException("PowerShell bridge returned no unique framed response.", ErrorCode.ProcessFailed);
        BridgeResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<BridgeResponse>(payloads[0][ResponseMarker.Length..], JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new BridgeFailureException($"PowerShell bridge returned invalid JSON: {exception.Message}", ErrorCode.ProcessFailed);
        }
        if (response is null || response.ProtocolVersion != ProtocolVersion || response.RunId != request.RunId || response.TaskId != request.TaskId)
            throw new BridgeFailureException("PowerShell bridge response failed correlation validation.", ErrorCode.ProcessFailed);
        return response with { Message = Redact(response.Message), Summary = Redact(response.Summary) };
    }

    private static void ValidateRequest(BridgeRequest request)
    {
        if (request.ProtocolVersion != ProtocolVersion || !IsSafeIdentifier(request.RunId) || !SupportsTask(request.TaskId))
            throw new BridgeFailureException("Invalid bridge request envelope.", ErrorCode.InvalidProfile);
        if (request.Operation is not ("Check" or "Apply" or "Verify"))
            throw new BridgeFailureException("Unsupported bridge operation.", ErrorCode.InvalidProfile);
        var acceptsValue = ConfigurableRegistrySettingCatalog.Find(request.TaskId) is not null || request.TaskId is
            "computer-name" or "language-ui-preference" or "device-setup-region" or "setting-windows-update-mode" or
            "setting-power-plan" or "setting-sleep-timeouts";
        var allowedParameters = request.TaskId.Equals("accounts-local", StringComparison.OrdinalIgnoreCase)
            ? request.Operation == "Apply" ? new[] { "accountNames", "accountsJson" } : ["accountNames"]
            : acceptsValue ? ["value"] : [];
        foreach (var parameter in request.Parameters.Keys)
        {
            if (!allowedParameters.Contains(parameter, StringComparer.Ordinal))
                throw new BridgeFailureException("The bridge request contains an unsupported parameter.", ErrorCode.InvalidProfile);
            if (parameter.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                parameter.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                parameter.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                parameter.Contains("private", StringComparison.OrdinalIgnoreCase))
                throw new BridgeFailureException("Secret values cannot be sent to the PowerShell bridge.", ErrorCode.InvalidProfile);
            if (request.Parameters[parameter] is null || request.Parameters[parameter].Length > 64 * 1024)
                throw new BridgeFailureException("The bridge parameter exceeds the safety limit.", ErrorCode.InvalidProfile);
        }
    }

    private static bool IsSafeIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static string Redact(string value) =>
        value.Replace("password", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
             .Replace("token", "[REDACTED]", StringComparison.OrdinalIgnoreCase)
             .Replace("secret", "[REDACTED]", StringComparison.OrdinalIgnoreCase);

    private static string ProcessFailureMessage(string stderr)
    {
        var detail = stderr.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(detail)) return "PowerShell bridge process failed.";
        detail = Redact(detail);
        return "PowerShell bridge process failed: " + (detail.Length > 1024 ? detail[..1024] : detail);
    }

    private static ErrorCode MapProcessError(int exitCode) => exitCode switch
    {
        1223 => ErrorCode.Cancelled,
        5 => ErrorCode.AccessDenied,
        _ => ErrorCode.ProcessFailed
    };
}

public sealed class PowerShellProcessRunner : IBridgeProcessRunner
{
    public async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string executablePath, string fixedCommand, string requestJson, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-Command");
        // Keep the command line bounded. The trusted script is the first stdin frame;
        // its existing ReadToEnd consumes only the subsequent JSON request.
        process.StartInfo.ArgumentList.Add("$bridgeSource = [Console]::In.ReadLine(); & ([scriptblock]::Create([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($bridgeSource))))");
        try
        {
            if (!process.Start()) throw new BridgeFailureException("Could not start PowerShell.", ErrorCode.MissingPowerShell);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.StandardInput.WriteLineAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(fixedCommand)).AsMemory(), timeoutCts.Token).ConfigureAwait(false);
            await process.StandardInput.WriteAsync(requestJson.AsMemory(), timeoutCts.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            return (process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw new BridgeFailureException("PowerShell bridge timed out or was cancelled.", cancellationToken.IsCancellationRequested ? ErrorCode.Cancelled : ErrorCode.Timeout);
        }
        catch (Win32Exception exception)
        {
            throw new BridgeFailureException($"PowerShell is unavailable: {exception.Message}", ErrorCode.MissingPowerShell);
        }
    }
}
