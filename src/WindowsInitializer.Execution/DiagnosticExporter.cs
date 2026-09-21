using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsInitializer.Contracts;

namespace WindowsInitializer.Execution;

public static class DiagnosticExporter
{
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
        var sanitized = snapshot with
        {
            Results = snapshot.Results.Select(result => result with { Message = SensitiveDataRedactor.Redact(result.Message) }).ToImmutableArray(),
            SerializedPlan = RedactPlanPaths(snapshot.SerializedPlan)
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

    private static string? RedactPlanPaths(string? serializedPlan)
    {
        if (string.IsNullOrWhiteSpace(serializedPlan)) return serializedPlan;
        try
        {
            var plan = JsonSerializer.Deserialize<ImmutablePlan>(serializedPlan, Options);
            if (plan is null || plan.Tasks.IsDefault) return "[INVALID_PLAN_REDACTED]";
            var tasks = plan.Tasks.Select(task => task with
            {
                ExtensionExecutablePath = string.IsNullOrWhiteSpace(task.ExtensionExecutablePath) ? null : "[LOCAL_EXTENSION_PATH]"
            }).ToImmutableArray();
            return JsonSerializer.Serialize(plan with { Tasks = tasks }, Options);
        }
        catch (JsonException)
        {
            return "[INVALID_PLAN_REDACTED]";
        }
    }
}
