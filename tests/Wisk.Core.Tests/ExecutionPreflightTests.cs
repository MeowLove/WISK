using System.Collections.Immutable;
using Wisk.Contracts;
using Wisk.Core;
using Wisk.Execution;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class ExecutionPreflightTests
{
    [Fact]
    public void BlocksMissingRuntimePrerequisitesAndLowDiskSpace()
    {
        var catalog = new Catalog();
        var task = catalog.Find("app-vscode")!;
        var plan = BuildPlan(task);
        var compatibility = Compatibility(administrator: true, powerShell: true, winGet: false, network: false);

        var report = ExecutionPreflightEvaluator.Evaluate(plan, [task], compatibility,
            new ExecutionEnvironmentSnapshot(1024, false, true));

        Assert.False(report.CanExecute);
        Assert.Contains(report.Checks, check => check.Id == "winGet" && check.Status == PreflightStatus.Blocked);
        Assert.Contains(report.Checks, check => check.Id == "network" && check.Status == PreflightStatus.Blocked);
        Assert.Contains(report.Checks, check => check.Id == "diskSpace" && check.Status == PreflightStatus.Blocked);
    }

    [Fact]
    public void PendingRestartWarnsWithoutBlocking()
    {
        var catalog = new Catalog();
        var task = catalog.Find("setting-show-file-extensions")!;
        var report = ExecutionPreflightEvaluator.Evaluate(BuildPlan(task), [task],
            Compatibility(true, true, true, true), new ExecutionEnvironmentSnapshot(long.MaxValue, true, true));

        Assert.True(report.CanExecute);
        Assert.Contains(report.Checks, check => check.Id == "pendingReboot" && check.Status == PreflightStatus.Warning);
    }

    [Theory]
    [InlineData(SimulationOutcome.Success, TaskState.Succeeded, "None")]
    [InlineData(SimulationOutcome.AlreadyComplete, TaskState.Succeeded, "None")]
    [InlineData(SimulationOutcome.ApplyFailure, TaskState.Failed, "ProcessFailed")]
    [InlineData(SimulationOutcome.VerifyFailure, TaskState.Failed, "VerificationFailed")]
    [InlineData(SimulationOutcome.Timeout, TaskState.Failed, "Timeout")]
    [InlineData(SimulationOutcome.NeedsReboot, TaskState.NeedsReboot, "None")]
    public async Task SimulationRunsThroughTheRealExecutionPipeline(
        SimulationOutcome outcome, TaskState expectedRunState, string expectedCode)
    {
        var taskId = "setting-show-file-extensions";
        var executor = new SimulationTaskExecutor(new Dictionary<string, SimulationOutcome> { [taskId] = outcome });

        var snapshot = await new ExecutionEngine(executor).ExecuteAsync(
            BuildPlan(new Catalog().Find(taskId)!), new ExecutionPolicy(), true);

        Assert.Equal(expectedRunState, snapshot.State);
        Assert.Equal(expectedCode, snapshot.Results.Single().Code);
    }

    private static ImmutablePlan BuildPlan(TaskDescriptor task)
    {
        var parameters = ConfigurableRegistrySettingCatalog.Find(task.Id) is not null
            ? ImmutableDictionary<string, string>.Empty.Add(task.Id, ConfigurableRegistrySettingCatalog.DisabledState)
            : null;
        var profile = new ProfileDocument("2.0", "preflight", [task.Id], new ProfileTarget(), new ExecutionPolicy(),
            task.Risk == RiskLevel.Elevated, task.Risk == RiskLevel.High, Parameters: parameters);
        return new PlanBuilder(new Catalog()).Build(profile);
    }

    private static CompatibilitySnapshot Compatibility(bool administrator, bool powerShell, bool winGet, bool network) =>
        new("Windows 11 Pro", "24H2", 26100, 1000, "Professional", "X64", "X64", true, true,
            administrator, powerShell, winGet, network, false, true, true);
}
