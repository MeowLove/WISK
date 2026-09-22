using Wisk.Contracts;
using Wisk.Core;

namespace Wisk.App;

public partial class TaskDetailsDialog : Wpf.Ui.Controls.FluentWindow
{
    public TaskDetailsDialog(TaskDescriptor task, string currentState, string configurationSummary, Func<string, string> taskName)
    {
        InitializeComponent();
        TitleText.Text = CatalogLocalization.TaskName(task.Id, task.DisplayName);
        SummaryText.Text = Localization.Get("taskSummary" + task.Kind);
        CurrentStateText.Text = currentState;
        TargetStateText.Text = Localization.Get("targetState" + task.Kind);
        ConfigurationText.Text = string.IsNullOrWhiteSpace(configurationSummary) ? Localization.Get("defaultConfiguration") : configurationSummary;
        if (task.Kind == TaskKind.RegistrySetting)
        {
            var entries = RegistryOptimizationCatalog.GetTaskEntries(task.Id);
            ConfigurationText.Text = string.Join("\n\n", entries.Select(entry =>
                $"{entry.Hive}\\{entry.Path}\n{entry.ValueName} [{entry.Operation}]\n{entry.RawValue}"));
        }
        PackageText.Text = task.PackageId ?? Localization.Get("notAvailable");
        PermissionText.Text = task.RequiresAdministrator ? Localization.Get("administratorRequired") : Localization.Get("standardUser");
        var dependencies = task.Dependencies.IsDefault ? System.Collections.Immutable.ImmutableArray<string>.Empty : task.Dependencies;
        var related = (task.Relations ?? System.Collections.Immutable.ImmutableArray<TaskRelation>.Empty).Where(relation => relation.Kind == TaskRelationKind.Requires)
            .Select(relation => relation.TargetTaskId);
        DependenciesText.Text = DisplayList(dependencies.Concat(related).Distinct(StringComparer.OrdinalIgnoreCase).Select(taskName));
        ResourcesText.Text = DisplayList(task.ResourceLocks ?? System.Collections.Immutable.ImmutableArray<string>.Empty);
        BoundaryText.Text = Localization.Get("boundary" + task.Boundary);
        RollbackText.Text = Localization.Get("rollback" + task.Rollback);
    }

    private static string DisplayList(IEnumerable<string> values)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return items.Length == 0 ? Localization.Get("none") : string.Join(", ", items);
    }

    private void Close_Click(object sender, System.Windows.RoutedEventArgs e) => Close();
}
