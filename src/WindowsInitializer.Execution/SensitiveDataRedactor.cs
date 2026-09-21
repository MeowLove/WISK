using System.Text.RegularExpressions;

namespace WindowsInitializer.Execution;

public static partial class SensitiveDataRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var redacted = LabeledSecret().Replace(value, match => $"{match.Groups[1].Value}=[REDACTED]");
        return BearerToken().Replace(redacted, "Bearer [REDACTED]");
    }

    [GeneratedRegex("(?i)\\b(password|passwd|token|secret|privatekey)\\s*[:=]\\s*['\\\"]?[^\\s,;\\\"]+['\\\"]?")]
    private static partial Regex LabeledSecret();

    [GeneratedRegex("(?i)\\bBearer\\s+[A-Za-z0-9._~+/-]+=*")]
    private static partial Regex BearerToken();
}
