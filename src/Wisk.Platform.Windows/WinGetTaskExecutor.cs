using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using Wisk.Contracts;
using Wisk.Core;
using Wisk.Execution;
using Wisk.PowerShell;

namespace Wisk.Platform.Windows;

public interface IWinGetProcessRunner
{
    Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class WinGetProcessRunner : IWinGetProcessRunner
{
    public async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var executablePath = Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe");
        if (!File.Exists(executablePath))
            throw new WinGetProcessException("WinGet is not installed for the current user.", ErrorCode.MissingWinGet);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            if (!process.Start()) throw new WinGetProcessException("WinGet could not be started.", ErrorCode.MissingWinGet);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            return (process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw new WinGetProcessException("WinGet timed out or was cancelled.", cancellationToken.IsCancellationRequested ? ErrorCode.Cancelled : ErrorCode.Timeout);
        }
        catch (Win32Exception exception)
        {
            throw new WinGetProcessException($"WinGet is unavailable: {exception.Message}", ErrorCode.MissingWinGet);
        }
    }
}

public sealed class WinGetProcessException(string message, ErrorCode code) : Exception(message)
{
    public ErrorCode Code { get; } = code;
}

public sealed class WinGetTaskExecutor(Catalog catalog, IWinGetProcessRunner runner) : IInitializerTaskExecutor
{
    private const int MaxOutputCharacters = 32 * 1024;
    private const int NoApplicationsFoundExitCode = unchecked((int)0x8A150014);
    private const int CommandRequiresAdminExitCode = unchecked((int)0x8A150019);
    private const int MsStoreBlockedByPolicyExitCode = unchecked((int)0x8A15001B);
    private const int MsStoreAppBlockedByPolicyExitCode = unchecked((int)0x8A15001C);
    private const int InstallCancelledByUserExitCode = unchecked((int)0x8A15010C);

    // These catalog entries use Microsoft Store IDs instead of community-winget IDs.
    // Keep the allow-list explicit so an unknown ID never silently changes source.
    private static readonly HashSet<string> MicrosoftStorePackageIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "9MZ1SNWT0N5D",
        "9PPSP2MKVTGT",
        "9NJXJSCB2JK0",
        "9NF7JTB3B17P",
        "XPDNH1FMW7NB40"
    };

    public async Task<TaskCheckResult> CheckAsync(PlannedTask task, CancellationToken cancellationToken)
    {
        var descriptor = catalog.Find(task.TaskId);
        if (descriptor?.Kind != TaskKind.Winget || string.IsNullOrWhiteSpace(descriptor.PackageId))
            return new TaskCheckResult(task.TaskId, TaskState.NeedsManualReview, ErrorCode.ManualReviewRequired, "No fixed WinGet adapter is registered for this task.", CanApply: false);
        if (!TryResolveAction(task.ParameterSummary, out var action))
            return InvalidAction(task);

        var source = SourceFor(descriptor.PackageId);
        var packageScope = task.PackageScope ?? descriptor.PackageScope;
        try
        {
            var installed = await runner.RunAsync(
                WithWinGetOptions(["list", "--id", descriptor.PackageId, "--exact", "--source", source, "--accept-source-agreements"], packageScope, task.Proxy),
                TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            var installedOutput = Truncate(installed.Stdout);
            if (installed.ExitCode != 0 && installed.ExitCode != NoApplicationsFoundExitCode)
                return CheckFailure(task, installed, "WinGet could not determine whether the package is installed.");

            var isInstalled = installed.ExitCode == 0 && ContainsExactPackageId(installedOutput, descriptor.PackageId);
            if (!isInstalled)
            {
                return action == WinGetAction.Upgrade
                    ? new TaskCheckResult(task.TaskId, TaskState.NeedsManualReview, ErrorCode.ManualReviewRequired,
                        "The package is not installed and cannot be upgraded.", CanApply: false)
                    : new TaskCheckResult(task.TaskId, TaskState.Ready, ErrorCode.None, "The package is ready for installation.");
            }

            if (action == WinGetAction.Install)
                return new TaskCheckResult(task.TaskId, TaskState.Skipped, ErrorCode.None, "The package is already installed.", AlreadyComplete: true, CanApply: true);

            var upgrade = await runner.RunAsync(
                WithWinGetOptions(["list", "--id", descriptor.PackageId, "--exact", "--upgrade-available", "--source", source, "--accept-source-agreements"], packageScope, task.Proxy),
                TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            var upgradeOutput = Truncate(upgrade.Stdout);
            if (upgrade.ExitCode == NoApplicationsFoundExitCode || (upgrade.ExitCode == 0 && !ContainsExactPackageId(upgradeOutput, descriptor.PackageId)))
                return new TaskCheckResult(task.TaskId, TaskState.Skipped, ErrorCode.None, "No eligible update is available.", AlreadyComplete: true, CanApply: true);
            if (upgrade.ExitCode != 0)
                return CheckFailure(task, upgrade, "WinGet could not determine whether an update is available.");

            return new TaskCheckResult(task.TaskId, TaskState.Ready, ErrorCode.None, "An update is available for this package.");
        }
        catch (WinGetProcessException exception)
        {
            return new TaskCheckResult(task.TaskId, exception.Code == ErrorCode.Cancelled ? TaskState.Cancelled : TaskState.UnsupportedPrerequisite,
                exception.Code, SensitiveDataRedactor.Redact(exception.Message), CanApply: false, Retryable: exception.Code == ErrorCode.Timeout);
        }
    }

    public async Task<ExecutionResult> ApplyAsync(PlannedTask task, ApplyContext context)
    {
        var descriptor = catalog.Find(task.TaskId);
        if (descriptor?.Kind != TaskKind.Winget || string.IsNullOrWhiteSpace(descriptor.PackageId))
            return new ExecutionResult(task.TaskId, TaskState.NeedsManualReview, ErrorCode.ManualReviewRequired.ToString(), "No fixed WinGet adapter is registered for this task.", false, task.RequiresReboot);
        if (!TryResolveAction(task.ParameterSummary, out var action))
            return InvalidActionResult(task);

        var source = SourceFor(descriptor.PackageId);
        var packageScope = task.PackageScope ?? descriptor.PackageScope;
        try
        {
            var command = action == WinGetAction.Upgrade ? "upgrade" : "install";
            var proxy = task.Proxy ?? context.Plan.Tasks.FirstOrDefault(item => item.TaskId.Equals(task.TaskId, StringComparison.OrdinalIgnoreCase))?.Proxy;
            var result = await runner.RunAsync(WithWinGetOptions([command, "--id", descriptor.PackageId, "--exact", "--source", source, "--accept-source-agreements", "--accept-package-agreements", "--silent", "--disable-interactivity"], packageScope, proxy), context.Timeout, context.CancellationToken).ConfigureAwait(false);
            if (result.ExitCode == 0)
                return new ExecutionResult(task.TaskId, TaskState.Succeeded, ErrorCode.None.ToString(), $"WinGet {command} completed.", true, task.RequiresReboot);
            var code = MapProcessError(result.ExitCode);
            return new ExecutionResult(task.TaskId, TaskState.Failed, code.ToString(), $"WinGet {command} failed.", false,
                task.RequiresReboot, Retryable: code == ErrorCode.ProcessFailed, FailureStage: "Apply", ProcessExitCode: result.ExitCode);
        }
        catch (WinGetProcessException exception)
        {
            return new ExecutionResult(task.TaskId, exception.Code == ErrorCode.Cancelled ? TaskState.Cancelled : TaskState.Failed,
                exception.Code.ToString(), SensitiveDataRedactor.Redact(exception.Message), false, task.RequiresReboot, Retryable: exception.Code == ErrorCode.Timeout);
        }
    }

    public async Task<VerifyResult> VerifyAsync(PlannedTask task, CancellationToken cancellationToken)
    {
        var check = await CheckAsync(task, cancellationToken).ConfigureAwait(false);
        return new VerifyResult(task.TaskId, check.AlreadyComplete, check.Code, check.Message, check.RequiresReboot);
    }

    private static string Truncate(string value) => value.Length <= MaxOutputCharacters ? value : value[..MaxOutputCharacters];

    private static IReadOnlyList<string> WithWinGetOptions(IReadOnlyList<string> arguments, string? packageScope, string? proxy)
    {
        var result = string.IsNullOrWhiteSpace(packageScope) ? arguments : [.. arguments, "--scope", packageScope];
        return string.IsNullOrWhiteSpace(proxy) ? result : [.. result, "--proxy", proxy];
    }

    private static bool TryResolveAction(string? summary, out WinGetAction action)
    {
        var normalized = summary?.Trim();
        if (string.IsNullOrEmpty(normalized) || string.Equals(normalized, "install", StringComparison.OrdinalIgnoreCase))
        {
            action = WinGetAction.Install;
            return true;
        }
        if (string.Equals(normalized, "upgrade", StringComparison.OrdinalIgnoreCase))
        {
            action = WinGetAction.Upgrade;
            return true;
        }
        action = default;
        return false;
    }

    private static string SourceFor(string packageId) => MicrosoftStorePackageIds.Contains(packageId) ? "msstore" : "winget";

    private static bool ContainsExactPackageId(string output, string packageId) =>
        output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Any(token => token.Equals(packageId, StringComparison.OrdinalIgnoreCase)));

    private static TaskCheckResult InvalidAction(PlannedTask task) =>
        new(task.TaskId, TaskState.NeedsManualReview, ErrorCode.ManualReviewRequired,
            "The WinGet action is invalid. Use an empty value, install, or upgrade.", CanApply: false);

    private static ExecutionResult InvalidActionResult(PlannedTask task) =>
        new(task.TaskId, TaskState.NeedsManualReview, ErrorCode.ManualReviewRequired.ToString(),
            "The WinGet action is invalid. Use an empty value, install, or upgrade.", false, task.RequiresReboot,
            FailureStage: "Apply");

    private static TaskCheckResult CheckFailure(PlannedTask task, (int ExitCode, string Stdout, string Stderr) result, string message)
    {
        var detail = string.IsNullOrWhiteSpace(result.Stderr) ? string.Empty : $" {SensitiveDataRedactor.Redact(Truncate(result.Stderr))}";
        return new TaskCheckResult(task.TaskId, TaskState.Failed, ErrorCode.ProcessFailed,
            $"{message} Exit code {result.ExitCode}.{detail}", CanApply: false);
    }

    private static ErrorCode MapProcessError(int exitCode) => exitCode switch
    {
        5 or CommandRequiresAdminExitCode => ErrorCode.AccessDenied,
        MsStoreBlockedByPolicyExitCode or MsStoreAppBlockedByPolicyExitCode => ErrorCode.PolicyBlocked,
        InstallCancelledByUserExitCode => ErrorCode.Cancelled,
        _ => ErrorCode.ProcessFailed
    };

    private enum WinGetAction { Install, Upgrade }
}

public sealed class RegisteredTaskExecutor(
    Catalog catalog,
    IBridgeInvoker bridge,
    IWinGetProcessRunner? winGetRunner = null,
    CompatibilitySnapshot? compatibility = null,
    IInitializerTaskExecutor? controlledExtensionExecutor = null) : IInitializerTaskExecutor, IPlanPreparationExecutor
{
    private readonly WinGetTaskExecutor _winGet = new(catalog, winGetRunner ?? new WinGetProcessRunner());
    private readonly WindowsTaskExecutor _bridge = new(bridge, catalog);
    private readonly RegistryTaskExecutor _registry = new();
    private readonly IInitializerTaskExecutor _extension = controlledExtensionExecutor ?? new ControlledExtensionTaskExecutor();

    public Task PrepareAsync(ImmutablePlan plan, string runId, CancellationToken cancellationToken) =>
        _registry.PreparePlanAsync(plan, runId, cancellationToken);

    public Task<TaskCheckResult> CheckAsync(PlannedTask task, CancellationToken cancellationToken)
    {
        if (task.Source == TaskSource.ControlledExtension) return _extension.CheckAsync(task, cancellationToken);
        if (catalog.Find(task.TaskId) is { } descriptor && compatibility is not null)
        {
            if (descriptor.RequiresInternet && !compatibility.NetworkAvailable)
                return Unsupported(task, ErrorCode.NetworkUnavailable, "A network connection is required for this task.");
            if (descriptor.Kind == TaskKind.Winget && !compatibility.WinGetAvailable)
                return Unsupported(task, ErrorCode.MissingWinGet, "WinGet is not available for the current user.");
            if (descriptor.Kind != TaskKind.Winget && !compatibility.PowerShellAvailable)
                return Unsupported(task, ErrorCode.MissingPowerShell, "Windows PowerShell 5.1 is not available.");
            if (descriptor.Kind is TaskKind.LanguagePack or TaskKind.Capability && !compatibility.WindowsUpdateAvailable)
                return Unsupported(task, ErrorCode.PolicyBlocked, "Windows Update or Features on Demand is unavailable.");
        }
        return IsRegistry(task) ? _registry.CheckAsync(task, cancellationToken) : IsWinGet(task) ? _winGet.CheckAsync(task, cancellationToken) : _bridge.CheckAsync(task, cancellationToken);
    }
    public Task<ExecutionResult> ApplyAsync(PlannedTask task, ApplyContext context) =>
        task.Source == TaskSource.ControlledExtension ? _extension.ApplyAsync(task, context) :
        IsRegistry(task) ? _registry.ApplyAsync(task, context) : IsWinGet(task) ? _winGet.ApplyAsync(task, context) : _bridge.ApplyAsync(task, context);
    public Task<VerifyResult> VerifyAsync(PlannedTask task, CancellationToken cancellationToken) =>
        task.Source == TaskSource.ControlledExtension ? _extension.VerifyAsync(task, cancellationToken) :
        IsRegistry(task) ? _registry.VerifyAsync(task, cancellationToken) : IsWinGet(task) ? _winGet.VerifyAsync(task, cancellationToken) : _bridge.VerifyAsync(task, cancellationToken);
    private bool IsWinGet(PlannedTask task) => catalog.Find(task.TaskId) is { Kind: TaskKind.Winget, PackageId: not null };
    private bool IsRegistry(PlannedTask task) => catalog.Find(task.TaskId) is { Kind: TaskKind.RegistrySetting };
    private static Task<TaskCheckResult> Unsupported(PlannedTask task, ErrorCode code, string message) =>
        Task.FromResult(new TaskCheckResult(task.TaskId, TaskState.UnsupportedPrerequisite, code, message, CanApply: false));
}
