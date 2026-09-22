using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wisk.Contracts;

namespace Wisk.Core;

public sealed class PlanValidationException(string message, ErrorCode code) : Exception(message)
{
    public ErrorCode Code { get; } = code;
}

public sealed class PlanBuilder(Catalog catalog)
{
    public ImmutablePlan Build(ProfileDocument profile, CompatibilitySnapshot? compatibility = null, string? extensionPackageHash = null)
    {
        ValidateProfile(profile, compatibility);

        var selected = profile.Tasks
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => catalog.Find(id) ?? throw new PlanValidationException($"Unknown task: {id}", ErrorCode.UnknownTask))
            .ToArray();

        ValidateRiskAndExclusivity(selected, profile);
        ValidateRelations(selected);
        var unavailable = selected.FirstOrDefault(task => !task.IsAvailable);
        if (unavailable is not null)
            throw new PlanValidationException($"Task '{unavailable.Id}' is unavailable: {unavailable.UnavailableReason}", ErrorCode.UnavailableExtension);
        var ordered = TopologicalSort(selected);
        var planned = ordered
            .Select(task => new PlannedTask(
                task.Id,
                task.Version,
                task.Risk,
                task.Source,
                HardDependencies(task),
                task.RequiresReboot,
                SummarizeParameters(profile, task.Id),
                RequiresAdministrator: task.RequiresAdministrator,
                ExtensionPackageId: task.ExtensionPackageId,
                ExtensionPackageVersion: task.ExtensionPackageVersion,
                ExtensionManifestHash: task.ExtensionManifestHash,
                ExtensionExecutablePath: task.ExtensionExecutablePath,
                ExtensionExecutableHash: task.ExtensionExecutableHash,
                ExtensionProtocol: task.ExtensionProtocol,
                Relations: task.Relations ?? ImmutableArray<TaskRelation>.Empty,
                ResourceLocks: task.ResourceLocks ?? ImmutableArray<string>.Empty,
                Rollback: task.Rollback,
                Boundary: task.Boundary,
                Proxy: profile.Proxy,
                PackageScope: task.PackageScope))
            .ToImmutableArray();

        var maximumRisk = planned.Length == 0 ? RiskLevel.Standard : planned.Max(task => task.Risk);
        var plan = new ImmutablePlan(
            Guid.NewGuid().ToString("N"),
            profile.ProfileId,
            Catalog.Version,
            DateTimeOffset.UtcNow,
            planned,
            maximumRisk,
            planned.Any(task => task.RequiresReboot),
            string.Empty,
            extensionPackageHash ?? AggregateExtensionHashes(planned),
            profile.Policy);

        return plan.WithSemanticHash(ComputeSemanticHash(plan));
    }

    public static string ComputeSemanticHash(ImmutablePlan plan)
    {
        var canonical = new
        {
            plan.ProfileId,
            plan.CatalogVersion,
            Tasks = plan.Tasks.Select(task => new
            {
                task.TaskId,
                task.Version,
                Risk = task.Risk.ToString(),
                Source = task.Source.ToString(),
                Dependencies = task.Dependencies.Order(StringComparer.Ordinal).ToArray(),
                task.RequiresReboot,
                task.ParameterSummary,
                task.RequiresAdministrator,
                task.ExtensionPackageId,
                task.ExtensionPackageVersion,
                task.ExtensionManifestHash,
                task.ExtensionExecutablePath,
                task.ExtensionExecutableHash,
                task.ExtensionProtocol,
                task.Proxy,
                task.PackageScope,
                Relations = (task.Relations ?? ImmutableArray<TaskRelation>.Empty)
                    .OrderBy(relation => relation.Kind).ThenBy(relation => relation.TargetTaskId, StringComparer.Ordinal)
                    .Select(relation => new { relation.TargetTaskId, Kind = relation.Kind.ToString(), relation.ReasonKey }).ToArray(),
                ResourceLocks = (task.ResourceLocks ?? ImmutableArray<string>.Empty)
                    .Order(StringComparer.Ordinal).ToArray(),
                Rollback = task.Rollback.ToString(),
                Boundary = task.Boundary.ToString(),
            }).ToArray(),
            plan.MaximumRisk,
            plan.RequiresReboot,
            plan.ExtensionPackageHash,
            Policy = plan.Policy is null ? null : new
            {
                plan.Policy.StopOnError,
                plan.Policy.MaxRetries,
                plan.Policy.DefaultTimeout,
                plan.Policy.RetryBaseDelay
            }
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static void ValidatePlanIntegrity(ImmutablePlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.SemanticHash) ||
            !string.Equals(plan.SemanticHash, ComputeSemanticHash(plan), StringComparison.OrdinalIgnoreCase))
        {
            throw new PlanValidationException("Plan semantic hash does not match its content.", ErrorCode.PlanTampered);
        }
    }

    private void ValidateProfile(ProfileDocument profile, CompatibilitySnapshot? compatibility)
    {
        var issue = ProfileDocumentValidator.Validate(profile, catalog, compatibility);
        if (issue is not null) throw new PlanValidationException(issue.Message, issue.Code);
    }

    private static void ValidateRiskAndExclusivity(IEnumerable<TaskDescriptor> tasks, ProfileDocument profile)
    {
        var selected = tasks.ToArray();
        if (selected.Any(task => task.Risk == RiskLevel.Elevated) && !profile.AllowElevated)
            throw new PlanValidationException("Elevated tasks require explicit authorization.", ErrorCode.PolicyBlocked);
        if (selected.Any(task => task.Risk == RiskLevel.High) && !profile.AllowHighRisk)
            throw new PlanValidationException("High risk tasks require explicit authorization.", ErrorCode.PolicyBlocked);

        var conflict = selected.Where(task => task.ExclusiveGroup is not null)
            .GroupBy(task => task.ExclusiveGroup!, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (conflict is not null)
            throw new PlanValidationException($"Tasks in exclusive group '{conflict.Key}' cannot be selected together.", ErrorCode.MutualExclusion);
    }

    private static void ValidateRelations(IReadOnlyCollection<TaskDescriptor> tasks)
    {
        var selected = tasks.Select(task => task.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var task in tasks)
        {
            foreach (var relation in task.Relations ?? ImmutableArray<TaskRelation>.Empty)
            {
                if (relation.Kind == TaskRelationKind.Requires && !selected.Contains(relation.TargetTaskId))
                    throw new PlanValidationException($"Task '{task.Id}' requires '{relation.TargetTaskId}'.", ErrorCode.InvalidProfile);
                if (relation.Kind is TaskRelationKind.ConflictsWith or TaskRelationKind.Supersedes && selected.Contains(relation.TargetTaskId))
                    throw new PlanValidationException($"Task '{task.Id}' conflicts with '{relation.TargetTaskId}'.", ErrorCode.MutualExclusion);
            }
        }
    }

    private TaskDescriptor[] TopologicalSort(IReadOnlyCollection<TaskDescriptor> selected)
    {
        var all = selected.ToDictionary(task => task.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var task in selected)
        {
            foreach (var dependency in HardDependencies(task))
            {
                if (!all.ContainsKey(dependency))
                    throw new PlanValidationException($"Task '{task.Id}' requires '{dependency}'.", ErrorCode.InvalidProfile);
            }
        }

        var result = new List<TaskDescriptor>(selected.Count);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(TaskDescriptor task)
        {
            if (visited.Contains(task.Id)) return;
            if (!visiting.Add(task.Id))
                throw new PlanValidationException($"Dependency cycle detected at '{task.Id}'.", ErrorCode.DependencyCycle);
            foreach (var dependency in OrderingDependencies(task, all))
                Visit(all[dependency]);
            visiting.Remove(task.Id);
            visited.Add(task.Id);
            result.Add(task);
        }

        foreach (var task in selected) Visit(task);
        return result.ToArray();
    }

    private static ImmutableArray<string> HardDependencies(TaskDescriptor task) =>
        (task.Dependencies.IsDefault ? ImmutableArray<string>.Empty : task.Dependencies)
        .Concat((task.Relations ?? ImmutableArray<TaskRelation>.Empty)
            .Where(relation => relation.Kind == TaskRelationKind.Requires).Select(relation => relation.TargetTaskId))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray();

    private static IEnumerable<string> OrderingDependencies(TaskDescriptor task, IReadOnlyDictionary<string, TaskDescriptor> selected) =>
        HardDependencies(task).Concat((task.Relations ?? ImmutableArray<TaskRelation>.Empty)
            .Where(relation => relation.Kind == TaskRelationKind.OrderAfter && selected.ContainsKey(relation.TargetTaskId))
            .Select(relation => relation.TargetTaskId)).Distinct(StringComparer.OrdinalIgnoreCase);

    private static string SummarizeParameters(ProfileDocument profile, string taskId)
    {
        if (taskId.Equals("accounts-local", StringComparison.OrdinalIgnoreCase) && profile.Accounts is { IsDefaultOrEmpty: false })
            return string.Join(",", profile.Accounts.Value.Select(account => account.Name));
        if (profile.Parameters is null || !profile.Parameters.TryGetValue(taskId, out var value)) return string.Empty;
        if (taskId.Equals(FontSupplementCatalog.TaskId, StringComparison.OrdinalIgnoreCase) &&
            FontSupplementCatalog.TryNormalizeSelection(value, out var normalizedSelection))
            value = normalizedSelection;
        if (taskId.Equals(LanguagePreferenceCatalog.TaskId, StringComparison.OrdinalIgnoreCase) &&
            LanguagePreferenceCatalog.TryNormalize(value, out var normalizedLanguagePreference))
            value = normalizedLanguagePreference;
        return IsSecretKey(taskId) || IsSecretValue(value) ? "[REDACTED]" : value.Length > 256 ? value[..256] : value;
    }

    private static bool IsSecretKey(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("private", StringComparison.OrdinalIgnoreCase);

    private static bool IsSecretValue(string value) =>
        value.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("secret", StringComparison.OrdinalIgnoreCase);

    private static string? AggregateExtensionHashes(IEnumerable<PlannedTask> tasks)
    {
        var hashes = tasks.Select(task => task.ExtensionManifestHash).Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return hashes.Length == 0 ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", hashes))));
    }
}
