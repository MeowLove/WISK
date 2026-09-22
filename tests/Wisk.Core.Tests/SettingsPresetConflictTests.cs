using System.Collections.Immutable;
using Wisk.Contracts;
using Wisk.Core;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class SettingsPresetConflictTests
{
    [Fact]
    public void ExplicitFileExtensionChoiceCannotBeOverwrittenByDesktopPresetInEitherOrder()
    {
        var catalog = new Catalog();
        var setting = catalog.Find("setting-show-file-extensions")!;
        var preset = catalog.Find("registry-group-user-desktop-start")!;
        Assert.Contains(TaskRelationPlanner.Conflicts(setting, [preset.Id]), relation => relation.TargetTaskId == preset.Id);
        Assert.Contains(TaskRelationPlanner.Conflicts(preset, [setting.Id]), relation => relation.TargetTaskId == setting.Id);
        foreach (var ids in new[] { new[] { setting.Id, preset.Id }, new[] { preset.Id, setting.Id } })
        {
            var profile = new ProfileDocument("3.0", "conflict", [.. ids], new ProfileTarget(), new ExecutionPolicy(), true, true,
                Parameters: ImmutableDictionary<string, string>.Empty.Add(setting.Id, ConfigurableRegistrySettingCatalog.EnabledState));
            var error = Assert.Throws<PlanValidationException>(() => new PlanBuilder(catalog).Build(profile));
            Assert.Equal(ErrorCode.MutualExclusion, error.Code);
        }
    }

    [Fact]
    public void UnrelatedRegistryPresetAndSettingRemainComposable()
    {
        var catalog = new Catalog();
        Assert.Empty(TaskRelationPlanner.Conflicts(catalog.Find("setting-show-file-extensions")!, ["registry-group-user-notifications"]));
    }
}
