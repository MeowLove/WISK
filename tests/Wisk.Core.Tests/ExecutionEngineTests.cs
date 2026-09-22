using System.Collections.Immutable;
using Wisk.Contracts;
using Wisk.Core;
using Wisk.Execution;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class ExecutionEngineTests
{
    [Fact]
    public async Task ExecuteRunsCheckApplyVerifyAndPersistsState()
    {
        var executor = new FakeExecutor();
        var store = new MemoryStore();
        var engine = new ExecutionEngine(executor, store);
        var plan = BuildPlan("runtime-webview2");

        var snapshot = await engine.ExecuteAsync(plan, new ExecutionPolicy(), true);

        Assert.Equal(TaskState.Succeeded, snapshot.State);
        Assert.Equal(TaskState.Succeeded, snapshot.Results.Single().State);
        Assert.Equal(VerificationStatus.Verified, snapshot.Results.Single().VerificationStatus);
        Assert.Equal(1, executor.ApplyCount);
        Assert.NotNull(store.Last);
        Assert.Contains(plan.PlanId, store.Last!.SerializedPlan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestartRequiredApplyIsCompletedAndVerificationWaitsForRestart()
    {
        var executor = new FakeExecutor { ApplyRequiresReboot = true };

        var snapshot = await new ExecutionEngine(executor).ExecuteAsync(
            BuildPlan("runtime-webview2"), new ExecutionPolicy(), true);

        Assert.Equal(TaskState.Succeeded, snapshot.State);
        var result = snapshot.Results.Single();
        Assert.Equal(TaskState.Succeeded, result.State);
        Assert.True(result.RebootRequired);
        Assert.Equal(VerificationStatus.PendingRestart, result.VerificationStatus);
        Assert.Contains("restart", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, executor.VerifyCount);
    }

    [Fact]
    public async Task FreshRunPreparesPlanBeforeCheckingTasks()
    {
        var executor = new PreparingExecutor();

        var snapshot = await new ExecutionEngine(executor).ExecuteAsync(BuildPlan("runtime-webview2"), new ExecutionPolicy(), true);

        Assert.Equal(TaskState.Succeeded, snapshot.State);
        Assert.Equal(["prepare", "check", "apply", "verify"], executor.Events);
    }

    [Fact]
    public async Task ConsecutivePlansCreateIndependentCompletedRuns()
    {
        var store = new RecordingStore();
        var engine = new ExecutionEngine(new FakeExecutor(), store);

        var first = await engine.ExecuteAsync(BuildPlan("runtime-webview2"), new ExecutionPolicy(), true);
        var second = await engine.ExecuteAsync(BuildPlan("app-vscode"), new ExecutionPolicy(), true);
        var history = await store.ListAsync(CancellationToken.None);

        Assert.NotEqual(first.RunId, second.RunId);
        Assert.Equal(2, history.Count);
        Assert.All(history, run => Assert.Equal(TaskState.Succeeded, run.State));
        Assert.Contains(history, run => run.Results.Single().TaskId == "runtime-webview2");
        Assert.Contains(history, run => run.Results.Single().TaskId == "app-vscode");
    }

    [Fact]
    public async Task StopOnErrorStopsFollowingTasks()
    {
        var executor = new FakeExecutor { FailApply = true };
        var engine = new ExecutionEngine(executor);
        var plan = BuildPlan("runtime-webview2", "app-vscode");

        var snapshot = await engine.ExecuteAsync(plan, new ExecutionPolicy(StopOnError: true), true);

        Assert.Equal(TaskState.Failed, snapshot.State);
        Assert.Equal(2, snapshot.Results.Length);
        var skipped = snapshot.Results.Single(result => result.TaskId == "app-vscode");
        Assert.Equal(TaskState.Skipped, skipped.State);
        Assert.Equal(ErrorCode.StoppedByEarlierFailure.ToString(), skipped.Code);
    }

    [Fact]
    public async Task CancellationMarksPendingTasksWithoutApplyingThem()
    {
        var executor = new FakeExecutor { BlockApply = true };
        var engine = new ExecutionEngine(executor);
        var plan = BuildPlan("runtime-webview2", "app-vscode");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var snapshot = await engine.ExecuteAsync(plan, new ExecutionPolicy(), true, cancellation.Token);

        Assert.Equal(TaskState.Cancelled, snapshot.State);
        Assert.Equal(2, snapshot.Results.Count(result => result.State == TaskState.Cancelled));
        Assert.Equal(0, executor.ApplyCount);
    }

    [Fact]
    public async Task CancellationDuringCheckIsPersisted()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new FakeExecutor { CheckAction = cancellation.Cancel };
        var store = new MemoryStore();

        var snapshot = await new ExecutionEngine(executor, store).ExecuteAsync(
            BuildPlan("runtime-webview2"), new ExecutionPolicy(), true, cancellation.Token);

        Assert.Equal(TaskState.Cancelled, snapshot.State);
        Assert.Equal(TaskState.Cancelled, snapshot.Results.Single().State);
        Assert.Equal(0, executor.ApplyCount);
        Assert.Equal(TaskState.Cancelled, store.Last!.State);
    }

    [Fact]
    public async Task CancellationAfterApplyRequiresManualReview()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new FakeExecutor { VerifyAction = cancellation.Cancel };

        var snapshot = await new ExecutionEngine(executor).ExecuteAsync(
            BuildPlan("runtime-webview2"), new ExecutionPolicy(), true, cancellation.Token);

        Assert.Equal(TaskState.Failed, snapshot.State);
        Assert.Equal(TaskState.NeedsManualReview, snapshot.Results.Single().State);
        Assert.Equal(ErrorCode.Cancelled.ToString(), snapshot.Results.Single().Code);
    }

    [Fact]
    public async Task ResumeSkipsCompletedTasksAndContinuesPendingTasks()
    {
        var store = new MemoryStore();
        var executor = new FakeExecutor();
        var engine = new ExecutionEngine(executor, store);
        var plan = BuildPlan("runtime-webview2", "app-vscode");
        store.Last = new RunStateSnapshot("resume", plan.PlanId, plan.SemanticHash, TaskState.Failed,
            [new ExecutionResult("runtime-webview2", TaskState.Succeeded, "None", "done", true, false)], DateTimeOffset.UtcNow);

        var snapshot = await engine.ResumeAsync("resume", plan, new ExecutionPolicy(), true);

        Assert.Equal(TaskState.Succeeded, snapshot.State);
        Assert.Equal(1, executor.ApplyCount);
        Assert.Contains(snapshot.Results, result => result.TaskId == "runtime-webview2" && result.State == TaskState.Succeeded);
    }

    [Fact]
    public async Task ResumeRunsTasksSkippedByEarlierFailure()
    {
        var store = new MemoryStore();
        var plan = BuildPlan("runtime-webview2", "app-vscode");
        await new ExecutionEngine(new FakeExecutor { FailApply = true }, store).ExecuteAsync(
            plan, new ExecutionPolicy(StopOnError: true), true, runId: "resume-skipped");
        var recoveryExecutor = new FakeExecutor();

        var snapshot = await new ExecutionEngine(recoveryExecutor, store).ResumeAsync(
            "resume-skipped", plan, new ExecutionPolicy(), true);

        Assert.Equal(TaskState.Succeeded, snapshot.State);
        Assert.Equal(2, recoveryExecutor.ApplyCount);
    }

    [Fact]
    public async Task PersistedStateRedactsLabeledSecrets()
    {
        var store = new MemoryStore();
        var engine = new ExecutionEngine(new FakeExecutor { Message = "password=not-for-state Bearer abc.def" }, store);

        await engine.ExecuteAsync(BuildPlan("runtime-webview2"), new ExecutionPolicy(), true);

        Assert.DoesNotContain("not-for-state", store.Last!.Results[0].Message);
        Assert.DoesNotContain("abc.def", store.Last.Results[0].Message);
        Assert.Contains("[REDACTED]", store.Last.Results[0].Message);
    }

    [Fact]
    public async Task AtomicStateStoreRejectsUnsafeRunIds()
    {
        var store = new AtomicJsonStateStore(Path.Combine(Path.GetTempPath(), "WISK wisk-tests"));
        var snapshot = new RunStateSnapshot("safe", "plan", "hash", TaskState.Succeeded, [], DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(snapshot with { RunId = "../escape" }, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteRejectsPolicyThatDiffersFromImmutablePlan()
    {
        var plan = BuildPlan(new ExecutionPolicy(MaxRetries: 1), "runtime-webview2");

        var exception = await Assert.ThrowsAsync<PlanValidationException>(() =>
            new ExecutionEngine(new FakeExecutor()).ExecuteAsync(plan, new ExecutionPolicy(MaxRetries: 2), true));

        Assert.Equal(ErrorCode.PlanTampered, exception.Code);
    }

    [Fact]
    public async Task ExecuteRejectsUnboundedProgrammaticPolicy()
    {
        var plan = BuildPlan("runtime-webview2");

        var exception = await Assert.ThrowsAsync<PlanValidationException>(() =>
            new ExecutionEngine(new FakeExecutor()).ExecuteAsync(plan, new ExecutionPolicy(DefaultTimeout: TimeSpan.Zero), true));

        Assert.Equal(ErrorCode.InvalidProfile, exception.Code);
    }

    [Fact]
    public async Task AtomicStateStoreMarksCorruptJsonForRecovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "WISK wisk-corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "broken.json"), "{ invalid");
        var store = new AtomicJsonStateStore(root);

        var snapshot = await store.LoadAsync("broken", CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.RecoveryRequired);
        Assert.Equal(TaskState.RecoveryRequired, snapshot.State);
    }

    [Fact]
    public async Task HistoryRetainsCorruptJsonForRecovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "WISK wisk-corrupt-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "broken.json"), "{ invalid");
        try
        {
            var history = await new AtomicJsonStateStore(root).ListAsync(CancellationToken.None);

            var snapshot = Assert.Single(history);
            Assert.Equal("broken", snapshot.RunId);
            Assert.Equal(TaskState.RecoveryRequired, snapshot.State);
            Assert.True(snapshot.RecoveryRequired);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StateStoreTreatsValidJsonWithMissingRequiredFieldsAsRecoveryState()
    {
        var root = Path.Combine(Path.GetTempPath(), "WISK wisk-incomplete-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "incomplete.json"), "{}");
        try
        {
            var store = new AtomicJsonStateStore(root);

            var loaded = await store.LoadAsync("incomplete", CancellationToken.None);
            var listed = Assert.Single(await store.ListAsync(CancellationToken.None));

            Assert.NotNull(loaded);
            Assert.True(loaded!.RecoveryRequired);
            Assert.Equal("incomplete", loaded.RunId);
            Assert.True(listed.RecoveryRequired);
            Assert.Equal("incomplete", listed.RunId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RunLeaseRejectsConcurrentAcquisition()
    {
        var root = Path.Combine(Path.GetTempPath(), "WISK wisk-lock-" + Guid.NewGuid().ToString("N"));
        using var first = RunLease.Acquire(root);

        Assert.Throws<ConcurrentRunException>(() => RunLease.Acquire(root));
    }

    [Fact]
    public async Task ResumeMapsCorruptStateToStateCorrupt()
    {
        var store = new MemoryStore();
        var plan = BuildPlan("runtime-webview2");
        store.Last = new RunStateSnapshot("corrupt", plan.PlanId, plan.SemanticHash, TaskState.RecoveryRequired, [], DateTimeOffset.UtcNow, true);
        var engine = new ExecutionEngine(new FakeExecutor(), store);

        var exception = await Assert.ThrowsAsync<PlanValidationException>(() => engine.ResumeAsync("corrupt", plan, new ExecutionPolicy(), true));

        Assert.Equal(ErrorCode.StateCorrupt, exception.Code);
    }

    [Fact]
    public async Task HistoryListsPersistedRunsThroughEngine()
    {
        var store = new MemoryStore();
        var engine = new ExecutionEngine(new FakeExecutor(), store);
        await engine.ExecuteAsync(BuildPlan("runtime-webview2"), new ExecutionPolicy(), true);

        var history = await engine.ListHistoryAsync();

        Assert.Single(history);
        Assert.Equal(TaskState.Succeeded, history[0].State);
    }

    [Fact]
    public async Task ReportsPerTaskProgress()
    {
        var progress = new RecordingProgress();
        var snapshot = await new ExecutionEngine(new FakeExecutor()).ExecuteAsync(BuildPlan("runtime-webview2"), new ExecutionPolicy(), true, progress: progress);

        Assert.Equal(TaskState.Succeeded, snapshot.State);
        Assert.Contains(progress.Values, value => value.State == TaskState.Checking);
        Assert.Contains(progress.Values, value => value.State == TaskState.Applying);
        Assert.Contains(progress.Values, value => value.State == TaskState.Verifying);
        Assert.Contains(progress.Values, value => value.State == TaskState.Succeeded);
        Assert.Equal(snapshot.RunId, progress.Values[^1].RunId);
    }

    [Fact]
    public async Task RetriesRetryableFailureWithinBound()
    {
        var executor = new FakeExecutor { FailuresBeforeSuccess = 1 };
        var policy = new ExecutionPolicy(MaxRetries: 2, RetryBaseDelay: TimeSpan.FromMilliseconds(1));

        var snapshot = await new ExecutionEngine(executor).ExecuteAsync(BuildPlan(policy, "runtime-webview2"), policy, true);

        Assert.Equal(TaskState.Succeeded, snapshot.State);
        Assert.Equal(2, executor.ApplyCount);
    }

    [Theory]
    [InlineData("accounts-local")]
    [InlineData("device-setup-region")]
    public async Task DoesNotAutomaticallyRetrySensitiveTasks(string taskId)
    {
        var executor = new FakeExecutor { FailuresBeforeSuccess = 1 };
        var policy = new ExecutionPolicy(MaxRetries: 2, RetryBaseDelay: TimeSpan.FromMilliseconds(1));

        var snapshot = await new ExecutionEngine(executor).ExecuteAsync(BuildPlan(policy, taskId), policy, true);

        Assert.Equal(TaskState.Failed, snapshot.State);
        Assert.Equal(1, executor.ApplyCount);
        Assert.False(snapshot.Results.Single().Retryable);
    }

    [Fact]
    public async Task MapsSoftTimeoutToRetryableFailure()
    {
        var executor = new FakeExecutor { BlockApply = true };
        var policy = new ExecutionPolicy(DefaultTimeout: TimeSpan.FromMilliseconds(20));

        var snapshot = await new ExecutionEngine(executor).ExecuteAsync(BuildPlan(policy, "runtime-webview2"), policy, true);

        Assert.Equal(TaskState.Failed, snapshot.State);
        Assert.Equal(ErrorCode.Timeout.ToString(), snapshot.Results.Single().Code);
    }

    private static ImmutablePlan BuildPlan(params string[] taskIds) => BuildPlan(new ExecutionPolicy(), taskIds);

    private static ImmutablePlan BuildPlan(ExecutionPolicy policy, params string[] taskIds)
    {
        var catalog = new Catalog();
        var tasks = taskIds.Select(id => catalog.Find(id)!).ToArray();
        var profile = new ProfileDocument("3.0", "execution", taskIds.ToImmutableArray(), new ProfileTarget(), policy,
            tasks.Any(task => task.Risk == RiskLevel.Elevated), tasks.Any(task => task.Risk == RiskLevel.High));
        return new PlanBuilder(catalog).Build(profile);
    }

    private sealed class FakeExecutor : IInitializerTaskExecutor
    {
        public int ApplyCount { get; private set; }
        public int VerifyCount { get; private set; }
        public bool FailApply { get; init; }
        public bool BlockApply { get; init; }
        public bool ApplyRequiresReboot { get; init; }
        public int FailuresBeforeSuccess { get; init; }
        public string Message { get; init; } = "applied";
        public Action? CheckAction { get; init; }
        public Action? VerifyAction { get; init; }
        public Task<TaskCheckResult> CheckAsync(PlannedTask task, CancellationToken cancellationToken)
        {
            CheckAction?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new TaskCheckResult(task.TaskId, TaskState.Ready, ErrorCode.None, "ready"));
        }

        public async Task<ExecutionResult> ApplyAsync(PlannedTask task, ApplyContext context)
        {
            ApplyCount++;
            if (BlockApply) await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            if (ApplyCount <= FailuresBeforeSuccess)
                return new ExecutionResult(task.TaskId, TaskState.Failed, ErrorCode.ProcessFailed.ToString(), "retryable failure", false, false, true);
            return FailApply
                ? new ExecutionResult(task.TaskId, TaskState.Failed, ErrorCode.ProcessFailed.ToString(), "failed", false, false)
                : new ExecutionResult(task.TaskId, TaskState.Succeeded, ErrorCode.None.ToString(), Message, true, ApplyRequiresReboot);
        }

        public Task<VerifyResult> VerifyAsync(PlannedTask task, CancellationToken cancellationToken)
        {
            VerifyCount++;
            VerifyAction?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new VerifyResult(task.TaskId, true, ErrorCode.None, Message));
        }
    }

    private sealed class PreparingExecutor : IInitializerTaskExecutor, IPlanPreparationExecutor
    {
        public List<string> Events { get; } = [];
        public Task PrepareAsync(ImmutablePlan plan, string runId, CancellationToken cancellationToken) { Events.Add("prepare"); return Task.CompletedTask; }
        public Task<TaskCheckResult> CheckAsync(PlannedTask task, CancellationToken cancellationToken) { Events.Add("check"); return Task.FromResult(new TaskCheckResult(task.TaskId, TaskState.Ready, ErrorCode.None, "ready")); }
        public Task<ExecutionResult> ApplyAsync(PlannedTask task, ApplyContext context) { Events.Add("apply"); return Task.FromResult(new ExecutionResult(task.TaskId, TaskState.Succeeded, "None", "applied", true, false)); }
        public Task<VerifyResult> VerifyAsync(PlannedTask task, CancellationToken cancellationToken) { Events.Add("verify"); return Task.FromResult(new VerifyResult(task.TaskId, true, ErrorCode.None, "verified")); }
    }

    private sealed class MemoryStore : IRunStateStore
    {
        public RunStateSnapshot? Last { get; set; }
        public Task SaveAsync(RunStateSnapshot snapshot, CancellationToken cancellationToken) { Last = snapshot; return Task.CompletedTask; }
        public Task<RunStateSnapshot?> LoadAsync(string runId, CancellationToken cancellationToken) => Task.FromResult(Last);
        public Task<IReadOnlyList<RunStateSnapshot>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RunStateSnapshot>>(Last is null ? Array.Empty<RunStateSnapshot>() : [Last]);
    }

    private sealed class RecordingStore : IRunStateStore
    {
        private readonly Dictionary<string, RunStateSnapshot> _runs = new(StringComparer.OrdinalIgnoreCase);
        public Task SaveAsync(RunStateSnapshot snapshot, CancellationToken cancellationToken)
        {
            _runs[snapshot.RunId] = snapshot;
            return Task.CompletedTask;
        }
        public Task<RunStateSnapshot?> LoadAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult(_runs.GetValueOrDefault(runId));
        public Task<IReadOnlyList<RunStateSnapshot>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RunStateSnapshot>>(_runs.Values.OrderBy(run => run.UpdatedAt).ToArray());
    }

    private sealed class RecordingProgress : IProgress<ExecutionProgress>
    {
        public List<ExecutionProgress> Values { get; } = [];
        public void Report(ExecutionProgress value) => Values.Add(value);
    }
}
