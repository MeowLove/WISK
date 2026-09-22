using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Wisk.Contracts;
using Wisk.Core;
using Wisk.Execution;

namespace Wisk.Platform.Windows;

public sealed record RegistryStoredValue(bool Exists, RegistryValueKind Kind, object? Value);
public sealed record RegistryBackupRecord(string TaskId, string Hive, string Path, string ValueName, string RestoreExpression);
public sealed record RegistryBackupRunSummary(string RunId, DateTimeOffset UpdatedAt, int RecordCount);

public interface IRegistryValueStore
{
    RegistryStoredValue Read(RegistryOptimizationEntry entry);
    void Write(RegistryOptimizationEntry entry, RegistryValueKind kind, object value);
    void DeleteValue(RegistryOptimizationEntry entry);
}

public static class SystemRegistryBackupCatalog
{
    private static readonly ImmutableDictionary<string, RegistryOptimizationEntry> Entries =
        ConfigurableRegistrySettingCatalog.All
            .Select(Entry)
            .Concat([Entry("setting-windows-update-mode", RegistrySourceFiles.System, "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions", "dword:00000000", "Windows Update", RiskLevel.Elevated, true)])
            .ToImmutableDictionary(entry => entry.TaskId, StringComparer.OrdinalIgnoreCase);

    public static ImmutableArray<RegistryOptimizationEntry> GetEntries(string taskId) =>
        Entries.TryGetValue(taskId, out var entry) ? [entry] : [];

    private static RegistryOptimizationEntry Entry(ConfigurableRegistrySettingDefinition setting) =>
        new(setting.TaskId, setting.SourceFile, setting.Hive, setting.Path, setting.ValueName, RegistryOperationKind.DWord,
            setting.SourceValue, setting.Category, setting.Risk, setting.RequiresAdministrator, true, null);

    private static RegistryOptimizationEntry Entry(string taskId, string sourceFile, string hive, string path, string valueName,
        string sourceValue, string category, RiskLevel risk, bool requiresAdministrator) =>
        new(taskId, sourceFile, hive, path, valueName, RegistryOperationKind.DWord,
            sourceValue, category, risk, requiresAdministrator, true, null);
}

public sealed class WindowsRegistryValueStore : IRegistryValueStore
{
    public RegistryStoredValue Read(RegistryOptimizationEntry entry)
    {
        using var root = OpenRoot(entry.Hive);
        using var key = root.OpenSubKey(entry.Path, writable: false);
        if (key is null) return new(false, RegistryValueKind.None, null);
        var name = entry.ValueName == "@" ? string.Empty : entry.ValueName;
        var names = key.GetValueNames();
        if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) return new(false, RegistryValueKind.None, null);
        return new(true, key.GetValueKind(name), key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
    }

    public void Write(RegistryOptimizationEntry entry, RegistryValueKind kind, object value)
    {
        using var root = OpenRoot(entry.Hive);
        using var key = root.CreateSubKey(entry.Path, writable: true) ?? throw new InvalidOperationException("Registry key could not be created.");
        key.SetValue(entry.ValueName == "@" ? string.Empty : entry.ValueName, value, kind);
    }

    public void DeleteValue(RegistryOptimizationEntry entry)
    {
        using var root = OpenRoot(entry.Hive);
        using var key = root.OpenSubKey(entry.Path, writable: true);
        key?.DeleteValue(entry.ValueName == "@" ? string.Empty : entry.ValueName, throwOnMissingValue: false);
    }

    private static RegistryKey OpenRoot(string hive) => hive switch
    {
        "HKCU" => Registry.CurrentUser,
        "HKLM" => Registry.LocalMachine,
        "HKCR" => Registry.ClassesRoot,
        _ => throw new InvalidOperationException("Unsupported registry hive.")
    };
}

public sealed class RegistryTaskExecutor(IRegistryValueStore? store = null) : IInitializerTaskExecutor
{
    private readonly IRegistryValueStore _store = store ?? new WindowsRegistryValueStore();

    public async Task PreparePlanAsync(ImmutablePlan plan, string runId, CancellationToken cancellationToken)
    {
        var entries = plan.Tasks.SelectMany(task => RegistryOptimizationCatalog.GetTaskEntries(task.TaskId)
                .Concat(SystemRegistryBackupCatalog.GetEntries(task.TaskId)))
            .Where(entry => entry.IsSupported)
            .DistinctBy(entry => entry.TaskId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (entries.Length == 0) return;

        var originals = entries.Select(entry => (Entry: entry, Original: _store.Read(entry))).ToArray();
        foreach (var item in originals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SaveBackupAsync(runId, item.Entry, item.Original, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<TaskCheckResult> CheckAsync(PlannedTask task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = RegistryOptimizationCatalog.GetTaskEntries(task.TaskId);
        if (entries.IsDefaultOrEmpty || entries.Any(entry => !entry.IsSupported))
            return Task.FromResult(new TaskCheckResult(task.TaskId, TaskState.NeedsManualReview, ErrorCode.ManualReviewRequired,
                entries.FirstOrDefault(entry => !entry.IsSupported)?.UnsupportedReason ?? "Registry catalog entry was not found.", CanApply: false));
        try
        {
            var complete = entries.All(entry => Matches(entry, _store.Read(entry)));
            return Task.FromResult(new TaskCheckResult(task.TaskId, complete ? TaskState.Skipped : TaskState.Ready, ErrorCode.None,
                complete ? "All registry values are already configured." : $"{entries.Length} registry value(s) are ready to be applied.",
                AlreadyComplete: complete, CanApply: true));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return Task.FromResult(new TaskCheckResult(task.TaskId, TaskState.Failed, ErrorCode.AccessDenied,
                SensitiveDataRedactor.Redact(exception.Message), CanApply: false));
        }
    }

    public async Task<ExecutionResult> ApplyAsync(PlannedTask task, ApplyContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var entries = RegistryOptimizationCatalog.GetTaskEntries(task.TaskId);
        if (entries.IsDefaultOrEmpty || entries.Any(entry => !entry.IsSupported))
            return new(task.TaskId, TaskState.NeedsManualReview, ErrorCode.ManualReviewRequired.ToString(),
                entries.FirstOrDefault(entry => !entry.IsSupported)?.UnsupportedReason ?? "Registry catalog entry was not found.", false, task.RequiresReboot);
        try
        {
            var originals = entries.Select(entry => (Entry: entry, Original: _store.Read(entry))).ToArray();
            foreach (var item in originals)
                await SaveBackupAsync(context.RunId, item.Entry, item.Original, context.CancellationToken).ConfigureAwait(false);

            var applied = new List<(RegistryOptimizationEntry Entry, RegistryStoredValue Original)>();
            try
            {
                foreach (var item in originals)
                {
                    applied.Add(item);
                    ApplyEntry(item.Entry);
                }
            }
            catch (Exception applyException)
            {
                Exception? restoreException = null;
                foreach (var item in applied.AsEnumerable().Reverse())
                {
                    try { RestoreEntry(item.Entry, item.Original); }
                    catch (Exception exception) { restoreException ??= exception; }
                }
                if (restoreException is not null)
                    throw new InvalidOperationException("Registry feature apply and compensation both failed.",
                        new AggregateException(applyException, restoreException));
                throw;
            }
            return new(task.TaskId, TaskState.Succeeded, ErrorCode.None.ToString(),
                $"{entries.Length} registry value(s) applied; original states captured.", true, task.RequiresReboot);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or InvalidOperationException or FormatException)
        {
            return new(task.TaskId, TaskState.Failed, ErrorCode.ProcessFailed.ToString(), SensitiveDataRedactor.Redact(exception.Message), false, task.RequiresReboot);
        }
    }

    public Task<VerifyResult> VerifyAsync(PlannedTask task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = RegistryOptimizationCatalog.GetTaskEntries(task.TaskId);
        if (entries.IsDefaultOrEmpty || entries.Any(entry => !entry.IsSupported))
            return Task.FromResult(new VerifyResult(task.TaskId, false, ErrorCode.ManualReviewRequired,
                entries.FirstOrDefault(entry => !entry.IsSupported)?.UnsupportedReason ?? "Registry catalog entry was not found."));
        try
        {
            var succeeded = entries.All(entry => Matches(entry, _store.Read(entry)));
            return Task.FromResult(new VerifyResult(task.TaskId, succeeded, succeeded ? ErrorCode.None : ErrorCode.VerificationFailed,
                succeeded ? $"{entries.Length} registry value(s) verified." : "One or more registry values do not match the requested state."));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return Task.FromResult(new VerifyResult(task.TaskId, false, ErrorCode.VerificationFailed, SensitiveDataRedactor.Redact(exception.Message)));
        }
    }

    private void ApplyEntry(RegistryOptimizationEntry entry)
    {
        if (entry.Operation == RegistryOperationKind.DeleteValue)
            _store.DeleteValue(entry);
        else
        {
            var (kind, value) = Decode(entry);
            _store.Write(entry, kind, value);
        }
    }

    private void RestoreEntry(RegistryOptimizationEntry entry, RegistryStoredValue original)
    {
        if (original.Exists)
            _store.Write(entry, original.Kind, original.Value ?? throw new InvalidOperationException("The original registry value is missing."));
        else
            _store.DeleteValue(entry);
    }

    private static bool Matches(RegistryOptimizationEntry entry, RegistryStoredValue current)
    {
        if (entry.Operation == RegistryOperationKind.DeleteValue) return !current.Exists;
        if (!current.Exists) return false;
        var (kind, desired) = Decode(entry);
        if (current.Kind != kind) return false;
        return desired is byte[] desiredBytes && current.Value is byte[] currentBytes
            ? desiredBytes.SequenceEqual(currentBytes)
            : Equals(desired, current.Value);
    }

    internal static (RegistryValueKind Kind, object Value) Decode(RegistryOptimizationEntry entry) => entry.Operation switch
    {
        RegistryOperationKind.String => (RegistryValueKind.String, DecodeString(entry.RawValue)),
        RegistryOperationKind.DWord => (RegistryValueKind.DWord, unchecked((int)uint.Parse(entry.RawValue[6..], NumberStyles.HexNumber, CultureInfo.InvariantCulture))),
        RegistryOperationKind.Binary => (RegistryValueKind.Binary, DecodeBytes(entry.RawValue[4..])),
        RegistryOperationKind.ExpandString => (RegistryValueKind.ExpandString, Encoding.Unicode.GetString(DecodeBytes(entry.RawValue[7..])).TrimEnd('\0')),
        _ => throw new InvalidOperationException("Registry operation cannot be written as a value.")
    };

    private static string DecodeString(string raw) => raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"'
        ? raw[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal)
        : throw new FormatException("Registry string is malformed.");

    private static byte[] DecodeBytes(string raw) => raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => byte.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();

    private static async Task SaveBackupAsync(string runId, RegistryOptimizationEntry entry, RegistryStoredValue original, CancellationToken cancellationToken)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WISK", "state", "registry-backups", runId);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, entry.TaskId + ".json");
        if (File.Exists(path)) return;
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        var backup = new RegistryBackupRecord(entry.TaskId, entry.Hive, entry.Path, entry.ValueName, EncodeRestoreExpression(original));
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(backup), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(path)) File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string EncodeRestoreExpression(RegistryStoredValue original)
    {
        if (!original.Exists) return "-";
        return original.Kind switch
        {
            RegistryValueKind.String => $"\"{EscapeRegistryString(Convert.ToString(original.Value, CultureInfo.InvariantCulture) ?? string.Empty)}\"",
            RegistryValueKind.ExpandString => "hex(2):" + EncodeBytes(Encoding.Unicode.GetBytes((Convert.ToString(original.Value, CultureInfo.InvariantCulture) ?? string.Empty) + "\0")),
            RegistryValueKind.Binary => "hex:" + EncodeBytes((byte[]?)original.Value ?? []),
            RegistryValueKind.DWord => "dword:" + Convert.ToUInt32(original.Value, CultureInfo.InvariantCulture).ToString("x8", CultureInfo.InvariantCulture),
            RegistryValueKind.QWord => "hex(b):" + EncodeBytes(BitConverter.GetBytes(Convert.ToUInt64(original.Value, CultureInfo.InvariantCulture))),
            RegistryValueKind.MultiString => "hex(7):" + EncodeBytes(Encoding.Unicode.GetBytes(string.Join('\0', (string[]?)original.Value ?? []) + "\0\0")),
            _ => throw new InvalidOperationException("The original registry value type cannot be exported safely.")
        };
    }

    private static string EscapeRegistryString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    private static string EncodeBytes(IEnumerable<byte> bytes) => string.Join(',', bytes.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
}

public static class RegistryBackupExporter
{
    public static async Task<int> ExportLatestAsync(string stateRoot, string destinationPath, CancellationToken cancellationToken = default)
    {
        var backupRoot = Path.Combine(stateRoot, "registry-backups");
        var runDirectory = Directory.Exists(backupRoot)
            ? Directory.EnumerateDirectories(backupRoot).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (runDirectory is null) throw new InvalidOperationException("No registry backup is available.");

        return await ExportDirectoryAsync(runDirectory, destinationPath, cancellationToken).ConfigureAwait(false);
    }

    public static Task<int> ExportRunAsync(string stateRoot, string runId, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId) || runId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || runId is "." or "..")
            throw new ArgumentException("RunId is not a safe directory name.", nameof(runId));
        var runDirectory = Path.Combine(stateRoot, "registry-backups", runId);
        if (!Directory.Exists(runDirectory)) throw new InvalidOperationException("No registry backup is available for the selected run.");
        return ExportDirectoryAsync(runDirectory, destinationPath, cancellationToken);
    }

    private static async Task<int> ExportDirectoryAsync(string runDirectory, string destinationPath, CancellationToken cancellationToken)
    {
        var records = new List<RegistryBackupRecord>();
        foreach (var path in Directory.EnumerateFiles(runDirectory, "*.json", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            records.Add(JsonSerializer.Deserialize<RegistryBackupRecord>(json) ?? throw new InvalidOperationException("A registry backup record is invalid."));
        }
        if (records.Count == 0) throw new InvalidOperationException("No registry backup is available.");

        var text = new StringBuilder("Windows Registry Editor Version 5.00\r\n");
        foreach (var group in records.GroupBy(record => (record.Hive, record.Path)))
        {
            text.Append("\r\n[").Append(FullHiveName(group.Key.Hive)).Append('\\').Append(group.Key.Path).Append("]\r\n");
            foreach (var record in group)
            {
                var name = record.ValueName == "@" ? "@" : $"\"{EscapeName(record.ValueName)}\"";
                text.Append(name).Append('=').Append(record.RestoreExpression).Append("\r\n");
            }
        }
        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination) ?? throw new IOException("The rollback destination directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, text.ToString(), Encoding.Unicode, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return records.Count;
    }

    private static string FullHiveName(string hive) => hive switch
    {
        "HKCU" => "HKEY_CURRENT_USER",
        "HKLM" => "HKEY_LOCAL_MACHINE",
        "HKCR" => "HKEY_CLASSES_ROOT",
        _ => throw new InvalidOperationException("A registry backup record has an unsupported hive.")
    };

    private static string EscapeName(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

public static class RegistryBackupCatalogReader
{
    public static async Task<IReadOnlyList<RegistryBackupRunSummary>> ListRunsAsync(
        string stateRoot, CancellationToken cancellationToken = default)
    {
        var backupRoot = Path.Combine(stateRoot, "registry-backups");
        if (!Directory.Exists(backupRoot)) return [];
        var runs = new List<RegistryBackupRunSummary>();
        foreach (var directory in Directory.EnumerateDirectories(backupRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runId = Path.GetFileName(directory);
            if (!IsSafeRunId(runId)) continue;
            var records = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Count();
            runs.Add(new RegistryBackupRunSummary(runId, Directory.GetLastWriteTimeUtc(directory), records));
        }
        await Task.CompletedTask;
        return runs.OrderByDescending(run => run.UpdatedAt).ToArray();
    }

    public static async Task<IReadOnlyList<RegistryBackupRecord>> LoadRunAsync(
        string stateRoot, string runId, CancellationToken cancellationToken = default)
    {
        if (!IsSafeRunId(runId)) throw new ArgumentException("RunId is not a safe directory name.", nameof(runId));
        var directory = Path.Combine(stateRoot, "registry-backups", runId);
        if (!Directory.Exists(directory)) return [];
        var records = new List<RegistryBackupRecord>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            records.Add(JsonSerializer.Deserialize<RegistryBackupRecord>(json) ??
                        throw new InvalidOperationException("A registry backup record is invalid."));
        }
        return records;
    }

    private static bool IsSafeRunId(string runId) =>
        !string.IsNullOrWhiteSpace(runId) && runId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        runId is not "." and not "..";
}
