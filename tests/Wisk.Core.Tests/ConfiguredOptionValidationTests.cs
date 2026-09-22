using System.Collections.Immutable;
using Wisk.Contracts;
using Wisk.Core;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class ConfiguredOptionValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    public void NewOptionsCannotBypassConfiguration(string? value)
    {
        foreach (var id in new[] { "setting-taskbar-seconds", "setting-taskbar-end-task", "setting-taskbar-widgets",
            "setting-notification-banners", "setting-lock-screen-notifications", "setting-explorer-this-pc" })
        {
            var profile = new ProfileDocument("2.0", "test", [id], new ProfileTarget(), new ExecutionPolicy(), false, false,
                Parameters: value is null ? null : ImmutableDictionary<string, string>.Empty.Add(id, value));
            Assert.Equal(ErrorCode.InvalidProfile, ProfileDocumentValidator.Validate(profile)!.Code);
        }
    }

    [Fact]
    public void NewOptionsAcceptAnExplicitRestoreDefaultState()
    {
        foreach (var definition in ConfigurableRegistrySettingCatalog.All)
        {
            var profile = new ProfileDocument("2.0", "test", [definition.TaskId], new ProfileTarget(), new ExecutionPolicy(), false, false,
                Parameters: ImmutableDictionary<string, string>.Empty.Add(definition.TaskId, ConfigurableRegistrySettingCatalog.DefaultState));

            Assert.Null(ProfileDocumentValidator.Validate(profile));
        }
    }
}
