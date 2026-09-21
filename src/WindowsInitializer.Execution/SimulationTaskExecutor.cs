using WindowsInitializer.Contracts;

namespace WindowsInitializer.Execution;

public enum SimulationOutcome { Success, AlreadyComplete, ApplyFailure, VerifyFailure, Timeout, NeedsReboot }

public sealed class SimulationTaskExecutor(
    IReadOnlyDictionary<string, SimulationOutcome>? outcomes = null) : IInitializerTaskExecutor
{
    private readonly IReadOnlyDictionary<string, SimulationOutcome> _outcomes =
        outcomes ?? new Dictionary<string, SimulationOutcome>(StringComparer.OrdinalIgnoreCase);

    private SimulationOutcome Outcome(string taskId) =>
        _outcomes.TryGetValue(taskId, out var outcome) ? outcome : SimulationOutcome.Success;

    public Task<TaskCheckResult> CheckAsync(PlannedTask task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var outcome = Outcome(task.TaskId);
        return Task.FromResult(outcome == SimulationOutcome.AlreadyComplete
            ? new TaskCheckResult(task.TaskId, TaskState.Skipped, ErrorCode.None, "Simulation: task is already complete.", true)
            : new TaskCheckResult(task.TaskId, TaskState.Ready, ErrorCode.None, "Simulation: prerequisites passed."));
    }

    public Task<ExecutionResult> ApplyAsync(PlannedTask task, ApplyContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var result = Outcome(task.TaskId) switch
        {
            SimulationOutcome.ApplyFailure => new ExecutionResult(task.TaskId, TaskState.Failed,
                ErrorCode.ProcessFailed.ToString(), "Simulation: apply failed.", false, false, true,
                FailureStage: "Apply", ProcessExitCode: 1),
            SimulationOutcome.Timeout => new ExecutionResult(task.TaskId, TaskState.Failed,
                ErrorCode.Timeout.ToString(), "Simulation: apply timed out.", false, false, true,
                FailureStage: "Apply"),
            SimulationOutcome.NeedsReboot => new ExecutionResult(task.TaskId, TaskState.Succeeded,
                ErrorCode.None.ToString(), "Simulation: apply succeeded and requires restart.", true, true),
            _ => new ExecutionResult(task.TaskId, TaskState.Succeeded, ErrorCode.None.ToString(),
                "Simulation: apply succeeded.", true, task.RequiresReboot)
        };
        return Task.FromResult(result);
    }

    public Task<VerifyResult> VerifyAsync(PlannedTask task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Outcome(task.TaskId) == SimulationOutcome.VerifyFailure
            ? new VerifyResult(task.TaskId, false, ErrorCode.VerificationFailed, "Simulation: verification failed.")
            : new VerifyResult(task.TaskId, true, ErrorCode.None, "Simulation: verification succeeded.",
                Outcome(task.TaskId) == SimulationOutcome.NeedsReboot || task.RequiresReboot));
    }
}
