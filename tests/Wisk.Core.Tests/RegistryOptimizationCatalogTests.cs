using Wisk.Contracts;
using Wisk.Core;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class RegistryOptimizationCatalogTests
{
    [Fact]
    public void EmbeddedCatalogCoversEverySourceValueAndDeletedKey()
    {
        var entries = RegistryOptimizationCatalog.GetEntries();

        Assert.Equal(79, entries.Count(entry => entry.SourceFile == "CXT_User.reg" && entry.Operation != RegistryOperationKind.DeleteKey));
        Assert.Equal(224, entries.Count(entry => entry.SourceFile == "CXT_System.reg" && entry.Operation != RegistryOperationKind.DeleteKey));
        Assert.Equal(2, entries.Count(entry => entry.Operation == RegistryOperationKind.DeleteKey));
        Assert.Equal(entries.Count, entries.Select(entry => entry.TaskId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ParserRecognizesSupportedKindsAndDeletion()
    {
        const string content = """
            Windows Registry Editor Version 5.00
            [HKEY_CURRENT_USER\Software\Example]
            "Text"="value"
            "Number"=dword:00000001
            "Bytes"=hex:01,02
            "Expanded"=hex(2):25,00
            "Old"=-
            [-HKEY_CURRENT_USER\Software\Obsolete]
            """;

        var entries = RegistryOptimizationCatalog.Parse("sample.reg", content);

        Assert.Collection(entries,
            entry => Assert.Equal(RegistryOperationKind.String, entry.Operation),
            entry => Assert.Equal(RegistryOperationKind.DWord, entry.Operation),
            entry => Assert.Equal(RegistryOperationKind.Binary, entry.Operation),
            entry => Assert.Equal(RegistryOperationKind.ExpandString, entry.Operation),
            entry => Assert.Equal(RegistryOperationKind.DeleteValue, entry.Operation),
            entry => Assert.Equal(RegistryOperationKind.DeleteKey, entry.Operation));
    }

    [Fact]
    public void SecurityAndMachineSettingsReceiveStrongerRisk()
    {
        var entries = RegistryOptimizationCatalog.GetEntries();

        Assert.Contains(entries, entry => entry.Hive == "HKCU" && entry.Risk == RiskLevel.Standard);
        Assert.Contains(entries, entry => entry.Hive == "HKLM" && entry.Risk == RiskLevel.Elevated);
        Assert.Contains(entries, entry => entry.Risk == RiskLevel.High && entry.Path.Contains("DeviceGuard", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompoundContextMenuFeaturesAreExposedAsSingleTasks()
    {
        var groups = RegistryOptimizationCatalog.GetGroups();
        var hash = Assert.Single(groups, group => group.TaskId == "registry-group-hash-context-menus");
        var ownership = Assert.Single(groups, group => group.TaskId == "registry-group-take-ownership-menus");

        Assert.Equal(74, hash.Members.Length);
        Assert.Equal(16, ownership.Members.Length);
        Assert.All(hash.Members, member => Assert.True(member.IsSupported));
        Assert.All(ownership.Members, member => Assert.True(member.IsSupported));

        var tasks = RegistryOptimizationCatalog.CreateTaskDescriptors();
        Assert.Contains(tasks, task => task.Id == hash.TaskId);
        Assert.Contains(tasks, task => task.Id == ownership.TaskId);
        Assert.DoesNotContain(tasks, task => hash.Members.Any(member => member.TaskId == task.Id));
        Assert.DoesNotContain(tasks, task => ownership.Members.Any(member => member.TaskId == task.Id));
    }

    [Fact]
    public void EveryAtomicRegistryOperationIsRepresentedExactlyOnce()
    {
        var entries = RegistryOptimizationCatalog.GetEntries();
        var tasks = RegistryOptimizationCatalog.CreateTaskDescriptors();
        var represented = tasks.SelectMany(task => RegistryOptimizationCatalog.GetTaskEntries(task.Id)).ToArray();

        Assert.Equal(entries.Count, represented.Length);
        Assert.Equal(entries.Count, represented.Select(entry => entry.TaskId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(entries.Select(entry => entry.TaskId).Order(StringComparer.OrdinalIgnoreCase),
            represented.Select(entry => entry.TaskId).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void RegistryCatalogUsesFeatureLevelCardsInsteadOfValueLevelCards()
    {
        var groups = RegistryOptimizationCatalog.GetGroups();
        var tasks = RegistryOptimizationCatalog.CreateTaskDescriptors();

        Assert.Equal(35, groups.Count);
        Assert.True(tasks.Length < 70, $"Expected fewer than 70 feature cards, found {tasks.Length}.");
        Assert.All(groups, group => Assert.True(group.Members.Length >= 2));
        Assert.All(groups, group => Assert.True(group.IsSupported));
    }
}
