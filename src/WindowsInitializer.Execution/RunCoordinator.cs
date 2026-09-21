using WindowsInitializer.Contracts;

namespace WindowsInitializer.Execution;

public sealed class RunCoordinator(ExecutionEngine engine, string stateRoot)
{
    public async Task<RunStateSnapshot> ExecuteAsync(
        ImmutablePlan plan, ExecutionPolicy policy, bool isElevated, CancellationToken cancellationToken = default,
        IReadOnlyList<ProfileAccount>? accounts = null, IProgress<ExecutionProgress>? progress = null)
    {
        using var lease = RunLease.Acquire(stateRoot);
        return await engine.ExecuteAsync(plan, policy, isElevated, cancellationToken, accounts: accounts, progress: progress).ConfigureAwait(false);
    }

    public async Task<RunStateSnapshot> ResumeAsync(
        string runId, ImmutablePlan plan, ExecutionPolicy policy, bool isElevated, CancellationToken cancellationToken = default,
        IReadOnlyList<ProfileAccount>? accounts = null, IProgress<ExecutionProgress>? progress = null)
    {
        using var lease = RunLease.Acquire(stateRoot);
        return await engine.ResumeAsync(runId, plan, policy, isElevated, cancellationToken, accounts, progress).ConfigureAwait(false);
    }
}
