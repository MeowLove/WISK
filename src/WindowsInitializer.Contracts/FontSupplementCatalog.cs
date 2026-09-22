using System.Collections.Immutable;

namespace WindowsInitializer.Contracts;

public sealed record FontSupplementDefinition(string Id, string DisplayName, string CapabilityPattern);

public static class FontSupplementCatalog
{
    public const string TaskId = "font-supplements-cjk-indic-europe";

    private static readonly ImmutableArray<FontSupplementDefinition> Definitions =
    [
        new("japanese", "Japanese fonts", "Language.Fonts.Jpan*"),
        new("korean", "Korean fonts", "Language.Fonts.Kore*"),
        new("european", "European supplemental fonts", "Language.Fonts.PanEuropeanSupplementalFonts*"),
        new("indic", "Indic (Devanagari) fonts", "Language.Fonts.Deva*")
    ];

    public static IReadOnlyList<FontSupplementDefinition> All => Definitions;

    public static bool IsValidSelection(string? value) => TryNormalizeSelection(value, out _);

    public static bool TryNormalizeSelection(string? value, out string normalized)
    {
        var ids = (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ids.Length == 0 || ids.Any(id => Definitions.All(definition => !definition.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = string.Join(',', Definitions
            .Where(definition => ids.Contains(definition.Id, StringComparer.OrdinalIgnoreCase))
            .Select(definition => definition.Id));
        return true;
    }

    public static FontSupplementDefinition? Find(string id) =>
        Definitions.FirstOrDefault(definition => definition.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
