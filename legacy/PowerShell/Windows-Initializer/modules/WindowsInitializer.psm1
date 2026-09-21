Set-StrictMode -Version Latest

$script:RootPath = Split-Path -Parent $PSScriptRoot
$script:CatalogPath = Join-Path $script:RootPath 'config\catalog.psd1'
$script:LogPath = $null
$script:RestartRequired = $false

function Write-InitializerMessage {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('Info', 'Success', 'Warning', 'Error')][string]$Level = 'Info'
    )

    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $line = "[$stamp] [$Level] $Message"
    $color = @{ Info = 'Gray'; Success = 'Green'; Warning = 'Yellow'; Error = 'Red' }[$Level]
    Write-Host $line -ForegroundColor $color

    if ($script:LogPath) {
        [System.IO.File]::AppendAllText($script:LogPath, $line + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    }
}

function New-InitializerLog {
    $logDirectory = Join-Path $script:RootPath 'logs'
    if (-not (Test-Path -LiteralPath $logDirectory)) {
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    }

    $script:LogPath = Join-Path $logDirectory ('Initialize-Windows-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    [System.IO.File]::WriteAllText($script:LogPath, "Windows Initializer log`r`n", [System.Text.UTF8Encoding]::new($false))
    Write-InitializerMessage -Message "日志：$script:LogPath"
}

function Set-InitializerLogPath {
    param([Parameter(Mandatory)][string]$Path)
    $script:LogPath = $Path
}

function Get-InitializerRestartRequired {
    return [bool]$script:RestartRequired
}

function Test-InitializerAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-InitializerPlatformInfo {
    $currentVersion = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    [PSCustomObject]@{
        ProductName = $currentVersion.ProductName
        DisplayVersion = $currentVersion.DisplayVersion
        Build = [int]$currentVersion.CurrentBuildNumber
        UBR = $currentVersion.UBR
        Is64BitProcess = [Environment]::Is64BitProcess
        IsAdministrator = Test-InitializerAdministrator
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    }
}

function Get-InitializerGeoRegionInfo {
    param([Parameter(Mandatory)][ValidateRange(1, 65535)][int]$GeoId)

    if (-not ('WindowsInitializer.NativeGeo' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
namespace WindowsInitializer {
    public static class NativeGeo {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetGeoInfo(int location, int geoType, StringBuilder data, int dataLength, int langId);
    }
}
'@
    }

    $nameBuffer = [Text.StringBuilder]::new(256)
    # GEO_FRIENDLYNAME = 8. 仅用于验证 GeoID 并显示名称，不写入任何用户区域设置。
    $length = [WindowsInitializer.NativeGeo]::GetGeoInfo($GeoId, 8, $nameBuffer, $nameBuffer.Capacity, 0)
    if ($length -le 0) {
        $nativeError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw "无效或不受支持的 Windows GeoID：$GeoId（Win32 错误 $nativeError）。"
    }
    return [PSCustomObject]@{ GeoId = $GeoId; Name = $nameBuffer.ToString() }
}

function Get-InitializerDeviceSetupRegionGeoId {
    $registryPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion'
    try { return [int](Get-ItemPropertyValue -Path $registryPath -Name 'DeviceRegion' -ErrorAction Stop) }
    catch { throw "无法读取 Device setup region：$($_.Exception.Message)" }
}

function Test-InitializerSupportedWindows {
    param([switch]$AllowUnsupportedWindows)

    $platform = Get-InitializerPlatformInfo
    # Windows 11 still reports "Windows 10" in this registry value on many installs.
    # The build number is the reliable compatibility gate for this tool.
    $isSupported = $platform.Build -ge 26200
    if (-not $isSupported -and -not $AllowUnsupportedWindows) {
        throw "此工具的目标环境为 Windows 11 25H2（内部版本 26200）及更高版本；检测到 $($platform.ProductName) build $($platform.Build)。如确需测试，请使用 -AllowUnsupportedWindows。"
    }
    return $platform
}

function Get-InitializerCatalog {
    if (-not (Test-Path -LiteralPath $script:CatalogPath)) {
        throw "找不到清单文件：$script:CatalogPath"
    }
    $catalog = Import-PowerShellDataFile -LiteralPath $script:CatalogPath
    if (-not $catalog.ContainsKey('Items')) {
        throw '清单文件缺少 Items。'
    }
    Write-Output -NoEnumerate $catalog
}

function Get-InitializerItem {
    param([Parameter(Mandatory)][string]$Id)

    $item = (Get-InitializerCatalog).Items | Where-Object { $_.Id -eq $Id } | Select-Object -First 1
    if ($null -eq $item) { throw "未知项目 ID：$Id" }
    return $item
}

function Get-InitializerDefaultTaskIds {
    return @((Get-InitializerCatalog).Items | Where-Object { $_.Default -eq $true } | ForEach-Object { $_.Id })
}

function Get-InitializerTaskDefinition {
    param([Parameter(Mandatory)][string]$Id)
    return Get-InitializerItem -Id $Id
}

function Get-InitializerCatalogItems {
    $items = (Get-InitializerCatalog).Items
    foreach ($item in $items) {
        [PSCustomObject]$item
    }
}

function Get-InitializerProfile {
    param([Parameter(Mandatory)][string]$ProfilePath)

    $resolved = Resolve-Path -LiteralPath $ProfilePath -ErrorAction Stop
    if ([IO.Path]::GetExtension($resolved.Path) -ne '.psd1') {
        throw '配置文件必须是 .psd1 PowerShell 数据文件。'
    }
    $profile = Import-PowerShellDataFile -LiteralPath $resolved.Path
    if (-not $profile.ContainsKey('Tasks')) { throw '配置文件缺少 Tasks。' }
    Write-Output -NoEnumerate $profile
}

function Merge-InitializerContext {
    param([hashtable]$Profile, [hashtable]$Configuration)

    $context = @{}
    if ($Profile) {
        foreach ($key in $Profile.Keys) { $context[$key] = $Profile[$key] }
    }
    if ($Configuration) {
        foreach ($key in $Configuration.Keys) { $context[$key] = $Configuration[$key] }
    }
    # Empty hashtables are enumerated unexpectedly by Write-Output in PowerShell 7.
    # Unary comma preserves the context object for both an empty and populated plan.
    return ,$context
}

function Add-InitializerResolvedTask {
    param(
        [Parameter(Mandatory)][string]$TaskId,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Resolved,
        [Parameter(Mandatory)][hashtable]$Visiting
    )

    if ($Resolved.Contains($TaskId)) { return }
    if ($Visiting.ContainsKey($TaskId)) {
        throw "检测到项目依赖循环：$($Visiting.Keys -join ' -> ') -> $TaskId"
    }

    $Visiting[$TaskId] = $true
    try {
        $item = Get-InitializerItem -Id $TaskId
        $dependencies = if ($item.ContainsKey('DependsOn')) { @($item.DependsOn) } else { @() }
        foreach ($dependency in $dependencies) {
            if ($dependency) { Add-InitializerResolvedTask -TaskId $dependency -Resolved $Resolved -Visiting $Visiting }
        }
        [void]$Resolved.Add($TaskId)
    }
    finally {
        [void]$Visiting.Remove($TaskId)
    }
}

function Resolve-InitializerTasks {
    param([string[]]$Tasks, [hashtable]$Profile)

    if ($Profile -and $Profile.ContainsKey('Tasks')) { $Tasks = @($Profile.Tasks) }
    if (-not $Tasks -or $Tasks.Count -eq 0) { $Tasks = Get-InitializerDefaultTaskIds }

    $legacyAliases = @{
        'capability-wireless-zhcn' = 'feature-wireless-display'
        'capability-zhhk' = 'language-zh-hk'
        'capability-zhtw' = 'language-zh-tw'
    }
    $normalized = foreach ($task in $Tasks) {
        if ($legacyAliases.ContainsKey($task)) { $legacyAliases[$task] } else { $task }
    }
    $unique = @($normalized | Select-Object -Unique)
    $resolved = [System.Collections.Generic.List[string]]::new()
    $visiting = @{}
    foreach ($task in $unique) {
        Add-InitializerResolvedTask -TaskId $task -Resolved $resolved -Visiting $visiting
    }

    # 同一互斥组中的项目不能在同一次运行中并存；例如两个区域格式最后只能保留一个，
    # 因此要在执行前明确拒绝，而不是让后一个项目静默覆盖前一个项目。
    $exclusiveGroups = @{}
    foreach ($taskId in $resolved) {
        $item = Get-InitializerItem -Id $taskId
        if ($item.ContainsKey('ExclusiveGroup') -and $item.ExclusiveGroup) {
            $group = [string]$item.ExclusiveGroup
            if ($exclusiveGroups.ContainsKey($group)) {
                $previousTaskId = [string]$exclusiveGroups[$group]
                $groupName = if ($item.ContainsKey('ExclusiveGroupName') -and $item.ExclusiveGroupName) { [string]$item.ExclusiveGroupName } else { $group }
                throw "项目互斥：$previousTaskId 与 $taskId 不能同时选择。请每次只选择一个 [$groupName] 项目。"
            }
            $exclusiveGroups[$group] = $taskId
        }
    }
    return @($resolved)
}

function Get-InitializerResolvedTaskIds {
    param([Parameter(Mandatory)][string[]]$Tasks)
    return Resolve-InitializerTasks -Tasks $Tasks -Profile $null
}

function ConvertTo-InitializerPath {
    param([Parameter(Mandatory)][string]$Value, [hashtable]$Context)

    $assetRoot = Join-Path $script:RootPath 'assets'
    if ($Context -and $Context.ContainsKey('AssetRoot') -and $Context.AssetRoot) {
        $assetRoot = $Context.AssetRoot
        if (-not [IO.Path]::IsPathRooted($assetRoot)) { $assetRoot = Join-Path $script:RootPath $assetRoot }
    }

    $replacements = [ordered]@{
        '$AssetRoot' = $assetRoot
        '$ProgramFiles' = $env:ProgramFiles
        '$PublicDesktop' = (Join-Path $env:PUBLIC 'Desktop')
        '$SystemRoot' = $env:SystemRoot
    }
    $result = $Value
    foreach ($key in $replacements.Keys) { $result = $result.Replace($key, [string]$replacements[$key]) }
    return $result
}

function Test-InitializerWinget {
    return $null -ne (Get-Command winget.exe -ErrorAction SilentlyContinue) -or $null -ne (Get-Command winget -ErrorAction SilentlyContinue)
}

function Test-InitializerWingetPackageInstalled {
    param([Parameter(Mandatory)][string]$PackageId)

    $output = & winget list --exact --id $PackageId --disable-interactivity 2>&1
    return (($output -join [Environment]::NewLine) -match [regex]::Escape($PackageId))
}

function Invoke-InitializerNativeCommand {
    param([Parameter(Mandatory)][string]$FilePath, [string[]]$Arguments)

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "命令失败（退出码 $LASTEXITCODE）：$FilePath $($Arguments -join ' ')" }
}

function Invoke-InitializerWinget {
    param([hashtable]$Item, [hashtable]$Context)

    if (-not (Test-InitializerWinget)) { throw '未找到 winget。请先通过 Microsoft Store 安装或更新 App Installer。' }
    if (Test-InitializerWingetPackageInstalled -PackageId $Item.PackageId) {
        Write-InitializerMessage -Level Success -Message "$($Item.Name)：已安装，跳过。"
        return
    }

    $arguments = @('install', '--exact', '--id', $Item.PackageId, '--accept-source-agreements', '--accept-package-agreements', '--disable-interactivity')
    if ($Item.ContainsKey('Source') -and $Item.Source) { $arguments += @('--source', $Item.Source) }
    if ($Item.ContainsKey('Scope') -and $Item.Scope) { $arguments += @('--scope', $Item.Scope) }
    if ($Context -and $Context.ContainsKey('Proxy') -and $Context.Proxy) {
        & winget settings --enable ProxyCommandLineOptions | Out-Null
        if ($LASTEXITCODE -ne 0) { throw '无法启用 winget 代理命令行选项。' }
        $arguments += @('--proxy', $Context.Proxy)
    }

    Write-InitializerMessage -Message "安装 $($Item.Name)（$($Item.PackageId)）"
    Invoke-InitializerNativeCommand -FilePath 'winget' -Arguments $arguments
}

function Invoke-InitializerOfflineOperations {
    param([hashtable]$Item, [hashtable]$Context)

    foreach ($operation in @($Item.Operations)) {
        $type = $operation.Type
        switch ($type) {
            'ExpandArchive' {
                $archive = ConvertTo-InitializerPath -Value $operation.Asset -Context $Context
                $destination = ConvertTo-InitializerPath -Value $operation.Destination -Context $Context
                if (-not (Test-Path -LiteralPath $archive)) { throw "缺少离线资产：$archive" }
                New-Item -ItemType Directory -Path $destination -Force | Out-Null
                Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
            }
            'CopyFile' {
                $source = ConvertTo-InitializerPath -Value $operation.Asset -Context $Context
                $destination = ConvertTo-InitializerPath -Value $operation.Destination -Context $Context
                if (-not (Test-Path -LiteralPath $source)) { throw "缺少离线资产：$source" }
                New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
                Copy-Item -LiteralPath $source -Destination $destination -Force
            }
            'StartProcess' {
                $file = ConvertTo-InitializerPath -Value $operation.FilePath -Context $Context
                if (-not (Test-Path -LiteralPath $file)) { throw "找不到要启动的文件：$file" }
                $parameters = @{ FilePath = $file; Wait = $true; PassThru = $true }
                if ($operation.ContainsKey('Arguments') -and $operation.Arguments) { $parameters.ArgumentList = $operation.Arguments }
                if ($operation.ContainsKey('Verb') -and $operation.Verb) { $parameters.Verb = $operation.Verb }
                $process = Start-Process @parameters
                if ($null -ne $process -and $process.ExitCode -ne 0) { throw "安装程序退出码为 $($process.ExitCode)：$file" }
            }
            'CreateShortcut' {
                $shortcutPath = ConvertTo-InitializerPath -Value $operation.ShortcutPath -Context $Context
                $targetPath = ConvertTo-InitializerPath -Value $operation.TargetPath -Context $Context
                if (-not (Test-Path -LiteralPath $targetPath)) { throw "快捷方式目标不存在：$targetPath" }
                New-Item -ItemType Directory -Path (Split-Path -Parent $shortcutPath) -Force | Out-Null
                $shell = New-Object -ComObject WScript.Shell
                $shortcut = $shell.CreateShortcut($shortcutPath)
                $shortcut.TargetPath = $targetPath
                if ($operation.ContainsKey('WorkingDirectory')) { $shortcut.WorkingDirectory = ConvertTo-InitializerPath -Value $operation.WorkingDirectory -Context $Context }
                if ($operation.ContainsKey('IconLocation')) { $shortcut.IconLocation = ConvertTo-InitializerPath -Value $operation.IconLocation -Context $Context }
                $shortcut.Save()
            }
            'CreateGodModeFolder' {
                $path = ConvertTo-InitializerPath -Value $operation.Path -Context $Context
                if (-not (Test-Path -LiteralPath $path)) { New-Item -ItemType Directory -Path $path | Out-Null }
            }
            default { throw "不支持的离线操作类型：$type" }
        }
    }
}

function Invoke-InitializerFeatureSet {
    param([hashtable]$Item)

    foreach ($featureName in @($Item.Features)) {
        $feature = Get-WindowsOptionalFeature -Online -FeatureName $featureName -ErrorAction Stop
        if ($feature.State -eq 'Enabled') {
            Write-InitializerMessage -Level Success -Message "Windows 功能已启用：$featureName"
            continue
        }
        Write-InitializerMessage -Message "启用 Windows 功能：$featureName"
        Enable-WindowsOptionalFeature -Online -FeatureName $featureName -All -NoRestart -ErrorAction Stop | Out-Null
        $script:RestartRequired = $true
    }

    if ($Item.ContainsKey('PostCommands') -and $Item.PostCommands -contains 'WSLSetup') {
        Invoke-InitializerNativeCommand -FilePath 'wsl.exe' -Arguments @('--install', '--no-distribution')
        Invoke-InitializerNativeCommand -FilePath 'wsl.exe' -Arguments @('--set-default-version', '2')
        Invoke-InitializerNativeCommand -FilePath 'wsl.exe' -Arguments @('--update')
        $script:RestartRequired = $true
    }
}

function Invoke-InitializerCapabilitySet {
    param([hashtable]$Item)

    $capabilityNames = @()
    if ($Item.ContainsKey('Capabilities')) { $capabilityNames += @($Item.Capabilities) }
    if ($Item.ContainsKey('CapabilityPatterns')) {
        $availableCapabilities = @(Get-WindowsCapability -Online -ErrorAction Stop)
        foreach ($pattern in @($Item.CapabilityPatterns)) {
            $matches = @($availableCapabilities | Where-Object { $_.Name -like $pattern })
            if ($matches.Count -eq 0) {
                Write-InitializerMessage -Level Warning -Message "当前 Windows 映像未提供能力：$pattern"
                continue
            }
            $capabilityNames += @($matches | ForEach-Object { $_.Name })
        }
    }

    foreach ($capabilityName in @($capabilityNames | Select-Object -Unique)) {
        $capability = Get-WindowsCapability -Online -Name $capabilityName -ErrorAction Stop
        if ($capability.State -eq 'Installed') {
            Write-InitializerMessage -Level Success -Message "Windows 能力已安装：$capabilityName"
            continue
        }
        Write-InitializerMessage -Message "安装 Windows 能力：$capabilityName"
        Add-WindowsCapability -Online -Name $capabilityName -ErrorAction Stop | Out-Null
    }
}

function Invoke-InitializerFontSupplementSet {
    param([Parameter(Mandatory)][hashtable]$Item, [hashtable]$Context)

    if (-not $Context -or -not $Context.ContainsKey('FontSupplementIds') -or -not $Context.FontSupplementIds) {
        throw '字体补充项目需要在 G 配置中选择至少一种字体。'
    }
    $selectedIds = @($Context.FontSupplementIds | Select-Object -Unique)
    $options = @($Item.FontOptions)
    $selectedOptions = @($options | Where-Object { $selectedIds -contains $_.Id })
    $unknownIds = @($selectedIds | Where-Object { $options.Id -notcontains $_ })
    if ($unknownIds.Count -gt 0) { throw "未知字体补充选项：$($unknownIds -join '、')" }
    if ($selectedOptions.Count -eq 0) { throw '字体补充项目未选择任何有效字体。' }

    $optionIndex = 0
    foreach ($option in $selectedOptions) {
        $optionIndex++
        Write-InitializerMessage -Message "字体补充 [$optionIndex/$($selectedOptions.Count)]：$($option.Name)"
        $capabilityItem = @{
            Name = "字体补充：$($option.Name)"
            CapabilityPatterns = @($option.CapabilityPatterns)
        }
        Invoke-InitializerCapabilitySet -Item $capabilityItem
    }
}

function Get-InitializerInstalledLanguageIds {
    $installed = Get-InstalledLanguage -ErrorAction Stop
    $languageIds = @(
        foreach ($language in $installed) {
            $languagePacks = @($language.LanguagePacks | Where-Object { $_ -and $_ -ne 'None' })
            if ($languagePacks.Count -gt 0 -and $language.LanguageId) { $language.LanguageId }
        }
    )
    return @($languageIds | Select-Object -Unique)
}

function Get-InitializerLanguageFeatureCapabilityNames {
    param([Parameter(Mandatory)][string]$LanguageTag)

    # Keep Basic first: the other language Features on Demand depend on it.
    $capabilityNames = @(
        "Language.Basic~~~$LanguageTag~0.0.1.0"
        "Language.Handwriting~~~$LanguageTag~0.0.1.0"
        "Language.OCR~~~$LanguageTag~0.0.1.0"
        "Language.TextToSpeech~~~$LanguageTag~0.0.1.0"
        "Language.Speech~~~$LanguageTag~0.0.1.0"
    )

    # Chinese supplemental fonts are separate Features on Demand. They are needed
    # for the language to be complete on a clean Windows installation.
    switch ($LanguageTag) {
        'zh-CN' { $capabilityNames += 'Language.Fonts.Hans~~~und-HANS~0.0.1.0' }
        'zh-TW' { $capabilityNames += 'Language.Fonts.Hant~~~und-HANT~0.0.1.0' }
    }

    return @($capabilityNames)
}

function Invoke-InitializerLanguageFeatures {
    param([Parameter(Mandatory)][string]$LanguageTag)

    $availableCapabilities = @(Get-WindowsCapability -Online -ErrorAction Stop)
    foreach ($capabilityName in Get-InitializerLanguageFeatureCapabilityNames -LanguageTag $LanguageTag) {
        $capability = @($availableCapabilities | Where-Object { $_.Name -eq $capabilityName }) | Select-Object -First 1
        if (-not $capability) {
            # Feature availability legitimately differs by language and Windows build.
            Write-InitializerMessage -Level Warning -Message "此 Windows 映像未提供语言功能，已跳过：$capabilityName"
            continue
        }
        if ($capability.State -eq 'Installed') {
            Write-InitializerMessage -Level Success -Message "语言功能已安装：$capabilityName"
            continue
        }

        Write-InitializerMessage -Message "补齐语言功能：$capabilityName"
        Add-WindowsCapability -Online -Name $capabilityName -ErrorAction Stop | Out-Null
        $verifiedCapability = Get-WindowsCapability -Online -Name $capabilityName -ErrorAction Stop
        if ($verifiedCapability.State -ne 'Installed') {
            throw "语言功能安装命令已返回，但未验证为已安装：$capabilityName（当前状态：$($verifiedCapability.State)）"
        }
        Write-InitializerMessage -Level Success -Message "已验证语言功能：$capabilityName"
        $script:RestartRequired = $true
    }
}

function Invoke-InitializerLanguagePack {
    param([hashtable]$Item)

    $languageTag = $Item.LanguageTag
    if ((Get-InitializerInstalledLanguageIds) -contains $languageTag) {
        Write-InitializerMessage -Level Success -Message "语言已安装：$languageTag"
    }
    else {
        Write-InitializerMessage -Message "安装语言包：$languageTag"
        Install-Language -Language $languageTag -ErrorAction Stop | Out-Null
        if ((Get-InitializerInstalledLanguageIds) -notcontains $languageTag) {
            throw "语言安装命令已返回，但未检测到完整语言包：$languageTag。该语言可能不可用、下载未完成，或仅安装了可选语言功能。"
        }
        Write-InitializerMessage -Level Success -Message "已验证完整语言包：$languageTag"
        $script:RestartRequired = $true
    }

    # Install-Language normally includes the available Features on Demand. Re-check
    # them even for a pre-existing language pack, otherwise a prior partial install
    # leaves Settings showing Download buttons while this tool reports success.
    Invoke-InitializerLanguageFeatures -LanguageTag $languageTag

    # Install-Language 负责安装系统语言包；Windows 设置中的“首选语言”是当前用户的另一份列表。
    # 即使语言包已存在，也要补登记，以修复旧版本只安装包、不显示语言卡片的情况。
    Add-InitializerLanguageToUserLanguageList -LanguageTag $languageTag
}

function Invoke-InitializerRegionalFormat {
    param([hashtable]$Item)

    $culture = if ($Item.ContainsKey('Culture') -and $Item.Culture) { [string]$Item.Culture } else { $null }
    if (-not $culture) {
        throw '区域格式配置缺少 Culture。'
    }

    try {
        [void][System.Globalization.CultureInfo]::GetCultureInfo($culture)
    }
    catch {
        throw "无效的区域格式 Culture：$culture"
    }

    Set-Culture -CultureInfo $culture -ErrorAction Stop

    # Set-Culture 更新的是当前用户的持久化区域设置；Worker 进程中的
    # Get-Culture 可能仍保留进程启动时的旧 CultureInfo（例如 en-GB），
    # 因此不能直接用当前进程的 Get-Culture 判断写入是否成功。
    $internationalPath = 'HKCU:\Control Panel\International'
    $actualCulture = [string](Get-ItemProperty -Path $internationalPath -Name LocaleName -ErrorAction Stop).LocaleName
    if (-not [string]::Equals($actualCulture, $culture, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "区域格式写入后未生效：期望 $culture，用户设置实际为 $actualCulture。"
    }

    Write-InitializerMessage -Level Success -Message "已验证当前用户区域格式：$culture。未修改 Windows 显示语言、首选语言、Country or region、Home location 或 Device setup region。"
}

function Get-InitializerCurrentUserLanguageTags {
    $currentList = Get-WinUserLanguageList -ErrorAction Stop
    $tags = @()
    for ($index = 0; $index -lt $currentList.Count; $index++) {
        if ($currentList[$index].LanguageTag) { $tags += [string]$currentList[$index].LanguageTag }
    }
    return @($tags)
}

function Add-InitializerLanguageToUserLanguageList {
    param([Parameter(Mandatory)][string]$LanguageTag)

    $newLanguageList = New-WinUserLanguageList -Language $LanguageTag
    $languageEntry = $newLanguageList[0]
    if (-not $languageEntry) { throw "无法创建当前用户语言条目：$LanguageTag" }

    # Get-WinUserLanguageList 返回可直接追加的 List<T>；不重新创建它，以保留现有语言的输入法及排序。
    $currentList = Get-WinUserLanguageList -ErrorAction Stop
    $registeredKeys = @(Get-InitializerCurrentUserLanguageTags | ForEach-Object { ConvertTo-InitializerLanguageTagKey -LanguageTag $_ })
    $entryKey = ConvertTo-InitializerLanguageTagKey -LanguageTag $LanguageTag
    if ($registeredKeys -contains $entryKey) {
        Write-InitializerMessage -Message "当前用户 Windows 设置语言列表已包含：$LanguageTag"
        return
    }

    [void]$currentList.Add($languageEntry)
    Set-WinUserLanguageList -LanguageList $currentList -Force -ErrorAction Stop
    $verifiedKeys = @(Get-InitializerCurrentUserLanguageTags | ForEach-Object { ConvertTo-InitializerLanguageTagKey -LanguageTag $_ })
    if ($verifiedKeys -notcontains $entryKey) {
        throw "Windows 设置语言列表未保留语言条目：$LanguageTag。系统拒绝或回退了该用户语言配置。"
    }
    Write-InitializerMessage -Level Success -Message "已验证并登记到当前用户 Windows 设置语言列表：$LanguageTag"
}

function ConvertTo-InitializerLanguageTagKey {
    param([Parameter(Mandatory)][string]$LanguageTag)

    # Windows 在 Settings 中可能把中文区域标签展开为显式脚本标签，例如 zh-CN -> zh-Hans-CN。
    # 对用户语言列表进行等价比较，避免同一语言被重复登记。
    switch -Regex ($LanguageTag) {
        '^(?i:zh-CN|zh-Hans-CN)$' { return 'zh-Hans-CN' }
        '^(?i:zh-TW|zh-Hant-TW)$' { return 'zh-Hant-TW' }
        default { return $LanguageTag.ToLowerInvariant() }
    }
}

function Get-InitializerExecutionDisplayName {
    param([Parameter(Mandatory)][hashtable]$Item, [hashtable]$Context)

    if ($Item.Kind -eq 'FontSupplementSet' -and $Context -and $Context.ContainsKey('FontSupplementIds')) {
        $selectedIds = @($Context.FontSupplementIds)
        $names = @($Item.FontOptions | Where-Object { $selectedIds -contains $_.Id } | ForEach-Object { $_.Name })
        if ($names.Count -gt 0) { return "字体补充（$($names -join '、')）" }
    }
    return $Item.Name
}

function Invoke-InitializerLanguageSettings {
    param([hashtable]$Context)

    if (-not $Context.ContainsKey('LanguagePreference') -or -not $Context.LanguagePreference) {
        throw '“设置 Windows 显示语言范围”需要执行前配置。'
    }

    $preference = $Context.LanguagePreference
    $targetLanguage = $preference.TargetLanguage
    if (-not $targetLanguage) { throw '显示语言配置缺少 TargetLanguage。' }
    if ((Get-InitializerInstalledLanguageIds) -notcontains $targetLanguage) {
        throw "目标语言尚未安装完整显示语言包：$targetLanguage。请同时选择对应 LanguagePack 项目。"
    }

    if ($preference.ApplyToCurrentUser) {
        Set-WinUILanguageOverride -Language $targetLanguage -ErrorAction Stop
        Write-InitializerMessage -Message "已设置当前用户显示语言：$targetLanguage"
        $script:RestartRequired = $true
    }
    if ($preference.ApplyToSystem) {
        Set-SystemPreferredUILanguage -Language $targetLanguage -ErrorAction Stop
        Write-InitializerMessage -Message "已设置系统首选 UI 语言：$targetLanguage"
        $script:RestartRequired = $true
    }
    if ($preference.SyncToWelcomeAndNewUsers) {
        if (-not $preference.ApplyToCurrentUser) {
            throw '同步欢迎界面与新账户前，必须同时选择“当前用户显示语言”。'
        }
        Copy-UserInternationalSettingsToSystem -WelcomeScreen $true -NewUser $true -ErrorAction Stop
        Write-InitializerMessage -Message '已将当前用户的语言、输入和区域设置同步到欢迎界面与新账户。'
        $script:RestartRequired = $true
    }
}

function Get-InitializerLocalGroupBySid {
    param([Parameter(Mandatory)][string]$Sid)
    return Get-LocalGroup -SID $Sid -ErrorAction Stop
}

function Add-InitializerLocalGroupMemberIfMissing {
    param([Parameter(Mandatory)][string]$GroupSid, [Parameter(Mandatory)][string]$AccountName)

    $group = Get-InitializerLocalGroupBySid -Sid $GroupSid
    $members = @(Get-LocalGroupMember -SID $group.SID -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
    if ($members -notcontains "$env:COMPUTERNAME\$AccountName") {
        Add-LocalGroupMember -SID $group.SID -Member $AccountName -ErrorAction Stop
    }
}

function Set-InitializerAccountSignInVisibility {
    param([Parameter(Mandatory)][string]$AccountName, [Parameter(Mandatory)][bool]$HideFromSignInScreen)

    $userListPath = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList'
    if ($HideFromSignInScreen) {
        New-Item -Path $userListPath -Force -ErrorAction Stop | Out-Null
        New-ItemProperty -LiteralPath $userListPath -Name $AccountName -PropertyType DWord -Value 0 -Force -ErrorAction Stop | Out-Null
        Write-InitializerMessage -Message "已从本机登录界面隐藏账户：$AccountName"
    }
    else {
        Remove-ItemProperty -LiteralPath $userListPath -Name $AccountName -ErrorAction SilentlyContinue
        Write-InitializerMessage -Message "已恢复在本机登录界面显示账户：$AccountName"
    }
}

function ConvertTo-InitializerAccountPassword {
    param([Parameter(Mandatory)][hashtable]$Account)

    if ($Account.ContainsKey('Password') -and $Account.Password -is [Security.SecureString]) {
        return $Account.Password
    }
    if ($Account.ContainsKey('Password') -and $Account.Password -is [string] -and -not [string]::IsNullOrWhiteSpace($Account.Password)) {
        return ConvertTo-SecureString -String $Account.Password -AsPlainText -Force
    }
    throw "账户 $($Account.Name) 缺少 Password。请在账户编辑器中设置密码，或在配置文件中提供明文 Password。"
}

function Invoke-InitializerAccountSetup {
    param([hashtable]$Context)

    if (-not $Context -or -not $Context.ContainsKey('Accounts') -or -not $Context.Accounts) {
        throw '“创建或更新本地账户”必须在 G 中配置账户，或在配置文件中提供 Accounts。'
    }
    $accounts = @($Context.Accounts)

    foreach ($account in $accounts) {
        if (-not $account.ContainsKey('Name')) { throw '账户定义缺少 Name。' }
        $existing = Get-LocalUser -Name $account.Name -ErrorAction SilentlyContinue
        $setPassword = $account.ContainsKey('Password') -and $null -ne $account.Password
        if (-not $setPassword -and $null -eq $existing) {
            throw "账户 $($account.Name) 当前不存在且未提供 Password；请在账户编辑器中设置密码，或在配置文件中提供明文 Password。"
        }
        $password = if ($setPassword) { ConvertTo-InitializerAccountPassword -Account $account } else { $null }

        if ($null -eq $existing) {
            Write-InitializerMessage -Message "创建本地账户：$($account.Name)"
            New-LocalUser -Name $account.Name -Password $password -FullName $account.FullName -Description $account.Description -ErrorAction Stop | Out-Null
        }
        else {
            Write-InitializerMessage -Message "更新现有本地账户：$($account.Name)"
            $updateParameters = @{ Name = $account.Name; FullName = $account.FullName; Description = $account.Description; ErrorAction = 'Stop' }
            if ($setPassword) { $updateParameters.Password = $password }
            Set-LocalUser @updateParameters
        }

        if ($account.ContainsKey('PasswordNeverExpires')) {
            Set-LocalUser -Name $account.Name -PasswordNeverExpires ([bool]$account.PasswordNeverExpires) -ErrorAction Stop
        }

        if ($account.ContainsKey('GroupSid') -and $account.GroupSid) {
            Add-InitializerLocalGroupMemberIfMissing -GroupSid $account.GroupSid -AccountName $account.Name
        }
        if ($account.ContainsKey('AllowRemoteDesktop') -and [bool]$account.AllowRemoteDesktop) {
            Add-InitializerLocalGroupMemberIfMissing -GroupSid 'S-1-5-32-555' -AccountName $account.Name
            Write-InitializerMessage -Message "已授予远程桌面用户组权限：$($account.Name)"
        }
        if ($account.ContainsKey('HideFromSignInScreen')) {
            Set-InitializerAccountSignInVisibility -AccountName $account.Name -HideFromSignInScreen ([bool]$account.HideFromSignInScreen)
        }
    }
}

function Invoke-InitializerComputerName {
    param([hashtable]$Context, [switch]$Interactive)

    $name = $null
    if ($Context -and $Context.ContainsKey('ComputerName')) { $name = $Context.ComputerName }
    if (-not $name -and $Interactive) { $name = Read-Host '请输入新的 Windows 主机名' }
    if (-not $name) { throw '主机名项目需要在配置文件中指定 ComputerName，或从交互菜单中执行。' }
    if ($env:COMPUTERNAME -eq $name) {
        Write-InitializerMessage -Level Success -Message "主机名已是 $name，跳过。"
        return
    }
    Rename-Computer -NewName $name -Force -ErrorAction Stop
    $script:RestartRequired = $true
    Write-InitializerMessage -Level Success -Message "主机名将从 $env:COMPUTERNAME 更改为 $name；重启后生效。"
}

function Invoke-InitializerDeviceSetupRegion {
    param([hashtable]$Context)

    if (-not $Context -or -not $Context.ContainsKey('DeviceSetupRegionGeoId')) {
        throw 'Device setup region 项目需要在配置文件中指定 DeviceSetupRegionGeoId，或从交互菜单中执行。'
    }
    $targetGeoId = [int]$Context.DeviceSetupRegionGeoId
    $targetRegion = Get-InitializerGeoRegionInfo -GeoId $targetGeoId
    $currentGeoId = Get-InitializerDeviceSetupRegionGeoId
    if ($currentGeoId -eq $targetGeoId) {
        Write-InitializerMessage -Level Success -Message "Device setup region 已是：$($targetRegion.Name) ($targetGeoId)，跳过。"
        return
    }

    $registryPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion'
    try {
        New-Item -Path $registryPath -Force -ErrorAction Stop | Out-Null
        New-ItemProperty -Path $registryPath -Name 'DeviceRegion' -PropertyType DWord -Value $targetGeoId -Force -ErrorAction Stop | Out-Null
    }
    catch {
        if ($_.Exception.Message -match '(?i)unauthorized|access.*denied|拒绝') {
            throw 'Device setup region 写入被拒绝。该键可能受 UCPD 或组织策略保护；本工具不会修改 UCPD，请先自行处理相关策略后重试。'
        }
        throw
    }
    $verifiedGeoId = Get-InitializerDeviceSetupRegionGeoId
    if ($verifiedGeoId -ne $targetGeoId) {
        throw "Device setup region 写入后验证失败：期望 $targetGeoId，实际 $verifiedGeoId。"
    }

    $script:RestartRequired = $true
    Write-InitializerMessage -Level Success -Message "Device setup region 已从 GeoID $currentGeoId 修改为 $($targetRegion.Name) ($targetGeoId)。"
    Write-InitializerMessage -Level Warning -Message '本工具未修改 UCPD；若 UCPD 或组织策略仍在管理该值，后续可能拒绝或重置此修改。建议先自行处理 UCPD，再重启验证。'
}

function Test-InitializerAssetAvailability {
    param([hashtable]$Item, [hashtable]$Context)
    if ($Item.Kind -ne 'Offline') { return $true }
    foreach ($operation in @($Item.Operations)) {
        if ($operation.ContainsKey('Asset')) {
            $asset = ConvertTo-InitializerPath -Value $operation.Asset -Context $Context
            if (-not (Test-Path -LiteralPath $asset)) { return $false }
        }
    }
    return $true
}

function Show-InitializerStatus {
    param([switch]$AllowUnsupportedWindows)
    $platform = Get-InitializerPlatformInfo
    Write-Host ''
    Write-Host 'Windows Initializer 状态' -ForegroundColor Cyan
    $platform | Format-List | Out-Host
    Write-Host "winget: $(if (Test-InitializerWinget) { '可用' } else { '未找到' })"
    try {
        $installedLanguageIds = @(Get-InitializerInstalledLanguageIds)
        Write-Host "系统已安装语言包：$(if ($installedLanguageIds) { $installedLanguageIds -join '、' } else { '未检测到' })"
        $userLanguageTags = @(Get-WinUserLanguageList | ForEach-Object { $_.LanguageTag })
        Write-Host "当前用户 Preferred languages：$(if ($userLanguageTags) { $userLanguageTags -join '、' } else { '未设置' })"
        Write-Host "当前用户 UI 覆盖：$(if (Get-WinUILanguageOverride) { (Get-WinUILanguageOverride).Name } else { '未设置' })"
        Write-Host "系统首选 UI 语言：$(Get-SystemPreferredUILanguage)"
    }
    catch { Write-Host "语言状态读取失败：$($_.Exception.Message)" -ForegroundColor Yellow }
    try {
        $deviceGeoId = Get-InitializerDeviceSetupRegionGeoId
        $deviceRegion = Get-InitializerGeoRegionInfo -GeoId $deviceGeoId
        Write-Host "Device setup region：$($deviceRegion.Name) ($deviceGeoId)"
    }
    catch { Write-Host "Device setup region 读取失败：$($_.Exception.Message)" -ForegroundColor Yellow }
    if ($platform.Build -lt 26200) {
        Write-Host '警告：当前系统不在 Windows 11 25H2+ 的目标范围内。' -ForegroundColor Yellow
    }
    if (-not $platform.IsAdministrator) { Write-Host '警告：应用系统初始化需要管理员权限。' -ForegroundColor Yellow }
}

function Show-InitializerCatalog {
    $items = (Get-InitializerCatalog).Items | Sort-Object Category, Name
    $items | ForEach-Object { [PSCustomObject]$_ } | Select-Object Id, Category, Name, Kind, Default, Risk, RequiresReboot, RequiresInternet | Format-Table -AutoSize | Out-Host
    Write-Host ''
    Write-Host 'Risk: Standard = 普通安装； Elevated = 系统级变更； High = 安全、授权或兼容性风险较高，必须显式允许。' -ForegroundColor Yellow
}

function Show-InitializerPlan {
    param([string[]]$Tasks, [string]$ProfilePath, [hashtable]$Configuration, [switch]$AllowRisky)
    $profile = $null
    if ($ProfilePath) { $profile = Get-InitializerProfile -ProfilePath $ProfilePath }
    $selected = Resolve-InitializerTasks -Tasks $Tasks -Profile $profile
    $context = Merge-InitializerContext -Profile $profile -Configuration $Configuration
    $items = foreach ($id in $selected) { Get-InitializerItem -Id $id }

    Write-Host ''
    Write-Host '执行计划（尚未执行）' -ForegroundColor Cyan
    $displayItems = @(
        foreach ($item in $items) {
            $displayItem = [ordered]@{}
            foreach ($key in $item.Keys) { $displayItem[$key] = $item[$key] }
            $displayItem.Name = Get-InitializerExecutionDisplayName -Item $item -Context $context
            [PSCustomObject]$displayItem
        }
    )
    $displayItems | Select-Object Id, Category, Name, Kind, Risk, RequiresReboot, RequiresInternet | Format-Table -AutoSize | Out-Host
    $selectedLanguagePackTags = @($items | Where-Object { $_.Kind -eq 'LanguagePack' } | ForEach-Object { $_.LanguageTag })
    if ($selectedLanguagePackTags.Count -gt 0) {
        Write-Host "完整语言包：验证成功后将追加到当前用户 Windows 设置的首选语言列表：$($selectedLanguagePackTags -join '、')" -ForegroundColor DarkGray
        Write-Host '会补齐当前 Windows 映像实际提供的 Basic、手写、OCR、文本转语音、语音识别及适用字体功能。' -ForegroundColor DarkGray
        Write-Host '不会改变现有语言排序、Windows 显示语言、系统首选 UI 语言或地区设置。' -ForegroundColor DarkGray
    }
    $selectedRegionalFormats = @($items | Where-Object { $_.Kind -eq 'RegionalFormat' })
    if ($selectedRegionalFormats.Count -gt 0) {
        $formatSummary = @($selectedRegionalFormats | ForEach-Object { $_.Culture }) -join '、'
        Write-Host "区域格式：将修改当前用户的日期、数字和货币格式：$formatSummary" -ForegroundColor DarkGray
        Write-Host '不会安装独立 Windows 显示语言包，不会加入 Windows 设置的“首选语言”，也不会改变显示语言、Country or region、Home location 或 Device setup region。' -ForegroundColor DarkGray
        Write-Host '如需对应的中文显示语言，请另选简体中文（中国）或繁体中文（台湾）；区域格式项目不会自动安装语言包。' -ForegroundColor DarkGray
    }
    if ($selected -contains 'computer-name') {
        if ($context.ContainsKey('ComputerName') -and $context.ComputerName) { Write-Host "主机名配置：$($context.ComputerName)" -ForegroundColor Cyan }
        else { Write-Host '主机名配置：执行前需要输入目标主机名。' -ForegroundColor Yellow }
    }
    if ($selected -contains 'device-setup-region') {
        if ($context.ContainsKey('DeviceSetupRegionGeoId')) {
            try {
                $deviceRegion = Get-InitializerGeoRegionInfo -GeoId ([int]$context.DeviceSetupRegionGeoId)
                Write-Host "Device setup region 配置：$($deviceRegion.Name) ($($deviceRegion.GeoId))" -ForegroundColor Cyan
            }
            catch { Write-Host "Device setup region 配置无效：$($_.Exception.Message)" -ForegroundColor Yellow }
            Write-Host '实验性项目：仅修改 HKLM DeviceRegion；不会修改 Home location、Country or region、区域格式或 UCPD。可能被 UCPD/策略拦截或后续重置，失败不会影响其他初始化项目。' -ForegroundColor Yellow
        }
        else { Write-Host 'Device setup region 配置：执行前需要选择目标地区。' -ForegroundColor Yellow }
    }
    if ($selected -contains 'accounts-local') {
        if ($context.ContainsKey('Accounts') -and $context.Accounts) {
            $accountNames = @($context.Accounts | ForEach-Object { $_.Name }) -join '、'
            Write-Host "账户配置：$accountNames（密码已在账户管理器或配置文件中提供）" -ForegroundColor Cyan
        }
        else { Write-Host '账户配置：执行前需要选择和编辑账户定义。' -ForegroundColor Yellow }
    }
    if ($selected -contains 'font-supplements-cjk-indic-europe') {
        if ($context.ContainsKey('FontSupplementIds') -and $context.FontSupplementIds) {
            $fontItem = Get-InitializerItem -Id 'font-supplements-cjk-indic-europe'
            $fontNames = @($fontItem.FontOptions | Where-Object { @($context.FontSupplementIds) -contains $_.Id } | ForEach-Object { $_.Name })
            Write-Host "字体补充配置：$($fontNames -join '、')" -ForegroundColor Cyan
        }
        else { Write-Host '字体补充配置：执行前需要选择至少一种字体。' -ForegroundColor Yellow }
    }
    if ($selected -contains 'language-ui-preference') {
        if ($context.ContainsKey('LanguagePreference') -and $context.LanguagePreference) {
            $preference = $context.LanguagePreference
            $scopes = @()
            if ($preference.ApplyToCurrentUser) { $scopes += '当前用户' }
            if ($preference.ApplyToSystem) { $scopes += '系统首选 UI' }
            if ($preference.SyncToWelcomeAndNewUsers) { $scopes += '欢迎界面与新账户' }
            Write-Host "显示语言配置：$($preference.TargetLanguage)；范围：$($scopes -join '、')" -ForegroundColor Cyan
        }
        else { Write-Host '显示语言配置：执行前需要选择目标语言与应用范围。' -ForegroundColor Yellow }
    }
    foreach ($item in $items) {
        if ($item.Risk -eq 'High' -and -not $AllowRisky) { Write-Host "高风险项目尚未获准：$($item.Id)" -ForegroundColor Yellow }
        if (-not (Test-InitializerAssetAvailability -Item $item -Context $context)) { Write-Host "缺少离线资产：$($item.Id)" -ForegroundColor Yellow }
    }
}

function Invoke-InitializerTask {
    param([Parameter(Mandatory)][hashtable]$Item, [hashtable]$Context, [switch]$Interactive)

    switch ($Item.Kind) {
        'Winget' { Invoke-InitializerWinget -Item $Item -Context $Context }
        'Offline' { Invoke-InitializerOfflineOperations -Item $Item -Context $Context }
        'FeatureSet' { Invoke-InitializerFeatureSet -Item $Item }
        'CapabilitySet' { Invoke-InitializerCapabilitySet -Item $Item }
        'FontSupplementSet' { Invoke-InitializerFontSupplementSet -Item $Item -Context $Context }
        'LanguagePack' { Invoke-InitializerLanguagePack -Item $Item }
        'RegionalFormat' { Invoke-InitializerRegionalFormat -Item $Item }
        'LanguageSettings' { Invoke-InitializerLanguageSettings -Context $Context }
        'AccountSetup' { Invoke-InitializerAccountSetup -Context $Context }
        'ComputerName' { Invoke-InitializerComputerName -Context $Context -Interactive:$Interactive }
        'DeviceSetupRegion' { Invoke-InitializerDeviceSetupRegion -Context $Context }
        default { throw "不支持的项目类型：$($Item.Kind)" }
    }
}

function Get-InitializerSoftTimeoutSeconds {
    param([Parameter(Mandatory)][hashtable]$Item)
    switch ($Item.Kind) {
        { $_ -in @('LanguagePack', 'CapabilitySet', 'FontSupplementSet', 'FeatureSet') } { return 5400 }
        'Winget' { return 1800 }
        'DeviceSetupRegion' { return 300 }
        default { return 900 }
    }
}

function Write-InitializerJsonFile {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)]$Value)
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $temporaryPath = "$Path.tmp"
    [IO.File]::WriteAllText($temporaryPath, ($Value | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
}

function Invoke-InitializerTaskWorker {
    param([Parameter(Mandatory)][hashtable]$Item, [Parameter(Mandatory)][hashtable]$Context, [Parameter(Mandatory)][string]$RunPath, [switch]$Interactive)

    $workerPath = Join-Path $script:RootPath 'worker\Invoke-InitializerWorker.ps1'
    if (-not (Test-Path -LiteralPath $workerPath)) { throw "找不到 Worker：$workerPath" }
    $taskPath = Join-Path $RunPath $Item.Id
    New-Item -ItemType Directory -Path $taskPath -Force | Out-Null
    $contextPath = Join-Path $taskPath 'context.clixml'
    $statePath = Join-Path $taskPath 'state.json'
    $cancelPath = Join-Path $taskPath 'cancel.requested'
    Export-Clixml -LiteralPath $contextPath -InputObject $Context -Force
    Write-InitializerJsonFile -Path $statePath -Value ([ordered]@{ Id = $Item.Id; Status = 'Preparing'; StartedAt = (Get-Date).ToString('o'); LastHeartbeat = (Get-Date).ToString('o') })

    $powerShellPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $process = Start-Process -FilePath $powerShellPath -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $workerPath, '-TaskId', $Item.Id, '-ContextPath', $contextPath, '-StatePath', $statePath, '-LogPath', $script:LogPath, '-CancelPath', $cancelPath)
    $started = Get-Date
    $lastHeartbeat = Get-Date '2000-01-01'
    $cancelRequested = $false
    $softTimeout = Get-InitializerSoftTimeoutSeconds -Item $Item
    try {
        while (-not $process.HasExited) {
            $elapsed = [int]((Get-Date) - $started).TotalSeconds
            if (((Get-Date) - $lastHeartbeat).TotalSeconds -ge 15) {
                $phase = if ($elapsed -ge 15) { '等待 Windows/安装服务返回' } else { '正在启动 Worker' }
                Write-Host "[$($Item.Id)] $phase；已运行 $([TimeSpan]::FromSeconds($elapsed).ToString('hh\:mm\:ss'))。按 Q 请求在当前步骤结束后停止队列。" -ForegroundColor DarkCyan
                if ($elapsed -ge $softTimeout) { Write-Host "[$($Item.Id)] 已超过软超时 $([TimeSpan]::FromSeconds($softTimeout).ToString('hh\:mm\:ss'))；不强制终止系统服务。" -ForegroundColor Yellow }
                $lastHeartbeat = Get-Date
            }
            if ($Interactive -and -not $cancelRequested -and [Console]::KeyAvailable) {
                $key = [Console]::ReadKey($true)
                if ($key.Key -in @('Q', 'Escape')) {
                    New-Item -ItemType File -Path $cancelPath -Force | Out-Null
                    Write-Host '已请求停止：当前系统调用返回后不会启动后续任务。' -ForegroundColor Yellow
                    $cancelRequested = $true
                }
            }
            Start-Sleep -Seconds 1
        }
        $process.WaitForExit()
        $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($state.RestartRequired) { $script:RestartRequired = $true }
        if ($state.Status -eq 'Cancelled') { return [PSCustomObject]@{ Status = 'Cancelled'; CancelRequested = $true } }
        if ($state.Status -ne 'Completed') { throw "Worker 任务失败：$($state.Error)" }
        return [PSCustomObject]@{ Status = 'Success'; CancelRequested = $cancelRequested }
    }
    catch {
        # 父进程的进度显示不能覆盖 Worker 已落盘的最终结果。
        if (Test-Path -LiteralPath $statePath) {
            try {
                $recoveredState = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
                if ($recoveredState.Status -eq 'Completed') {
                    if ($recoveredState.RestartRequired) { $script:RestartRequired = $true }
                    Write-Host "[$($Item.Id)] Worker 已完成；已忽略父进程监控异常：$($_.Exception.Message)" -ForegroundColor Yellow
                    return [PSCustomObject]@{ Status = 'Success'; CancelRequested = $cancelRequested; RecoveredFromMonitorError = $true }
                }
            }
            catch { }
        }
        throw
    }
}

function Invoke-WindowsInitializer {
    param(
        [string[]]$Tasks,
        [string]$ProfilePath,
        [hashtable]$Configuration,
        [switch]$AllowRisky,
        [switch]$Force,
        [switch]$Interactive,
        [switch]$AllowUnsupportedWindows
    )

    $profile = $null
    if ($ProfilePath) { $profile = Get-InitializerProfile -ProfilePath $ProfilePath }
    $selected = Resolve-InitializerTasks -Tasks $Tasks -Profile $profile
    $context = Merge-InitializerContext -Profile $profile -Configuration $Configuration
    $items = foreach ($id in $selected) { Get-InitializerItem -Id $id }
    # 显示语言设置必须在本次语言包安装之后执行，无论用户以何种顺序勾选。
    $items = @($items | Where-Object { $_.Kind -ne 'LanguageSettings' }) + @($items | Where-Object { $_.Kind -eq 'LanguageSettings' })
    [void](Test-InitializerSupportedWindows -AllowUnsupportedWindows:$AllowUnsupportedWindows)
    if (-not (Test-InitializerAdministrator)) { throw '请在“以管理员身份运行”的 64 位 PowerShell 中执行 Apply。' }
    $highRisk = @($items | Where-Object { $_.Risk -eq 'High' })
    if ($highRisk.Count -gt 0 -and -not $AllowRisky) {
        if ($Interactive) {
            $riskConfirmation = (Read-Host "计划包含高风险项目：$($highRisk.Id -join ', ')。输入 RISKY 允许执行（不区分大小写）").Trim()
            if ($riskConfirmation -ieq 'RISKY') {
                $AllowRisky = $true
            }
            else {
                throw '未确认高风险项目，未执行任何操作。'
            }
        }
        else {
            throw "计划包含高风险项目：$($highRisk.Id -join ', ')。请复核后使用 -AllowRisky。"
        }
    }
    if (-not $Force) {
        Show-InitializerPlan -Tasks $selected -ProfilePath $ProfilePath -Configuration $Configuration -AllowRisky:$AllowRisky
        $confirmation = (Read-Host '输入 APPLY 确认执行（不区分大小写）').Trim()
        if ($confirmation -ine 'APPLY') { Write-Host '已取消，未执行任何项目。'; return }
    }

    New-InitializerLog
    $results = @()
    $runPath = Join-Path $script:RootPath ('runs\{0}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $runPath -Force | Out-Null
    $queuePath = Join-Path $runPath 'queue.json'
    $queue = [ordered]@{ CreatedAt = (Get-Date).ToString('o'); LogPath = $script:LogPath; Status = 'Running'; Tasks = @($items | ForEach-Object { [ordered]@{ Id = $_.Id; Status = 'Pending' } }) }
    Write-InitializerJsonFile -Path $queuePath -Value $queue
    foreach ($item in $items) {
        try {
            $displayName = Get-InitializerExecutionDisplayName -Item $item -Context $context
            Write-InitializerMessage -Message "开始：$($item.Id) — $displayName"
            ($queue.Tasks | Where-Object { $_.Id -eq $item.Id }).Status = 'Running'
            Write-InitializerJsonFile -Path $queuePath -Value $queue
            $nextQueueTask = @($queue.Tasks | Where-Object { $_.Status -eq 'Pending' } | Select-Object -First 1)
            $nextText = if ($nextQueueTask.Count -gt 0) {
                $nextItem = Get-InitializerItem -Id $nextQueueTask[0].Id
                "下一个：$(Get-InitializerExecutionDisplayName -Item $nextItem -Context $context)"
            }
            else { '下一个：无（这是队列最后一项）' }
            Write-Host "当前任务：$displayName；$nextText" -ForegroundColor Cyan
            $workerResult = Invoke-InitializerTaskWorker -Item $item -Context $context -RunPath $runPath -Interactive:$Interactive
            if ($workerResult.Status -eq 'Cancelled') {
                ($queue.Tasks | Where-Object { $_.Id -eq $item.Id }).Status = 'Cancelled'
                $queue.Status = 'Cancelled'
                Write-InitializerJsonFile -Path $queuePath -Value $queue
                Write-InitializerMessage -Level Warning -Message "已停止队列：$($item.Id)"
                break
            }
            $results += [PSCustomObject]@{ Id = $item.Id; Status = 'Success'; Message = '' }
            ($queue.Tasks | Where-Object { $_.Id -eq $item.Id }).Status = 'Completed'
            Write-InitializerJsonFile -Path $queuePath -Value $queue
            Write-InitializerMessage -Level Success -Message "完成：$($item.Id)"
            if ($workerResult.CancelRequested) { $queue.Status = 'Cancelled'; break }
        }
        catch {
            $results += [PSCustomObject]@{ Id = $item.Id; Status = 'Failed'; Message = $_.Exception.Message }
            ($queue.Tasks | Where-Object { $_.Id -eq $item.Id }).Status = 'Failed'
            Write-InitializerJsonFile -Path $queuePath -Value $queue
            Write-InitializerMessage -Level Error -Message "失败：$($item.Id) — $($_.Exception.Message)"
            if ($context.ContainsKey('StopOnError') -and $context.StopOnError) { break }
        }
    }

    if ($queue.Status -eq 'Running') { $queue.Status = 'Completed' }
    $queue.FinishedAt = (Get-Date).ToString('o')
    Write-InitializerJsonFile -Path $queuePath -Value $queue

    Write-Host ''
    $results | Format-Table -AutoSize | Out-Host
    if ($script:RestartRequired) { Write-InitializerMessage -Level Warning -Message '至少一项更改需要重启后完全生效。' }
    Write-InitializerMessage -Message "完成。日志：$script:LogPath"
    return $results
}

function Resume-InitializerRun {
    param([Parameter(Mandatory)][string]$RunPath, [switch]$Interactive)

    $queuePath = Join-Path $RunPath 'queue.json'
    if (-not (Test-Path -LiteralPath $queuePath)) { throw "找不到执行队列：$queuePath" }
    $queue = Get-Content -LiteralPath $queuePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $interrupted = @($queue.Tasks | Where-Object { $_.Status -eq 'Running' })
    if ($interrupted.Count -gt 0) {
        foreach ($task in $interrupted) { $task.Status = 'Unknown' }
        $queue.Status = 'ManualReviewRequired'
        Write-InitializerJsonFile -Path $queuePath -Value $queue
        throw "检测到中断时正在运行的任务：$($interrupted.Id -join '、')。为避免重复系统操作，已标记为 Unknown，请先人工核验后再重新选择该项目。"
    }
    $pending = @($queue.Tasks | Where-Object { $_.Status -eq 'Pending' })
    if ($pending.Count -eq 0) { Write-Host '该队列没有可安全恢复的待执行任务。' -ForegroundColor Yellow; return @() }
    if ($queue.LogPath) { Set-InitializerLogPath -Path $queue.LogPath } else { New-InitializerLog }

    $queue.Status = 'Running'
    Write-InitializerJsonFile -Path $queuePath -Value $queue
    $results = @()
    foreach ($task in $pending) {
        $taskPath = Join-Path $RunPath $task.Id
        $contextPath = Join-Path $taskPath 'context.clixml'
        if (-not (Test-Path -LiteralPath $contextPath)) { throw "任务缺少恢复上下文：$($task.Id)" }
        $context = Import-Clixml -LiteralPath $contextPath
        $item = Get-InitializerItem -Id $task.Id
        $task.Status = 'Running'
        Write-InitializerJsonFile -Path $queuePath -Value $queue
        $result = Invoke-InitializerTaskWorker -Item $item -Context $context -RunPath $RunPath -Interactive:$Interactive
        if ($result.Status -eq 'Cancelled') { $task.Status = 'Cancelled'; $queue.Status = 'Cancelled'; break }
        $task.Status = 'Completed'
        $results += [PSCustomObject]@{ Id = $task.Id; Status = 'Success'; Message = '' }
        Write-InitializerJsonFile -Path $queuePath -Value $queue
    }
    if ($queue.Status -eq 'Running') { $queue.Status = 'Completed' }
    $queue.FinishedAt = (Get-Date).ToString('o')
    Write-InitializerJsonFile -Path $queuePath -Value $queue
    return $results
}

Export-ModuleMember -Function Get-InitializerCatalogItems, Get-InitializerTaskDefinition, Get-InitializerDefaultTaskIds, Get-InitializerResolvedTaskIds, Get-InitializerGeoRegionInfo, Show-InitializerStatus, Show-InitializerCatalog, Show-InitializerPlan, Invoke-WindowsInitializer, Resume-InitializerRun, Invoke-InitializerTask, Set-InitializerLogPath, Get-InitializerRestartRequired, Write-InitializerJsonFile
