using System.Text.RegularExpressions;
using Wisk.Contracts;
using Xunit;

namespace Wisk.Core.Tests;

public sealed partial class LocalizationTests
{
    [Fact]
    public void DebugCatalogValidationCoversEverySupportedLanguage()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Wisk.App", "Localization.cs"));

        Assert.Contains("foreach (var language in Languages.Where(language => language.Code != \"en\"))", source);
        Assert.Contains("throw new InvalidOperationException", source);
    }

    [Fact]
    public void SimplifiedChineseCoversThePrimaryWorkspace()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Wisk.App", "Localization.cs"));

        Assert.Contains("[\"home\"]=\"首页\"", source);
        Assert.Contains("[\"planExecution\"]=\"计划与执行\"", source);
        Assert.Contains("[\"apply\"]=\"执行计划\"", source);
        Assert.Contains("[\"authorizeHigh\"]=\"授权已选的高风险任务\"", source);
    }

    [Fact]
    public void EveryConfigurableRegistrySettingHasLocalizedDisplayText()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Wisk.App", "Localization.cs"));

        Assert.All(ConfigurableRegistrySettingCatalog.All, setting =>
            Assert.Contains($"Add(\"{setting.TaskId}\"", source, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryDeclarativeLocalizationKeyExists()
    {
        var root = RepositoryRoot();
        var appDirectory = Path.Combine(root, "src", "Wisk.App");
        var localizationSource = File.ReadAllText(Path.Combine(appDirectory, "Localization.cs"));
        var definedKeys = DefinedKeyPattern().Matches(localizationSource)
            .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keys = Directory.EnumerateFiles(appDirectory, "*.xaml")
            .SelectMany(path => LocalizationKeyPattern().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(keys);
        Assert.All(keys, key => Assert.Contains(key, definedKeys));
    }

    [Fact]
    public void DuplicateEnglishLabelsUseAStableGroupedReverseIndex()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Wisk.App", "Localization.cs"));

        Assert.Contains(".GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)", source);
        Assert.DoesNotContain("Text[\"en\"].ToDictionary(pair => pair.Value", source);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var marker = Path.Combine(directory.FullName, "src", "Wisk.App", "Localization.cs");
            if (File.Exists(marker)) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("WISK repository root could not be located from the test output directory.");
    }

    [GeneratedRegex(@"\{local:Loc\s+([A-Za-z0-9]+)\}")]
    private static partial Regex LocalizationKeyPattern();

    [GeneratedRegex("Add\\(\\\"([^\\\"]+)\\\"|\\[\\\"([^\\\"]+)\\\"\\]\\s*=")]
    private static partial Regex DefinedKeyPattern();
}
