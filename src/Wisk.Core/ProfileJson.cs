using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wisk.Contracts;

namespace Wisk.Core;

public sealed class ProfileValidationException(string message) : Exception(message);

public static class ProfileJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ProfileDocument Deserialize(string json, Catalog catalog)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ProfileValidationException("Profile JSON is empty.");
        ProfileDocument? profile;
        try
        {
            StrictJson.ValidateNoAmbiguousProperties(json);
            profile = JsonSerializer.Deserialize<ProfileDocument>(json, Options);
        }
        catch (JsonException ex) { throw new ProfileValidationException($"Profile JSON is invalid: {ex.Message}"); }
        var issue = ProfileDocumentValidator.Validate(profile, catalog);
        if (issue is not null) throw new ProfileValidationException(issue.Message);
        return profile! with
        {
            Tasks = profile!.Tasks.ToImmutableArray(),
            Parameters = profile.Parameters ?? ImmutableDictionary<string, string>.Empty
        };
    }

    public static string Serialize(ProfileDocument profile) => JsonSerializer.Serialize(profile, Options);

    public static string SerializeForDiagnostics(ProfileDocument profile)
    {
        var safe = profile with
        {
            Accounts = profile.Accounts?.Select(account => account with { Password = null }).ToImmutableArray(),
            Parameters = profile.Parameters?.ToImmutableDictionary(
                pair => pair.Key,
                pair => IsSecret(pair.Key) || IsSecret(pair.Value) ? "[REDACTED]" : pair.Value,
                StringComparer.OrdinalIgnoreCase)
        };
        return Serialize(safe);
    }

    private static bool IsSecret(string value) => value.Contains("password", StringComparison.OrdinalIgnoreCase)
        || value.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || value.Contains("token", StringComparison.OrdinalIgnoreCase)
        || value.Contains("private", StringComparison.OrdinalIgnoreCase);
}
