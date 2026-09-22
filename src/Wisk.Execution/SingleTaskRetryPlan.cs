using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wisk.Contracts;
using Wisk.Core;

namespace Wisk.Execution;

public static class SingleTaskRetryPlan
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ImmutablePlan Create(RunStateSnapshot snapshot, string taskId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        if (snapshot.RecoveryRequired || string.IsNullOrWhiteSpace(snapshot.SerializedPlan))
            throw new PlanValidationException("The historical run does not contain a recoverable plan.", ErrorCode.StateCorrupt);

        var result = snapshot.Results.SingleOrDefault(item => item.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase))
            ?? throw new PlanValidationException("The selected task is not present in the historical run.", ErrorCode.UnknownTask);
        if (result.State != TaskState.Failed)
            throw new PlanValidationException("Only failed tasks can be retried individually.", ErrorCode.InvalidProfile);
        if (taskId.Equals("accounts-local", StringComparison.OrdinalIgnoreCase))
            throw new PlanValidationException("Local account credentials are not persisted. Configure the task again.", ErrorCode.InvalidProfile);

        var source = JsonSerializer.Deserialize<ImmutablePlan>(snapshot.SerializedPlan, JsonOptions)
            ?? throw new PlanValidationException("The historical plan is invalid.", ErrorCode.StateCorrupt);
        PlanBuilder.ValidatePlanIntegrity(source);
        var task = source.Tasks.SingleOrDefault(item => item.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase))
            ?? throw new PlanValidationException("The selected task is not present in the historical plan.", ErrorCode.UnknownTask);

        var retry = new ImmutablePlan(
            Guid.NewGuid().ToString("N"),
            source.ProfileId + "-retry",
            source.CatalogVersion,
            DateTimeOffset.UtcNow,
            ImmutableArray.Create(task),
            task.Risk,
            task.RequiresReboot,
            string.Empty,
            AggregateExtensionHash(task.ExtensionManifestHash),
            source.Policy);
        return retry.WithSemanticHash(PlanBuilder.ComputeSemanticHash(retry));
    }

    private static string? AggregateExtensionHash(string? manifestHash) => string.IsNullOrWhiteSpace(manifestHash)
        ? null
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifestHash)));
}
