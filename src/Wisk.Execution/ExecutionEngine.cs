using System.Collections.Immutable;
using System.Text.Json;
using Wisk.Contracts;
using Wisk.Core;

namespace Wisk.Execution;

public interface IInitializerTaskExecutor
{
    Task<TaskCheckResult> CheckAsync(PlannedTask task, CancellationToken cancellationToken);
    Task<ExecutionResult> ApplyAsync(PlannedTask task, ApplyContext context);
    Task<VerifyResult> VerifyAsync(PlannedTask task, CancellationToken cancellationToken);
}

public interface IPlanPreparationExecutor
{
    Task PrepareAsync(ImmutablePlan plan, string runId, CancellationToken cancellationToken);
}

public sealed record ExecutionProgress(string RunId, string TaskId, TaskState State, int Completed, int Total, string Message);

public interface IRunStateStore
{
    Task SaveAsync(RunStateSnapshot snapshot, CancellationToken cancellationToken);
    Task<RunStateSnapshot?> LoadAsync(string runId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RunStateSnapshot>> ListAsync(CancellationToken cancellationToken);
}

public sealed class NoOpTaskExecutor : IInitializerTaskExecutor
{
    public Task<TaskCheckResult> CheckAsync(PlannedTask task, CancellationToken cancellationToken) =>
        Task.FromResult(new TaskCheckResult(task.TaskId, TaskState.NeedsManualReview, ErrorCode.ManualReviewRequired,
            "No platform executor is registered for this task.", CanApply: false));

    public Task<ExecutionResult> ApplyAsync(PlannedTask task, ApplyContext context) =>
        Task.FromResult(new ExecutionResult(task.TaskId, TaskState.NeedsManualReview, ErrorCode.ManualReviewRequired.ToString(),
            "This task requires a registered platform executor.", false, task.RequiresReboot));

    public Task<VerifyResult> VerifyAsync(PlannedTask task, CancellationToken cancellationToken) =>
        Task.FromResult(new VerifyResult(task.TaskId, false, ErrorCode.ManualReviewRequired,
            "No verification executor is registered for this task."));
}

public sealed class ExecutionEngine
{
    private static readonly JsonSerializerOptions PlanJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IInitializerTaskExecutor _executor;
    private readonly IRunStateStore? _stateStore;

    public ExecutionEngine(IInitializerTaskExecutor? executor = null, IRunStateStore? stateStore = null)
    {
        _executor = executor ?? new NoOpTaskExecutor();
        _stateStore = stateStore;
    }

    public Task<ExecutionResult> CheckAsync(TaskDescriptor task, CancellationToken cancellationToken) =>
        Task.FromResult(new ExecutionResult(task.Id, TaskState.Ready, ErrorCode.None.ToString(), "Task is ready for execution.", false, task.RequiresReboot));

    public Task<IReadOnlyList<RunStateSnapshot>> ListHistoryAsync(CancellationToken cancellationToken = default) =>
        _stateStore is null
            ? Task.FromResult<IReadOnlyList<RunStateSnapshot>>(Array.Empty<RunStateSnapshot>())
            : _stateStore.ListAsync(cancellationToken);

    public async Task<RunStateSnapshot> ExecuteAsync(
        ImmutablePlan plan,
        ExecutionPolicy policy,
        bool isElevated,
        CancellationToken cancellationToken = default,
        string? runId = null,
        IReadOnlyDictionary<string, ExecutionResult>? recoveredResults = null,
        IReadOnlyList<ProfileAccount>? accounts = null,
        IProgress<ExecutionProgress>? progress = null)
    {
        PlanBuilder.ValidatePlanIntegrity(plan);
        var policyIssue = ProfileDocumentValidator.ValidateExecutionPolicy(policy);
        if (policyIssue is not null)
            throw new PlanValidationException(policyIssue.Message, policyIssue.Code);
        if (plan.Policy is not null && plan.Policy != policy)
            throw new PlanValidationException("Execution policy does not match the immutable plan.", ErrorCode.PlanTampered);
        var effectiveRunId = runId ?? Guid.NewGuid().ToString("N");
        var results = new List<ExecutionResult>();
        if (recoveredResults is not null) results.AddRange(recoveredResults.Values);

        if (recoveredResults is null && _executor is IPlanPreparationExecutor preparationExecutor)
            await preparationExecutor.PrepareAsync(plan, effectiveRunId, cancellationToken).ConfigureAwait(false);

        foreach (var task in plan.Tasks)
        {
            if (results.Any(result => result.TaskId.Equals(task.TaskId, StringComparison.OrdinalIgnoreCase))) continue;

            progress?.Report(new ExecutionProgress(effectiveRunId, task.TaskId, TaskState.Checking, results.Count, plan.Tasks.Length, "Checking task prerequisites."));

            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(Cancelled(task.TaskId));
                progress?.Report(new ExecutionProgress(effectiveRunId, task.TaskId, TaskState.Cancelled, results.Count, plan.Tasks.Length, "Task was cancelled before completion."));
                continue;
            }

            TaskCheckResult check;
            try
            {
                check = await _executor.CheckAsync(task, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                results.Add(Cancelled(task.TaskId));
                progress?.Report(new ExecutionProgress(effectiveRunId, task.TaskId, TaskState.Cancelled, results.Count, plan.Tasks.Length, "Task check was cancelled."));
                await PersistAsync(effectiveRunId, plan, results, TaskState.Cancelled, CancellationToken.None).ConfigureAwait(false);
                continue;
            }
            if (check.AlreadyComplete)
            {
                results.Add(new ExecutionResult(task.TaskId, TaskState.Skipped, check.Code.ToString(), check.Message, false, check.RequiresReboot,
                    VerificationStatus: check.RequiresReboot ? VerificationStatus.PendingRestart : VerificationStatus.NotRequired));
                progress?.Report(new ExecutionProgress(effectiveRunId, task.TaskId, TaskState.Skipped, results.Count, plan.Tasks.Length, check.Message));
                await PersistAsync(effectiveRunId, plan, results, TaskState.Running, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (check.State is TaskState.UnsupportedPrerequisite or TaskState.UnavailableExtension || !check.CanApply)
            {
                results.Add(new ExecutionResult(task.TaskId, check.State, check.Code.ToString(), check.Message, false,
                    check.RequiresReboot, check.Retryable, FailureStage: "Check"));
                progress?.Report(new ExecutionProgress(effectiveRunId, task.TaskId, check.State, results.Count, plan.Tasks.Length, check.Message));
                await PersistAsync(effectiveRunId, plan, results, check.State, cancellationToken).ConfigureAwait(false);
                if (policy.StopOnError) break;
                continue;
            }

            if ((task.Risk != RiskLevel.Standard || task.RequiresAdministrator) && !isElevated)
            {
                results.Add(new ExecutionResult(task.TaskId, TaskState.NeedsManualReview, ErrorCode.NotAdministrator.ToString(),
                    "An elevated administrator context is required.", false, task.RequiresReboot, FailureStage: "Check"));
                progress?.Report(new ExecutionProgress(effectiveRunId, task.TaskId, TaskState.NeedsManualReview, results.Count, plan.Tasks.Length, "An elevated administrator context is required."));
                await PersistAsync(effectiveRunId, plan, results, TaskState.NeedsManualReview, cancellationToken).ConfigureAwait(false);
                if (policy.StopOnError) break;
                continue;
            }

            var result = await ExecuteTaskAsync(task, plan, policy, isElevated, effectiveRunId, cancellationToken, accounts, progress, results.Count).ConfigureAwait(false);
            results.Add(result);
            progress?.Report(new ExecutionProgress(effectiveRunId, task.TaskId, result.State, results.Count, plan.Tasks.Length, result.Message));
            await PersistAsync(effectiveRunId, plan, results, result.State, cancellationToken).ConfigureAwait(false);
            if (result.State is not TaskState.Succeeded and not TaskState.Skipped && policy.StopOnError) break;
        }

        var completedTaskIds = results.Select(result => result.TaskId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (results.Any(result => result.State is TaskState.Failed or TaskState.NeedsManualReview or TaskState.UnsupportedPrerequisite or TaskState.UnavailableExtension))
        {
            foreach (var pending in plan.Tasks.Where(task => !completedTaskIds.Contains(task.TaskId)))
                results.Add(new ExecutionResult(pending.TaskId, TaskState.Skipped, ErrorCode.StoppedByEarlierFailure.ToString(),
                    "Task was skipped after an earlier task stopped the run.", false, false));
        }

        var finalState = results.Any(result => result.State is TaskState.Failed or TaskState.NeedsManualReview or TaskState.UnsupportedPrerequisite or TaskState.UnavailableExtension)
            ? TaskState.Failed
            : results.Any(result => result.State == TaskState.Cancelled) ? TaskState.Cancelled
            : TaskState.Succeeded;
        return await PersistAsync(effectiveRunId, plan, results, finalState, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<RunStateSnapshot> ResumeAsync(
        string runId,
        ImmutablePlan plan,
        ExecutionPolicy policy,
        bool isElevated,
        CancellationToken cancellationToken = default,
        IReadOnlyList<ProfileAccount>? accounts = null,
        IProgress<ExecutionProgress>? progress = null)
    {
        if (_stateStore is null) throw new InvalidOperationException("Resume requires a run state store.");
        var snapshot = await _stateStore.LoadAsync(runId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null) throw new InvalidOperationException("Run state was not found.");
        if (snapshot.RecoveryRequired)
            throw new PlanValidationException("Run state is corrupt and requires recovery.", ErrorCode.StateCorrupt);
        if (!string.Equals(snapshot.PlanId, plan.PlanId, StringComparison.Ordinal) ||
            !string.Equals(snapshot.PlanSemanticHash, plan.SemanticHash, StringComparison.OrdinalIgnoreCase))
            throw new PlanValidationException("Run state does not match the immutable plan.", ErrorCode.PlanTampered);
        var completed = snapshot.Results
            .Where(result => result.State == TaskState.Succeeded ||
                             result.State == TaskState.NeedsReboot ||
                             result.State == TaskState.Skipped && result.Code.Equals(ErrorCode.None.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToDictionary(result => result.TaskId, StringComparer.OrdinalIgnoreCase);
        return await ExecuteAsync(plan, policy, isElevated, cancellationToken, runId, completed, accounts, progress).ConfigureAwait(false);
    }

    private async Task<ExecutionResult> ExecuteTaskAsync(
        PlannedTask task,
        ImmutablePlan plan,
        ExecutionPolicy policy,
        bool isElevated,
        string runId,
        CancellationToken cancellationToken,
        IReadOnlyList<ProfileAccount>? accounts,
        IProgress<ExecutionProgress>? progress,
        int completedCount)
    {
        var started = DateTimeOffset.UtcNow;
        for (var attempt = 0; attempt <= policy.MaxRetries; attempt++)
        {
            if (cancellationToken.IsCancellationRequested) return Cancelled(task.TaskId, started);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(policy.EffectiveTimeout);
            var context = new ApplyContext(runId, plan, timeout.Token, policy.EffectiveTimeout, isElevated,
                accounts is null ? ImmutableArray<ProfileAccount>.Empty : accounts.ToImmutableArray());
            ExecutionResult applied;
            try
            {
                progress?.Report(new ExecutionProgress(runId, task.TaskId, TaskState.Applying, completedCount, plan.Tasks.Length, "Applying task changes."));
                applied = await _executor.ApplyAsync(task, context).WaitAsync(policy.EffectiveTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(task.TaskId, started);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                applied = new ExecutionResult(task.TaskId, TaskState.Failed, ErrorCode.Timeout.ToString(),
                    "Task timed out.", false, task.RequiresReboot, true, FailureStage: "Apply");
            }
            catch (TimeoutException)
            {
                applied = new ExecutionResult(task.TaskId, TaskState.Failed, ErrorCode.Timeout.ToString(),
                    "Task timed out.", false, task.RequiresReboot, true, FailureStage: "Apply");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                applied = new ExecutionResult(task.TaskId, TaskState.Failed, ErrorCode.ProcessFailed.ToString(),
                    exception.Message, false, task.RequiresReboot, true, FailureStage: "Apply");
            }

            if (applied.State == TaskState.NeedsReboot)
            {
                applied = applied with
                {
                    State = TaskState.Succeeded,
                    RebootRequired = true,
                    VerificationStatus = VerificationStatus.PendingRestart,
                    Message = RestartPendingMessage(applied.Message)
                };
            }

            if (applied.State is TaskState.Cancelled or TaskState.NeedsManualReview or TaskState.UnsupportedPrerequisite or TaskState.UnavailableExtension)
                return Complete(applied, started);
            if (applied.State == TaskState.Succeeded)
            {
                var rebootRequired = applied.RebootRequired || task.RequiresReboot;
                if (rebootRequired)
                    return Complete(applied with
                    {
                        RebootRequired = true,
                        VerificationStatus = VerificationStatus.PendingRestart,
                        Message = RestartPendingMessage(applied.Message)
                    }, started);

                VerifyResult verification;
                try
                {
                    progress?.Report(new ExecutionProgress(runId, task.TaskId, TaskState.Verifying, completedCount, plan.Tasks.Length, "Verifying task result."));
                    verification = await _executor.VerifyAsync(task, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return new ExecutionResult(task.TaskId, TaskState.NeedsManualReview, ErrorCode.Cancelled.ToString(),
                        "Apply completed, but verification was cancelled. Manual review is required.", applied.Changed,
                        applied.RebootRequired, false, started, DateTimeOffset.UtcNow);
                }
                if (verification.Succeeded)
                {
                    var verificationStatus = verification.RebootRequired
                        ? VerificationStatus.PendingRestart
                        : VerificationStatus.Verified;
                    return new ExecutionResult(task.TaskId, TaskState.Succeeded, verification.Code.ToString(), verification.Message,
                        applied.Changed, verification.RebootRequired, false, started, DateTimeOffset.UtcNow,
                        VerificationStatus: verificationStatus);
                }
                applied = new ExecutionResult(task.TaskId, TaskState.Failed, verification.Code.ToString(), verification.Message,
                    applied.Changed, verification.RebootRequired || applied.RebootRequired, false,
                    FailureStage: "Verify", ProcessExitCode: applied.ProcessExitCode);
            }

            if (!applied.Retryable || !AllowsAutomaticRetry(task) || attempt == policy.MaxRetries)
                return Complete(applied with
                {
                    Retryable = applied.Retryable && AllowsAutomaticRetry(task) && attempt < policy.MaxRetries,
                    FailureStage = applied.State == TaskState.Failed ? applied.FailureStage ?? "Apply" : applied.FailureStage
                }, started);
            await Task.Delay(TimeSpan.FromTicks(policy.EffectiveRetryBaseDelay.Ticks * (1L << Math.Min(attempt, 6))), cancellationToken).ConfigureAwait(false);
        }

        return new ExecutionResult(task.TaskId, TaskState.Failed, ErrorCode.RetryExhausted.ToString(), "Retry limit exhausted.", false, task.RequiresReboot, false, started, DateTimeOffset.UtcNow);
    }

    private async Task<RunStateSnapshot> PersistAsync(string runId, ImmutablePlan plan, List<ExecutionResult> results, TaskState state, CancellationToken cancellationToken)
    {
        var sanitized = results.Select(result => result with { Message = SensitiveDataRedactor.Redact(result.Message) }).ToImmutableArray();
        var serializedPlan = JsonSerializer.Serialize(plan, PlanJsonOptions);
        var snapshot = new RunStateSnapshot(runId, plan.PlanId, plan.SemanticHash, state, sanitized, DateTimeOffset.UtcNow,
            SerializedPlan: serializedPlan);
        if (_stateStore is not null) await _stateStore.SaveAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
        return snapshot;
    }

    private static ExecutionResult Complete(ExecutionResult result, DateTimeOffset started) => result with
    {
        StartedAt = result.StartedAt ?? started,
        CompletedAt = result.CompletedAt ?? DateTimeOffset.UtcNow
    };

    private static ExecutionResult Cancelled(string taskId, DateTimeOffset? started = null) =>
        new(taskId, TaskState.Cancelled, ErrorCode.Cancelled.ToString(), "Task was cancelled before completion.", false, false, false, started, DateTimeOffset.UtcNow);

    private static string RestartPendingMessage(string message) =>
        string.IsNullOrWhiteSpace(message)
            ? "Restart is required before verification."
            : $"{message} Restart is required before verification.";

    private static bool AllowsAutomaticRetry(PlannedTask task) =>
        task.Risk != RiskLevel.High &&
        !task.TaskId.Equals("accounts-local", StringComparison.OrdinalIgnoreCase) &&
        !task.TaskId.Equals("system-dnscrypt-proxy", StringComparison.OrdinalIgnoreCase);
}
