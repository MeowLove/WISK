using System.Collections.Immutable;
using Wisk.Contracts;
using Wisk.Execution;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class DiagnosticExporterTests
{
    [Fact]
    public async Task ExportRedactsResultMessagesAndReplacesAtomically()
    {
        var root = Path.Combine(Path.GetTempPath(), "WISK wisk-diagnostics-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "run.json");
        var snapshot = new RunStateSnapshot("run", "plan", "hash", TaskState.Failed,
            [new ExecutionResult("task", TaskState.Failed, "ProcessFailed", "password=secret Bearer abc.def", false, false)],
            DateTimeOffset.UtcNow, SerializedPlan: "{\"planId\":\"plan\"}");
        try
        {
            await DiagnosticExporter.ExportAsync(snapshot, path);
            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("secret", json, StringComparison.Ordinal);
            Assert.DoesNotContain("abc.def", json, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", json, StringComparison.Ordinal);
            Assert.DoesNotContain(".tmp", Directory.EnumerateFiles(root).Select(Path.GetFileName));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExportRedactsExtensionExecutablePathFromPersistedPlan()
    {
        var root = Path.Combine(Path.GetTempPath(), "WISK wisk-diagnostics-path-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "run.json");
        var localPath = Path.Combine(root, "extensions", "worker.exe");
        var task = new PlannedTask("runtime-ms-bundle", "3.0.0", RiskLevel.High, TaskSource.ControlledExtension,
            [], false, string.Empty, ExtensionExecutablePath: localPath);
        var plan = new ImmutablePlan("plan", "profile", "3.0.0", DateTimeOffset.UtcNow, [task], RiskLevel.High, false, "hash");
        var snapshot = new RunStateSnapshot("run", "plan", "hash", TaskState.Failed, [], DateTimeOffset.UtcNow,
            SerializedPlan: System.Text.Json.JsonSerializer.Serialize(plan, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        try
        {
            await DiagnosticExporter.ExportAsync(snapshot, path);
            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain(localPath, json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[LOCAL_EXTENSION_PATH]", json, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
