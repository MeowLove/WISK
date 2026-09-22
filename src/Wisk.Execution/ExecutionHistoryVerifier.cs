using System.Collections.Immutable;
using System.Text.Json;
using Wisk.Contracts;

namespace Wisk.Execution;

public sealed record ExecutionHistoryVerification(
    VerificationStatus Status,
    ImmutableDictionary<string, VerificationStatus> TaskStatuses)
{
    public static ExecutionHistoryVerification NotRequired { get; } =
        new(VerificationStatus.NotRequired, ImmutableDictionary<string, VerificationStatus>.Empty);
}

/// <summary>
/// Rechecks only the read-only verification boundary for completed tasks that reported a restart.
/// The persisted Apply audit record is never changed.
/// </summary>
public sealed class ExecutionHistoryVerifier(IInitializerTaskExecutor executor)
{
    private static readonly JsonSerializerOptions PlanJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ExecutionHistoryVerification> VerifyAsync(
        RunStateSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var candidates = snapshot.Results
            .Where(result => (result.State is TaskState.Succeeded or TaskState.NeedsReboot) && result.RebootRequired)
            .ToArray();
        if (candidates.Length == 0) return ExecutionHistoryVerification.NotRequired;

        var statuses = ImmutableDictionary.CreateBuilder<string, VerificationStatus>(StringComparer.OrdinalIgnoreCase);
        var pending = candidates.Where(result => result.VerificationStatus != VerificationStatus.Verified).ToArray();
        foreach (var verified in candidates.Where(result => result.VerificationStatus == VerificationStatus.Verified))
            statuses[verified.TaskId] = VerificationStatus.Verified;

        ImmutablePlan? plan = null;
        if (pending.Length > 0 && !string.IsNullOrWhiteSpace(snapshot.SerializedPlan))
        {
            try { plan = JsonSerializer.Deserialize<ImmutablePlan>(snapshot.SerializedPlan, PlanJsonOptions); }
            catch (JsonException) { }
            catch (NotSupportedException) { }
        }

        if (pending.Length > 0 && plan is null)
        {
            foreach (var result in pending) statuses[result.TaskId] = VerificationStatus.Unknown;
        }
        else if (pending.Length > 0)
        {
            var plannedTasks = plan!.Tasks.ToDictionary(task => task.TaskId, StringComparer.OrdinalIgnoreCase);
            foreach (var result in pending)
            {
                if (!plannedTasks.TryGetValue(result.TaskId, out var task))
                {
                    statuses[result.TaskId] = VerificationStatus.Unknown;
                    continue;
                }

                try
                {
                    var verification = await executor.VerifyAsync(task, cancellationToken).ConfigureAwait(false);
                    statuses[result.TaskId] = ResolveStatus(verification);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
                {
                    statuses[result.TaskId] = VerificationStatus.Unknown;
                }
            }
        }

        var status = statuses.Values.Any(value => value == VerificationStatus.Unknown)
            ? VerificationStatus.Unknown
            : statuses.Values.Any(value => value == VerificationStatus.Failed)
                ? VerificationStatus.Failed
                : statuses.Values.Any(value => value == VerificationStatus.PendingRestart)
                    ? VerificationStatus.PendingRestart
                    : VerificationStatus.Verified;
        return new ExecutionHistoryVerification(status, statuses.ToImmutable());
    }

    private static VerificationStatus ResolveStatus(VerifyResult verification)
    {
        if (verification.VerificationStatus != VerificationStatus.NotRequired)
            return verification.VerificationStatus;

        if (verification.Code is ErrorCode.ManualReviewRequired or ErrorCode.UnsupportedOperatingSystem or
            ErrorCode.MissingPowerShell or ErrorCode.MissingWinGet or ErrorCode.NetworkUnavailable or
            ErrorCode.NotAdministrator or ErrorCode.AccessDenied or ErrorCode.Timeout or
            ErrorCode.Cancelled or ErrorCode.ProcessFailed)
            return VerificationStatus.Unknown;

        if (verification.Succeeded)
            return verification.RebootRequired ? VerificationStatus.PendingRestart : VerificationStatus.Verified;

        return VerificationStatus.Failed;
    }
}
