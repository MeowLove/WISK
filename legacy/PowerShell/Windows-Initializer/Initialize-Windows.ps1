#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Menu', 'Status', 'Plan', 'Apply', 'Resume', 'Catalog', 'Help')]
    [string]$Action = 'Menu',

    [string]$ProfilePath,

    [string[]]$Tasks,

    [string]$RunPath,

    [switch]$AllowRisky,

    [switch]$Force,

    [switch]$AllowUnsupportedWindows
)

function Test-InitializerAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function ConvertTo-InitializerPowerShellLiteral {
    param([Parameter(Mandatory)][string]$Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

function Start-InitializerElevated {
    $powerShellPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if ([Environment]::Is64BitOperatingSystem -and -not [Environment]::Is64BitProcess) {
        $powerShellPath = Join-Path $env:WINDIR 'SysNative\WindowsPowerShell\v1.0\powershell.exe'
    }

    $scriptPathLiteral = ConvertTo-InitializerPowerShellLiteral -Value $PSCommandPath
    $commandParts = @("& $scriptPathLiteral", '-Action', (ConvertTo-InitializerPowerShellLiteral -Value $Action))
    if ($ProfilePath) { $commandParts += @('-ProfilePath', (ConvertTo-InitializerPowerShellLiteral -Value $ProfilePath)) }
    if ($Tasks) {
        $taskLiterals = @($Tasks | ForEach-Object { ConvertTo-InitializerPowerShellLiteral -Value $_ })
        $commandParts += @('-Tasks', ($taskLiterals -join ','))
    }
    if ($RunPath) { $commandParts += @('-RunPath', (ConvertTo-InitializerPowerShellLiteral -Value $RunPath)) }
    if ($AllowRisky) { $commandParts += '-AllowRisky' }
    if ($Force) { $commandParts += '-Force' }
    if ($AllowUnsupportedWindows) { $commandParts += '-AllowUnsupportedWindows' }

    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes(($commandParts -join ' ')))
    try {
        $process = Start-Process -FilePath $powerShellPath -Verb RunAs -Wait -PassThru -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encodedCommand
        )
    }
    catch {
        throw '必须以管理员身份运行。UAC 提升请求被取消或无法启动提升后的 PowerShell。'
    }

    if ($process.ExitCode -ne 0) {
        throw "提升后的初始化进程失败，退出码：$($process.ExitCode)"
    }
}

# powershell.exe -File forwards "task-a,task-b" as one string. Normalize it
# before elevation so direct command-line use matches PowerShell array syntax.
if ($Tasks) {
    $Tasks = @(
        foreach ($taskValue in $Tasks) {
            foreach ($taskId in ([string]$taskValue -split ',')) {
                $normalizedTaskId = $taskId.Trim()
                if ($normalizedTaskId) { $normalizedTaskId }
            }
        }
    )
}

if (-not (Test-InitializerAdministrator)) {
    Start-InitializerElevated
    exit
}

$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $PSScriptRoot 'modules\WindowsInitializer.psm1'
Import-Module -Name $modulePath -Force

function Show-InitializerHelp {
    @"

Windows 11 Initializer

Examples:
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Initialize-Windows.ps1
  .\Initialize-Windows.ps1 -Action Status
  .\Initialize-Windows.ps1 -Action Catalog
  .\Initialize-Windows.ps1 -Action Plan -Tasks runtime-dotnet-8,app-vscode
  .\Initialize-Windows.ps1 -Action Apply -ProfilePath .\config\profile.example.psd1
  .\Initialize-Windows.ps1 -Action Apply -Tasks system-rdp-wrapper -AllowRisky

Notes:
  - Apply requires an elevated 64-bit Windows PowerShell 5.1 or PowerShell 7 session.
  - Profile files are PowerShell data files (.psd1), not executable scripts.
  - Passwords are never stored in a profile. Account tasks prompt securely when required.
  - High-risk entries require -AllowRisky and an explicit confirmation unless -Force is used.
  - In the interactive menu, use Up/Down to move, Space to select, and Enter to return.

"@
}

function Wait-InitializerContinue {
    Read-Host '按 Enter 返回主菜单' | Out-Null
}

function Show-InitializerActionBar {
    Write-Host ''
    Write-Host '┌────────────────────────── 操作 ──────────────────────────┐' -ForegroundColor DarkCyan
    Write-Host '│ [G] 配置已选项  [P] 查看计划  [E] 执行已选项目         │' -ForegroundColor White
    Write-Host '│ [C] 清空选择    [F] 配置文件  [M] 更多工具  [Q] 退出   │' -ForegroundColor White
    Write-Host '└─────────────────────────────────────────────────────────┘' -ForegroundColor DarkCyan
}

function Get-InitializerMenuChoice {
    param([Parameter(Mandatory)]$KeyInfo)

    if ($KeyInfo.KeyChar -and -not [char]::IsControl($KeyInfo.KeyChar)) {
        return ([string]$KeyInfo.KeyChar).ToUpperInvariant()
    }
    return $KeyInfo.Key.ToString().ToUpperInvariant()
}

function Read-InitializerEditableValue {
    param([Parameter(Mandatory)][string]$Prompt, [string]$DefaultValue)
    $answer = Read-Host "$Prompt [$DefaultValue]"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $DefaultValue }
    return $answer.Trim()
}

function Read-InitializerYesNo {
    param([Parameter(Mandatory)][string]$Prompt, [bool]$Default = $true)
    $defaultLabel = if ($Default) { 'Y' } else { 'N' }
    $answer = (Read-Host "$Prompt (Y/N) [$defaultLabel]").Trim().ToUpperInvariant()
    if (-not $answer) { return $Default }
    if ($answer -eq 'Y') { return $true }
    if ($answer -eq 'N') { return $false }
    throw "请输入 Y 或 N：$Prompt"
}

function Read-InitializerConfirmedSecurePassword {
    param([Parameter(Mandatory)][string]$AccountName)

    while ($true) {
        $first = Read-Host -AsSecureString "为账户 $AccountName 设置密码"
        $second = Read-Host -AsSecureString '再次输入密码以确认'
        $firstBstr = [IntPtr]::Zero
        $secondBstr = [IntPtr]::Zero
        try {
            $firstBstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($first)
            $secondBstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($second)
            $firstText = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($firstBstr)
            $secondText = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secondBstr)
            if ([string]::IsNullOrEmpty($firstText)) {
                Write-Host '密码不能为空。' -ForegroundColor Yellow
                continue
            }
            if ($firstText -ne $secondText) {
                Write-Host '两次输入的密码不一致，请重新输入。' -ForegroundColor Yellow
                continue
            }
            return $first
        }
        finally {
            if ($firstBstr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($firstBstr) }
            if ($secondBstr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secondBstr) }
        }
    }
}

function Copy-InitializerConfiguration {
    param([hashtable]$Configuration)
    $copy = @{}
    if ($Configuration) { foreach ($key in $Configuration.Keys) { $copy[$key] = $Configuration[$key] } }
    Write-Output -NoEnumerate $copy
}

function Read-InitializerAccountDefinition {
    param([hashtable]$ExistingAccount)

    $defaultName = if ($ExistingAccount) { $ExistingAccount.Name } else { '' }
    do {
        $name = Read-InitializerEditableValue -Prompt '用户名（字母、数字、点、下划线或连字符）' -DefaultValue $defaultName
        if ($name -notmatch '^[A-Za-z0-9._-]{1,20}$') { Write-Host '账户名格式无效。' -ForegroundColor Yellow }
    }
    while ($name -notmatch '^[A-Za-z0-9._-]{1,20}$')

    $systemAccount = Get-InitializerSystemAccountDefinition -Name $name
    $useSystemDefaults = $systemAccount -and (-not $ExistingAccount -or $ExistingAccount.InitializerDraftSource -eq 'SystemImport')
    $defaultAccount = if ($useSystemDefaults) { $systemAccount } elseif ($ExistingAccount) { $ExistingAccount } else { $null }
    if ($useSystemDefaults) {
        Write-Host "检测到现有本地账户 $name，以下默认值来自系统当前设置。" -ForegroundColor DarkGray
    }

    $fullName = Read-InitializerEditableValue -Prompt '显示名称' -DefaultValue $(if ($defaultAccount) { $defaultAccount.FullName } else { $name })
    $description = Read-InitializerEditableValue -Prompt '说明' -DefaultValue $(if ($defaultAccount) { $defaultAccount.Description } else { '' })
    $defaultRole = if ($defaultAccount -and $defaultAccount.GroupSid -eq 'S-1-5-32-544') { 'A' } else { 'U' }
    do {
        $role = (Read-Host "账户类型：A=管理员，U=普通用户 [$defaultRole]").Trim().ToUpperInvariant()
        if (-not $role) { $role = $defaultRole }
        if ($role -notin @('A', 'U')) { Write-Host '账户类型只能是 A 或 U。' -ForegroundColor Yellow }
    }
    while ($role -notin @('A', 'U'))

    $neverExpires = Read-InitializerYesNo -Prompt '密码永不过期' -Default $(if ($defaultAccount) { [bool]$defaultAccount.PasswordNeverExpires } else { $true })
    $hideFromSignInScreen = Read-InitializerYesNo -Prompt '不显示在本机登录界面' -Default $(if ($defaultAccount) { [bool]$defaultAccount.HideFromSignInScreen } else { $false })
    $allowRemoteDesktop = Read-InitializerYesNo -Prompt '加入远程桌面用户组（需另行启用系统远程桌面）' -Default $(if ($defaultAccount) { [bool]$defaultAccount.AllowRemoteDesktop } else { $true })
    $existingLocalUser = Get-LocalUser -Name $name -ErrorAction SilentlyContinue
    $defaultPasswordChoice = if ($defaultAccount -and $defaultAccount.PasswordAction -eq 'Keep') { 'K' } else { 'S' }
    do {
        $passwordChoice = (Read-Host "密码处理：S=现在安全设置，K=保留已有密码 [$defaultPasswordChoice]").Trim().ToUpperInvariant()
        if (-not $passwordChoice) { $passwordChoice = $defaultPasswordChoice }
        if ($passwordChoice -notin @('S', 'K')) {
            Write-Host '密码处理只能是 S 或 K。' -ForegroundColor Yellow
            continue
        }
        if ($passwordChoice -eq 'K' -and $null -eq $existingLocalUser) {
            Write-Host '该账户当前不存在，不能保留密码；请使用 S 立即设置密码。' -ForegroundColor Yellow
            continue
        }
    }
    while ($passwordChoice -notin @('S', 'K') -or ($passwordChoice -eq 'K' -and $null -eq $existingLocalUser))

    $password = $null
    $passwordAction = if ($passwordChoice -eq 'S') { 'Configured' } else { 'Keep' }
    if ($passwordAction -eq 'Configured') { $password = Read-InitializerConfirmedSecurePassword -AccountName $name }

    $account = @{
        Name = $name
        FullName = $fullName
        Description = $description
        GroupSid = if ($role -eq 'A') { 'S-1-5-32-544' } else { 'S-1-5-32-545' }
        PasswordNeverExpires = $neverExpires
        HideFromSignInScreen = $hideFromSignInScreen
        AllowRemoteDesktop = $allowRemoteDesktop
        PasswordAction = $passwordAction
        InitializerDraftSource = 'UserDraft'
    }
    if ($password) { $account.Password = $password }
    Write-Output -NoEnumerate $account
}

function Get-InitializerCurrentLocalAccountDefinitions {
    $administratorMemberSids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $remoteDesktopMemberSids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $hiddenUserNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    try {
        foreach ($member in (Get-LocalGroupMember -SID 'S-1-5-32-544' -ErrorAction Stop)) {
            if ($member.SID) { [void]$administratorMemberSids.Add([string]$member.SID) }
        }
    }
    catch {
        throw "无法读取本地 Administrators 组成员：$($_.Exception.Message)"
    }
    try {
        foreach ($member in (Get-LocalGroupMember -SID 'S-1-5-32-555' -ErrorAction Stop)) {
            if ($member.SID) { [void]$remoteDesktopMemberSids.Add([string]$member.SID) }
        }
    }
    catch { }
    try {
        $hiddenUserList = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList' -ErrorAction Stop
        foreach ($property in $hiddenUserList.PSObject.Properties) {
            if ($property.Name -notlike 'PS*' -and [int]$property.Value -eq 0) { [void]$hiddenUserNames.Add($property.Name) }
        }
    }
    catch { }

    try { $localUsers = Get-LocalUser -ErrorAction Stop }
    catch { throw "无法读取当前本地账户：$($_.Exception.Message)" }

    $result = @(
        foreach ($user in $localUsers) {
            $isAdministrator = $user.SID -and $administratorMemberSids.Contains([string]$user.SID)
            [PSCustomObject]@{
                Name = $user.Name
                Enabled = [bool]$user.Enabled
                Role = if ($isAdministrator) { '管理员' } else { '普通用户' }
                PasswordNeverExpires = [bool]$user.PasswordNeverExpires
                HiddenFromSignInScreen = $hiddenUserNames.Contains($user.Name)
                AllowRemoteDesktop = $user.SID -and $remoteDesktopMemberSids.Contains([string]$user.SID)
                Definition = @{
                    Name = $user.Name
                    FullName = $user.FullName
                    Description = $user.Description
                    GroupSid = if ($isAdministrator) { 'S-1-5-32-544' } else { 'S-1-5-32-545' }
                    PasswordNeverExpires = [bool]$user.PasswordNeverExpires
                    HideFromSignInScreen = $hiddenUserNames.Contains($user.Name)
                    AllowRemoteDesktop = $user.SID -and $remoteDesktopMemberSids.Contains([string]$user.SID)
                    PasswordAction = 'Keep'
                    InitializerDraftSource = 'SystemImport'
                }
            }
        }
    )
    return @($result | Sort-Object Name)
}

function Get-InitializerSystemAccountDefinition {
    param([Parameter(Mandatory)][string]$Name)

    $candidate = Get-InitializerCurrentLocalAccountDefinitions | Where-Object { $_.Name -eq $Name } | Select-Object -First 1
    if ($candidate) { return $candidate.Definition }
    return $null
}

function Select-InitializerLocalAccountsToImport {
    param([object[]]$ExistingAccounts)

    $candidates = @(Get-InitializerCurrentLocalAccountDefinitions)
    if ($candidates.Count -eq 0) {
        Write-Host '未读取到可导入的本地账户。' -ForegroundColor Yellow
        Start-Sleep -Milliseconds 800
        return @()
    }

    $existingNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($account in $ExistingAccounts) {
        if ($account -and $account.Name) { [void]$existingNames.Add($account.Name) }
    }
    $selectedNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $position = 0

    while ($true) {
        Clear-Host
        Write-Host '读取并导入当前本地账户' -ForegroundColor Cyan
        Write-Host '仅导入到执行草稿，不会立即修改任何系统账户。已在草稿中的账户不能重复导入。' -ForegroundColor DarkGray
        Write-Host ''
        for ($index = 0; $index -lt $candidates.Count; $index++) {
            $candidate = $candidates[$index]
            $cursor = if ($index -eq $position) { '>' } else { ' ' }
            $inDraft = $existingNames.Contains($candidate.Name)
            $checked = if ($inDraft) { '[-]' } elseif ($selectedNames.Contains($candidate.Name)) { '[x]' } else { '[ ]' }
            $enabled = if ($candidate.Enabled) { '已启用' } else { '已禁用' }
            $neverExpires = if ($candidate.PasswordNeverExpires) { '密码永不过期' } else { '密码按系统策略' }
            $signInVisibility = if ($candidate.HiddenFromSignInScreen) { '登录隐藏' } else { '登录显示' }
            $remoteDesktop = if ($candidate.AllowRemoteDesktop) { 'RDP 已授权' } else { 'RDP 未授权' }
            $color = if ($inDraft) { 'DarkGray' } elseif ($candidate.Enabled) { 'Gray' } else { 'Yellow' }
            Write-Host "$cursor $checked $($candidate.Name) — $($candidate.Role)；$enabled；$neverExpires；$signInVisibility；$remoteDesktop" -ForegroundColor $color
        }
        Write-Host ''
        Write-Host '↑↓ 移动  Space 勾选  A 全选可导入项  N 全不选  Enter 导入  Esc 取消' -ForegroundColor White
        $key = [Console]::ReadKey($true).Key
        switch ($key) {
            'UpArrow' { if ($position -gt 0) { $position-- } }
            'DownArrow' { if ($position -lt ($candidates.Count - 1)) { $position++ } }
            'Spacebar' {
                $name = $candidates[$position].Name
                if ($existingNames.Contains($name)) {
                    Write-Host '该账户已在草稿中；如需修改，请返回编辑器使用 E 编辑。' -ForegroundColor Yellow
                    Start-Sleep -Milliseconds 900
                }
                elseif ($selectedNames.Contains($name)) { [void]$selectedNames.Remove($name) }
                else { [void]$selectedNames.Add($name) }
            }
            'A' {
                foreach ($candidate in $candidates) {
                    if (-not $existingNames.Contains($candidate.Name)) { [void]$selectedNames.Add($candidate.Name) }
                }
            }
            'N' { $selectedNames.Clear() }
            'Enter' {
                $definitions = @(
                    foreach ($candidate in $candidates) {
                        if ($selectedNames.Contains($candidate.Name)) { $candidate.Definition }
                    }
                )
                return $definitions
            }
            'Escape' { return @() }
        }
    }
}

function Edit-InitializerAccounts {
    param([object[]]$InitialAccounts)

    $accounts = @($InitialAccounts | Where-Object { $null -ne $_ -and $_.Name })
    while ($true) {
        Clear-Host
        Write-Host '本地账户列表编辑器' -ForegroundColor Cyan
        Write-Host '密码可在新增或编辑账户时安全输入；只保存在本次 PowerShell 会话内。' -ForegroundColor DarkGray
        Write-Host ''
        if ($accounts.Count -eq 0) { Write-Host '（尚未定义账户）' -ForegroundColor Yellow }
        for ($index = 0; $index -lt $accounts.Count; $index++) {
            $role = if ($accounts[$index].GroupSid -eq 'S-1-5-32-544') { '管理员' } else { '普通用户' }
            $passwordState = switch ($accounts[$index].PasswordAction) {
                'Configured' { '已在当前会话设置' }
                'Keep' { '保留已有密码' }
                default { '尚未设置（请用 E 或 P）' }
            }
            $signInVisibility = if ($accounts[$index].HideFromSignInScreen) { '登录隐藏' } else { '登录显示' }
            $remoteDesktop = if ($accounts[$index].AllowRemoteDesktop) { 'RDP 已授权' } else { 'RDP 未授权' }
            Write-Host "[$($index + 1)] $($accounts[$index].Name) — $role；$signInVisibility；$remoteDesktop；密码：$passwordState"
        }
        Write-Host ''
        Write-Host '[A] 新增  [E] 编辑  [D] 删除  [I] 读取并导入当前本地账户' -ForegroundColor White
        Write-Host '[R] 从系统刷新一个账户  [P] 依次设置全部密码  [Enter] 完成' -ForegroundColor White
        $choice = (Read-Host '请选择').Trim().ToUpperInvariant()
        switch ($choice) {
            'A' { $accounts += Read-InitializerAccountDefinition }
            'E' {
                $number = [int](Read-Host '输入要编辑的序号')
                if ($number -lt 1 -or $number -gt $accounts.Count) { throw '账户序号超出范围。' }
                $accounts[$number - 1] = Read-InitializerAccountDefinition -ExistingAccount $accounts[$number - 1]
            }
            'D' {
                $number = [int](Read-Host '输入要删除的序号')
                if ($number -lt 1 -or $number -gt $accounts.Count) { throw '账户序号超出范围。' }
                $accounts = @($accounts | Where-Object { $_ -ne $accounts[$number - 1] })
            }
            'I' {
                try {
                    $importedAccounts = @(Select-InitializerLocalAccountsToImport -ExistingAccounts $accounts)
                    if ($importedAccounts.Count -gt 0) {
                        $accounts += $importedAccounts
                        Write-Host "已导入 $($importedAccounts.Count) 个本地账户到草稿。" -ForegroundColor Green
                        Start-Sleep -Milliseconds 800
                    }
                }
                catch {
                    Write-Host "读取本地账户失败：$($_.Exception.Message)" -ForegroundColor Red
                    Read-Host '按 Enter 返回账户编辑器' | Out-Null
                }
            }
            'R' {
                $number = [int](Read-Host '输入要从系统刷新的账户序号')
                if ($number -lt 1 -or $number -gt $accounts.Count) { throw '账户序号超出范围。' }
                $previousAccount = $accounts[$number - 1]
                $systemAccount = Get-InitializerSystemAccountDefinition -Name $previousAccount.Name
                if (-not $systemAccount) { throw "系统中不存在本地账户：$($previousAccount.Name)" }
                if ($previousAccount.Password) {
                    $systemAccount.Password = $previousAccount.Password
                    $systemAccount.PasswordAction = $previousAccount.PasswordAction
                }
                $accounts[$number - 1] = $systemAccount
                Write-Host "已使用系统当前值刷新 $($systemAccount.Name)；已在草稿中设置的密码已保留。" -ForegroundColor Green
                Start-Sleep -Milliseconds 800
            }
            'P' {
                if ($accounts.Count -eq 0) { throw '尚未定义账户，无法设置密码。' }
                foreach ($account in $accounts) {
                    Write-Host "设置账户 $($account.Name) 的密码" -ForegroundColor Cyan
                    $account.Password = Read-InitializerConfirmedSecurePassword -AccountName $account.Name
                    $account.PasswordAction = 'Configured'
                }
                Write-Host '已在当前会话中安全保存全部账户密码。' -ForegroundColor Green
                Start-Sleep -Milliseconds 800
            }
            '' {
                if ($accounts.Count -eq 0) { throw '至少需要定义一个本地账户。' }
                $names = @($accounts | ForEach-Object { $_.Name.ToUpperInvariant() })
                if (($names | Select-Object -Unique).Count -ne $names.Count) { throw '本地账户名称不能重复。' }
                $unsetPasswordAccounts = @($accounts | Where-Object { $_.PasswordAction -eq 'Configured' -and -not $_.Password })
                if ($unsetPasswordAccounts.Count -gt 0) {
                    throw "以下账户尚未在编辑器中设置密码：$($unsetPasswordAccounts.Name -join '、')。请使用 E 或 P 设置密码。"
                }
                Write-Output -NoEnumerate ([object[]]$accounts)
                return
            }
            default { Write-Host '无效选择。' -ForegroundColor Yellow; Start-Sleep -Milliseconds 500 }
        }
    }
}

function Test-InitializerDraftConfiguration {
    param([Parameter(Mandatory)]$Item, [hashtable]$Configuration)
    if ($Item.Id -eq 'computer-name') { return $Configuration -and $Configuration.ComputerName }
    if ($Item.Id -eq 'device-setup-region') { return $Configuration -and $null -ne $Configuration.DeviceSetupRegionGeoId }
    if ($Item.Id -eq 'accounts-local') { return $Configuration -and $Configuration.Accounts -and @($Configuration.Accounts).Count -gt 0 }
    if ($Item.Id -eq 'language-ui-preference') { return $Configuration -and $Configuration.LanguagePreference -and $Configuration.LanguagePreference.TargetLanguage }
    if ($Item.Id -eq 'font-supplements-cjk-indic-europe') { return $Configuration -and $Configuration.FontSupplementIds -and @($Configuration.FontSupplementIds).Count -gt 0 }
    return $true
}

function Test-InitializerTaskRequiresDraftConfiguration {
    param([Parameter(Mandatory)]$Item)
    return $Item.Id -in @('computer-name', 'device-setup-region', 'accounts-local', 'language-ui-preference', 'font-supplements-cjk-indic-europe')
}

function Select-InitializerFontSupplements {
    param([string[]]$InitialIds)

    $fontItem = Get-InitializerCatalogItems | Where-Object { $_.Id -eq 'font-supplements-cjk-indic-europe' } | Select-Object -First 1
    $options = @($fontItem.FontOptions)
    if ($options.Count -eq 0) { throw '字体补充选项未在目录中定义。' }
    $selectedIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($id in @($InitialIds)) {
        if ($options.Id -contains $id) { [void]$selectedIds.Add($id) }
    }
    $position = 0

    while ($true) {
        Clear-Host
        Write-Host '执行前配置：字体补充' -ForegroundColor Cyan
        Write-Host '按实际兼容需求选择。A 可一键选择本工具提供的全部四类字体补充。' -ForegroundColor DarkGray
        Write-Host ''
        for ($index = 0; $index -lt $options.Count; $index++) {
            $option = $options[$index]
            $cursor = if ($index -eq $position) { '>' } else { ' ' }
            $checked = if ($selectedIds.Contains($option.Id)) { '[x]' } else { '[ ]' }
            Write-Host "$cursor $checked $($option.Name)" -ForegroundColor Gray
        }
        Write-Host ''
        Write-Host '↑↓ 移动  Space 勾选/取消  A 全选  N 全不选  Enter 保存  Esc 取消' -ForegroundColor White
        $key = [Console]::ReadKey($true).Key
        switch ($key) {
            'UpArrow' { if ($position -gt 0) { $position-- } }
            'DownArrow' { if ($position -lt ($options.Count - 1)) { $position++ } }
            'Spacebar' {
                $id = $options[$position].Id
                if ($selectedIds.Contains($id)) { [void]$selectedIds.Remove($id) }
                else { [void]$selectedIds.Add($id) }
            }
            'A' { foreach ($option in $options) { [void]$selectedIds.Add($option.Id) } }
            'N' { $selectedIds.Clear() }
            'Enter' {
                if ($selectedIds.Count -eq 0) {
                    Write-Host '请至少选择一种字体补充，或返回配置列表取消该项目。' -ForegroundColor Yellow
                    Start-Sleep -Milliseconds 900
                    continue
                }
                return @($options | Where-Object { $selectedIds.Contains($_.Id) } | ForEach-Object { $_.Id })
            }
            'Escape' { throw '字体补充尚未配置。' }
        }
    }
}

function Get-InitializerInteractiveConfiguration {
    param(
        [Parameter(Mandatory)][string[]]$SelectedTasks,
        [hashtable]$ExistingConfiguration,
        [string[]]$LanguagePackTasks
    )

    $configuration = Copy-InitializerConfiguration -Configuration $ExistingConfiguration
    if ($SelectedTasks -contains 'computer-name') {
        Clear-Host
        Write-Host '执行前配置：Windows 主机名' -ForegroundColor Cyan
        Write-Host "当前主机名：$env:COMPUTERNAME" -ForegroundColor DarkGray
        $existingName = if ($configuration.ComputerName) { $configuration.ComputerName } else { '' }
        do {
            $computerName = Read-InitializerEditableValue -Prompt '请输入新的主机名（1–15 位，仅限字母、数字、连字符）' -DefaultValue $existingName
            $validComputerName = -not [string]::IsNullOrWhiteSpace($computerName) -and $computerName -match '^[A-Za-z0-9-]{1,15}$' -and -not $computerName.StartsWith('-') -and -not $computerName.EndsWith('-')
            if (-not $validComputerName) {
                Write-Host '主机名格式无效。请重新输入；不会退出配置列表。' -ForegroundColor Yellow
                $existingName = ''
            }
        }
        while (-not $validComputerName)
        $computerName = $computerName.Trim()
        $configuration.ComputerName = $computerName
    }

    if ($SelectedTasks -contains 'device-setup-region') {
        Clear-Host
        Write-Host '执行前配置：Device setup region（安装时地区）' -ForegroundColor Cyan
        Write-Host '仅修改 HKLM DeviceRegion。不会修改 Country or region、Regional format、Home location 或 UCPD。' -ForegroundColor DarkGray
        Write-Host '警告：本工具不会关闭或修改 UCPD；UCPD 或组织策略可能在之后拒绝或重置该值。建议先自行处理 UCPD，再重启验证。' -ForegroundColor Yellow
        $presets = @(
            @{ Name = '中国'; GeoId = 45 }, @{ Name = '中国香港'; GeoId = 104 }, @{ Name = '日本'; GeoId = 122 }, @{ Name = '韩国'; GeoId = 134 },
            @{ Name = '新加坡'; GeoId = 215 }, @{ Name = '中国台湾'; GeoId = 237 }, @{ Name = '英国'; GeoId = 242 }, @{ Name = '美国'; GeoId = 244 }
        )
        $currentGeoId = $null
        try { $currentGeoId = [int](Get-ItemPropertyValue -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion' -Name 'DeviceRegion' -ErrorAction Stop) }
        catch { Write-Host "无法读取当前 Device setup region：$($_.Exception.Message)" -ForegroundColor Yellow }
        if ($null -ne $currentGeoId) {
            try { $currentRegion = Get-InitializerGeoRegionInfo -GeoId $currentGeoId; Write-Host "当前 Device setup region：$($currentRegion.Name) ($currentGeoId)" -ForegroundColor DarkGray }
            catch { Write-Host "当前 Device setup region GeoID：$currentGeoId" -ForegroundColor DarkGray }
        }
        Write-Host ''
        for ($index = 0; $index -lt $presets.Count; $index++) { Write-Host "[$($index + 1)] $($presets[$index].Name) ($($presets[$index].GeoId))" }
        $defaultGeoId = if ($null -ne $configuration.DeviceSetupRegionGeoId) { [string]$configuration.DeviceSetupRegionGeoId } elseif ($null -ne $currentGeoId) { [string]$currentGeoId } else { '' }
        $validGeoId = $false
        while (-not $validGeoId) {
            $regionInput = Read-InitializerEditableValue -Prompt '输入预设序号或 Windows GeoID' -DefaultValue $defaultGeoId
            if ($regionInput -match '^\d+$' -and [int]$regionInput -ge 1 -and [int]$regionInput -le $presets.Count) { $geoId = [int]$presets[[int]$regionInput - 1].GeoId }
            elseif ($regionInput -match '^\d+$') { $geoId = [int]$regionInput }
            else { $geoId = 0 }
            try {
                $region = Get-InitializerGeoRegionInfo -GeoId $geoId
                $validGeoId = $true
                $configuration.DeviceSetupRegionGeoId = $region.GeoId
                Write-Host "已选择：$($region.Name) ($($region.GeoId))" -ForegroundColor Green
            }
            catch {
                Write-Host "GeoID 无效：$($_.Exception.Message)" -ForegroundColor Yellow
                $defaultGeoId = ''
            }
        }
    }

    if ($SelectedTasks -contains 'accounts-local') {
        $configuration.Accounts = Edit-InitializerAccounts -InitialAccounts @($configuration.Accounts)
    }

    if ($SelectedTasks -contains 'font-supplements-cjk-indic-europe') {
        $configuration.FontSupplementIds = @(Select-InitializerFontSupplements -InitialIds @($configuration.FontSupplementIds))
    }

    if ($SelectedTasks -contains 'language-ui-preference') {
        Clear-Host
        Write-Host '执行前配置：Windows 显示语言范围' -ForegroundColor Cyan
        Write-Host '说明：安装系统语言包、系统首选 UI 语言与当前用户 Preferred languages 是三项不同设置。' -ForegroundColor DarkGray
        $items = @(Get-InitializerCatalogItems)
        $languageTaskIds = if ($LanguagePackTasks) { @($LanguagePackTasks) } else { @($SelectedTasks) }
        $selectedLanguageItems = @($items | Where-Object { $_.Kind -eq 'LanguagePack' -and $languageTaskIds -contains $_.Id })
        $installedTags = @()
        try {
            $installedLanguageResults = Get-InstalledLanguage
            $installedTags = @(
                foreach ($installedLanguage in $installedLanguageResults) {
                    $languagePacks = @($installedLanguage.LanguagePacks | Where-Object { $_ -and $_ -ne 'None' })
                    if ($languagePacks.Count -gt 0 -and $installedLanguage.LanguageId) { $installedLanguage.LanguageId }
                }
            )
        }
        catch { Write-Host '无法读取已安装语言；仅显示本次勾选的语言。' -ForegroundColor Yellow }
        $selectedLanguageTags = @($selectedLanguageItems | ForEach-Object { $_.LanguageTag })
        $candidateTags = @($installedTags + $selectedLanguageTags | Select-Object -Unique)
        if ($candidateTags.Count -eq 0) { throw '没有可选目标语言。请先勾选至少一个语言包，或确认系统已安装语言可被读取。' }
        for ($index = 0; $index -lt $candidateTags.Count; $index++) {
            $tag = $candidateTags[$index]
            $source = if ($installedTags -contains $tag) { '已安装' } else { '本次安装后可用' }
            $catalogItem = $items | Where-Object { $_.Kind -eq 'LanguagePack' -and $_.LanguageTag -eq $tag } | Select-Object -First 1
            $displayName = if ($catalogItem) { $catalogItem.Name } else {
                try { [Globalization.CultureInfo]::GetCultureInfo($tag).NativeName } catch { $tag }
            }
            Write-Host "[$($index + 1)] $displayName ($tag) — $source"
        }
        $defaultTarget = if ($configuration.LanguagePreference -and $configuration.LanguagePreference.TargetLanguage) {
            $configuration.LanguagePreference.TargetLanguage
        }
        else { $candidateTags[0] }
        $targetInput = Read-InitializerEditableValue -Prompt '输入目标语言序号或语言标签' -DefaultValue $defaultTarget
        if ($targetInput -match '^\d+$') {
            $targetIndex = [int]$targetInput
            if ($targetIndex -lt 1 -or $targetIndex -gt $candidateTags.Count) { throw '目标语言序号超出范围。' }
            $targetLanguage = $candidateTags[$targetIndex - 1]
        }
        else {
            $targetLanguage = $targetInput
            if ($candidateTags -notcontains $targetLanguage) { throw "目标语言不在候选列表中：$targetLanguage" }
        }
        $applyCurrent = Read-InitializerYesNo -Prompt '应用到当前用户显示语言' -Default $true
        $applySystem = Read-InitializerYesNo -Prompt '应用为系统首选 UI 语言' -Default $false
        $syncWelcome = Read-InitializerYesNo -Prompt '同步当前用户语言、输入法和区域设置到欢迎界面与新账户' -Default $false
        if ($syncWelcome) { $applyCurrent = $true }
        if (-not $applyCurrent -and -not $applySystem) { throw '至少需要选择当前用户显示语言或系统首选 UI 语言。' }
        $configuration.LanguagePreference = @{
            TargetLanguage = $targetLanguage
            ApplyToCurrentUser = $applyCurrent
            ApplyToSystem = $applySystem
            SyncToWelcomeAndNewUsers = $syncWelcome
        }
    }

    Write-Output -NoEnumerate $configuration
}

function Start-InitializerConfigurationMenu {
    param([Parameter(Mandatory)][string[]]$SelectedTasks, [hashtable]$ExistingConfiguration)

    $configuration = Copy-InitializerConfiguration -Configuration $ExistingConfiguration
    $items = @(Get-InitializerCatalogItems | Where-Object {
        $SelectedTasks -contains $_.Id -and (Test-InitializerTaskRequiresDraftConfiguration -Item $_)
    })
    if ($items.Count -eq 0) {
        Write-Host '当前没有需要额外配置的已选项目。' -ForegroundColor Green
        Wait-InitializerContinue
        Write-Output -NoEnumerate $configuration
        return
    }

    while ($true) {
        Clear-Host
        Write-Host '========================================' -ForegroundColor Cyan
        Write-Host ' 已选项目配置列表' -ForegroundColor Cyan
        Write-Host '========================================' -ForegroundColor Cyan
        Write-Host '选择一个项目进入设置；某项失败只返回此列表，已经保存的项目不会丢失。' -ForegroundColor DarkGray
        Write-Host ''
        for ($index = 0; $index -lt $items.Count; $index++) {
            $item = $items[$index]
            $state = if (Test-InitializerDraftConfiguration -Item $item -Configuration $configuration) { '[*]' } else { '[x]' }
            Write-Host "[$($index + 1)] $state $($item.Name)"
        }
        Write-Host ''
        Write-Host '[Enter] 完成配置列表   [Q] 返回主菜单' -ForegroundColor White
        $choice = (Read-Host '请选择项目序号').Trim().ToUpperInvariant()
        if (-not $choice -or $choice -eq 'Q') {
            Write-Output -NoEnumerate $configuration
            return
        }
        if ($choice -notmatch '^\d+$') {
            Write-Host '请输入列表中的数字。' -ForegroundColor Yellow
            Start-Sleep -Milliseconds 700
            continue
        }
        $number = [int]$choice
        if ($number -lt 1 -or $number -gt $items.Count) {
            Write-Host '项目序号超出范围。' -ForegroundColor Yellow
            Start-Sleep -Milliseconds 700
            continue
        }

        try {
            $configurationTaskId = $items[$number - 1].Id
            $languagePackTasks = if ($configurationTaskId -eq 'language-ui-preference') {
                $resolvedTaskIds = @(Get-InitializerResolvedTaskIds -Tasks $SelectedTasks)
                @(Get-InitializerCatalogItems | Where-Object { $_.Kind -eq 'LanguagePack' -and $resolvedTaskIds -contains $_.Id } | ForEach-Object { $_.Id })
            }
            else { @() }
            $configuration = Get-InitializerInteractiveConfiguration -SelectedTasks @($configurationTaskId) -ExistingConfiguration $configuration -LanguagePackTasks $languagePackTasks
            Write-Host '该项目配置已保存。' -ForegroundColor Green
            Start-Sleep -Milliseconds 700
        }
        catch {
            Write-Host "配置失败：$($_.Exception.Message)" -ForegroundColor Red
            Read-Host '按 Enter 返回配置列表并重试' | Out-Null
        }
    }
}

function Show-InitializerTaskPicker {
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string[]]$Categories,
        # HashSet 在首次进入分类时必然为空；不能标记为 Mandatory，
        # 否则 Windows PowerShell 会把空集合判定为“没有提供参数”。
        [AllowEmptyCollection()][System.Collections.Generic.HashSet[string]]$SelectedIds,
        [hashtable]$DraftConfiguration
    )

    # 清单中的声明顺序就是初始化顺序，不再按名称打散。
    $items = @(Get-InitializerCatalogItems | Where-Object { $Categories -contains $_.Category })
    if ($items.Count -eq 0) { throw "分类没有可选项目：$($Categories -join '、')" }
    $position = 0

    while ($true) {
        Clear-Host
        Write-Host '========================================' -ForegroundColor Cyan
        Write-Host " $Title" -ForegroundColor Cyan
        Write-Host '========================================' -ForegroundColor Cyan
        Write-Host '↑↓ 移动  Space 勾选/取消  [*] 已配置  [x] 尚待配置  A 全选  N 全不选  Enter/Esc 返回' -ForegroundColor DarkGray
        Write-Host ''

        for ($index = 0; $index -lt $items.Count; $index++) {
            $item = $items[$index]
            $cursor = if ($index -eq $position) { '>' } else { ' ' }
            $checked = if (-not $SelectedIds.Contains($item.Id)) { '[ ]' } elseif ((Test-InitializerTaskRequiresDraftConfiguration -Item $item) -and (Test-InitializerDraftConfiguration -Item $item -Configuration $DraftConfiguration)) { '[*]' } else { '[x]' }
            $color = if ($item.Risk -eq 'High') { 'Yellow' } elseif ($item.Risk -eq 'Elevated') { 'Cyan' } else { 'Gray' }
            Write-Host "$cursor $checked $($item.Name)" -ForegroundColor $color
            Write-Host "      [$($item.Id)]  风险: $($item.Risk)  重启: $($item.RequiresReboot)" -ForegroundColor DarkGray
            if ($item.PSObject.Properties['Note'] -and $item.Note) {
                Write-Host "      $($item.Note)" -ForegroundColor DarkYellow
            }
        }

        Write-Host ''
        Write-Host "当前已勾选：$($SelectedIds.Count) 项" -ForegroundColor Green
        $key = [Console]::ReadKey($true).Key
        switch ($key) {
            'UpArrow' { if ($position -gt 0) { $position-- } }
            'DownArrow' { if ($position -lt ($items.Count - 1)) { $position++ } }
            'Spacebar' {
                $id = $items[$position].Id
                if ($SelectedIds.Contains($id)) { [void]$SelectedIds.Remove($id) }
                else { [void]$SelectedIds.Add($id) }
            }
            'A' { foreach ($item in $items) { [void]$SelectedIds.Add($item.Id) } }
            'N' { foreach ($item in $items) { [void]$SelectedIds.Remove($item.Id) } }
            'Enter' { return }
            'Escape' { return }
        }
    }
}

function Start-InitializerToolsMenu {
    while ($true) {
        Clear-Host
        Write-Host '========================================' -ForegroundColor Cyan
        Write-Host ' 更多工具' -ForegroundColor Cyan
        Write-Host '========================================' -ForegroundColor Cyan
        Write-Host ''
        Write-Host '[S] 查看环境状态' -ForegroundColor White
        Write-Host '[R] 恢复未完成队列' -ForegroundColor White
        Write-Host '[H] 帮助与命令示例' -ForegroundColor White
        Write-Host '[B] 返回主菜单' -ForegroundColor White
        $choice = Get-InitializerMenuChoice -KeyInfo ([Console]::ReadKey($true))
        switch ($choice) {
            'S' {
                Clear-Host
                Show-InitializerStatus -AllowUnsupportedWindows:$AllowUnsupportedWindows
                Read-Host '按 Enter 返回更多工具' | Out-Null
            }
            'H' {
                Clear-Host
                Show-InitializerHelp
                Read-Host '按 Enter 返回更多工具' | Out-Null
            }
            'R' {
                $runPath = Read-Host '输入 runs 目录路径'
                Clear-Host
                Resume-InitializerRun -RunPath $runPath -Interactive
                Read-Host '按 Enter 返回更多工具' | Out-Null
            }
            'B' { return }
            'Escape' { return }
        }
    }
}

function Start-InitializerMenu {
    $selectedIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $draftConfiguration = @{}
    $menuGroups = @(
        @{ Key = '1'; Name = '系统与账户'; Categories = @('设备与账户', 'Windows 功能', '语言与显示') },
        @{ Key = '2'; Name = '运行库'; Categories = @('运行库') },
        @{ Key = '3'; Name = '常用软件'; Categories = @('常用软件') },
        @{ Key = '4'; Name = '开发与虚拟化'; Categories = @('虚拟化平台', '开发与虚拟化', '开发与终端') },
        @{ Key = '5'; Name = '网络与系统增强'; Categories = @('网络', '安全与系统增强') },
        @{ Key = '6'; Name = '离线维护'; Categories = @('离线与旧版软件') }
    )

    while ($true) {
        Clear-Host
        Write-Host '========================================' -ForegroundColor Cyan
        Write-Host ' Windows 11 Initializer (25H2+)' -ForegroundColor Cyan
        Write-Host '========================================' -ForegroundColor Cyan
        Write-Host "已勾选项目：$($selectedIds.Count)" -ForegroundColor Green
        Write-Host ''
        foreach ($group in $menuGroups) { Write-Host "$($group.Key). $($group.Name)" }
        Show-InitializerActionBar
        Write-Host ''
        Write-Host '选择分类后按 Space 勾选；[x] 表示已选，[*] 表示必填配置已完成；按 G 配置，按 E 执行，按 M 查看工具。' -ForegroundColor DarkGray

        $choice = Get-InitializerMenuChoice -KeyInfo ([Console]::ReadKey($true))
        try {
            $group = $menuGroups | Where-Object { $_.Key -eq $choice } | Select-Object -First 1
            if ($group) {
                Show-InitializerTaskPicker -Title $group.Name -Categories $group.Categories -SelectedIds $selectedIds -DraftConfiguration $draftConfiguration
                continue
            }

            switch ($choice) {
                'P' {
                    if ($selectedIds.Count -eq 0) { throw '尚未勾选项目。' }
                    Clear-Host
                    Show-InitializerPlan -Tasks @($selectedIds | ForEach-Object { $_ }) -Configuration $draftConfiguration -AllowRisky:$AllowRisky
                    Wait-InitializerContinue
                }
                'G' {
                    if ($selectedIds.Count -eq 0) { throw '尚未勾选项目。' }
                    $selectedTasks = @($selectedIds | ForEach-Object { $_ })
                    $draftConfiguration = Start-InitializerConfigurationMenu -SelectedTasks $selectedTasks -ExistingConfiguration $draftConfiguration
                }
                'E' {
                    if ($selectedIds.Count -eq 0) { throw '尚未勾选项目。' }
                    $selectedTasks = @($selectedIds | ForEach-Object { $_ })
                    $items = @(Get-InitializerCatalogItems | Where-Object { $selectedTasks -contains $_.Id })
                    $needsConfiguration = @($items | Where-Object { -not (Test-InitializerDraftConfiguration -Item $_ -Configuration $draftConfiguration) })
                    if ($needsConfiguration.Count -gt 0) {
                        Clear-Host
                        Write-Host "以下已勾选项目尚未配置：$($needsConfiguration.Name -join '、')" -ForegroundColor Yellow
                        $draftConfiguration = Start-InitializerConfigurationMenu -SelectedTasks $selectedTasks -ExistingConfiguration $draftConfiguration
                        $items = @(Get-InitializerCatalogItems | Where-Object { $selectedTasks -contains $_.Id })
                        $needsConfiguration = @($items | Where-Object { -not (Test-InitializerDraftConfiguration -Item $_ -Configuration $draftConfiguration) })
                        if ($needsConfiguration.Count -gt 0) { throw '仍有必填配置未完成；请使用 G 继续配置后再执行。' }
                    }
                    Clear-Host
                    Invoke-WindowsInitializer -Tasks $selectedTasks -Configuration $draftConfiguration -AllowRisky:$AllowRisky -Interactive -AllowUnsupportedWindows:$AllowUnsupportedWindows
                    Wait-InitializerContinue
                }
                'C' {
                    $selectedIds.Clear()
                    $draftConfiguration = @{}
                    Write-Host '已清空勾选列表。' -ForegroundColor Green
                    Wait-InitializerContinue
                }
                'F' {
                    $path = Read-Host '输入配置文件路径（例如 .\config\profile.example.psd1）'
                    Clear-Host
                    Invoke-WindowsInitializer -ProfilePath $path -AllowRisky:$AllowRisky -Interactive -AllowUnsupportedWindows:$AllowUnsupportedWindows
                    Wait-InitializerContinue
                }
                'M' { Start-InitializerToolsMenu }
                'Q' { return }
                default { }
            }
        }
        catch {
            Write-Host "错误：$($_.Exception.Message)" -ForegroundColor Red
            Wait-InitializerContinue
        }
    }
}

switch ($Action) {
    'Menu' { Start-InitializerMenu }
    'Status' { Show-InitializerStatus -AllowUnsupportedWindows:$AllowUnsupportedWindows }
    'Catalog' { Show-InitializerCatalog }
    'Plan' { Show-InitializerPlan -Tasks $Tasks -ProfilePath $ProfilePath -AllowRisky:$AllowRisky }
    'Apply' {
        Invoke-WindowsInitializer -Tasks $Tasks -ProfilePath $ProfilePath -AllowRisky:$AllowRisky -Force:$Force -AllowUnsupportedWindows:$AllowUnsupportedWindows
    }
    'Resume' { Resume-InitializerRun -RunPath $RunPath }
    'Help' { Show-InitializerHelp }
}
