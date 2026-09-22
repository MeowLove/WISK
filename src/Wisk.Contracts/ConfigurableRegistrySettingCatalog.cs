using System.Collections.Immutable;

namespace Wisk.Contracts;

/// <summary>
/// Canonical metadata for registry-backed settings that accept a safe state value.
/// The default state removes the per-user/per-machine override instead of guessing a
/// Windows-version default value.
/// </summary>
public sealed record ConfigurableRegistrySettingDefinition(
    string TaskId,
    string DisplayName,
    string SourceFile,
    string Hive,
    string Path,
    string ValueName,
    int EnabledValue,
    int DisabledValue,
    string SourceValue,
    string Category,
    RiskLevel Risk,
    bool RequiresAdministrator,
    ExecutionBoundary Boundary,
    ImmutableArray<string> ResourceLocks = default)
{
    public bool IsValidState(string? state) => state is
        ConfigurableRegistrySettingCatalog.EnabledState or
        ConfigurableRegistrySettingCatalog.DisabledState or
        ConfigurableRegistrySettingCatalog.DefaultState;

    public int? ResolveValue(string state) => state switch
    {
        ConfigurableRegistrySettingCatalog.EnabledState => EnabledValue,
        ConfigurableRegistrySettingCatalog.DisabledState => DisabledValue,
        ConfigurableRegistrySettingCatalog.DefaultState => null,
        _ => throw new ArgumentException("The registry setting state is invalid.", nameof(state))
    };
}

public static class ConfigurableRegistrySettingCatalog
{
    public const string EnabledState = "enabled";
    public const string DisabledState = "disabled";
    public const string DefaultState = "default";

    private static readonly ImmutableArray<ConfigurableRegistrySettingDefinition> Definitions =
    [
        new("setting-long-paths", "Long path support", RegistrySourceFiles.System, "HKLM", @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1, 0, "dword:00000001", "Windows features", RiskLevel.Elevated, true, ExecutionBoundary.Reboot),
        new("setting-developer-mode", "Developer mode", RegistrySourceFiles.System, "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock", "AllowDevelopmentWithoutDevLicense", 1, 0, "dword:00000001", "Windows features", RiskLevel.Elevated, true, ExecutionBoundary.None),
        new("setting-show-file-extensions", "Show file name extensions", RegistrySourceFiles.User, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 0, 1, "dword:00000000", "Desktop and Explorer", RiskLevel.Standard, false, ExecutionBoundary.None),
        new("setting-fast-startup", "Enable fast startup", RegistrySourceFiles.System, "HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 1, 0, "dword:00000001", "Power and shutdown", RiskLevel.Elevated, true, ExecutionBoundary.None, ["power-policy", "registry"]),
        new("setting-taskbar-seconds", "Show seconds in taskbar clock", RegistrySourceFiles.User, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSecondsInSystemClock", 1, 0, "dword:00000001", "Desktop and Explorer", RiskLevel.Standard, false, ExecutionBoundary.SignOut),
        new("setting-taskbar-end-task", "Enable taskbar End task", RegistrySourceFiles.User, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask", 1, 0, "dword:00000001", "Desktop and Explorer", RiskLevel.Standard, false, ExecutionBoundary.SignOut),
        new("setting-taskbar-widgets", "Show taskbar widgets", RegistrySourceFiles.User, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 1, 0, "dword:00000000", "Desktop and Explorer", RiskLevel.Standard, false, ExecutionBoundary.SignOut),
        new("setting-taskbar-meet-now", "Show Meet Now button", RegistrySourceFiles.User, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarMn", 1, 0, "dword:00000000", "Desktop and Explorer", RiskLevel.Standard, false, ExecutionBoundary.SignOut),
        new("setting-taskbar-copilot-button", "Show Copilot button", RegistrySourceFiles.User, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton", 1, 0, "dword:00000000", "Desktop and Explorer", RiskLevel.Standard, false, ExecutionBoundary.SignOut),
        new("setting-explorer-this-pc", "Open File Explorer to This PC", RegistrySourceFiles.User, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo", 1, 2, "dword:00000001", "Desktop and Explorer", RiskLevel.Standard, false, ExecutionBoundary.SignOut),
        new("setting-notification-banners", "Show notification banners", RegistrySourceFiles.User, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 1, 0, "dword:00000000", "Privacy and notifications", RiskLevel.Standard, false, ExecutionBoundary.SignOut),
        new("setting-lock-screen-notifications", "Show lock-screen notifications", RegistrySourceFiles.User, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "LockScreenToastEnabled", 1, 0, "dword:00000000", "Privacy and notifications", RiskLevel.Standard, false, ExecutionBoundary.SignOut),
        new("setting-explorer-full-path", "Show full path in File Explorer title", RegistrySourceFiles.User, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState", "FullPath", 1, 0, "dword:00000001", "Desktop and Explorer", RiskLevel.Standard, false, ExecutionBoundary.SignOut),
        new("setting-explorer-separate-process", "Run File Explorer windows in separate processes", RegistrySourceFiles.User, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "SeparateProcess", 1, 0, "dword:00000001", "Desktop and Explorer", RiskLevel.Standard, false, ExecutionBoundary.SignOut),
        new("setting-recent-documents-tracking", "Track recent documents in Start and jump lists", RegistrySourceFiles.User, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackDocs", 1, 0, "dword:00000000", "Privacy and notifications", RiskLevel.Standard, false, ExecutionBoundary.SignOut),
        new("setting-advertising-id", "Personalized advertising ID", RegistrySourceFiles.User, "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 1, 0, "dword:00000000", "Privacy and notifications", RiskLevel.Standard, false, ExecutionBoundary.SignOut),
        new("setting-tailored-experiences", "Tailored experiences from diagnostic data", RegistrySourceFiles.User, "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 1, 0, "dword:00000000", "Privacy and notifications", RiskLevel.Standard, false, ExecutionBoundary.SignOut),
        new("setting-search-web-results", "Allow web results in Windows Search", RegistrySourceFiles.User, "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 1, 0, "dword:00000000", "Privacy and notifications", RiskLevel.Standard, false, ExecutionBoundary.SignOut)
    ];

    public static IReadOnlyList<ConfigurableRegistrySettingDefinition> All => Definitions;

    public static ConfigurableRegistrySettingDefinition? Find(string taskId) =>
        Definitions.FirstOrDefault(setting => setting.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase));

    public static bool IsKnownState(string? state) => state is EnabledState or DisabledState or DefaultState;
}
