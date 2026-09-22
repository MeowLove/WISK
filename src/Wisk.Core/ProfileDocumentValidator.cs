using Wisk.Contracts;

namespace Wisk.Core;

public sealed record ProfileValidationIssue(ErrorCode Code, string Message);

public static class ProfileDocumentValidator
{
    public const int MaximumTaskCount = 256;
    public const int MaximumParameterCount = 256;
    public const int MaximumAccountCount = 64;
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromHours(2);
    public static readonly TimeSpan MaximumRetryBaseDelay = TimeSpan.FromMinutes(5);

    public static ProfileValidationIssue? Validate(
        ProfileDocument? profile,
        Catalog? catalog = null,
        CompatibilitySnapshot? compatibility = null)
    {
        if (profile is null || profile.SchemaVersion != "3.0" ||
            string.IsNullOrWhiteSpace(profile.ProfileId) || profile.ProfileId.Length > 128)
            return Invalid("schemaVersion 3.0 and a profileId of at most 128 characters are required.");

        if (profile.Tasks.IsDefaultOrEmpty || profile.Tasks.Length > MaximumTaskCount ||
            profile.Tasks.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128))
            return Invalid($"Between 1 and {MaximumTaskCount} valid task IDs are required.");
        if (profile.Tasks.Length != profile.Tasks.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return Invalid("Duplicate task IDs are not allowed.");
        if (catalog is not null)
        {
            var unknown = profile.Tasks.FirstOrDefault(id => catalog.Find(id) is null);
            if (unknown is not null) return new ProfileValidationIssue(ErrorCode.UnknownTask, $"Unknown task ID: {unknown}");
        }

        if (profile.Target is null || profile.Target.MinimumBuild < 26100 || profile.Target.RequireWindows11 is false ||
            !string.Equals(profile.Target.Architecture, "x64", StringComparison.OrdinalIgnoreCase))
            return Invalid("Target must require Windows 11 build 26100 or newer on x64.");

        var policyIssue = ValidateExecutionPolicy(profile.Policy);
        if (policyIssue is not null) return policyIssue;

        if (profile.Proxy is not null)
        {
            if (profile.Proxy.Length > 2048 || !Uri.TryCreate(profile.Proxy, UriKind.Absolute, out var proxyUri) ||
                proxyUri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(proxyUri.Host) ||
                !string.IsNullOrEmpty(proxyUri.UserInfo) || !string.IsNullOrEmpty(proxyUri.Query) ||
                !string.IsNullOrEmpty(proxyUri.Fragment))
                return Invalid("proxy must be an HTTP or HTTPS URI without credentials, query, or fragment.");
        }

        if (profile.Parameters is { Count: > MaximumParameterCount })
            return Invalid($"A profile can contain at most {MaximumParameterCount} parameters.");
        if (profile.Parameters is not null)
        {
            var selected = profile.Tasks.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (profile.Parameters.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 128 ||
                                               pair.Value is null || pair.Value.Length > 4096 || !selected.Contains(pair.Key)))
                return Invalid("Parameters must target selected task IDs and contain values no longer than 4096 characters.");
            if (catalog is not null && profile.Parameters.Any(pair => catalog.Find(pair.Key)?.Kind == TaskKind.Winget &&
                pair.Value is not ("" or "install" or "upgrade")))
                return Invalid("Software actions must be install or upgrade.");
        }

        foreach (var taskId in profile.Tasks.Where(id => ConfigurableRegistrySettingCatalog.Find(id) is not null))
        {
            var value = profile.Parameters?.FirstOrDefault(pair => pair.Key.Equals(taskId, StringComparison.OrdinalIgnoreCase)).Value;
            if (ConfigurableRegistrySettingCatalog.Find(taskId) is not { } definition || !definition.IsValidState(value))
                return Invalid($"Task '{taskId}' requires an explicit enabled, disabled, or default value before planning.");
        }

        if (profile.Tasks.Contains(FontSupplementCatalog.TaskId, StringComparer.OrdinalIgnoreCase))
        {
            var value = profile.Parameters?.FirstOrDefault(pair => pair.Key.Equals(FontSupplementCatalog.TaskId, StringComparison.OrdinalIgnoreCase)).Value;
            if (!FontSupplementCatalog.IsValidSelection(value))
                return Invalid("The supplemental font task requires at least one valid font selection.");
        }

        if (profile.Tasks.Contains(LanguagePreferenceCatalog.TaskId, StringComparer.OrdinalIgnoreCase))
        {
            var value = profile.Parameters?.FirstOrDefault(pair => pair.Key.Equals(LanguagePreferenceCatalog.TaskId, StringComparison.OrdinalIgnoreCase)).Value;
            if (!LanguagePreferenceCatalog.IsValid(value))
                return Invalid("The display language task requires a valid language and at least one target scope.");
        }

        if (profile.Accounts is { } accounts)
        {
            if (accounts.Length > MaximumAccountCount)
                return Invalid($"A profile can contain at most {MaximumAccountCount} accounts.");
            if (!accounts.IsDefaultOrEmpty)
            {
                if (!profile.Tasks.Contains("accounts-local", StringComparer.OrdinalIgnoreCase))
                    return Invalid("Accounts require the accounts-local task.");
                if (accounts.Any(IsInvalidAccount))
                    return Invalid("Account fields do not satisfy the profile limits.");
                if (accounts.Select(account => account!.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != accounts.Length)
                    return Invalid("Duplicate account names are not allowed.");
            }
        }

        if (compatibility is not null)
        {
            if (profile.Target.RequireWindows11 && !compatibility.IsWindows11)
                return new ProfileValidationIssue(ErrorCode.UnsupportedOperatingSystem, "The target system is not Windows 11.");
            if (compatibility.CurrentBuild < profile.Target.MinimumBuild)
                return new ProfileValidationIssue(ErrorCode.UnsupportedOperatingSystem,
                    $"Windows build {profile.Target.MinimumBuild} or newer is required.");
            if (!string.Equals(profile.Target.Architecture, compatibility.OsArchitecture, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(profile.Target.Architecture, compatibility.ProcessArchitecture, StringComparison.OrdinalIgnoreCase))
                return new ProfileValidationIssue(ErrorCode.UnsupportedArchitecture,
                    "The profile architecture does not match the process and OS.");
        }

        return null;
    }

    public static ProfileValidationIssue? ValidateExecutionPolicy(ExecutionPolicy? policy)
    {
        if (policy is null) return Invalid("Execution policy is required.");
        if (policy.MaxRetries is < 0 or > 5)
            return Invalid("maxRetries must be between 0 and 5.");
        if (!IsDurationInRange(policy.DefaultTimeout, MaximumTimeout))
            return Invalid("defaultTimeout must be greater than zero and no more than 2 hours.");
        if (!IsDurationInRange(policy.RetryBaseDelay, MaximumRetryBaseDelay))
            return Invalid("retryBaseDelay must be greater than zero and no more than 5 minutes.");
        return null;
    }

    private static bool IsDurationInRange(TimeSpan? value, TimeSpan maximum) =>
        value is null || value.Value > TimeSpan.Zero && value.Value <= maximum;

    private static bool IsInvalidAccount(ProfileAccount? account) => account is null ||
        string.IsNullOrWhiteSpace(account.Name) || account.Name.Length > 20 ||
        !System.Text.RegularExpressions.Regex.IsMatch(account.Name, "^[A-Za-z0-9._-]{1,20}$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)) ||
        account.FullName is { Length: > 256 } || account.Description is { Length: > 1024 } ||
        account.Password is { Length: > 256 } ||
        account.GroupSid is not null && account.GroupSid is not ("S-1-5-32-544" or "S-1-5-32-545");

    private static ProfileValidationIssue Invalid(string message) => new(ErrorCode.InvalidProfile, message);
}
