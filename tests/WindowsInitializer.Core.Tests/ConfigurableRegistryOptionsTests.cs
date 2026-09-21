using WindowsInitializer.Core;
using WindowsInitializer.Contracts;
using WindowsInitializer.Platform.Windows;
using WindowsInitializer.PowerShell;
using Xunit;

namespace WindowsInitializer.Core.Tests;

public sealed class ConfigurableRegistryOptionsTests
{
    public static TheoryData<string, string, string, string> Options => new()
    {
        { "setting-taskbar-seconds", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSecondsInSystemClock" },
        { "setting-taskbar-end-task", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask" },
        { "setting-taskbar-widgets", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa" },
        { "setting-notification-banners", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled" },
        { "setting-lock-screen-notifications", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "LockScreenToastEnabled" },
        { "setting-explorer-this-pc", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "LaunchTo" },
        { "setting-explorer-full-path", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState", "FullPath" },
        { "setting-explorer-separate-process", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "SeparateProcess" },
        { "setting-recent-documents-tracking", "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackDocs" },
        { "setting-advertising-id", "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled" },
        { "setting-tailored-experiences", "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled" }
    };

    [Theory]
    [MemberData(nameof(Options))]
    public void ConfigurableOptionsHaveExactUserRegistryBackupTarget(string taskId, string hive, string path, string valueName)
    {
        var entry = Assert.Single(SystemRegistryBackupCatalog.GetEntries(taskId));
        Assert.Equal(taskId, entry.TaskId);
        Assert.Equal(hive, entry.Hive);
        Assert.Equal(path, entry.Path);
        Assert.Equal(valueName, entry.ValueName);
        Assert.True(entry.IsSupported);
        Assert.Equal(RegistryOperationKind.DWord, entry.Operation);
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void ConfigurableOptionsAreSystemSettingsAndBridgeAllowlisted(string taskId, string _, string __, string ___)
    {
        Assert.False(string.IsNullOrWhiteSpace(_));
        Assert.False(string.IsNullOrWhiteSpace(__));
        Assert.False(string.IsNullOrWhiteSpace(___));
        var descriptor = Assert.Single(new Catalog().GetTasks(), task => task.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(TaskKind.SystemSetting, descriptor.Kind);
        Assert.Equal(RollbackSupport.Exact, descriptor.Rollback);
        Assert.Equal(ExecutionBoundary.SignOut, descriptor.Boundary);
        Assert.False(descriptor.RequiresAdministrator);
        Assert.True(BridgeClient.SupportsTask(taskId));
    }

    [Theory]
    [MemberData(nameof(Options))]
    public void TemplatesAcceptAllExplicitStates(string taskId, string _, string __, string ___)
    {
        Assert.False(string.IsNullOrWhiteSpace(_));
        Assert.False(string.IsNullOrWhiteSpace(__));
        Assert.False(string.IsNullOrWhiteSpace(___));
        var catalog = new Catalog();
        foreach (var value in new[] { "enabled", "disabled", "default" })
        {
            var template = new PlanTemplateDocument(
                PlanTemplateJson.SchemaVersion, "registry-options", "Registry options", DateTimeOffset.UtcNow,
                [new PlanTemplateItem(taskId, value)], new ExecutionPolicy(), true, false);
            var restored = PlanTemplateJson.Deserialize(PlanTemplateJson.Serialize(template, catalog), catalog);
            Assert.Equal(value, Assert.Single(restored.Items).Value);
        }
    }

    [Fact]
    public void RegistryPresetsConflictWithTheNewExplicitOptions()
    {
        var catalog = new Catalog();
        var taskbarRelations = catalog.Find("setting-taskbar-seconds")!.Relations.GetValueOrDefault();
        var notificationRelations = catalog.Find("setting-notification-banners")!.Relations.GetValueOrDefault();
        var endTaskRelations = catalog.Find("setting-taskbar-end-task")!.Relations.GetValueOrDefault();
        Assert.Contains(taskbarRelations, relation =>
            relation.TargetTaskId == "registry-group-user-desktop-start" && relation.Kind == TaskRelationKind.ConflictsWith);
        Assert.Contains(notificationRelations, relation =>
            relation.TargetTaskId == "registry-group-user-notifications" && relation.Kind == TaskRelationKind.ConflictsWith);
        Assert.Contains(endTaskRelations, relation =>
            relation.TargetTaskId == "registry-group-user-developer" && relation.Kind == TaskRelationKind.ConflictsWith);

        foreach (var taskId in new[] { "setting-explorer-full-path", "setting-explorer-separate-process" })
            Assert.Contains(catalog.Find(taskId)!.Relations.GetValueOrDefault(), relation =>
                relation.TargetTaskId is "registry-group-user-file-explorer" or "registry-group-user-desktop-start" &&
                relation.Kind == TaskRelationKind.ConflictsWith);
        foreach (var taskId in new[] { "setting-recent-documents-tracking", "setting-advertising-id", "setting-tailored-experiences" })
            Assert.Contains(catalog.Find(taskId)!.Relations.GetValueOrDefault(), relation =>
                relation.TargetTaskId is "registry-group-user-privacy-personalization" or "registry-group-user-desktop-start" &&
                relation.Kind == TaskRelationKind.ConflictsWith);
    }

    [Fact]
    public void DefaultStateMeansRemovingTheOverrideInsteadOfWritingDisabledValue()
    {
        var setting = ConfigurableRegistrySettingCatalog.Find("setting-advertising-id")!;

        Assert.Null(setting.ResolveValue(ConfigurableRegistrySettingCatalog.DefaultState));
        Assert.Equal(0, setting.ResolveValue(ConfigurableRegistrySettingCatalog.DisabledState));
        Assert.NotEqual(setting.ResolveValue(ConfigurableRegistrySettingCatalog.DisabledState),
            setting.ResolveValue(ConfigurableRegistrySettingCatalog.DefaultState));
    }
}
