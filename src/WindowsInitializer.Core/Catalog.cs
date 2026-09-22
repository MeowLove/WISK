using System.Collections.Immutable;
using WindowsInitializer.Contracts;

namespace WindowsInitializer.Core;

public sealed class Catalog
{
    public const string Version = "3.0.0";

    private static readonly ImmutableArray<TaskDescriptor> RawTasks =
    [
        new("computer-name", "Windows computer name", "Device and accounts", RiskLevel.Elevated, TaskKind.SystemSetting, RequiresAdministrator: true, RequiresReboot: true),
        new("accounts-local", "Local accounts", "Device and accounts", RiskLevel.Elevated, TaskKind.Account, RequiresAdministrator: true),
        new("language-zh-cn", "Chinese (Simplified, China)", "Language and display", RiskLevel.Standard, TaskKind.LanguagePack, RequiresInternet: true, RequiresReboot: true, LanguageTag: "zh-CN"),
        new("language-zh-tw", "Chinese (Traditional, Taiwan)", "Language and display", RiskLevel.Standard, TaskKind.LanguagePack, RequiresInternet: true, RequiresReboot: true, LanguageTag: "zh-TW"),
        new("language-en-us", "English (United States)", "Language and display", RiskLevel.Standard, TaskKind.LanguagePack, RequiresInternet: true, RequiresReboot: true, LanguageTag: "en-US"),
        new("language-en-gb", "English (United Kingdom)", "Language and display", RiskLevel.Standard, TaskKind.LanguagePack, RequiresInternet: true, RequiresReboot: true, LanguageTag: "en-GB"),
        new("language-zh-sg", "Chinese (Singapore) regional format", "Language and display", RiskLevel.Standard, TaskKind.RegionalFormat, ExclusiveGroup: "regional-format", Culture: "zh-SG"),
        new("language-zh-hk", "Chinese (Hong Kong) regional format", "Language and display", RiskLevel.Standard, TaskKind.RegionalFormat, ExclusiveGroup: "regional-format", Culture: "zh-HK"),
        new("language-ui-preference", "Windows display language preference", "Language and display", RiskLevel.Elevated, TaskKind.SystemSetting, RequiresAdministrator: true, RequiresReboot: true),
        new("font-supplements-cjk-indic-europe", "Supplemental fonts", "Language and display", RiskLevel.Standard, TaskKind.Capability, RequiresInternet: true),
        new("feature-wireless-display", "Wireless display", "Language and display", RiskLevel.Elevated, TaskKind.Capability, RequiresAdministrator: true, RequiresInternet: true),
        new("feature-wsl", "Windows Subsystem for Linux", "Windows features", RiskLevel.Elevated, TaskKind.Capability, RequiresAdministrator: true, RequiresInternet: true, RequiresReboot: true,
            Relations: [new("feature-virtual-machine-platform", TaskRelationKind.Recommends, "relationWslRecommendsVirtualization")], ResourceLocks: ["dism"], Rollback: RollbackSupport.Conditional, Boundary: ExecutionBoundary.Reboot),
        new("feature-virtual-machine-platform", "Virtual Machine Platform", "Windows features", RiskLevel.Elevated, TaskKind.Capability, RequiresAdministrator: true, RequiresReboot: true),
        new("feature-sandbox", "Windows Sandbox", "Windows features", RiskLevel.Elevated, TaskKind.Capability, RequiresAdministrator: true, RequiresReboot: true),
        new("feature-hyper-v", "Hyper-V", "Windows features", RiskLevel.Elevated, TaskKind.Capability, RequiresAdministrator: true, RequiresReboot: true),
        new("capability-openssh-client", "OpenSSH Client", "Windows features", RiskLevel.Elevated, TaskKind.Capability, RequiresAdministrator: true),
        new("feature-telnet-client", "Telnet Client", "Windows features", RiskLevel.Elevated, TaskKind.Capability, RequiresAdministrator: true, Version: "3.0.0"),
        ..ConfigurableRegistrySettingCatalog.All.Select(CreateConfigurableTaskDescriptor),
        new("setting-hibernation", "Enable hibernation", "Power and shutdown", RiskLevel.Elevated, TaskKind.SystemSetting, RequiresAdministrator: true, Version: "3.0.0", ResourceLocks: ["power-policy"], Rollback: RollbackSupport.Conditional),
        new("safety-restore-point", "Create system restore point", "Safety and recovery", RiskLevel.Elevated, TaskKind.SystemSetting, RequiresAdministrator: true, Version: "3.0.0", ResourceLocks: ["system-restore"], Rollback: RollbackSupport.Manual),
        new("setting-windows-update-mode", "Windows Update mode", "Windows Update", RiskLevel.Elevated, TaskKind.SystemSetting, RequiresAdministrator: true, Version: "3.0.0", ResourceLocks: ["windows-update-policy", "registry"], Rollback: RollbackSupport.Exact),
        new("setting-power-plan", "Active power plan", "Power and shutdown", RiskLevel.Elevated, TaskKind.SystemSetting, RequiresAdministrator: true, Version: "3.0.0", ResourceLocks: ["power-policy"], Rollback: RollbackSupport.Conditional),
        new("setting-sleep-timeouts", "Sleep and display timeouts", "Power and shutdown", RiskLevel.Elevated, TaskKind.SystemSetting, RequiresAdministrator: true, Version: "3.0.0", ResourceLocks: ["power-policy"], Rollback: RollbackSupport.Conditional),
        new("device-setup-region", "Device setup region", "Device and accounts", RiskLevel.High, TaskKind.SystemSetting, RequiresAdministrator: true, RequiresReboot: true),
        new("runtime-ms-bundle", "Offline Microsoft runtime bundle", "Runtime", RiskLevel.High, TaskKind.OfflineAsset, RequiresAdministrator: true),
        new("runtime-dotnet-8", ".NET 8 Desktop Runtime", "Runtime", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Microsoft.DotNet.DesktopRuntime.8"),
        new("runtime-dotnet-9", ".NET 9 Desktop Runtime", "Runtime", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Microsoft.DotNet.DesktopRuntime.9"),
        new("runtime-dotnet-10", ".NET 10 Desktop Runtime", "Runtime", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Microsoft.DotNet.DesktopRuntime.10"),
        new("runtime-dotnet-6", ".NET 6 Desktop Runtime", "Runtime", RiskLevel.Elevated, TaskKind.Winget, RequiresInternet: true, PackageId: "Microsoft.DotNet.DesktopRuntime.6"),
        new("runtime-vcredist-2015-x64", "Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Runtime", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Microsoft.VCRedist.2015+.x64"),
        new("runtime-vcredist-2015-x86", "Microsoft Visual C++ 2015-2022 Redistributable (x86)", "Runtime", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Microsoft.VCRedist.2015+.x86"),
        new("app-everything", "Everything Alpha", "Applications", RiskLevel.Elevated, TaskKind.Winget, RequiresAdministrator: true, RequiresInternet: true, PackageId: "voidtools.Everything.Alpha"),
        new("app-everything-stable", "Everything", "Utilities", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "voidtools.Everything"),
        new("runtime-edge", "Microsoft Edge", "Browsers", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Microsoft.Edge"),
        new("runtime-webview2", "Microsoft Edge WebView2 Runtime", "Runtime", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Microsoft.EdgeWebView2Runtime"),
        new("terminal-powershell7", "PowerShell 7", "Development tools", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "9MZ1SNWT0N5D"),
        new("terminal-windows", "Windows Terminal", "Development tools", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Microsoft.WindowsTerminal"),
        new("app-git", "Git", "Development tools", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Git.Git"),
        new("app-vscode", "Visual Studio Code", "Development tools", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Microsoft.VisualStudioCode"),
        new("runtime-python-313", "Python 3.13", "Development tools", RiskLevel.Standard, TaskKind.Winget, Version: "3.0.0", RequiresInternet: true, PackageId: "Python.Python.3.13", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("runtime-nodejs-lts", "Node.js LTS", "Development tools", RiskLevel.Standard, TaskKind.Winget, Version: "3.0.0", RequiresInternet: true, PackageId: "OpenJS.NodeJS.LTS", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-github-cli", "GitHub CLI", "Development tools", RiskLevel.Standard, TaskKind.Winget, Version: "3.0.0", RequiresInternet: true, PackageId: "GitHub.cli", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("runtime-dotnet-sdk-10", ".NET 10 SDK", "Development tools", RiskLevel.Standard, TaskKind.Winget, Version: "3.0.0", RequiresInternet: true, PackageId: "Microsoft.DotNet.SDK.10", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("runtime-temurin-17", "Eclipse Temurin JDK 17", "Runtime", RiskLevel.Standard, TaskKind.Winget, Version: "3.0.0", RequiresInternet: true, PackageId: "EclipseAdoptium.Temurin.17.JDK", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("runtime-temurin-21", "Eclipse Temurin JDK 21", "Runtime", RiskLevel.Standard, TaskKind.Winget, Version: "3.0.0", RequiresInternet: true, PackageId: "EclipseAdoptium.Temurin.21.JDK", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-pixpin-beta", "PixPin Beta", "Graphics and media", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "PixPin.PixPin.Beta"),
        new("app-chocolatey", "Chocolatey", "Package managers", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Chocolatey.Chocolatey"),
        new("app-peazip", "PeaZip", "Utilities", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Giorgiotani.Peazip"),
        new("app-7zip", "7-Zip", "Utilities", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "7zip.7zip"),
        new("app-notepad-plus-plus", "Notepad++", "Utilities", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Notepad++.Notepad++"),
        new("app-sumatra-pdf", "SumatraPDF", "Utilities", RiskLevel.Standard, TaskKind.Winget, Version: "3.0.0", RequiresInternet: true, PackageId: "SumatraPDF.SumatraPDF", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-keepassxc", "KeePassXC", "Security", RiskLevel.Standard, TaskKind.Winget, Version: "3.0.0", RequiresInternet: true, PackageId: "KeePassXCTeam.KeePassXC", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-sharex", "ShareX", "Utilities", RiskLevel.Standard, TaskKind.Winget, Version: "3.0.0", RequiresInternet: true, PackageId: "ShareX.ShareX", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-sysinternals-suite", "Sysinternals Suite", "System utilities", RiskLevel.Standard, TaskKind.Winget, Version: "3.0.0", RequiresInternet: true, PackageId: "Microsoft.Sysinternals.Suite", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-powertoys", "Microsoft PowerToys", "System utilities", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Microsoft.PowerToys"),
        new("app-vlc", "VLC media player", "Graphics and media", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "VideoLAN.VLC"),
        new("browser-chrome", "Google Chrome", "Browsers", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Google.Chrome"),
        new("browser-firefox", "Mozilla Firefox", "Browsers", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Mozilla.Firefox"),
        new("app-localsend", "LocalSend", "Networking", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "LocalSend.LocalSend"),
        new("app-syncthing", "Syncthing", "Networking", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Syncthing.Syncthing"),
        new("app-syncthingtray", "Syncthing Tray", "Networking", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Martchus.syncthingtray"),
        new("app-obs", "OBS Studio", "Graphics and media", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "OBSProject.OBSStudio"),
        new("app-advanced-ip-scanner", "Advanced IP Scanner", "Networking", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Famatech.AdvancedIPScanner"),
        new("app-wsl-manager", "WSL Manager", "Virtualization", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Bostrot.WSLManager",
            Relations: [new("feature-wsl", TaskRelationKind.Requires, "relationWslManagerRequiresWsl")], ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-wsa-toolbox", "WSA Toolbox", "Virtualization", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "9PPSP2MKVTGT"),
        new("app-fxsound", "FxSound", "Graphics and media", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "FxSound.FxSound"),
        new("app-escrcpy", "Escrcpy", "Mobile tools", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "viarotel.Escrcpy"),
        new("app-nanabox", "NanaBox", "Virtualization", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "9NJXJSCB2JK0"),
        new("app-docker-desktop", "Docker Desktop", "Virtualization", RiskLevel.Elevated, TaskKind.Winget, RequiresAdministrator: true, RequiresInternet: true, PackageId: "Docker.DockerDesktop",
            Relations: [new("feature-wsl", TaskRelationKind.Recommends, "relationDockerRecommendsWsl"), new("feature-virtual-machine-platform", TaskRelationKind.Recommends, "relationDockerRecommendsVirtualization")], ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-vmware-workstation", "VMware Workstation", "Virtualization", RiskLevel.Elevated, TaskKind.Winget, RequiresAdministrator: true, RequiresInternet: true, PackageId: "VMware.WorkstationPro"),
        new("app-openvpn", "OpenVPN", "Networking", RiskLevel.Elevated, TaskKind.Winget, RequiresAdministrator: true, RequiresInternet: true, PackageId: "OpenVPNTechnologies.OpenVPN"),
        new("app-energy-star-x", "Energy Star X", "System utilities", RiskLevel.Elevated, TaskKind.Winget, RequiresAdministrator: true, RequiresInternet: true, PackageId: "9NF7JTB3B17P"),
        new("app-libreoffice", "LibreOffice", "Office and productivity", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "TheDocumentFoundation.LibreOffice", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-thunderbird", "Mozilla Thunderbird", "Office and productivity", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Mozilla.Thunderbird", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-obsidian", "Obsidian", "Office and productivity", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Obsidian.Obsidian", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-discord", "Discord", "Communication", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Discord.Discord", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-github-desktop", "GitHub Desktop", "Development tools", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "GitHub.GitHubDesktop", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-rufus", "Rufus", "System utilities", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Rufus.Rufus", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-inkscape", "Inkscape", "Graphics and media", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "Inkscape.Inkscape", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-putty", "PuTTY", "Networking", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "PuTTY.PuTTY", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("app-winscp", "WinSCP", "Networking", RiskLevel.Standard, TaskKind.Winget, RequiresInternet: true, PackageId: "WinSCP.WinSCP", ResourceLocks: ["winget"], Rollback: RollbackSupport.Conditional),
        new("legacy-god-mode", "Create Windows God Mode folder", "Offline and legacy", RiskLevel.Standard, TaskKind.SystemSetting),
        new("legacy-cursor-macos", "macOS cursor package", "Offline and legacy", RiskLevel.High, TaskKind.OfflineAsset, RequiresAdministrator: true),
        new("system-rdp-wrapper", "RDP Wrapper multi-session support", "Security and system", RiskLevel.High, TaskKind.OfflineAsset, RequiresAdministrator: true, RequiresReboot: true),
        new("app-sandboxie-plus", "Sandboxie-Plus", "Security", RiskLevel.High, TaskKind.Winget, RequiresAdministrator: true, RequiresInternet: true, PackageId: "Sandboxie.Plus"),
        new("legacy-dismpp", "Dism++ offline tool", "Offline and legacy", RiskLevel.High, TaskKind.OfflineAsset, RequiresAdministrator: true),
        new("legacy-glary-utilities", "Glary Utilities legacy package", "Offline and legacy", RiskLevel.High, TaskKind.OfflineAsset, RequiresAdministrator: true),
        new("legacy-windows11-easy-settings", "Windows 11 Easy Settings", "Offline and legacy", RiskLevel.High, TaskKind.OfflineAsset, RequiresAdministrator: true),
        new("legacy-asus-oled-screensaver", "ASUS OLED screensaver", "Offline and legacy", RiskLevel.High, TaskKind.OfflineAsset),
        new("legacy-careueyes", "CareUEyes", "Offline and legacy", RiskLevel.High, TaskKind.OfflineAsset),
        new("legacy-netsetman", "NetSetMan", "Network", RiskLevel.High, TaskKind.OfflineAsset, RequiresAdministrator: true),
        new("system-dnscrypt-proxy", "DNSCrypt proxy", "Network", RiskLevel.High, TaskKind.OfflineAsset, RequiresAdministrator: true),
        new("legacy-miniupnp", "MiniUPnP", "Network", RiskLevel.High, TaskKind.OfflineAsset, RequiresAdministrator: true),
        new("legacy-edge-installer", "Legacy Edge installer", "Offline and legacy", RiskLevel.High, TaskKind.OfflineAsset, RequiresAdministrator: true),
        new("legacy-chrome-installer", "Legacy Chrome installer", "Offline and legacy", RiskLevel.High, TaskKind.OfflineAsset, RequiresAdministrator: true),
        new("legacy-purecodec", "PureCodec", "Offline and legacy", RiskLevel.High, TaskKind.OfflineAsset),
        new("legacy-2345-pic", "2345 picture tool", "Offline and legacy", RiskLevel.High, TaskKind.OfflineAsset),
        new("legacy-imfile", "IMFile", "Offline and legacy", RiskLevel.High, TaskKind.OfflineAsset),
        new("legacy-desktop-shortcuts", "Desktop shortcuts", "Offline and legacy", RiskLevel.High, TaskKind.OfflineAsset),
        new("legacy-bandizip", "Bandizip legacy package", "Offline and legacy", RiskLevel.High, TaskKind.OfflineAsset),
        new("legacy-imgdrive", "ImgDrive", "Offline and legacy", RiskLevel.High, TaskKind.OfflineAsset, RequiresAdministrator: true),
        new("legacy-huorong", "Huorong Sysdiag", "Security and system", RiskLevel.High, TaskKind.Winget, RequiresAdministrator: true, RequiresInternet: true, PackageId: "XPDNH1FMW7NB40"),
        new("legacy-sogou-input", "Sogou input", "Language and display", RiskLevel.High, TaskKind.OfflineAsset),
        new("legacy-flclash", "FlClash", "Network", RiskLevel.High, TaskKind.OfflineAsset)
    ];

    private readonly ImmutableArray<TaskDescriptor> _tasks;
    private sealed record ExtensionRegistration(InstalledExtension Extension, ExtensionTaskEntry Task);

    private static TaskDescriptor CreateConfigurableTaskDescriptor(ConfigurableRegistrySettingDefinition setting) =>
        new(setting.TaskId, setting.DisplayName, setting.Category, setting.Risk, TaskKind.SystemSetting,
            Version: "3.0.0", RequiresAdministrator: setting.RequiresAdministrator,
            RequiresReboot: setting.Boundary == ExecutionBoundary.Reboot,
            ResourceLocks: setting.ResourceLocks.IsDefault ? ["registry"] : setting.ResourceLocks,
            Rollback: RollbackSupport.Exact, Boundary: setting.Boundary);

    public Catalog(IEnumerable<InstalledExtension>? installedExtensions = null)
    {
        var registrations = (installedExtensions ?? [])
            .SelectMany(extension => extension.Tasks.Select(task => new ExtensionRegistration(extension, task)))
            .GroupBy(item => item.Task.TaskId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        _tasks = SettingsPresetConflicts.Apply(RawTasks.AddRange(RegistryOptimizationCatalog.CreateTaskDescriptors())
            .Select(task => ActivateExtension(task, registrations)).ToImmutableArray());
    }

    private static TaskDescriptor ActivateExtension(
        TaskDescriptor task,
        IReadOnlyDictionary<string, ExtensionRegistration[]> registrations)
    {
        if (task.Kind != TaskKind.OfflineAsset) return task;
        if (!registrations.TryGetValue(task.Id, out var matches) || matches.Length != 1)
            return task with
            {
                Source = TaskSource.ControlledExtension,
                IsAvailable = false,
                UnavailableReason = matches is { Length: > 1 }
                    ? "Multiple installed extension packages provide this task."
                    : "Requires a validated controlled extension package."
            };
        var extension = matches[0].Extension;
        var entry = matches[0].Task;
        var executable = extension.Files.SingleOrDefault(file => file.Path.Replace('\\', '/').Equals(entry.ExecutablePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        if (executable is null || !executable.Executable)
            return task with { Source = TaskSource.ControlledExtension, IsAvailable = false, UnavailableReason = "Installed extension registration is invalid." };
        return task with
        {
            Source = TaskSource.ControlledExtension,
            IsAvailable = true,
            UnavailableReason = null,
            ExtensionPackageId = extension.PackageId,
            ExtensionPackageVersion = extension.Version,
            ExtensionManifestHash = extension.ManifestSha256,
            ExtensionExecutablePath = Path.GetFullPath(Path.Combine(extension.InstallPath, entry.ExecutablePath)),
            ExtensionExecutableHash = executable.Sha256,
            ExtensionProtocol = entry.Protocol
        };
    }

    public IReadOnlyList<TaskDescriptor> GetTasks() => _tasks;
    public TaskDescriptor? Find(string taskId) => _tasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.OrdinalIgnoreCase));
    public IReadOnlyList<TaskDescriptor> GetDefaultTasks() => _tasks.Where(task => task.DefaultSelected).ToArray();
}
