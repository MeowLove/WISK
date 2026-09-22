using System.Collections.Immutable;
using System.Text.Json;
using Wisk.Contracts;
using Wisk.Core;
using Wisk.Execution;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class ExecutionHistoryVerifierTests
{
    [Fact]
    public async Task VerifiesRestartBoundaryWithoutMutatingPersistedAudit()
    {
        var plan = BuildPlan();
        var original = new ExecutionResult("runtime-webview2", TaskState.Succeeded, "None", "Apply completed.", true, true,
            VerificationStatus: VerificationStatus.PendingRestart);
        var snapshot = Snapshot(plan, original);
        var executor = new FakeExecutor(succeeded: true);

        var verification = await new ExecutionHistoryVerifier(executor).VerifyAsync(snapshot);

        Assert.Equal(VerificationStatus.Verified, verification.Status);
        Assert.Equal(VerificationStatus.Verified, verification.TaskStatuses[original.TaskId]);
        Assert.Equal(1, executor.VerifyCount);
        Assert.Equal(VerificationStatus.PendingRestart, snapshot.Results.Single().VerificationStatus);
    }

    [Fact]
    public async Task FailedReadbackIsReportedAsVerificationFailure()
    {
        var plan = BuildPlan();
        var snapshot = Snapshot(plan, new ExecutionResult("runtime-webview2", TaskState.Succeeded,
            "None", "Apply completed.", true, true, VerificationStatus: VerificationStatus.PendingRestart));

        var verification = await new ExecutionHistoryVerifier(new FakeExecutor(succeeded: false)).VerifyAsync(snapshot);

        Assert.Equal(VerificationStatus.Failed, verification.Status);
        Assert.Equal(VerificationStatus.Failed, verification.TaskStatuses["runtime-webview2"]);
    }

    [Fact]
    public async Task ManualReviewReadbackIsNotShownAsPendingRestart()
    {
        var plan = BuildPlan();
        var snapshot = Snapshot(plan, new ExecutionResult("runtime-webview2", TaskState.Succeeded,
            "None", "Apply completed.", true, true, VerificationStatus: VerificationStatus.PendingRestart));

        var verification = await new ExecutionHistoryVerifier(
            new FakeExecutor(succeeded: false, failureCode: ErrorCode.ManualReviewRequired)).VerifyAsync(snapshot);

        Assert.Equal(VerificationStatus.Unknown, verification.Status);
        Assert.Equal(VerificationStatus.Unknown, verification.TaskStatuses["runtime-webview2"]);
    }

    [Fact]
    public async Task MissingSerializedPlanIsReportedAsUnknown()
    {
        var snapshot = new RunStateSnapshot("run", "plan", "hash", TaskState.Succeeded,
            [new ExecutionResult("runtime-webview2", TaskState.Succeeded, "None", "Apply completed.", true, true,
                VerificationStatus: VerificationStatus.PendingRestart)], DateTimeOffset.UtcNow);

        var verification = await new ExecutionHistoryVerifier(new FakeExecutor(succeeded: true)).VerifyAsync(snapshot);

        Assert.Equal(VerificationStatus.Unknown, verification.Status);
        Assert.Equal(VerificationStatus.Unknown, verification.TaskStatuses["runtime-webview2"]);
    }

    [Fact]
    public async Task ExistingNeedsRebootSnapshotCanBeReconciled()
    {
        var plan = BuildPlan();
        var result = new ExecutionResult("runtime-webview2", TaskState.NeedsReboot, "None",
            "Apply completed.", true, true, VerificationStatus: VerificationStatus.NotRequired);
        var snapshot = Snapshot(plan, result) with { State = TaskState.NeedsReboot };

        var verification = await new ExecutionHistoryVerifier(new FakeExecutor(succeeded: true)).VerifyAsync(snapshot);

        Assert.Equal(VerificationStatus.Verified, verification.Status);
    }

    private static RunStateSnapshot Snapshot(ImmutablePlan plan, ExecutionResult result) => new(
        "run", plan.PlanId, plan.SemanticHash, TaskState.Succeeded, [result], DateTimeOffset.UtcNow,
        SerializedPlan: JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static ImmutablePlan BuildPlan()
    {
        var profile = new ProfileDocument("3.0", "history", ["runtime-webview2"], new ProfileTarget(), new ExecutionPolicy(), false, false);
        return new PlanBuilder(new Catalog()).Build(profile);
    }

    private sealed class FakeExecutor(bool succeeded, ErrorCode failureCode = ErrorCode.VerificationFailed) : IInitializerTaskExecutor
    {
        public int VerifyCount { get; private set; }
        public Task<TaskCheckResult> CheckAsync(PlannedTask task, CancellationToken cancellationToken) =>
            Task.FromResult(new TaskCheckResult(task.TaskId, TaskState.Ready, ErrorCode.None, "ready"));

        public Task<ExecutionResult> ApplyAsync(PlannedTask task, ApplyContext context) =>
            Task.FromResult(new ExecutionResult(task.TaskId, TaskState.Succeeded, "None", "applied", true, false));

        public Task<VerifyResult> VerifyAsync(PlannedTask task, CancellationToken cancellationToken)
        {
            VerifyCount++;
            return Task.FromResult(new VerifyResult(task.TaskId, succeeded, succeeded ? ErrorCode.None : failureCode,
                succeeded ? "verified" : "not yet verified"));
        }
    }
}
