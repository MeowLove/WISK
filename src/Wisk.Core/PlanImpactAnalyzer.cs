using System.Collections.Immutable;
using Wisk.Contracts;

namespace Wisk.Core;

public sealed record PlanImpactSummary(
    int TaskCount,
    int AdministratorCount,
    int HighRiskCount,
    int InternetCount,
    int RebootCount,
    int SignOutCount,
    int DependencyCount,
    int RecommendationCount,
    int ExactRollbackCount,
    int ConditionalRollbackCount,
    int ManualRollbackCount,
    ImmutableArray<string> ResourceLocks,
    bool IncludesRestorePoint,
    bool RestorePointRecommended);

public static class PlanImpactAnalyzer
{
    public static PlanImpactSummary Analyze(IEnumerable<TaskDescriptor> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var selected = tasks.ToArray();
        var relations = selected.SelectMany(task => task.Relations ?? ImmutableArray<TaskRelation>.Empty).ToArray();
        var includesRestorePoint = selected.Any(task => task.Id.Equals("safety-restore-point", StringComparison.OrdinalIgnoreCase));
        var hasRecoverableSystemChange = selected.Any(task =>
            !task.Id.Equals("safety-restore-point", StringComparison.OrdinalIgnoreCase) &&
            task.Kind is TaskKind.RegistrySetting or TaskKind.SystemSetting or TaskKind.Capability);

        return new PlanImpactSummary(
            selected.Length,
            selected.Count(task => task.RequiresAdministrator),
            selected.Count(task => task.Risk == RiskLevel.High),
            selected.Count(task => task.RequiresInternet),
            selected.Count(task => task.RequiresReboot || task.Boundary == ExecutionBoundary.Reboot),
            selected.Count(task => task.Boundary == ExecutionBoundary.SignOut),
            selected.Sum(task => (task.Dependencies.IsDefault ? [] : task.Dependencies)
                .Concat((task.Relations ?? ImmutableArray<TaskRelation>.Empty)
                    .Where(relation => relation.Kind == TaskRelationKind.Requires)
                    .Select(relation => relation.TargetTaskId))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count()),
            relations.Count(relation => relation.Kind == TaskRelationKind.Recommends),
            selected.Count(task => task.Rollback == RollbackSupport.Exact),
            selected.Count(task => task.Rollback == RollbackSupport.Conditional),
            selected.Count(task => task.Rollback == RollbackSupport.Manual),
            selected.SelectMany(task => task.ResourceLocks ?? ImmutableArray<string>.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            includesRestorePoint,
            hasRecoverableSystemChange && !includesRestorePoint);
    }
}
