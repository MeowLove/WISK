using System.Collections.Immutable;
using System.Text.Json;
using Wisk.Contracts;
using Wisk.Core;
using Wisk.PowerShell;

namespace Wisk.Platform.Windows;

public sealed class WindowsTaskExecutor(IBridgeInvoker bridge, Catalog? catalog = null) : Wisk.Execution.IInitializerTaskExecutor
{
    private readonly Catalog? _catalog = catalog;

    public async Task<TaskCheckResult> CheckAsync(PlannedTask task, CancellationToken cancellationToken)
    {
        try
        {
            var response = await InvokeAsync(task, "Check", TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            return new TaskCheckResult(task.TaskId, response.Status, response.Code, response.Message,
                AlreadyComplete: response.Status == TaskState.Skipped, CanApply: response.Status is TaskState.Ready or TaskState.Succeeded,
                RequiresReboot: response.RebootRequired, Retryable: false);
        }
        catch (BridgeFailureException exception)
        {
            var state = exception.Code == ErrorCode.Cancelled ? TaskState.Cancelled :
                exception.Code == ErrorCode.MissingPowerShell ? TaskState.UnsupportedPrerequisite : TaskState.Failed;
            return new TaskCheckResult(task.TaskId, state, exception.Code, exception.Message, CanApply: false,
                Retryable: exception.Code == ErrorCode.Timeout);
        }
    }

    public async Task<ExecutionResult> ApplyAsync(PlannedTask task, ApplyContext context)
    {
        try
        {
            var response = await InvokeAsync(task, "Apply", context.Timeout, context.CancellationToken, context).ConfigureAwait(false);
            return new ExecutionResult(task.TaskId, response.Status, response.Code.ToString(), response.Message,
                response.Changed, response.RebootRequired);
        }
        catch (BridgeFailureException exception)
        {
            return new ExecutionResult(task.TaskId, exception.Code == ErrorCode.Cancelled ? TaskState.Cancelled : TaskState.Failed,
                exception.Code.ToString(), exception.Message, false, task.RequiresReboot, Retryable: exception.Code == ErrorCode.Timeout);
        }
    }

    public async Task<VerifyResult> VerifyAsync(PlannedTask task, CancellationToken cancellationToken)
    {
        try
        {
            var response = await InvokeAsync(task, "Verify", TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            return new VerifyResult(task.TaskId, response.Status is TaskState.Succeeded or TaskState.Skipped,
                response.Code, response.Message, response.RebootRequired);
        }
        catch (BridgeFailureException exception)
        {
            return new VerifyResult(task.TaskId, false, exception.Code, exception.Message, task.RequiresReboot);
        }
    }

    private Task<BridgeResponse> InvokeAsync(PlannedTask task, string operation, TimeSpan timeout, CancellationToken cancellationToken, ApplyContext? context = null)
    {
        var parameters = ImmutableDictionary<string, string>.Empty;
        if ((_catalog?.Find(task.TaskId) is { Kind: TaskKind.SystemSetting } || task.TaskId.Equals(FontSupplementCatalog.TaskId, StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(task.ParameterSummary))
            parameters = parameters.Add("value", task.ParameterSummary);
        if (task.TaskId.Equals("accounts-local", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(task.ParameterSummary))
            parameters = parameters.Add("accountNames", task.ParameterSummary);
        if (task.TaskId.Equals("accounts-local", StringComparison.OrdinalIgnoreCase) && context?.Accounts is { IsDefaultOrEmpty: false })
            parameters = parameters.Add("accountsJson", JsonSerializer.Serialize(context.Accounts));
        var request = new BridgeRequest(BridgeClient.ProtocolVersion, Guid.NewGuid().ToString("N"), task.TaskId, operation, parameters);
        return bridge.InvokeAsync(request, timeout, cancellationToken);
    }
}
