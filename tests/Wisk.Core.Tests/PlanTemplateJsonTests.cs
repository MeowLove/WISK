using System.Collections.Immutable;
using System.Text.Json;
using Wisk.Contracts;
using Wisk.Core;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class PlanTemplateJsonTests
{
    [Fact]
    public void RoundTripsOrderedItemsAndCollectionTasks()
    {
        var template = Template([
            new PlanTemplateItem("computer-name", "WORKSTATION-01"),
            new PlanTemplateItem("accounts-local"),
            new PlanTemplateItem("accounts-local"),
            new PlanTemplateItem("runtime-dotnet-8")]);

        var json = PlanTemplateJson.Serialize(template, new Catalog());
        var restored = PlanTemplateJson.Deserialize(json, new Catalog());

        Assert.Equal(template.Items.ToArray(), restored.Items.ToArray());
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"accounts\":", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsDuplicateNonCollectionTasks()
    {
        Assert.Throws<ProfileValidationException>(() =>
            PlanTemplateJson.Serialize(Template([
                new PlanTemplateItem("runtime-dotnet-8"),
                new PlanTemplateItem("runtime-dotnet-8")]), new Catalog()));
    }

    [Fact]
    public void RoundTripsExecutionPolicy()
    {
        var policy = new ExecutionPolicy(StopOnError: false, MaxRetries: 2,
            DefaultTimeout: TimeSpan.FromMinutes(10), RetryBaseDelay: TimeSpan.FromSeconds(3));
        var template = Template([new PlanTemplateItem("runtime-dotnet-8")]) with { Policy = policy };

        var restored = PlanTemplateJson.Deserialize(PlanTemplateJson.Serialize(template, new Catalog()), new Catalog());

        Assert.Equal(policy, restored.Policy);
    }

    [Fact]
    public void TemplateSerializationAndImportNeverCarryRiskAuthorization()
    {
        var catalog = new Catalog();
        var template = Template([new PlanTemplateItem("runtime-dotnet-8")]) with
        {
            AllowElevated = true,
            AllowHighRisk = true
        };

        var exported = PlanTemplateJson.Serialize(template, catalog);
        using var exportedJson = JsonDocument.Parse(exported);
        var importedUntrusted = PlanTemplateJson.Deserialize(
            JsonSerializer.Serialize(template, new JsonSerializerOptions(JsonSerializerDefaults.Web)), catalog);

        Assert.False(exportedJson.RootElement.GetProperty("allowElevated").GetBoolean());
        Assert.False(exportedJson.RootElement.GetProperty("allowHighRisk").GetBoolean());
        Assert.False(importedUntrusted.AllowElevated);
        Assert.False(importedUntrusted.AllowHighRisk);
    }

    [Theory]
    [InlineData("app-vscode", "upgrade")]
    [InlineData("app-vscode", "install")]
    [InlineData("setting-fast-startup", "disabled")]
    [InlineData("language-ui-preference", "language=en-US;currentUser=true;system=false;welcome=false")]
    [InlineData("font-supplements-cjk-indic-europe", "japanese,korean")]
    public void RetainsExplicitActionThroughTemplateAndPlan(string taskId, string value)
    {
        var catalog = new Catalog();
        var restored = PlanTemplateJson.Deserialize(PlanTemplateJson.Serialize(Template([new(taskId, value)]), catalog), catalog);
        Assert.Equal(value, Assert.Single(restored.Items).Value);
        var profile = new ProfileDocument("3.0", "action", [taskId], new ProfileTarget(), new ExecutionPolicy(), true, false,
            Parameters: ImmutableDictionary<string, string>.Empty.Add(taskId, value));
        Assert.Equal(value, Assert.Single(new PlanBuilder(catalog).Build(profile).Tasks).ParameterSummary);
    }

    [Theory]
    [InlineData("runtime-dotnet-8", "secret")]
    [InlineData("computer-name", "name with spaces")]
    [InlineData("language-ui-preference", "not_a_tag")]
    public void RejectsUnsupportedOrInvalidValues(string taskId, string value)
    {
        Assert.Throws<ProfileValidationException>(() =>
            PlanTemplateJson.Serialize(Template([new PlanTemplateItem(taskId, value)]), new Catalog()));
    }

    [Fact]
    public void RejectsUnknownPropertiesAndUnknownTasks()
    {
        var json = PlanTemplateJson.Serialize(Template([new PlanTemplateItem("runtime-dotnet-8")]), new Catalog());
        Assert.Throws<ProfileValidationException>(() => PlanTemplateJson.Deserialize(json.Replace("\"name\":", "\"unexpected\":true,\"name\":"), new Catalog()));
        Assert.Throws<ProfileValidationException>(() => PlanTemplateJson.Serialize(Template([new PlanTemplateItem("missing-task")]), new Catalog()));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("item")]
    [InlineData("taskId")]
    [InlineData("policy")]
    public void RejectsNullRequiredTemplateValues(string field)
    {
        var template = field switch
        {
            "schema" => Template([new PlanTemplateItem("runtime-webview2")]) with { SchemaVersion = null! },
            "item" => Template([null!]),
            "taskId" => Template([new PlanTemplateItem(null!)]),
            "policy" => Template([new PlanTemplateItem("runtime-webview2")]) with { Policy = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var json = JsonSerializer.Serialize(template, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Throws<ProfileValidationException>(() => PlanTemplateJson.Deserialize(json, new Catalog()));
    }

    [Fact]
    public void RejectsTemplatesWithConflictingRegistrySettings()
    {
        var catalog = new Catalog();
        var template = Template([
            new PlanTemplateItem("setting-show-file-extensions", "enabled"),
            new PlanTemplateItem("registry-group-user-desktop-start")]);
        var json = JsonSerializer.Serialize(template, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var exception = Assert.Throws<ProfileValidationException>(() => PlanTemplateJson.Deserialize(json, catalog));

        Assert.Contains("conflicts", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PlanTemplateDocument Template(ImmutableArray<PlanTemplateItem> items) => new(
        PlanTemplateJson.SchemaVersion, "test-template", "Test template", DateTimeOffset.UtcNow, items,
        new ExecutionPolicy(), true, false);
}
