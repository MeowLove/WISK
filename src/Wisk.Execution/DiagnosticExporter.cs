using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wisk.Contracts;

namespace Wisk.Execution;

public static class DiagnosticExporter
{
    private const string RedactedValue = "[REDACTED]";
    private static readonly Regex LocalPath = new(
        @"(?i)(?:%[A-Z0-9_]+%[\\/]|[A-Z]:[\\/]|\\\\[^\\/\s]+[\\/])[^\s""<>|]+",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task ExportAsync(RunStateSnapshot snapshot, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(destinationPath)) throw new ArgumentException("Diagnostic destination must be absolute.", nameof(destinationPath));
        var directory = Path.GetDirectoryName(destinationPath) ?? throw new ArgumentException("Diagnostic destination is invalid.", nameof(destinationPath));
        Directory.CreateDirectory(directory);
        var personalValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddPersonalValue(personalValues, Environment.UserName);
        AddPersonalValue(personalValues, Environment.MachineName);
        var sanitizedPlan = RedactPlanPaths(snapshot.SerializedPlan, personalValues);
        var sanitized = snapshot with
        {
            Results = snapshot.Results.Select(result => result with { Message = RedactMessage(result.Message, personalValues) }).ToImmutableArray(),
            SerializedPlan = sanitizedPlan
        };
        var tempPath = destinationPath + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(sanitized, Options);
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(tempPath, destinationPath, true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static string? RedactPlanPaths(string? serializedPlan, HashSet<string> personalValues)
    {
        if (string.IsNullOrWhiteSpace(serializedPlan)) return serializedPlan;
        try
        {
            var plan = JsonSerializer.Deserialize<ImmutablePlan>(serializedPlan, Options);
            if (plan is null || plan.Tasks.IsDefault ||
                plan.Tasks.Any(task => task is null || string.IsNullOrWhiteSpace(task.TaskId)))
                return "[INVALID_PLAN_REDACTED]";
            AddPersonalValue(personalValues, plan.ProfileId);
            var tasks = plan.Tasks.Select(task => task with
            {
                ParameterSummary = IsPersonalParameter(task.TaskId) ? RedactedValue : task.ParameterSummary,
                Proxy = string.IsNullOrWhiteSpace(task.Proxy) ? null : RedactedValue,
                ExtensionExecutablePath = string.IsNullOrWhiteSpace(task.ExtensionExecutablePath) ? null : "[LOCAL_EXTENSION_PATH]"
            }).ToImmutableArray();
            foreach (var task in plan.Tasks)
            {
                if (IsPersonalParameter(task.TaskId))
                {
                    if (task.TaskId.Equals("accounts-local", StringComparison.OrdinalIgnoreCase))
                        foreach (var accountName in (task.ParameterSummary ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            AddPersonalValue(personalValues, accountName);
                    else
                        AddPersonalValue(personalValues, task.ParameterSummary);
                }

                if (!string.IsNullOrWhiteSpace(task.Proxy))
                {
                    AddPersonalValue(personalValues, task.Proxy);
                    if (Uri.TryCreate(task.Proxy, UriKind.Absolute, out var proxy)) AddPersonalValue(personalValues, proxy.Host);
                }
                AddPersonalValue(personalValues, task.ExtensionExecutablePath);
            }
            return JsonSerializer.Serialize(plan with { ProfileId = RedactedValue, Tasks = tasks }, Options);
        }
        catch (JsonException)
        {
            return "[INVALID_PLAN_REDACTED]";
        }
    }

    private static bool IsPersonalParameter(string? taskId) =>
        string.Equals(taskId, "accounts-local", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(taskId, "computer-name", StringComparison.OrdinalIgnoreCase);

    private static void AddPersonalValue(HashSet<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Length <= 256) values.Add(value);
    }

    private static string RedactMessage(string? message, IEnumerable<string> personalValues)
    {
        var redacted = SensitiveDataRedactor.Redact(message);
        redacted = LocalPath.Replace(redacted, "[LOCAL_PATH]");
        foreach (var value in personalValues.OrderByDescending(value => value.Length))
        {
            if (value.Length == 0) continue;
            var pattern = $"(?<![\\p{{L}}\\p{{N}}_.-]){Regex.Escape(value)}(?![\\p{{L}}\\p{{N}}_.-])";
            redacted = Regex.Replace(redacted, pattern, RedactedValue,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        return redacted;
    }
}
