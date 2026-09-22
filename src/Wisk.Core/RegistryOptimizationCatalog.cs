using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Wisk.Contracts;

namespace Wisk.Core;

public enum RegistryOperationKind
{
    String,
    DWord,
    Binary,
    ExpandString,
    DeleteValue,
    DeleteKey,
    Unsupported
}

public sealed record RegistryOptimizationEntry(
    string TaskId,
    string SourceFile,
    string Hive,
    string Path,
    string ValueName,
    RegistryOperationKind Operation,
    string RawValue,
    string Category,
    RiskLevel Risk,
    bool RequiresAdministrator,
    bool IsSupported,
    string? UnsupportedReason)
{
    public string DisplayName => ValueName == "@" ? Path.Split('\\').Last() + " default value" : ValueName;
    public string Location => $"{Hive}\\{Path}";
}

public sealed record RegistryOptimizationGroup(
    string TaskId,
    string DisplayName,
    string Category,
    ImmutableArray<RegistryOptimizationEntry> Members)
{
    public RiskLevel Risk => Members.Max(entry => entry.Risk);
    public bool RequiresAdministrator => Members.Any(entry => entry.RequiresAdministrator);
    public bool IsSupported => Members.All(entry => entry.IsSupported);
    public string? UnsupportedReason => Members.FirstOrDefault(entry => !entry.IsSupported)?.UnsupportedReason;
    public string Scope => string.Join(" / ", Members.Select(entry => entry.Hive).Distinct(StringComparer.OrdinalIgnoreCase));
    public string Location
    {
        get
        {
            var paths = Members.Select(entry => entry.Path).ToArray();
            var common = paths[0];
            while (common.Length > 0 && paths.Any(path => !path.StartsWith(common, StringComparison.OrdinalIgnoreCase)))
            {
                var separator = common.LastIndexOf('\\');
                common = separator < 0 ? string.Empty : common[..separator];
            }
            return string.IsNullOrEmpty(common) ? Scope : $"{Scope}\\{common}";
        }
    }
}

public static partial class RegistryOptimizationCatalog
{
    private static readonly Lazy<ImmutableArray<RegistryOptimizationEntry>> Entries = new(LoadEmbedded);
    private static readonly Lazy<ImmutableArray<RegistryOptimizationGroup>> Groups = new(() => CreateGroups(Entries.Value));

    public static IReadOnlyList<RegistryOptimizationEntry> GetEntries() => Entries.Value;

    public static RegistryOptimizationEntry? Find(string taskId) =>
        Entries.Value.FirstOrDefault(entry => entry.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<RegistryOptimizationGroup> GetGroups() => Groups.Value;

    public static RegistryOptimizationGroup? FindGroup(string taskId) =>
        Groups.Value.FirstOrDefault(group => group.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase));

    public static ImmutableArray<RegistryOptimizationEntry> GetTaskEntries(string taskId)
    {
        if (Find(taskId) is { } entry) return [entry];
        return FindGroup(taskId)?.Members ?? [];
    }

    public static ImmutableArray<RegistryOptimizationEntry> Parse(string sourceFile, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        ArgumentNullException.ThrowIfNull(content);

        var entries = ImmutableArray.CreateBuilder<RegistryOptimizationEntry>();
        string? hive = null;
        string? path = null;
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.Equals("Windows Registry Editor Version 5.00", StringComparison.OrdinalIgnoreCase))
                continue;

            var section = SectionRegex().Match(line);
            if (section.Success)
            {
                var deleteKey = section.Groups[1].Value == "-";
                var fullPath = section.Groups[2].Value.Trim();
                (hive, path) = SplitHive(fullPath);
                if (deleteKey && hive is not null && path is not null)
                    entries.Add(Create(sourceFile, hive, path, "@", RegistryOperationKind.DeleteKey, "-", supported: false,
                        "Deleting an entire registry key requires manual review."));
                continue;
            }

            if (hive is null || path is null) continue;
            var value = ValueRegex().Match(line);
            if (!value.Success) continue;
            var name = value.Groups[1].Success ? Unescape(value.Groups[1].Value) : "@";
            var raw = value.Groups[2].Value.Trim();
            var operation = GetOperation(raw);
            var supported = operation is RegistryOperationKind.String or RegistryOperationKind.DWord or RegistryOperationKind.Binary or
                RegistryOperationKind.ExpandString or RegistryOperationKind.DeleteValue;
            string? reason = supported ? null : "The registry value format is not supported.";

            if (raw.StartsWith('"') && !HasClosingQuote(raw))
            {
                var continuation = new StringBuilder(raw);
                while (++index < lines.Length)
                {
                    continuation.Append('\n').Append(lines[index]);
                    if (HasClosingQuote(lines[index].Trim())) break;
                }
                raw = continuation.ToString();
                supported = false;
                reason = "Multiline registry strings require manual review.";
            }

            entries.Add(Create(sourceFile, hive, path, name, operation, raw, supported, reason));
        }

        return entries.ToImmutable();
    }

    public static ImmutableArray<TaskDescriptor> CreateTaskDescriptors()
    {
        var groupedIds = Groups.Value.SelectMany(group => group.Members).Select(entry => entry.TaskId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tasks = GetEntries().Where(entry => !groupedIds.Contains(entry.TaskId))
            .Select(entry => new TaskDescriptor(entry.TaskId, entry.DisplayName, entry.Category, entry.Risk,
                TaskKind.RegistrySetting, Version: "3.0.0", RequiresAdministrator: entry.RequiresAdministrator,
                IsAvailable: entry.IsSupported, UnavailableReason: entry.UnsupportedReason,
                ResourceLocks: ["registry"], Rollback: entry.IsSupported ? RollbackSupport.Exact : RollbackSupport.Manual));
        var groups = Groups.Value.Select(group => new TaskDescriptor(group.TaskId, group.DisplayName, group.Category, group.Risk,
            TaskKind.RegistrySetting, Version: "3.0.0", RequiresAdministrator: group.RequiresAdministrator,
            IsAvailable: group.IsSupported, UnavailableReason: group.UnsupportedReason,
            ResourceLocks: ["registry"], Rollback: group.IsSupported ? RollbackSupport.Exact : RollbackSupport.Manual));
        return tasks.Concat(groups).ToImmutableArray();
    }

    private static ImmutableArray<RegistryOptimizationGroup> CreateGroups(ImmutableArray<RegistryOptimizationEntry> entries)
    {
        static bool At(RegistryOptimizationEntry entry, string path) => entry.Path.Equals(path, StringComparison.OrdinalIgnoreCase);
        static bool Under(RegistryOptimizationEntry entry, string path) => entry.Path.StartsWith(path, StringComparison.OrdinalIgnoreCase);
        static bool Named(RegistryOptimizationEntry entry, params string[] names) => names.Contains(entry.ValueName, StringComparer.OrdinalIgnoreCase);

        var groups = ImmutableArray.Create(
            CreateGroup(entries, "registry-group-user-desktop-start", "Desktop, taskbar and Start menu", "Desktop and Explorer",
                entry => entry.Hive == "HKCU" &&
                    (Under(entry, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\HideDesktopIcons") ||
                     At(entry, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced") ||
                     At(entry, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Search") && entry.ValueName == "SearchboxTaskbarMode")),
            CreateGroup(entries, "registry-group-user-file-explorer", "File Explorer behavior", "Desktop and Explorer",
                entry => entry.Hive == "HKCU" &&
                    (At(entry, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer") ||
                     At(entry, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\CabinetState"))),
            CreateGroup(entries, "registry-group-user-screen-saver", "Screen saver configuration", "Desktop and Explorer",
                entry => entry.Hive == "HKCU" && At(entry, "Control Panel\\Desktop") &&
                    Named(entry, "ScreenSaveActive", "SCRNSAVE.EXE", "ScreenSaveTimeOut", "ScreenSaverIsSecure")),
            CreateGroup(entries, "registry-group-user-visual-responsiveness", "Visual effects and desktop responsiveness", "Desktop and Explorer",
                entry => entry.Hive == "HKCU" && At(entry, "Control Panel\\Desktop") &&
                    !Named(entry, "ScreenSaveActive", "SCRNSAVE.EXE", "ScreenSaveTimeOut", "ScreenSaverIsSecure")),
            CreateGroup(entries, "registry-group-user-mouse", "Mouse behavior", "Desktop and Explorer",
                entry => entry.Hive == "HKCU" && At(entry, "Control Panel\\Mouse")),
            CreateGroup(entries, "registry-group-user-focus-assist", "Focus Assist behavior", "Privacy and notifications",
                entry => entry.Hive == "HKCU" && Under(entry, "Software\\Microsoft\\Windows\\CurrentVersion\\FocusAssist")),
            CreateGroup(entries, "registry-group-user-privacy-personalization", "Privacy and personalization", "Privacy and notifications",
                entry => entry.Hive == "HKCU" &&
                    (Under(entry, "Software\\Microsoft\\Windows\\CurrentVersion\\CloudExperienceHost\\Intent\\PersonalDataExport") ||
                     At(entry, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo") ||
                     At(entry, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Privacy") ||
                     At(entry, "Software\\Microsoft\\InputPersonalization"))),
            CreateGroup(entries, "registry-group-user-recommended-content", "Recommended and sponsored content", "Privacy and notifications",
                entry => entry.Hive == "HKCU" && At(entry, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager")),
            CreateGroup(entries, "registry-group-user-search-privacy", "Windows search and cloud history", "Privacy and notifications",
                entry => entry.Hive == "HKCU" &&
                    (At(entry, "Software\\Microsoft\\Windows\\CurrentVersion\\Search") && entry.ValueName != "SearchboxTaskbarMode" ||
                     At(entry, "Software\\Microsoft\\Windows\\CurrentVersion\\SearchSettings"))),
            CreateGroup(entries, "registry-group-user-feedback-onboarding", "Feedback and onboarding prompts", "Privacy and notifications",
                entry => entry.Hive == "HKCU" &&
                    (At(entry, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\UserProfileEngagement") ||
                     At(entry, "Control Panel\\International\\User Profile") ||
                     At(entry, "Software\\Microsoft\\Personalization\\Settings") ||
                     At(entry, "Software\\Policies\\Microsoft\\Windows\\EdgeUI") ||
                     At(entry, "SOFTWARE\\Microsoft\\Siuf\\Rules"))),
            CreateGroup(entries, "registry-group-user-notifications", "Toast and lock-screen notifications", "Privacy and notifications",
                entry => entry.Hive == "HKCU" && At(entry, "Software\\Microsoft\\Windows\\CurrentVersion\\PushNotifications")),
            CreateGroup(entries, "registry-group-user-gaming", "Gaming and Game Bar", "Gaming and multimedia",
                entry => entry.Hive == "HKCU" &&
                    (At(entry, "System\\GameConfigStore") || At(entry, "Software\\Microsoft\\GameBar"))),
            CreateGroup(entries, "registry-group-user-terminal", "Command Prompt and console behavior", "Developer and terminal",
                entry => entry.Hive == "HKCU" &&
                    (At(entry, "SOFTWARE\\Microsoft\\Command Processor") || At(entry, "Console"))),
            CreateGroup(entries, "registry-group-user-developer", "Developer features", "Developer and terminal",
                entry => entry.Hive == "HKCU" && At(entry, "Software\\Microsoft\\Windows\\CurrentVersion\\DeveloperSettings")),

            CreateGroup(entries, "registry-group-system-explorer-policy", "Explorer, taskbar and Start policies", "Desktop and Explorer",
                entry => entry.Hive == "HKLM" && At(entry, "SOFTWARE\\Policies\\Microsoft\\Windows\\Explorer")),
            CreateGroup(entries, "registry-group-system-feed-copilot", "News, feeds and Copilot", "Cloud content and Edge",
                entry => entry.Hive == "HKLM" &&
                    (At(entry, "SOFTWARE\\Policies\\Microsoft\\Dsh") ||
                     At(entry, "SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Feeds") ||
                     At(entry, "SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsCopilot"))),
            CreateGroup(entries, "registry-group-system-cloud-content", "Windows consumer and cloud content", "Cloud content and Edge",
                entry => entry.Hive == "HKLM" && At(entry, "Software\\Policies\\Microsoft\\Windows\\CloudContent")),
            CreateGroup(entries, "registry-group-system-edge", "Microsoft Edge policies", "Cloud content and Edge",
                entry => entry.Hive == "HKLM" && At(entry, "SOFTWARE\\Policies\\Microsoft\\Edge")),
            CreateGroup(entries, "registry-group-system-windows-update", "Windows Update and delivery policies", "Windows Update",
                entry => entry.Hive == "HKLM" &&
                    (At(entry, "SOFTWARE\\Microsoft\\WindowsUpdate\\UX\\Settings") ||
                     At(entry, "SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU") ||
                     At(entry, "SOFTWARE\\Policies\\Microsoft\\MRT") ||
                     At(entry, "SOFTWARE\\Policies\\Microsoft\\WindowsUpdate") ||
                     At(entry, "SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization"))),
            CreateGroup(entries, "registry-group-system-install-bypass", "Windows installation requirement bypass", "Security and setup",
                entry => entry.Hive == "HKLM" &&
                    (At(entry, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\OOBE") ||
                     At(entry, "SYSTEM\\Setup\\LabConfig") || At(entry, "SYSTEM\\Setup\\MoSetup"))),
            CreateGroup(entries, "registry-group-system-power-core", "Hibernate, fast startup and active power plan", "Power and shutdown",
                entry => entry.Hive == "HKLM" &&
                    (At(entry, "SYSTEM\\CurrentControlSet\\Control\\Power") ||
                     At(entry, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power") ||
                     At(entry, "SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes"))),
            CreateGroup(entries, "registry-group-system-power-balanced", "Balanced power plan behavior", "Power and shutdown",
                entry => entry.Hive == "HKLM" && Under(entry, "SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes\\381b4222-f694-41f0-9685-ff5bb260df2e\\")),
            CreateGroup(entries, "registry-group-system-power-performance", "High performance power plan behavior", "Power and shutdown",
                entry => entry.Hive == "HKLM" && Under(entry, "SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes\\8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c\\")),
            CreateGroup(entries, "registry-group-system-power-saver", "Power saver plan behavior", "Power and shutdown",
                entry => entry.Hive == "HKLM" && Under(entry, "SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes\\a1841308-3541-4fab-bc81-f71556f20b4a\\")),
            CreateGroup(entries, "registry-group-system-power-ultimate", "Ultimate performance power plan behavior", "Power and shutdown",
                entry => entry.Hive == "HKLM" && Under(entry, "SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes\\e9a42b02-d5df-448d-aa00-03f14749eb61\\")),
            CreateGroup(entries, "registry-group-system-performance", "System and gaming performance", "Performance and file system",
                entry => entry.Hive == "HKLM" &&
                    (At(entry, "SYSTEM\\CurrentControlSet\\Control") ||
                     At(entry, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management") && Named(entry, "LargeSystemCache", "IoPageLockLimit") ||
                     Under(entry, "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile") ||
                     At(entry, "SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers"))),
            CreateGroup(entries, "registry-group-system-security", "System security and UAC", "Security and setup",
                entry => entry.Hive == "HKLM" &&
                    (At(entry, "SYSTEM\\CurrentControlSet\\Control\\BitLocker") ||
                     At(entry, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System"))),
            CreateGroup(entries, "registry-group-system-file-sharing", "Administrative file shares", "Network and remote access",
                entry => entry.Hive == "HKLM" && At(entry, "SYSTEM\\CurrentControlSet\\Services\\LanmanServer\\Parameters")),
            CreateGroup(entries, "registry-group-system-remote-desktop", "Remote Desktop access", "Network and remote access",
                entry => entry.Hive == "HKLM" &&
                    (Under(entry, "SYSTEM\\CurrentControlSet\\Control\\Terminal Server") ||
                     At(entry, "SYSTEM\\CurrentControlSet\\Services\\SharedAccess\\Parameters\\FirewallPolicy\\FirewallRules") ||
                     At(entry, "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon"))),
            CreateGroup(entries, "registry-group-system-device-guard", "Device Guard and speculative execution settings", "Security and setup",
                entry => entry.Hive == "HKLM" &&
                    (Under(entry, "SYSTEM\\CurrentControlSet\\Control\\DeviceGuard") ||
                     At(entry, "SOFTWARE\\Policies\\Microsoft\\Windows\\DeviceGuard") ||
                     At(entry, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management") &&
                        Named(entry, "FeatureSettingsOverride", "FeatureSettingsOverrideMask", "FeatureSettings"))),
            CreateGroup(entries, "registry-group-system-file-system", "File system and long path settings", "Performance and file system",
                entry => entry.Hive == "HKLM" && At(entry, "SYSTEM\\CurrentControlSet\\Control\\FileSystem")),
            CreateGroup(entries, "registry-group-system-shell", "PowerShell and Command Prompt policies", "Developer and terminal",
                entry => entry.Hive == "HKLM" &&
                    (At(entry, "SOFTWARE\\Microsoft\\PowerShell\\1\\ShellIds\\Microsoft.PowerShell") ||
                     At(entry, "SOFTWARE\\Microsoft\\Command Processor"))),
            CreateGroup(entries, "registry-group-system-telemetry", "Telemetry service and scheduled task", "Privacy and notifications",
                entry => entry.Hive == "HKLM" &&
                    (At(entry, "SYSTEM\\CurrentControlSet\\Services\\UCPD") ||
                     Under(entry, "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Schedule\\TaskCache\\Tree\\Microsoft\\Windows\\Customer Experience Improvement Program"))),
            CreateGroup(entries, "registry-group-hash-context-menus", "File and folder hash context menus", "Context menu tools",
                entry => entry.Hive == "HKLM" &&
                    (entry.Path.StartsWith("SOFTWARE\\Classes\\*\\shell\\hash", StringComparison.OrdinalIgnoreCase) ||
                     entry.Path.StartsWith("SOFTWARE\\Classes\\Directory\\shell\\hashDirectory", StringComparison.OrdinalIgnoreCase))),
            CreateGroup(entries, "registry-group-take-ownership-menus", "Take Ownership context menus", "Context menu tools",
                entry => entry.Hive == "HKLM" &&
                    (entry.Path.StartsWith("SOFTWARE\\Classes\\*\\shell\\TakeOwnership", StringComparison.OrdinalIgnoreCase) ||
                     entry.Path.StartsWith("SOFTWARE\\Classes\\Directory\\shell\\TakeOwnership", StringComparison.OrdinalIgnoreCase) ||
                     entry.Path.StartsWith("SOFTWARE\\Classes\\Drive\\shell\\runas", StringComparison.OrdinalIgnoreCase))));

        var duplicates = groups.SelectMany(group => group.Members)
            .GroupBy(entry => entry.TaskId, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicates is not null) throw new InvalidOperationException($"Registry entry {duplicates.Key} belongs to multiple feature groups.");
        return groups;
    }

    private static RegistryOptimizationGroup CreateGroup(ImmutableArray<RegistryOptimizationEntry> entries, string taskId,
        string displayName, string category, Func<RegistryOptimizationEntry, bool> predicate)
    {
        var members = entries.Where(predicate).ToImmutableArray();
        if (members.IsDefaultOrEmpty) throw new InvalidOperationException($"Registry feature group {taskId} has no members.");
        return new(taskId, displayName, category, members);
    }

    private static ImmutableArray<RegistryOptimizationEntry> LoadEmbedded()
    {
        var assembly = typeof(RegistryOptimizationCatalog).Assembly;
        var result = ImmutableArray.CreateBuilder<RegistryOptimizationEntry>();
        foreach (var sourceFile in RegistrySourceFiles.All)
        {
            var resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith(sourceFile, StringComparison.OrdinalIgnoreCase));
            using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"Missing resource {sourceFile}.");
            using var reader = new StreamReader(stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
            result.AddRange(Parse(sourceFile, reader.ReadToEnd()));
        }
        return result.ToImmutable();
    }

    private static RegistryOptimizationEntry Create(string sourceFile, string hive, string path, string name,
        RegistryOperationKind operation, string raw, bool supported, string? reason)
    {
        var category = Classify(path, name);
        var requiresAdministrator = hive is "HKLM" or "HKCR";
        var risk = ClassifyRisk(hive, path, name, operation);
        var canonical = $"{hive}\\{path}|{name}".ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..10].ToLowerInvariant();
        var leaf = Slug(name == "@" ? path.Split('\\').Last() : name);
        return new($"registry-{hive.ToLowerInvariant()}-{leaf}-{hash}", sourceFile, hive, path, name, operation,
            raw, category, risk, requiresAdministrator, supported, reason);
    }

    private static (string? Hive, string? Path) SplitHive(string fullPath)
    {
        var separator = fullPath.IndexOf('\\');
        var root = separator < 0 ? fullPath : fullPath[..separator];
        var path = separator < 0 ? string.Empty : fullPath[(separator + 1)..];
        var hive = root.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" => "HKCU",
            "HKEY_LOCAL_MACHINE" => "HKLM",
            "HKEY_CLASSES_ROOT" => "HKCR",
            _ => null
        };
        return (hive, hive is null ? null : path);
    }

    private static RegistryOperationKind GetOperation(string raw) => raw switch
    {
        "-" => RegistryOperationKind.DeleteValue,
        _ when raw.StartsWith("dword:", StringComparison.OrdinalIgnoreCase) => RegistryOperationKind.DWord,
        _ when raw.StartsWith("hex(2):", StringComparison.OrdinalIgnoreCase) => RegistryOperationKind.ExpandString,
        _ when raw.StartsWith("hex:", StringComparison.OrdinalIgnoreCase) => RegistryOperationKind.Binary,
        _ when raw.StartsWith('"') => RegistryOperationKind.String,
        _ => RegistryOperationKind.Unsupported
    };

    private static RiskLevel ClassifyRisk(string hive, string path, string name, RegistryOperationKind operation)
    {
        var value = $"{path}\\{name}";
        if (operation == RegistryOperationKind.DeleteKey ||
            ContainsAny(value, "DeviceGuard", "EnableLUA", "ConsentPromptBehavior", "FirewallRules", "TakeOwnership", "LabConfig", "Bypass", "Terminal Server", "WinStations", "Remote Assistance", "UCPD"))
            return RiskLevel.High;
        return hive == "HKCU" ? RiskLevel.Standard : RiskLevel.Elevated;
    }

    private static string Classify(string path, string name)
    {
        var value = $"{path}\\{name}";
        if (ContainsAny(value, "Explorer", "Taskbar", "Start", "HideDesktopIcons")) return "Desktop and Explorer";
        if (ContainsAny(value, "Privacy", "Advertising", "ContentDelivery", "InputPersonalization", "PushNotifications", "Siuf")) return "Privacy and notifications";
        if (ContainsAny(value, "Power", "Hibernate", "Hiberboot")) return "Power and shutdown";
        if (ContainsAny(value, "WindowsUpdate", "DeliveryOptimization", "MRT")) return "Windows Update";
        if (ContainsAny(value, "Edge", "CloudContent", "WindowsCopilot", "Windows Feeds", "Dsh")) return "Cloud content and Edge";
        if (ContainsAny(value, "Game", "Multimedia", "GraphicsDrivers")) return "Gaming and multimedia";
        if (ContainsAny(value, "Terminal Server", "Remote Assistance", "LanmanServer", "Firewall", "Wlan", "Network")) return "Network and remote access";
        if (ContainsAny(value, "DeviceGuard", "Policies\\System", "BitLocker", "LabConfig", "OOBE", "MoSetup")) return "Security and setup";
        if (ContainsAny(value, "Classes", "shell", "TakeOwnership", "hash")) return "Context menu tools";
        if (ContainsAny(value, "Memory Management", "FileSystem", "Control\\Session Manager")) return "Performance and file system";
        if (ContainsAny(value, "Developer", "Command Processor", "Console", "PowerShell")) return "Developer and terminal";
        return "Other registry settings";
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static bool HasClosingQuote(string value)
    {
        if (!value.EndsWith('"')) return false;
        var escapes = 0;
        for (var index = value.Length - 2; index >= 0 && value[index] == '\\'; index--) escapes++;
        return escapes % 2 == 0;
    }

    private static string Unescape(string value) => value.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
    private static string Slug(string value)
    {
        var slug = NonAlphaNumericRegex().Replace(value.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "default" : slug.Length > 36 ? slug[..36].TrimEnd('-') : slug;
    }

    [GeneratedRegex("^\\[(-?)([^]]+)\\]$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionRegex();

    [GeneratedRegex("^(?:\"((?:\\\\.|[^\"])*)\"|@)=(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ValueRegex();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumericRegex();
}
