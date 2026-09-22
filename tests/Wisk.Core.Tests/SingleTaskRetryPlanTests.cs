using System.Collections.Immutable;
using System.Text.Json;
using Wisk.Contracts;
using Wisk.Core;
using Wisk.Execution;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class SingleTaskRetryPlanTests
{
    [Fact]
    public void CreatesIndependentOneTaskPlanFromFailedHistoricalResult()
    {
        var original = BuildPlan("system-computer-name", "runtime-dotnet-8");
        var snapshot = Snapshot(original,
            new ExecutionResult("system-computer-name", TaskState.Failed, "ProcessFailed", "failed", false, true),
            new ExecutionResult("runtime-dotnet-8", TaskState.Succeeded, "None", "done", true, false));

        var retry = SingleTaskRetryPlan.Create(snapshot, "system-computer-name");

        Assert.NotEqual(original.PlanId, retry.PlanId);
        Assert.Single(retry.Tasks);
        Assert.Equal("system-computer-name", retry.Tasks[0].TaskId);
        Assert.Equal("NEW-PC", retry.Tasks[0].ParameterSummary);
        Assert.Equal(original.Policy, retry.Policy);
        PlanBuilder.ValidatePlanIntegrity(retry);
    }

    [Fact]
    public void RejectsSuccessfulTaskAndSensitiveAccountRetry()
    {
        var successful = BuildPlan("runtime-dotnet-8");
        var successfulSnapshot = Snapshot(successful,
            new ExecutionResult("runtime-dotnet-8", TaskState.Succeeded, "None", "done", true, false));
        Assert.Equal(ErrorCode.InvalidProfile,
            Assert.Throws<PlanValidationException>(() => SingleTaskRetryPlan.Create(successfulSnapshot, "runtime-dotnet-8")).Code);

        var accounts = BuildPlan("accounts-local");
        var accountSnapshot = Snapshot(accounts,
            new ExecutionResult("accounts-local", TaskState.Failed, "ProcessFailed", "failed", false, false));
        Assert.Equal(ErrorCode.InvalidProfile,
            Assert.Throws<PlanValidationException>(() => SingleTaskRetryPlan.Create(accountSnapshot, "accounts-local")).Code);
    }

    private static ImmutablePlan BuildPlan(params string[] taskIds)
    {
        var tasks = taskIds.Select(id => new PlannedTask(id, "3.0.0", RiskLevel.Elevated, TaskSource.BuiltIn,
            [], id == "system-computer-name", id == "system-computer-name" ? "NEW-PC" : string.Empty,
            RequiresAdministrator: true)).ToImmutableArray();
        var plan = new ImmutablePlan("original", "interactive", "3.0.0", DateTimeOffset.UtcNow, tasks,
            RiskLevel.Elevated, tasks.Any(task => task.RequiresReboot), string.Empty, Policy: new ExecutionPolicy(MaxRetries: 1));
        return plan.WithSemanticHash(PlanBuilder.ComputeSemanticHash(plan));
    }

    private static RunStateSnapshot Snapshot(ImmutablePlan plan, params ExecutionResult[] results) => new(
        "run", plan.PlanId, plan.SemanticHash, TaskState.Failed, results.ToImmutableArray(), DateTimeOffset.UtcNow,
        SerializedPlan: JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}
