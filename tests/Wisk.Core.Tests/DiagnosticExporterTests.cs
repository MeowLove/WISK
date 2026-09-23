using System.Collections.Immutable;
using System.Text.Json;
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

    [Fact]
    public async Task ExportRedactsConfiguredIdentityProxyAndLocalPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "WISK wisk-diagnostics-identity-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "run.json");
        var tasks = ImmutableArray.Create(
            new PlannedTask("accounts-local", "3.0.0", RiskLevel.Elevated, TaskSource.BuiltIn, [], false, "alice"),
            new PlannedTask("computer-name", "3.0.0", RiskLevel.Elevated, TaskSource.BuiltIn, [], false, "WISK-LAB-07",
                Proxy: "https://proxy.private.lan:8443"));
        var plan = new ImmutablePlan("plan", "AliceProfile", "3.0.0", DateTimeOffset.UtcNow, tasks,
            RiskLevel.Elevated, false, "hash");
        var snapshot = new RunStateSnapshot("run", "plan", "hash", TaskState.Failed,
            [new ExecutionResult("computer-name", TaskState.Failed, "ProcessFailed",
                "Failed for alice on WISK-LAB-07 using proxy.private.lan at C:\\Users\\alice\\Desktop\\wisk.log and %LOCALAPPDATA%\\WISK\\logs\\run.log and \\\\private-nas\\users\\alice\\report.json", false, false)],
            DateTimeOffset.UtcNow, SerializedPlan: JsonSerializer.Serialize(plan,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var originalPlan = snapshot.SerializedPlan;
        var originalMessage = snapshot.Results[0].Message;
        try
        {
            await DiagnosticExporter.ExportAsync(snapshot, path);
            var json = await File.ReadAllTextAsync(path);

            Assert.DoesNotContain("alice", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("WISK-LAB-07", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AliceProfile", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("proxy.private.lan", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("C:\\\\Users\\\\alice", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("%LOCALAPPDATA%", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private-nas", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[LOCAL_PATH]", json, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", json, StringComparison.Ordinal);
            Assert.Equal(originalPlan, snapshot.SerializedPlan);
            Assert.Equal(originalMessage, snapshot.Results[0].Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("{\"tasks\":[null]}")]
    [InlineData("{\"tasks\":[{\"taskId\":null}]}")]
    public async Task ExportRedactsMalformedSerializedPlansWithoutFailing(string serializedPlan)
    {
        var root = Path.Combine(Path.GetTempPath(), "WISK wisk-diagnostics-invalid-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "run.json");
        var snapshot = new RunStateSnapshot("run", "plan", "hash", TaskState.Failed, [], DateTimeOffset.UtcNow,
            SerializedPlan: serializedPlan);
        try
        {
            await DiagnosticExporter.ExportAsync(snapshot, path);

            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("[INVALID_PLAN_REDACTED]", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"tasks\":[null]", json, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
