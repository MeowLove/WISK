using System.Collections.Immutable;
using System.Text.RegularExpressions;
using WindowsInitializer.Contracts;
using WindowsInitializer.Core;

namespace WindowsInitializer.Platform.Windows;

public enum SoftwareInventoryStatus { NotInstalled, Installed, UpgradeAvailable, DetectionFailed }

public sealed record SoftwareInventoryItem(string TaskId, string PackageId, SoftwareInventoryStatus Status);

public sealed record SoftwareInventorySnapshot(
    ImmutableDictionary<string, SoftwareInventoryItem> Items,
    DateTimeOffset CheckedAt,
    string? Error = null);

public sealed class SoftwareInventoryService(Catalog catalog, IWinGetProcessRunner? runner = null)
{
    private const int NoApplicationsFoundExitCode = unchecked((int)0x8A150014);
    private readonly IWinGetProcessRunner _runner = runner ?? new WinGetProcessRunner();

    public async Task<SoftwareInventorySnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        var software = catalog.GetTasks().Where(task => task.Kind == TaskKind.Winget && !string.IsNullOrWhiteSpace(task.PackageId)).ToArray();
        try
        {
            var installed = await _runner.RunAsync(
                ["list", "--accept-source-agreements", "--disable-interactivity"], TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            var upgrades = await _runner.RunAsync(
                ["list", "--upgrade-available", "--accept-source-agreements", "--disable-interactivity"], TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            if (installed.ExitCode != 0)
                throw new WinGetProcessException("WinGet inventory returned a non-zero exit code.", ErrorCode.ProcessFailed);
            if (upgrades.ExitCode != 0 && upgrades.ExitCode != NoApplicationsFoundExitCode)
                throw new WinGetProcessException("WinGet update detection failed; installed packages cannot be classified as up to date.", ErrorCode.ProcessFailed);

            var items = software.ToImmutableDictionary(task => task.Id, task =>
            {
                var packageId = task.PackageId!;
                var isInstalled = ContainsPackageId(installed.Stdout, packageId);
                var canUpgrade = upgrades.ExitCode == 0 && ContainsPackageId(upgrades.Stdout, packageId);
                return new SoftwareInventoryItem(task.Id, packageId,
                    canUpgrade ? SoftwareInventoryStatus.UpgradeAvailable :
                    isInstalled ? SoftwareInventoryStatus.Installed : SoftwareInventoryStatus.NotInstalled);
            }, StringComparer.OrdinalIgnoreCase);
            return new SoftwareInventorySnapshot(items, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is WinGetProcessException or IOException or UnauthorizedAccessException)
        {
            var items = software.ToImmutableDictionary(task => task.Id, task =>
                new SoftwareInventoryItem(task.Id, task.PackageId!, SoftwareInventoryStatus.DetectionFailed),
                StringComparer.OrdinalIgnoreCase);
            return new SoftwareInventorySnapshot(items, DateTimeOffset.UtcNow,
                WindowsInitializer.Execution.SensitiveDataRedactor.Redact(exception.Message));
        }
    }

    private static bool ContainsPackageId(string output, string packageId) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(line => Regex.Split(line.Trim(), @"\s+"))
            .Any(token => token.Equals(packageId, StringComparison.OrdinalIgnoreCase));
}
