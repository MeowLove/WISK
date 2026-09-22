using System.Collections.Immutable;
using Wisk.Contracts;

namespace Wisk.Core;

/// <summary>Prevents a legacy preset from silently replacing an explicitly configured setting.</summary>
public static class SettingsPresetConflicts
{
    private static readonly (string TaskId, string Hive, string Path, string Name)[] AdditionalTargets =
    [
        ("setting-taskbar-end-task", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\DeveloperSettings", "TaskbarEndTask"),
        ("setting-windows-update-mode", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate"),
        ("setting-windows-update-mode", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions")
    ];

    public static ImmutableArray<TaskDescriptor> Apply(ImmutableArray<TaskDescriptor> tasks)
    {
        var conflicts = tasks.ToDictionary(task => task.Id, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var preset in tasks.Where(task => task.Kind == TaskKind.RegistrySetting))
        {
            if (preset.Id.StartsWith("registry-group-system-power-", StringComparison.OrdinalIgnoreCase))
                foreach (var taskId in new[] { "setting-power-plan", "setting-sleep-timeouts", "setting-hibernation", "setting-fast-startup" })
                    if (conflicts.ContainsKey(taskId))
                    {
                        conflicts[preset.Id].Add(taskId);
                        conflicts[taskId].Add(preset.Id);
                    }
            var entries = RegistryOptimizationCatalog.GetTaskEntries(preset.Id);
            var targets = ConfigurableRegistrySettingCatalog.All
                .Select(setting => (setting.TaskId, setting.Hive, setting.Path, Name: setting.ValueName))
                .Concat(AdditionalTargets);
            foreach (var target in targets.Where(target => conflicts.ContainsKey(target.TaskId)))
            {
                if (!entries.Any(entry => entry.Hive.Equals(target.Hive, StringComparison.OrdinalIgnoreCase) &&
                    entry.Path.Equals(target.Path, StringComparison.OrdinalIgnoreCase) &&
                    entry.ValueName.Equals(target.Name, StringComparison.OrdinalIgnoreCase))) continue;
                conflicts[preset.Id].Add(target.TaskId);
                conflicts[target.TaskId].Add(preset.Id);
            }
        }
        return tasks.Select(task => task with
        {
            Relations = (task.Relations ?? ImmutableArray<TaskRelation>.Empty)
                .AddRange(conflicts[task.Id].Order(StringComparer.OrdinalIgnoreCase)
                    .Select(id => new TaskRelation(id, TaskRelationKind.ConflictsWith, "overlappingSettings")))
        }).ToImmutableArray();
    }
}
