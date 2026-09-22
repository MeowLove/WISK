using Wisk.Contracts;

namespace Wisk.App;

internal enum TaskConfigurationMode
{
    None,
    SingleValue,
    Collection
}

internal sealed record TaskConfigurationDefinition(
    TaskConfigurationMode Mode,
    bool ConfigureBeforeAdd,
    bool AllowsMultipleEntries = false);

internal static class TaskConfigurationCatalog
{
    public static bool IsRegistryBoolean(string taskId) =>
        ConfigurableRegistrySettingCatalog.Find(taskId) is not null;
    private static readonly IReadOnlyDictionary<string, TaskConfigurationDefinition> Definitions =
        new Dictionary<string, TaskConfigurationDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["computer-name"] = new(TaskConfigurationMode.SingleValue, true),
            ["device-setup-region"] = new(TaskConfigurationMode.SingleValue, true),
            ["language-ui-preference"] = new(TaskConfigurationMode.SingleValue, true),
            [FontSupplementCatalog.TaskId] = new(TaskConfigurationMode.SingleValue, true),
            ["setting-windows-update-mode"] = new(TaskConfigurationMode.SingleValue, true),
            ["setting-power-plan"] = new(TaskConfigurationMode.SingleValue, true),
            ["setting-sleep-timeouts"] = new(TaskConfigurationMode.SingleValue, true),
            ["accounts-local"] = new(TaskConfigurationMode.Collection, true, AllowsMultipleEntries: true)
        };

    public static TaskConfigurationDefinition Get(string taskId) =>
        Definitions.GetValueOrDefault(taskId) ?? (IsRegistryBoolean(taskId)
            ? new(TaskConfigurationMode.SingleValue, true) : new(TaskConfigurationMode.None, false));

    public static bool RequiresInput(string taskId) => Get(taskId).Mode != TaskConfigurationMode.None;
}
