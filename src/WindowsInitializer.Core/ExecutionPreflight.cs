using System.Collections.Immutable;
using WindowsInitializer.Contracts;

namespace WindowsInitializer.Core;

public enum PreflightStatus { Passed, Warning, Blocked }

public sealed record ExecutionEnvironmentSnapshot(
    long SystemDriveFreeBytes,
    bool PendingReboot,
    bool SystemProtectionManagementAvailable);

public sealed record ExecutionPreflightCheck(
    string Id,
    PreflightStatus Status,
    ErrorCode Code,
    string MessageKey);

public sealed record ExecutionPreflightReport(ImmutableArray<ExecutionPreflightCheck> Checks)
{
    public bool CanExecute => Checks.All(check => check.Status != PreflightStatus.Blocked);
    public int BlockingCount => Checks.Count(check => check.Status == PreflightStatus.Blocked);
    public int WarningCount => Checks.Count(check => check.Status == PreflightStatus.Warning);
}

public static class ExecutionPreflightEvaluator
{
    public const long MinimumSoftwareFreeBytes = 2L * 1024 * 1024 * 1024;

    public static ExecutionPreflightReport Evaluate(
        ImmutablePlan plan,
        IEnumerable<TaskDescriptor> descriptors,
        CompatibilitySnapshot compatibility,
        ExecutionEnvironmentSnapshot environment)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(descriptors);
        var selected = descriptors.ToArray();
        var checks = ImmutableArray.CreateBuilder<ExecutionPreflightCheck>();
        Add(checks, "operatingSystem", compatibility.IsApplySupported, true, ErrorCode.UnsupportedOperatingSystem, "preflightOperatingSystem");

        var needsAdministrator = selected.Any(task => task.RequiresAdministrator || task.Risk != RiskLevel.Standard);
        Add(checks, "administrator", !needsAdministrator || compatibility.IsAdministrator, true, ErrorCode.NotAdministrator, "preflightAdministrator");

        var needsPowerShell = selected.Any(task => task.Kind != TaskKind.Winget);
        Add(checks, "powerShell", !needsPowerShell || compatibility.PowerShellAvailable, true, ErrorCode.MissingPowerShell, "preflightPowerShell");

        var needsWinGet = selected.Any(task => task.Kind == TaskKind.Winget);
        Add(checks, "winGet", !needsWinGet || compatibility.WinGetAvailable, true, ErrorCode.MissingWinGet, "preflightWinGet");

        var needsNetwork = selected.Any(task => task.RequiresInternet);
        Add(checks, "network", !needsNetwork || compatibility.NetworkAvailable, true, ErrorCode.NetworkUnavailable, "preflightNetwork");

        var needsWindowsUpdate = selected.Any(task => task.Kind is TaskKind.LanguagePack or TaskKind.Capability ||
                                                       task.Id.Equals("setting-windows-update-mode", StringComparison.OrdinalIgnoreCase));
        Add(checks, "windowsUpdate", !needsWindowsUpdate || compatibility.WindowsUpdateAvailable, true, ErrorCode.PolicyBlocked, "preflightWindowsUpdate");

        var needsSoftwareSpace = selected.Any(task => task.Kind is TaskKind.Winget or TaskKind.OfflineAsset);
        Add(checks, "diskSpace", !needsSoftwareSpace || environment.SystemDriveFreeBytes >= MinimumSoftwareFreeBytes,
            true, ErrorCode.ProcessFailed, "preflightDiskSpace");

        checks.Add(new ExecutionPreflightCheck("pendingReboot",
            environment.PendingReboot ? PreflightStatus.Warning : PreflightStatus.Passed,
            ErrorCode.None, "preflightPendingReboot"));

        var includesRestorePoint = selected.Any(task => task.Id.Equals("safety-restore-point", StringComparison.OrdinalIgnoreCase));
        checks.Add(new ExecutionPreflightCheck("systemProtection",
            includesRestorePoint && !environment.SystemProtectionManagementAvailable ? PreflightStatus.Blocked : PreflightStatus.Passed,
            includesRestorePoint && !environment.SystemProtectionManagementAvailable ? ErrorCode.PolicyBlocked : ErrorCode.None,
            "preflightSystemProtection"));

        return new ExecutionPreflightReport(checks.ToImmutable());
    }

    private static void Add(ImmutableArray<ExecutionPreflightCheck>.Builder checks, string id, bool passed, bool blocking,
        ErrorCode code, string messageKey) =>
        checks.Add(new ExecutionPreflightCheck(id, passed ? PreflightStatus.Passed :
            blocking ? PreflightStatus.Blocked : PreflightStatus.Warning, passed ? ErrorCode.None : code, messageKey));
}
