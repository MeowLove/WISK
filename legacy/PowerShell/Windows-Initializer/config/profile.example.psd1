@{
    SchemaVersion = '1.0'

    # 复制本文件并按机器修改；此文件可以用于无人值守的软件、功能和语言安装。
    # 账户密码可保存在配置文件；请将此文件排除在版本控制、同步盘和非加密备份之外。
    ComputerName = 'WORKSTATION-01'
    StopOnError = $false
    # Proxy = 'http://127.0.0.1:7890'
    # AssetRoot = 'assets'

    Tasks = @(
        'runtime-dotnet-8',
        'runtime-dotnet-6',
        'runtime-webview2',
        'app-vscode',
        'app-everything',
        'app-peazip',
        'app-localsend',
        'app-syncthing',
        'app-syncthingtray'
    )

    # 仅在 Tasks 包含 accounts-local 时使用。GroupSid 使用内置组 SID，避免中文/英文系统组名差异。
    # Password 存在时设置或更新密码；省略 Password 时仅保留当前已存在账户的密码。
    # 新账户必须提供 Password。明文方式适合跨机器初始化，但不要提交或同步含密码的配置文件。
    # HideFromSignInScreen = $true 时，账户不显示在本机登录界面，但不会禁用该账户。
    # AllowRemoteDesktop = $true 时，账户加入“远程桌面用户”组；还需另行启用系统远程桌面。
    Accounts = @(
        @{ Name = 'ExampleAdmin'; FullName = 'Example Administrator'; Description = '示例管理员账户'; GroupSid = 'S-1-5-32-544'; PasswordNeverExpires = $true; HideFromSignInScreen = $false; AllowRemoteDesktop = $true; Password = 'REPLACE_WITH_A_STRONG_PASSWORD' },
        @{ Name = 'ExampleUser'; FullName = 'Example User'; Description = '示例普通账户'; GroupSid = 'S-1-5-32-545'; PasswordNeverExpires = $true; HideFromSignInScreen = $false; AllowRemoteDesktop = $true; Password = 'REPLACE_WITH_A_STRONG_PASSWORD' }
    )

    # 仅在 Tasks 包含 language-ui-preference 时使用。默认不会修改区域格式、输入法或非 Unicode 系统区域。
    # 每个已验证成功的 LanguagePack 任务都会自动登记到当前用户的 Windows 设置“首选语言”列表，保留原有排序。
    # language-zh-sg 与 language-zh-hk 分别设置当前用户的新加坡/香港区域格式；两者互斥，
    # 不会安装系统语言包，也不会加入 Windows 设置的“首选语言”列表，不能作为 TargetLanguage。
    # 如需中文显示语言，请在 Tasks 中显式加入 language-zh-cn 或 language-zh-tw；区域格式项目不会自动安装语言包。
    # LanguagePreference = @{ TargetLanguage = 'en-GB'; ApplyToCurrentUser = $true; ApplyToSystem = $false; SyncToWelcomeAndNewUsers = $false }

    # 仅在 Tasks 包含 device-setup-region 时使用。该实验性项目仅写入 HKLM DeviceRegion，不修改 UCPD、Home location、Country or region 或区域格式。
    # 可能被 UCPD 或组织策略拦截；失败不会影响其他项目。工具不会关闭、修改或绕过 UCPD。
    # DeviceSetupRegionGeoId = 242 # 英国；中国 = 45，美国 = 244

    # 仅在 Tasks 包含 font-supplements-cjk-indic-europe 时使用：japanese、korean、european、indic。
    # FontSupplementIds = @('japanese')
}
