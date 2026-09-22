using System.Collections.Immutable;
using Wisk.Contracts;
using Wisk.Core;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class ProfileJsonTests
{
    [Fact]
    public void DeserializeValidatesCatalogAndNormalizesParameters()
    {
        const string json = """
            { "schemaVersion": "2.0", "profileId": "safe", "tasks": [ "runtime-dotnet-8" ],
              "target": {}, "policy": {}, "allowElevated": false, "allowHighRisk": false }
            """;

        var profile = ProfileJson.Deserialize(json, new Catalog());

        Assert.Equal("safe", profile.ProfileId);
        Assert.NotNull(profile.Parameters);
        Assert.Empty(profile.Parameters!);
    }

    [Fact]
    public void DiagnosticsSerializationRedactsAccountAndParameterSecrets()
    {
        var profile = new ProfileDocument(
            "2.0", "redacted", ["runtime-dotnet-8"], new ProfileTarget(), new ExecutionPolicy(), false, false,
            Parameters: ImmutableDictionary<string, string>.Empty.Add("apiToken", "not-for-logs"),
            Accounts: [new ProfileAccount("operator", null, null, null, false, false, false, "not-for-logs")]);

        var json = ProfileJson.SerializeForDiagnostics(profile);

        Assert.DoesNotContain("not-for-logs", json, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DeserializeRejectsUnknownMembers()
    {
        const string json = """
            { "schemaVersion": "2.0", "profileId": "safe", "tasks": [ "runtime-dotnet-8" ],
              "target": {}, "policy": {}, "allowElevated": false, "allowHighRisk": false, "unexpected": true }
            """;

        Assert.Throws<ProfileValidationException>(() => ProfileJson.Deserialize(json, new Catalog()));
    }

    [Fact]
    public void DeserializeRejectsCommentsAndAmbiguousPropertyCasing()
    {
        const string commented = """
            { "schemaVersion": "2.0", /* no comments in profiles */ "profileId": "safe", "tasks": [ "runtime-dotnet-8" ],
              "target": {}, "policy": {}, "allowElevated": false, "allowHighRisk": false }
            """;
        const string wrongCase = """
            { "SchemaVersion": "2.0", "profileId": "safe", "tasks": [ "runtime-dotnet-8" ],
              "target": {}, "policy": {}, "allowElevated": false, "allowHighRisk": false }
            """;

        Assert.Throws<ProfileValidationException>(() => ProfileJson.Deserialize(commented, new Catalog()));
        Assert.Throws<ProfileValidationException>(() => ProfileJson.Deserialize(wrongCase, new Catalog()));
    }

    [Fact]
    public void DeserializeRejectsDuplicatePropertiesAtAnyDepth()
    {
        const string duplicate = """
            { "schemaVersion": "2.0", "profileId": "safe", "profileId": "changed", "tasks": [ "runtime-dotnet-8" ],
              "target": {}, "policy": {}, "allowElevated": false, "allowHighRisk": false }
            """;
        const string ambiguousNested = """
            { "schemaVersion": "2.0", "profileId": "safe", "tasks": [ "runtime-dotnet-8" ],
              "target": { "minimumBuild": 26100, "MinimumBuild": 26200 }, "policy": {}, "allowElevated": false, "allowHighRisk": false }
            """;

        Assert.Throws<ProfileValidationException>(() => ProfileJson.Deserialize(duplicate, new Catalog()));
        Assert.Throws<ProfileValidationException>(() => ProfileJson.Deserialize(ambiguousNested, new Catalog()));
    }

    [Theory]
    [InlineData("null", "{}")]
    [InlineData("{}", "null")]
    [InlineData("{}", "{\"defaultTimeout\":\"-00:00:01\"}")]
    [InlineData("{}", "{\"retryBaseDelay\":\"00:06:00\"}")]
    public void DeserializeRejectsMissingOrUnboundedTargetAndPolicy(string target, string policy)
    {
        var json = $$"""
            { "schemaVersion": "2.0", "profileId": "safe", "tasks": [ "runtime-dotnet-8" ],
              "target": {{target}}, "policy": {{policy}}, "allowElevated": false, "allowHighRisk": false }
            """;

        Assert.Throws<ProfileValidationException>(() => ProfileJson.Deserialize(json, new Catalog()));
    }

    [Fact]
    public void DeserializeRejectsParametersForUnselectedTasks()
    {
        const string json = """
            { "schemaVersion": "2.0", "profileId": "safe", "tasks": [ "runtime-dotnet-8" ],
              "target": {}, "policy": {}, "allowElevated": false, "allowHighRisk": false,
              "parameters": { "app-vscode": "unexpected" } }
            """;

        Assert.Throws<ProfileValidationException>(() => ProfileJson.Deserialize(json, new Catalog()));
    }

    [Fact]
    public void DeserializeAllowsAnExplicitEmptyAccountArrayWithoutAccountTask()
    {
        const string json = """
            { "schemaVersion": "2.0", "profileId": "safe", "tasks": [ "runtime-dotnet-8" ],
              "target": {}, "policy": {}, "allowElevated": false, "allowHighRisk": false,
              "accounts": [] }
            """;

        var profile = ProfileJson.Deserialize(json, new Catalog());

        Assert.True(profile.Accounts!.Value.IsEmpty);
    }

    [Theory]
    [InlineData("name with spaces", "S-1-5-32-545")]
    [InlineData("valid-name", "S-1-5-32-999")]
    public void DeserializeRejectsUnsafeAccountIdentityFields(string name, string groupSid)
    {
        var json = $$"""
            { "schemaVersion": "2.0", "profileId": "account", "tasks": [ "accounts-local" ],
              "target": {}, "policy": {}, "allowElevated": true, "allowHighRisk": false,
              "accounts": [{ "name": "{{name}}", "groupSid": "{{groupSid}}", "passwordNeverExpires": false,
                "hideFromSignInScreen": false, "allowRemoteDesktop": false }] }
            """;

        Assert.Throws<ProfileValidationException>(() => ProfileJson.Deserialize(json, new Catalog()));
    }
}
