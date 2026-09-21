@{
    SchemaVersion = '1.2'

    # Default 项目是纯净系统上的低风险基础集合；其余全部保留为可选项目。
    Items = @(
        @{
            Id = 'computer-name'; Category = '设备与账户'; Name = '修改 Windows 主机名'; Kind = 'ComputerName'
            Default = $false; Risk = 'Elevated'; RequiresReboot = $true; RequiresInternet = $false
        },
        @{
            Id = 'accounts-local'; Category = '设备与账户'; Name = '创建或更新本地账户'; Kind = 'AccountSetup'
            Default = $false; Risk = 'Elevated'; RequiresReboot = $false; RequiresInternet = $false
        },
        @{
            Id = 'language-zh-cn'; Category = '语言与显示'; Name = '简体中文（中国）'; Kind = 'LanguagePack'; LanguageTag = 'zh-CN'
            Default = $false; Risk = 'Standard'; RequiresReboot = $true; RequiresInternet = $true
        },
        @{
            Id = 'language-zh-sg'; Category = '语言与显示'; Name = '简体中文（新加坡）区域格式'; Kind = 'RegionalFormat'; Culture = 'zh-SG'; ExclusiveGroup = 'regional-format'; ExclusiveGroupName = '区域格式'
            Note = '仅修改区域格式；如需中文显示语言，请另选“简体中文（中国）”。'; Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $false
        },
        @{
            Id = 'language-zh-tw'; Category = '语言与显示'; Name = '繁体中文（台湾）'; Kind = 'LanguagePack'; LanguageTag = 'zh-TW'
            Default = $false; Risk = 'Standard'; RequiresReboot = $true; RequiresInternet = $true
        },
        @{
            Id = 'language-zh-hk'; Category = '语言与显示'; Name = '繁体中文（香港）区域格式'; Kind = 'RegionalFormat'; Culture = 'zh-HK'; ExclusiveGroup = 'regional-format'; ExclusiveGroupName = '区域格式'
            Note = '仅修改区域格式；如需繁体中文显示语言，请另选“繁体中文（台湾）”。'; Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $false
        },
        @{
            Id = 'language-en-us'; Category = '语言与显示'; Name = '英文（美国）'; Kind = 'LanguagePack'; LanguageTag = 'en-US'
            Default = $false; Risk = 'Standard'; RequiresReboot = $true; RequiresInternet = $true
        },
        @{
            Id = 'language-en-gb'; Category = '语言与显示'; Name = '英文（英国）'; Kind = 'LanguagePack'; LanguageTag = 'en-GB'
            Default = $false; Risk = 'Standard'; RequiresReboot = $true; RequiresInternet = $true
        },
        @{
            Id = 'language-ui-preference'; Category = '语言与显示'; Name = '设置 Windows 显示语言范围'; Kind = 'LanguageSettings'
            Default = $false; Risk = 'Elevated'; RequiresReboot = $true; RequiresInternet = $false
        },
        @{
            Id = 'font-supplements-cjk-indic-europe'; Category = '语言与显示'; Name = '字体补充（日本、韩国、欧洲、印度语）'; Kind = 'FontSupplementSet'
            Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true
            FontOptions = @(
                @{ Id = 'japanese'; Name = '日文字体'; CapabilityPatterns = @('Language.Fonts.Jpan*') },
                @{ Id = 'korean'; Name = '韩文字体'; CapabilityPatterns = @('Language.Fonts.Kore*') },
                @{ Id = 'european'; Name = '欧洲补充字体'; CapabilityPatterns = @('Language.Fonts.PanEuropeanSupplementalFonts*') },
                @{ Id = 'indic'; Name = '印度语（天城文）字体'; CapabilityPatterns = @('Language.Fonts.Deva*') }
            )
        },
        @{
            Id = 'feature-wireless-display'; Category = '语言与显示'; Name = '无线显示器'; Kind = 'CapabilitySet'
            Default = $false; Risk = 'Elevated'; RequiresReboot = $false; RequiresInternet = $true
            Capabilities = @('App.WirelessDisplay.Connect~~~~0.0.1.0')
        },
        @{
            Id = 'device-setup-region'; Category = '设备与账户'; Name = '实验性：处理 Device setup region（安装时地区）'; Kind = 'DeviceSetupRegion'
            Note = '可能被 UCPD 或组织策略拦截；失败不会影响其他初始化项目。'; Default = $false; Risk = 'High'; RequiresReboot = $true; RequiresInternet = $false
        },

        @{
            Id = 'runtime-ms-bundle'; Category = '运行库'; Name = '旧版 MS VC++、DX、.NET 离线运行库包'; Kind = 'Offline'
            Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(@{ Type = 'StartProcess'; FilePath = '$AssetRoot\【运行库】MSRuntimePackage_nanlon(推荐)_v4.2.24.1106.exe'; Arguments = '/S' })
        },
        @{
            Id = 'runtime-dotnet-8'; Category = '运行库'; Name = '.NET 8 Desktop Runtime'; Kind = 'Winget'
            PackageId = 'Microsoft.DotNet.DesktopRuntime.8'; Default = $true; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true
        },
        @{
            Id = 'runtime-dotnet-6'; Category = '运行库'; Name = '.NET 6 Desktop Runtime'; Kind = 'Winget'
            PackageId = 'Microsoft.DotNet.DesktopRuntime.6'; Default = $true; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true
        },
        @{
            Id = 'runtime-edge'; Category = '运行库'; Name = 'Microsoft Edge'; Kind = 'Winget'
            PackageId = 'Microsoft.Edge'; Scope = 'machine'; Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true
        },
        @{
            Id = 'runtime-webview2'; Category = '运行库'; Name = 'Microsoft Edge WebView2 Runtime'; Kind = 'Winget'
            PackageId = 'Microsoft.EdgeWebView2Runtime'; Scope = 'machine'; Default = $true; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true
        },
        @{
            Id = 'terminal-powershell7'; Category = '开发与终端'; Name = 'PowerShell 7（Microsoft Store）'; Kind = 'Winget'
            PackageId = '9MZ1SNWT0N5D'; Source = 'msstore'; Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true
        },

        @{ Id = 'app-vscode'; Category = '常用软件'; Name = 'Visual Studio Code'; Kind = 'Winget'; PackageId = 'Microsoft.VisualStudioCode'; Scope = 'machine'; Default = $true; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-pixpin-beta'; Category = '常用软件'; Name = 'PixPin Beta'; Kind = 'Winget'; PackageId = 'PixPin.PixPin.Beta'; Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-everything'; Category = '常用软件'; Name = 'Everything Alpha'; Kind = 'Winget'; PackageId = 'voidtools.Everything.Alpha'; Scope = 'machine'; Default = $true; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-chocolatey'; Category = '常用软件'; Name = 'Chocolatey'; Kind = 'Winget'; PackageId = 'Chocolatey.Chocolatey'; Scope = 'machine'; Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-peazip'; Category = '常用软件'; Name = 'PeaZip'; Kind = 'Winget'; PackageId = 'Giorgiotani.Peazip'; Scope = 'machine'; Default = $true; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-localsend'; Category = '常用软件'; Name = 'LocalSend'; Kind = 'Winget'; PackageId = 'LocalSend.LocalSend'; Scope = 'machine'; Default = $true; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-syncthing'; Category = '常用软件'; Name = 'Syncthing'; Kind = 'Winget'; PackageId = 'Syncthing.Syncthing'; Scope = 'machine'; Default = $true; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-syncthingtray'; Category = '常用软件'; Name = 'Syncthing Tray'; Kind = 'Winget'; PackageId = 'Martchus.syncthingtray'; Scope = 'machine'; Default = $true; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-docker-desktop'; Category = '开发与虚拟化'; Name = 'Docker Desktop'; Kind = 'Winget'; PackageId = 'Docker.DockerDesktop'; Scope = 'machine'; Default = $false; Risk = 'Elevated'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-vmware-workstation'; Category = '开发与虚拟化'; Name = 'VMware Workstation'; Kind = 'Winget'; PackageId = 'VMware.WorkstationPro'; Default = $false; Risk = 'Elevated'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-obs'; Category = '常用软件'; Name = 'OBS Studio'; Kind = 'Winget'; PackageId = 'OBSProject.OBSStudio'; Scope = 'machine'; Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-advanced-ip-scanner'; Category = '网络'; Name = 'Advanced IP Scanner'; Kind = 'Winget'; PackageId = 'Famatech.AdvancedIPScanner'; Scope = 'machine'; Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-openvpn'; Category = '网络'; Name = 'OpenVPN'; Kind = 'Winget'; PackageId = 'OpenVPNTechnologies.OpenVPN'; Scope = 'machine'; Default = $false; Risk = 'Elevated'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-wsl-manager'; Category = '开发与虚拟化'; Name = 'WSL Manager'; Kind = 'Winget'; PackageId = 'Bostrot.WSLManager'; Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-wsa-toolbox'; Category = '开发与虚拟化'; Name = 'WSA Toolbox'; Kind = 'Winget'; PackageId = '9PPSP2MKVTGT'; Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-sandboxie-plus'; Category = '安全与系统增强'; Name = 'Sandboxie-Plus'; Kind = 'Winget'; PackageId = 'Sandboxie.Plus'; Scope = 'machine'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-fxsound'; Category = '常用软件'; Name = 'FxSound'; Kind = 'Winget'; PackageId = 'FxSound.FxSound'; Scope = 'machine'; Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-escrcpy'; Category = '常用软件'; Name = 'Escrcpy'; Kind = 'Winget'; PackageId = 'viarotel.Escrcpy'; Scope = 'machine'; Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-nanabox'; Category = '开发与虚拟化'; Name = 'NanaBox'; Kind = 'Winget'; PackageId = '9NJXJSCB2JK0'; Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $true },
        @{ Id = 'app-energy-star-x'; Category = '安全与系统增强'; Name = 'Energy Star X'; Kind = 'Winget'; PackageId = '9NF7JTB3B17P'; Default = $false; Risk = 'Elevated'; RequiresReboot = $false; RequiresInternet = $true },

        @{
            Id = 'legacy-cursor-macos'; Category = '离线与旧版软件'; Name = 'macOS 黑色 Large With Shadow 光标'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(
                @{ Type = 'ExpandArchive'; Asset = '$AssetRoot\【系统优化】Windows鼠标指针优化\MacOS-Cursors-Medium-and-Small.zip'; Destination = '$ProgramFiles\MacOS-Cursors-Medium-and-Small' },
                @{ Type = 'StartProcess'; FilePath = '$ProgramFiles\MacOS-Cursors-Medium-and-Small\1. Sierra and newer\2. With Shadow\2. Large\~右键安装.inf'; Verb = 'Install' }
            )
        },
        @{
            Id = 'legacy-god-mode'; Category = '离线与旧版软件'; Name = '创建 Windows 上帝模式桌面文件夹'; Kind = 'Offline'; Default = $false; Risk = 'Standard'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(@{ Type = 'CreateGodModeFolder'; Path = '$PublicDesktop\【上帝模式】.{ED7BA470-8E54-465E-825C-99712043E01C}' })
        },
        @{
            Id = 'system-rdp-wrapper'; Category = '安全与系统增强'; Name = 'RDP Wrapper 多会话支持'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $true; RequiresInternet = $false
            Operations = @(@{ Type = 'StartProcess'; FilePath = '$AssetRoot\【系统增强】远程桌面多会话支持RDP_Wrapper_mod_v1.8.9.9.exe' })
        },
        @{
            Id = 'legacy-dismpp'; Category = '离线与旧版软件'; Name = 'Dism++ 离线工具'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(
                @{ Type = 'ExpandArchive'; Asset = '$AssetRoot\【系统优化】Dism++_Mod_v10.1.1002.1B.zip'; Destination = '$ProgramFiles\Dism++' },
                @{ Type = 'CreateShortcut'; ShortcutPath = '$PublicDesktop\Dism++.lnk'; TargetPath = '$ProgramFiles\Dism++\Dism++_Mod\Dism++x64.exe'; WorkingDirectory = '$ProgramFiles\Dism++\Dism++_Mod'; IconLocation = '$ProgramFiles\Dism++\Dism++_Mod\Dism++x64.exe,0' }
            )
        },
        @{
            Id = 'legacy-glary-utilities'; Category = '离线与旧版软件'; Name = 'Glary Utilities Portable'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(
                @{ Type = 'ExpandArchive'; Asset = '$AssetRoot\【系统优化】GlaryUtilities系统维护军刀-Pro-v6.17.0.21-Portable.zip'; Destination = '$ProgramFiles\GlaryUtilities' },
                @{ Type = 'CreateShortcut'; ShortcutPath = '$PublicDesktop\GlaryUtilities.lnk'; TargetPath = '$ProgramFiles\GlaryUtilities\GlaryUtilities\GlaryUtilitiesPortable.exe'; WorkingDirectory = '$ProgramFiles\GlaryUtilities\GlaryUtilities'; IconLocation = '$ProgramFiles\GlaryUtilities\GlaryUtilities\GlaryUtilitiesPortable.exe,0' }
            )
        },
        @{
            Id = 'legacy-windows11-easy-settings'; Category = '离线与旧版软件'; Name = 'Windows 11 轻松设置'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(
                @{ Type = 'ExpandArchive'; Asset = '$AssetRoot\【系统优化】Windows11轻松设置_PCbeta_V1.10Beta(20241023).zip'; Destination = '$ProgramFiles\Windows11轻松设置_PCbeta' },
                @{ Type = 'CreateShortcut'; ShortcutPath = '$PublicDesktop\Windows11轻松设置.lnk'; TargetPath = '$ProgramFiles\Windows11轻松设置_PCbeta\Windows11轻松设置_PCbeta\Windows11轻松设置.exe'; WorkingDirectory = '$ProgramFiles\Windows11轻松设置_PCbeta\Windows11轻松设置_PCbeta'; IconLocation = '$ProgramFiles\Windows11轻松设置_PCbeta\Windows11轻松设置_PCbeta\Windows11轻松设置.exe,0' }
            )
        },
        @{
            Id = 'legacy-asus-oled-screensaver'; Category = '离线与旧版软件'; Name = 'ASUS OLED 屏幕保护程序'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(@{ Type = 'CopyFile'; Asset = '$AssetRoot\【系统优化】ASUS-OLED屏幕保护程序-Care-Screensaver.scr'; Destination = '$SystemRoot\System32\Care-Screensaver.scr' })
        },
        @{
            Id = 'legacy-careueyes'; Category = '离线与旧版软件'; Name = 'CareUEyes'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(@{ Type = 'StartProcess'; FilePath = '$AssetRoot\【亮度调节】CareUEyes屏幕护眼_v2.4.5.0.exe'; Arguments = '/SILENT' })
        },
        @{
            Id = 'legacy-netsetman'; Category = '离线与旧版软件'; Name = 'NetSetMan 与 Wi-Fi 密码查看器'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(
                @{ Type = 'ExpandArchive'; Asset = '$AssetRoot\【网络工具】NetSetMan(网络快速切换工具)-Pro_v5.3.2.zip'; Destination = '$ProgramFiles\NetSetMan' },
                @{ Type = 'CopyFile'; Asset = '$AssetRoot\【网络工具】WIFI密码查看器.bat'; Destination = '$ProgramFiles\NetSetMan\WIFI密码查看器.bat' },
                @{ Type = 'CreateShortcut'; ShortcutPath = '$PublicDesktop\NetSetMan.lnk'; TargetPath = '$ProgramFiles\NetSetMan\netsetman.exe'; WorkingDirectory = '$ProgramFiles\NetSetMan'; IconLocation = '$ProgramFiles\NetSetMan\netsetman.exe,0' }
            )
        },
        @{
            Id = 'system-dnscrypt-proxy'; Category = '安全与系统增强'; Name = 'DNSCrypt-Proxy 服务'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(
                @{ Type = 'ExpandArchive'; Asset = '$AssetRoot\【系统增强】DNS安全加密DnsCrypt-Proxy-win64-2.1.5.zip'; Destination = '$ProgramFiles\DnsCrypt-Proxy' },
                @{ Type = 'StartProcess'; FilePath = '$ProgramFiles\DnsCrypt-Proxy\dnscrypt-proxy-win64\service-install.bat' }
            )
        },
        @{
            Id = 'legacy-miniupnp'; Category = '离线与旧版软件'; Name = 'MiniUPnP 路由映射工具'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(@{ Type = 'ExpandArchive'; Asset = '$AssetRoot\【网络工具】UPNP路由映射器-MiniUPnP_with_Shell_v20220515.zip'; Destination = '$ProgramFiles\MiniUPnP_with_Shell' })
        },
        @{
            Id = 'legacy-edge-installer'; Category = '离线与旧版软件'; Name = '旧版 Microsoft Edge 安装包'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(@{ Type = 'StartProcess'; FilePath = '$AssetRoot\【浏览器】MicrosoftEdgeSetup微软浏览器_zh-CN.exe' })
        },
        @{
            Id = 'legacy-chrome-installer'; Category = '离线与旧版软件'; Name = '旧版 Google Chrome 安装包'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(@{ Type = 'StartProcess'; FilePath = '$AssetRoot\【浏览器】ChromeSetup谷歌浏览器_zh-HK.exe' })
        },
        @{
            Id = 'legacy-purecodec'; Category = '离线与旧版软件'; Name = 'PureCodec'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(@{ Type = 'StartProcess'; FilePath = '$AssetRoot\【办公影音】PureCodec完美解码_v2024.09.24.exe'; Arguments = '/S /D=C:\Program Files\PureCodec' })
        },
        @{
            Id = 'legacy-2345-pic'; Category = '离线与旧版软件'; Name = '2345 看图王'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(@{ Type = 'StartProcess'; FilePath = '$AssetRoot\【办公影音】看图王_去广告版_v11.4.1.10796_mefcl_x64_Setup_v1.exe'; Arguments = '/S /D=C:\Program Files\2345Pic' })
        },
        @{
            Id = 'legacy-imfile'; Category = '离线与旧版软件'; Name = 'imFile'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(
                @{ Type = 'StartProcess'; FilePath = '$AssetRoot\【下载上传】imFile(配合Aria2-Explorer插件改RPC16800)-v1.1.2.exe'; Arguments = '/S /D=C:\Program Files\imFile' },
                @{ Type = 'CopyFile'; Asset = '$AssetRoot\【下载上传】NDM多线程下载器_单文件_v1.4.10.exe'; Destination = '$ProgramFiles\imFile\NDM多线程下载器_单文件_v1.4.10.exe' }
            )
        },
        @{
            Id = 'legacy-desktop-shortcuts'; Category = '离线与旧版软件'; Name = '旧版公共桌面快捷方式'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(
                @{ Type = 'CopyFile'; Asset = '$AssetRoot\【系统快捷方式】Internet Explorer for Windows 11.lnk'; Destination = '$PublicDesktop\Internet Explorer for Windows 11.lnk' },
                @{ Type = 'CopyFile'; Asset = '$AssetRoot\【系统快捷方式】This PC.lnk'; Destination = '$PublicDesktop\This PC.lnk' },
                @{ Type = 'CopyFile'; Asset = '$AssetRoot\【下载上传】微软文件服务器下载器(网页版).url'; Destination = '$PublicDesktop\微软文件服务器下载器(网页版).url' },
                @{ Type = 'CopyFile'; Asset = '$AssetRoot\【系统快捷方式】Windows Store.url'; Destination = '$PublicDesktop\Windows Store.url' }
            )
        },
        @{
            Id = 'legacy-bandizip'; Category = '离线与旧版软件'; Name = 'Bandizip Pro'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(@{ Type = 'StartProcess'; FilePath = '$AssetRoot\【压缩软件】Bandizip-Pro-v7.36-x64-Repack.exe'; Arguments = '/ai /gm2 /InstallPath="C:\Program Files\BandiZip"' })
        },
        @{
            Id = 'legacy-imgdrive'; Category = '离线与旧版软件'; Name = 'ImgDrive'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(
                @{ Type = 'ExpandArchive'; Asset = '$AssetRoot\【办公影音】ImgDrive-Pro_v2.1.9.zip'; Destination = '$ProgramFiles' },
                @{ Type = 'StartProcess'; FilePath = '$ProgramFiles\ImgDrive-Pro\imgdrive.exe' },
                @{ Type = 'CreateShortcut'; ShortcutPath = '$PublicDesktop\ImgDrive.lnk'; TargetPath = '$ProgramFiles\ImgDrive-Pro\imgdrive.exe'; WorkingDirectory = '$ProgramFiles\ImgDrive-Pro'; IconLocation = '$ProgramFiles\ImgDrive-Pro\imgdrive.exe,0' }
            )
        },
        @{
            Id = 'legacy-huorong'; Category = '离线与旧版软件'; Name = '火绒 Sysdiag'; Kind = 'Winget'; PackageId = 'XPDNH1FMW7NB40'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $true
        },
        @{
            Id = 'legacy-sogou-input'; Category = '离线与旧版软件'; Name = '搜狗输入法'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(@{ Type = 'StartProcess'; FilePath = '$AssetRoot\【输入法】搜狗输入法_去广告_精简优化版_v14.9.0.9966.exe'; Arguments = '/S /D=C:\Program Files\SogouInput' })
        },
        @{
            Id = 'legacy-flclash'; Category = '离线与旧版软件'; Name = 'FlClash'; Kind = 'Offline'; Default = $false; Risk = 'High'; RequiresReboot = $false; RequiresInternet = $false
            Operations = @(
                @{ Type = 'ExpandArchive'; Asset = '$AssetRoot\【富强工具】FlClash-Proxy_v0.8.66.zip'; Destination = '$ProgramFiles' },
                @{ Type = 'StartProcess'; FilePath = '$ProgramFiles\FlClash-Proxy\FlClash.exe' },
                @{ Type = 'CreateShortcut'; ShortcutPath = '$PublicDesktop\FlClash.lnk'; TargetPath = '$ProgramFiles\FlClash-Proxy\FlClash.exe'; WorkingDirectory = '$ProgramFiles\FlClash-Proxy'; IconLocation = '$ProgramFiles\FlClash-Proxy\FlClash.exe,0' }
            )
        }
    )
}
