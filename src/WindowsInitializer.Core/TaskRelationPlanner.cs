using System.Collections.Immutable;
using WindowsInitializer.Contracts;

namespace WindowsInitializer.Core;

public static class TaskRelationPlanner
{
    public static ImmutableArray<TaskDescriptor> RequiredClosure(Catalog catalog, string taskId)
    {
        var result = ImmutableArray.CreateBuilder<TaskDescriptor>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string id)
        {
            if (!visited.Add(id)) return;
            var task = catalog.Find(id) ?? throw new PlanValidationException($"Unknown related task: {id}", ErrorCode.UnknownTask);
            foreach (var dependency in RequiredIds(task)) Visit(dependency);
            result.Add(task);
        }

        Visit(taskId);
        return result.ToImmutable();
    }

    public static ImmutableArray<TaskRelation> Conflicts(TaskDescriptor task, IEnumerable<string> selectedTaskIds)
    {
        var selected = selectedTaskIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Relations(task).Where(relation =>
                (relation.Kind is TaskRelationKind.ConflictsWith or TaskRelationKind.Supersedes) && selected.Contains(relation.TargetTaskId))
            .ToImmutableArray();
    }

    public static ImmutableArray<TaskDescriptor> Dependents(Catalog catalog, IEnumerable<string> selectedTaskIds, string dependencyId)
    {
        var selected = selectedTaskIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return catalog.GetTasks().Where(task => selected.Contains(task.Id) && RequiredIds(task).Contains(dependencyId, StringComparer.OrdinalIgnoreCase))
            .ToImmutableArray();
    }

    public static ImmutableArray<TaskRelation> Recommendations(TaskDescriptor task) =>
        Relations(task).Where(relation => relation.Kind == TaskRelationKind.Recommends).ToImmutableArray();

    private static IEnumerable<string> RequiredIds(TaskDescriptor task) =>
        (task.Dependencies.IsDefault ? ImmutableArray<string>.Empty : task.Dependencies)
        .Concat(Relations(task).Where(relation => relation.Kind == TaskRelationKind.Requires).Select(relation => relation.TargetTaskId))
        .Distinct(StringComparer.OrdinalIgnoreCase);

    private static ImmutableArray<TaskRelation> Relations(TaskDescriptor task) =>
        task.Relations ?? ImmutableArray<TaskRelation>.Empty;
}
