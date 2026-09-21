using Microsoft.Win32;
using System.Globalization;
using System.Text;
using System.Text.Json;
using WindowsInitializer.Contracts;
using WindowsInitializer.Core;
using WindowsInitializer.Platform.Windows;
using Xunit;

namespace WindowsInitializer.Core.Tests;

public sealed class RegistryTaskExecutorTests
{
    [Fact]
    public async Task CheckSkipsMatchingDword()
    {
        var entry = RegistryOptimizationCatalog.GetEntries().First(item => item.Operation == RegistryOperationKind.DWord);
        var store = new FakeStore(new(true, RegistryValueKind.DWord, unchecked((int)uint.Parse(entry.RawValue[6..], System.Globalization.NumberStyles.HexNumber))));
        var result = await new RegistryTaskExecutor(store).CheckAsync(Plan(entry), CancellationToken.None);
        Assert.True(result.AlreadyComplete);
        Assert.Equal(TaskState.Skipped, result.State);
    }

    [Fact]
    public async Task DeleteValueIsReadyWhenValueExists()
    {
        var entry = RegistryOptimizationCatalog.GetEntries().First(item => item.Operation == RegistryOperationKind.DeleteValue);
        var result = await new RegistryTaskExecutor(new FakeStore(new(true, RegistryValueKind.String, "old")))
            .CheckAsync(Plan(entry), CancellationToken.None);
        Assert.Equal(TaskState.Ready, result.State);
    }

    [Fact]
    public async Task UnsupportedEntryRequiresManualReview()
    {
        var entry = RegistryOptimizationCatalog.GetEntries().First(item => !item.IsSupported);
        var result = await new RegistryTaskExecutor(new FakeStore(new(false, RegistryValueKind.None, null)))
            .CheckAsync(Plan(entry), CancellationToken.None);
        Assert.Equal(TaskState.NeedsManualReview, result.State);
        Assert.False(result.CanApply);
    }

    [Fact]
    public async Task CompoundFeatureIsCompleteOnlyWhenEveryMemberMatches()
    {
        var group = RegistryOptimizationCatalog.FindGroup("registry-group-take-ownership-menus")!;
        var values = group.Members.ToDictionary(entry => entry.TaskId, DesiredValue, StringComparer.OrdinalIgnoreCase);
        var store = new PerEntryFakeStore(values);
        var executor = new RegistryTaskExecutor(store);

        var complete = await executor.CheckAsync(Plan(group), CancellationToken.None);
        values[group.Members[0].TaskId] = new(false, RegistryValueKind.None, null);
        var incomplete = await executor.CheckAsync(Plan(group), CancellationToken.None);

        Assert.True(complete.AlreadyComplete);
        Assert.Equal(TaskState.Skipped, complete.State);
        Assert.False(incomplete.AlreadyComplete);
        Assert.Equal(TaskState.Ready, incomplete.State);
    }

    [Fact]
    public async Task CompoundFeatureRestoresEarlierMembersWhenAWriteFails()
    {
        var group = RegistryOptimizationCatalog.FindGroup("registry-group-take-ownership-menus")!;
        var values = group.Members.ToDictionary(entry => entry.TaskId,
            _ => new RegistryStoredValue(false, RegistryValueKind.None, null), StringComparer.OrdinalIgnoreCase);
        var failedMember = group.Members.Skip(3).First(entry => entry.Operation != RegistryOperationKind.DeleteValue);
        var store = new PerEntryFakeStore(values, failedMember.TaskId);
        var planned = Plan(group);
        var plan = new ImmutablePlan("plan", "profile", "3.0.0", DateTimeOffset.UtcNow, [planned], group.Risk, false, "hash");
        var runId = "test-" + Guid.NewGuid().ToString("N");
        var backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsInitializer", "state", "registry-backups", runId);

        try
        {
            var result = await new RegistryTaskExecutor(store).ApplyAsync(planned,
                new ApplyContext(runId, plan, CancellationToken.None, TimeSpan.FromSeconds(5), true));

            Assert.Equal(TaskState.Failed, result.State);
            Assert.All(values.Values, value => Assert.False(value.Exists));
            Assert.Equal(group.Members.Length, Directory.EnumerateFiles(backupRoot, "*.json").Count());
        }
        finally
        {
            if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, recursive: true);
        }
    }

    [Fact]
    public async Task LatestBackupExportsImportableRegistryFile()
    {
        var stateRoot = Path.Combine(Path.GetTempPath(), "WindowsInitializer.Tests", Guid.NewGuid().ToString("N"));
        var runRoot = Path.Combine(stateRoot, "registry-backups", "run-1");
        var destination = Path.Combine(stateRoot, "rollback.reg");
        Directory.CreateDirectory(runRoot);
        try
        {
            var record = new RegistryBackupRecord("registry-test", "HKCU", "Software\\Example", "Enabled", "dword:00000001");
            await File.WriteAllTextAsync(Path.Combine(runRoot, "registry-test.json"), JsonSerializer.Serialize(record));

            var count = await RegistryBackupExporter.ExportLatestAsync(stateRoot, destination);
            var text = await File.ReadAllTextAsync(destination, System.Text.Encoding.Unicode);

            Assert.Equal(1, count);
            Assert.Contains("Windows Registry Editor Version 5.00", text, StringComparison.Ordinal);
            Assert.Contains("[HKEY_CURRENT_USER\\Software\\Example]", text, StringComparison.Ordinal);
            Assert.Contains("\"Enabled\"=dword:00000001", text, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(stateRoot)) Directory.Delete(stateRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SelectedRunExportDoesNotUseAnewerBackupDirectory()
    {
        var stateRoot = Path.Combine(Path.GetTempPath(), "WindowsInitializer.Tests", Guid.NewGuid().ToString("N"));
        var selectedRoot = Path.Combine(stateRoot, "registry-backups", "selected-run");
        var newerRoot = Path.Combine(stateRoot, "registry-backups", "newer-run");
        var destination = Path.Combine(stateRoot, "rollback.reg");
        Directory.CreateDirectory(selectedRoot);
        Directory.CreateDirectory(newerRoot);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(selectedRoot, "selected.json"), JsonSerializer.Serialize(
                new RegistryBackupRecord("selected", "HKCU", "Software\\Selected", "Enabled", "dword:00000001")));
            await File.WriteAllTextAsync(Path.Combine(newerRoot, "newer.json"), JsonSerializer.Serialize(
                new RegistryBackupRecord("newer", "HKCU", "Software\\Newer", "Enabled", "dword:00000000")));

            var count = await RegistryBackupExporter.ExportRunAsync(stateRoot, "selected-run", destination);
            var text = await File.ReadAllTextAsync(destination, Encoding.Unicode);

            Assert.Equal(1, count);
            Assert.Contains("Software\\Selected", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Software\\Newer", text, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFiles(stateRoot, "*.tmp-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(stateRoot)) Directory.Delete(stateRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SelectedRunExportRejectsUnsafeRunId()
    {
        var stateRoot = Path.Combine(Path.GetTempPath(), "WindowsInitializer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stateRoot);
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => RegistryBackupExporter.ExportRunAsync(
                stateRoot, "../outside", Path.Combine(stateRoot, "rollback.reg")));
        }
        finally
        {
            Directory.Delete(stateRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PlanPreparationCapturesFixedSystemRegistrySettings()
    {
        var values = new Dictionary<string, RegistryStoredValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["setting-windows-update-mode"] = new(true, RegistryValueKind.DWord, 2)
        };
        var executor = new RegistryTaskExecutor(new PerEntryFakeStore(values));
        var planned = new PlannedTask("setting-windows-update-mode", "3.0.0", RiskLevel.Elevated,
            TaskSource.BuiltIn, [], false, "notify-download");
        var plan = new ImmutablePlan("plan", "profile", "3.0.0", DateTimeOffset.UtcNow, [planned],
            RiskLevel.Elevated, false, "hash");
        var runId = "system-prepare-" + Guid.NewGuid().ToString("N");
        var backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsInitializer", "state", "registry-backups", runId);

        try
        {
            await executor.PreparePlanAsync(plan, runId, CancellationToken.None);

            var backupPath = Assert.Single(Directory.EnumerateFiles(backupRoot, "*.json"));
            var backup = JsonSerializer.Deserialize<RegistryBackupRecord>(await File.ReadAllTextAsync(backupPath));
            Assert.NotNull(backup);
            Assert.Equal("AUOptions", backup!.ValueName);
            Assert.Equal("dword:00000002", backup.RestoreExpression);
        }
        finally
        {
            if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PlanPreparationCapturesEveryOriginalBeforeAnyRegistryWrite()
    {
        var group = RegistryOptimizationCatalog.FindGroup("registry-group-take-ownership-menus")!;
        var values = group.Members.ToDictionary(entry => entry.TaskId,
            _ => new RegistryStoredValue(false, RegistryValueKind.None, null), StringComparer.OrdinalIgnoreCase);
        var executor = new RegistryTaskExecutor(new PerEntryFakeStore(values));
        var planned = Plan(group);
        var plan = new ImmutablePlan("plan", "profile", "3.0.0", DateTimeOffset.UtcNow, [planned], group.Risk, false, "hash");
        var runId = "prepare-" + Guid.NewGuid().ToString("N");
        var backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsInitializer", "state", "registry-backups", runId);

        try
        {
            await executor.PreparePlanAsync(plan, runId, CancellationToken.None);
            var before = Directory.EnumerateFiles(backupRoot, "*.json")
                .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText, StringComparer.OrdinalIgnoreCase);
            await executor.ApplyAsync(planned, new ApplyContext(runId, plan, CancellationToken.None, TimeSpan.FromSeconds(5), true));
            var after = Directory.EnumerateFiles(backupRoot, "*.json")
                .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText, StringComparer.OrdinalIgnoreCase);

            Assert.Equal(group.Members.Length, before.Count);
            Assert.Equal(before.OrderBy(pair => pair.Key), after.OrderBy(pair => pair.Key));
        }
        finally
        {
            if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RecoveryCatalogListsAndLoadsRunSpecificEvidence()
    {
        var stateRoot = Path.Combine(Path.GetTempPath(), "WindowsInitializer.Recovery.Tests", Guid.NewGuid().ToString("N"));
        var runId = "run-a";
        var directory = Path.Combine(stateRoot, "registry-backups", runId);
        Directory.CreateDirectory(directory);
        var record = new RegistryBackupRecord("setting-long-paths", "HKLM", @"SOFTWARE\Test", "Value", "dword:00000001");
        await File.WriteAllTextAsync(Path.Combine(directory, "setting-long-paths.json"), JsonSerializer.Serialize(record));
        try
        {
            var runs = await RegistryBackupCatalogReader.ListRunsAsync(stateRoot);
            var records = await RegistryBackupCatalogReader.LoadRunAsync(stateRoot, runId);

            var run = Assert.Single(runs);
            Assert.Equal(runId, run.RunId);
            Assert.Equal(1, run.RecordCount);
            Assert.Equal(record, Assert.Single(records));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                RegistryBackupCatalogReader.LoadRunAsync(stateRoot, "../outside"));
        }
        finally
        {
            Directory.Delete(stateRoot, recursive: true);
        }
    }

    private static PlannedTask Plan(RegistryOptimizationEntry entry) => new(entry.TaskId, "3.0.0", entry.Risk, TaskSource.BuiltIn, [], false, string.Empty);
    private static PlannedTask Plan(RegistryOptimizationGroup group) => new(group.TaskId, "3.0.0", group.Risk, TaskSource.BuiltIn, [], false, string.Empty);

    private static RegistryStoredValue DesiredValue(RegistryOptimizationEntry entry)
    {
        if (entry.Operation == RegistryOperationKind.DeleteValue) return new(false, RegistryValueKind.None, null);
        return entry.Operation switch
        {
            RegistryOperationKind.String => new(true, RegistryValueKind.String,
                entry.RawValue[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal)),
            RegistryOperationKind.DWord => new(true, RegistryValueKind.DWord,
                unchecked((int)uint.Parse(entry.RawValue[6..], NumberStyles.HexNumber, CultureInfo.InvariantCulture))),
            RegistryOperationKind.Binary => new(true, RegistryValueKind.Binary, DecodeBytes(entry.RawValue[4..])),
            RegistryOperationKind.ExpandString => new(true, RegistryValueKind.ExpandString,
                Encoding.Unicode.GetString(DecodeBytes(entry.RawValue[7..])).TrimEnd('\0')),
            _ => throw new InvalidOperationException("The test entry is not writable.")
        };
    }

    private static byte[] DecodeBytes(string raw) => raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => byte.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();

    private sealed class FakeStore(RegistryStoredValue value) : IRegistryValueStore
    {
        public RegistryStoredValue Read(RegistryOptimizationEntry entry) => value;
        public void Write(RegistryOptimizationEntry entry, RegistryValueKind kind, object newValue) => value = new(true, kind, newValue);
        public void DeleteValue(RegistryOptimizationEntry entry) => value = new(false, RegistryValueKind.None, null);
    }

    private sealed class PerEntryFakeStore(Dictionary<string, RegistryStoredValue> values, string? failTaskId = null) : IRegistryValueStore
    {
        public RegistryStoredValue Read(RegistryOptimizationEntry entry) => values[entry.TaskId];
        public void Write(RegistryOptimizationEntry entry, RegistryValueKind kind, object newValue)
        {
            if (entry.TaskId.Equals(failTaskId, StringComparison.OrdinalIgnoreCase)) throw new IOException("Simulated write failure.");
            values[entry.TaskId] = new(true, kind, newValue);
        }
        public void DeleteValue(RegistryOptimizationEntry entry) => values[entry.TaskId] = new(false, RegistryValueKind.None, null);
    }
}
