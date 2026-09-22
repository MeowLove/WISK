namespace Wisk.Contracts;

public sealed record RegistryTarget(string TaskId, string Hive, string Path, string ValueName);

public static class RegistryTargetCatalog
{
    private static readonly IReadOnlyList<RegistryTarget> AdditionalTargets =
    [
        new("setting-taskbar-end-task", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\DeveloperSettings", "TaskbarEndTask"),
        new("setting-windows-update-mode", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate"),
        new("setting-windows-update-mode", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions")
    ];

    public static IReadOnlyList<RegistryTarget> All { get; } =
        ConfigurableRegistrySettingCatalog.All
            .Select(setting => new RegistryTarget(setting.TaskId, setting.Hive, setting.Path, setting.ValueName))
            .Concat(AdditionalTargets)
            .ToArray();
}
