using System.Reflection;

namespace WindowsInitializer.App;

public static class BuildInfo
{
    private static readonly Assembly Assembly = typeof(BuildInfo).Assembly;
    private static readonly IReadOnlyDictionary<string, string> Metadata = Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .GroupBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Last().Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    public static string Version => Assembly.GetName().Version?.ToString(3) ?? "3.0.0";
    public static string SourceCommit => Value("SourceCommit", "local");
    public static string ShortCommit => SourceCommit.Length > 8 ? SourceCommit[..8] : SourceCommit;
    public static string BuildTimestampUtc => Value("BuildTimestampUtc", "development");
    public static string SignatureStatus => Value("SignatureStatus", "NotSignedInLocalBuild");
    public static string DisplayVersion => $"v{Version} · {ShortCommit}";
    public static string Provenance => $"{BuildTimestampUtc} · {SignatureStatus}";

    private static string Value(string key, string fallback) =>
        Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}
