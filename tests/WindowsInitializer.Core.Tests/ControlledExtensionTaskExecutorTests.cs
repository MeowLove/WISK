using System.Collections.Immutable;
using System.Security.Cryptography;
using WindowsInitializer.Contracts;
using WindowsInitializer.Platform.Windows;
using Xunit;

namespace WindowsInitializer.Core.Tests;

public sealed class ControlledExtensionTaskExecutorTests
{
    [Fact]
    public async Task StructuredResponseIsCorrelatedWithoutExecutingTestAsset()
    {
        var (task, path) = CreateTask();
        try
        {
            var runner = new FakeRunner("{\"protocolVersion\":\"1.0\",\"runId\":\"run\",\"taskId\":\"runtime-ms-bundle\",\"status\":\"Succeeded\",\"code\":\"None\",\"message\":\"ok\",\"changed\":true,\"rebootRequired\":false,\"summary\":\"ok\"}");
            var executor = new ControlledExtensionTaskExecutor(runner);
            var context = new ApplyContext("run", Plan(task), CancellationToken.None, TimeSpan.FromSeconds(1), true);

            var result = await executor.ApplyAsync(task, context);

            Assert.Equal(TaskState.Succeeded, result.State);
            Assert.True(runner.Called);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task HashMismatchIsRejectedBeforeProcessLaunch()
    {
        var (task, path) = CreateTask();
        try
        {
            await File.WriteAllTextAsync(path, "tampered");
            var runner = new FakeRunner(string.Empty);

            var result = await new ControlledExtensionTaskExecutor(runner).CheckAsync(task, CancellationToken.None);

            Assert.Equal(ErrorCode.ExtensionHashMismatch, result.Code);
            Assert.False(runner.Called);
        }
        finally { File.Delete(path); }
    }

    private static (PlannedTask Task, string Path) CreateTask()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(path, [1, 2, 3]);
        var hash = Convert.ToHexString(SHA256.HashData([1, 2, 3]));
        return (new PlannedTask("runtime-ms-bundle", "2.0.0", RiskLevel.High, TaskSource.ControlledExtension,
            [], false, string.Empty, RequiresAdministrator: true, ExtensionPackageId: "demo",
            ExtensionPackageVersion: "1.0.0", ExtensionManifestHash: new string('A', 64),
            ExtensionExecutablePath: path, ExtensionExecutableHash: hash, ExtensionProtocol: "stdio-json-v1"), path);
    }

    private static ImmutablePlan Plan(PlannedTask task) => new ImmutablePlan("plan", "profile", "2.0.0", DateTimeOffset.UtcNow,
        [task], RiskLevel.High, false, string.Empty);

    private sealed class FakeRunner(string output) : IControlledExtensionProcessRunner
    {
        public bool Called { get; private set; }
        public Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string executablePath, string requestJson, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult((0, output, string.Empty));
        }
    }
}
