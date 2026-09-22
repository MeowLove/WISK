using System.Text.RegularExpressions;

namespace Wisk.Contracts;

public sealed record LanguagePreferenceSelection(
    string TargetLanguage,
    bool ApplyToCurrentUser,
    bool ApplyToSystem,
    bool SyncToWelcomeAndNewUsers);

public static class LanguagePreferenceCatalog
{
    public const string TaskId = "language-ui-preference";

    private static readonly Regex LanguageTagPattern = new(
        "^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public static bool IsValid(string? value) => TryNormalize(value, out _);

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.Trim();
        if (!trimmed.Contains('='))
            return TryCreate(trimmed, applyToCurrentUser: true, applyToSystem: false, syncToWelcomeAndNewUsers: false, out normalized);

        var parts = trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts)
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || !fields.TryAdd(pair[0], pair[1])) return false;
        }

        if (fields.Count != 4 || !fields.ContainsKey("language") || !fields.ContainsKey("currentUser") ||
            !fields.ContainsKey("system") || !fields.ContainsKey("welcome"))
            return false;
        if (!bool.TryParse(fields["currentUser"], out var applyToCurrentUser) ||
            !bool.TryParse(fields["system"], out var applyToSystem) ||
            !bool.TryParse(fields["welcome"], out var syncToWelcomeAndNewUsers))
            return false;

        return TryCreate(fields["language"], applyToCurrentUser, applyToSystem, syncToWelcomeAndNewUsers, out normalized);
    }

    public static bool TryNormalizeComponents(
        string? targetLanguage,
        bool applyToCurrentUser,
        bool applyToSystem,
        bool syncToWelcomeAndNewUsers,
        out string normalized) =>
        TryCreate(targetLanguage?.Trim() ?? string.Empty, applyToCurrentUser, applyToSystem, syncToWelcomeAndNewUsers, out normalized);

    public static bool TryParse(string? value, out LanguagePreferenceSelection selection)
    {
        selection = default!;
        if (!TryNormalize(value, out var normalized)) return false;

        var fields = normalized.Split(';')
            .Select(part => part.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.OrdinalIgnoreCase);
        selection = new LanguagePreferenceSelection(
            fields["language"],
            bool.Parse(fields["currentUser"]),
            bool.Parse(fields["system"]),
            bool.Parse(fields["welcome"]));
        return true;
    }

    private static bool TryCreate(
        string targetLanguage,
        bool applyToCurrentUser,
        bool applyToSystem,
        bool syncToWelcomeAndNewUsers,
        out string normalized)
    {
        normalized = string.Empty;
        if (!LanguageTagPattern.IsMatch(targetLanguage) || (!applyToCurrentUser && !applyToSystem) ||
            syncToWelcomeAndNewUsers && !applyToCurrentUser)
            return false;

        normalized = $"language={targetLanguage};currentUser={applyToCurrentUser.ToString().ToLowerInvariant()};" +
                     $"system={applyToSystem.ToString().ToLowerInvariant()};welcome={syncToWelcomeAndNewUsers.ToString().ToLowerInvariant()}";
        return true;
    }
}
