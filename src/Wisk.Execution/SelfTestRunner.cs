using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wisk.Contracts;
using Wisk.Core;

namespace Wisk.Execution;

public enum SelfTestDisposition { Ready, Satisfied, Unavailable, Failed }

public sealed record SelfTestTaskResult(
    string TaskId,
    string DisplayName,
    string Domain,
    RiskLevel Risk,
    TaskKind Kind,
    SelfTestDisposition Disposition,
    TaskState State,
    ErrorCode Code,
    string Message,
    bool RequiresReboot,
    DateTimeOffset CheckedAt);

public sealed record SelfTestProgress(int Completed, int Total, string TaskId, SelfTestDisposition Disposition, string Message);

public sealed record SelfTestReport(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    CompatibilitySnapshot Compatibility,
    ImmutableArray<SelfTestTaskResult> Results)
{
    public int Total => Results.Length;
    public int Ready => Results.Count(result => result.Disposition == SelfTestDisposition.Ready);
    public int Satisfied => Results.Count(result => result.Disposition == SelfTestDisposition.Satisfied);
    public int Unavailable => Results.Count(result => result.Disposition == SelfTestDisposition.Unavailable);
    public int Failed => Results.Count(result => result.Disposition == SelfTestDisposition.Failed);
}

public sealed class SelfTestRunner(Catalog catalog, IInitializerTaskExecutor executor)
{
    public async Task<SelfTestReport> RunAsync(
        CompatibilitySnapshot compatibility,
        IReadOnlyCollection<string>? taskIds = null,
        IReadOnlyDictionary<string, string>? parameterSummaries = null,
        IProgress<SelfTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var requested = taskIds is null
            ? catalog.GetTasks().Select(task => task.Id).ToArray()
            : taskIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var results = ImmutableArray.CreateBuilder<SelfTestTaskResult>(requested.Length);

        foreach (var taskId in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = catalog.Find(taskId);
            SelfTestTaskResult result;
            if (descriptor is null)
            {
                result = Unknown(taskId);
            }
            else if (!descriptor.IsAvailable)
            {
                result = FromUnavailable(descriptor);
            }
            else
            {
                var planned = ToPlannedTask(descriptor, parameterSummaries);
                try
                {
                    var check = await executor.CheckAsync(planned, cancellationToken).ConfigureAwait(false);
                    result = FromCheck(descriptor, check);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
                {
                    result = FromFailure(descriptor, exception);
                }
            }

            results.Add(result);
            progress?.Report(new SelfTestProgress(results.Count, requested.Length, taskId, result.Disposition, result.Message));
        }

        return new SelfTestReport(startedAt, DateTimeOffset.UtcNow, compatibility, results.ToImmutable());
    }

    private static PlannedTask ToPlannedTask(TaskDescriptor task, IReadOnlyDictionary<string, string>? summaries) => new(
        task.Id,
        task.Version,
        task.Risk,
        task.Source,
        task.Dependencies.IsDefault ? ImmutableArray<string>.Empty : task.Dependencies,
        task.RequiresReboot,
        summaries is not null && summaries.TryGetValue(task.Id, out var summary) ? summary : string.Empty,
        RequiresAdministrator: task.RequiresAdministrator,
        ExtensionPackageId: task.ExtensionPackageId,
        ExtensionPackageVersion: task.ExtensionPackageVersion,
        ExtensionManifestHash: task.ExtensionManifestHash,
        ExtensionExecutablePath: task.ExtensionExecutablePath,
        ExtensionExecutableHash: task.ExtensionExecutableHash,
        ExtensionProtocol: task.ExtensionProtocol);

    private static SelfTestTaskResult FromCheck(TaskDescriptor task, TaskCheckResult check)
    {
        var disposition = check.AlreadyComplete || check.State == TaskState.Skipped
            ? SelfTestDisposition.Satisfied
            : check.State is TaskState.Ready or TaskState.Succeeded && check.CanApply
                ? SelfTestDisposition.Ready
                : check.State is TaskState.UnsupportedPrerequisite or TaskState.UnavailableExtension or TaskState.NeedsManualReview || !check.CanApply
                    ? SelfTestDisposition.Unavailable
                    : SelfTestDisposition.Failed;
        return Result(task, disposition, check.State, check.Code, check.Message, check.RequiresReboot);
    }

    private static SelfTestTaskResult FromUnavailable(TaskDescriptor task) => Result(task, SelfTestDisposition.Unavailable,
        task.Source == TaskSource.ControlledExtension ? TaskState.UnavailableExtension : TaskState.UnsupportedPrerequisite,
        task.Source == TaskSource.ControlledExtension ? ErrorCode.UnavailableExtension : ErrorCode.ManualReviewRequired,
        task.UnavailableReason ?? "This task is unavailable in the current catalog.", task.RequiresReboot);

    private static SelfTestTaskResult FromFailure(TaskDescriptor task, Exception exception) => Result(task,
        SelfTestDisposition.Failed, TaskState.Failed, ErrorCode.ProcessFailed,
        SensitiveDataRedactor.Redact(exception.Message), task.RequiresReboot);

    private static SelfTestTaskResult Unknown(string taskId) => new(taskId, taskId, string.Empty, RiskLevel.Standard,
        TaskKind.ManualReview, SelfTestDisposition.Failed, TaskState.Failed, ErrorCode.UnknownTask,
        "The task is not present in the current catalog.", false, DateTimeOffset.UtcNow);

    private static SelfTestTaskResult Result(TaskDescriptor task, SelfTestDisposition disposition, TaskState state,
        ErrorCode code, string message, bool requiresReboot) => new(task.Id, task.DisplayName, task.Domain, task.Risk,
        task.Kind, disposition, state, code, SensitiveDataRedactor.Redact(message), requiresReboot, DateTimeOffset.UtcNow);
}

public static class SelfTestReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task ExportAsync(SelfTestReport report, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Report output directory is unavailable.");
        Directory.CreateDirectory(directory);
        var content = Path.GetExtension(fullPath).Equals(".html", StringComparison.OrdinalIgnoreCase)
            ? BuildHtml(report)
            : JsonSerializer.Serialize(report, JsonOptions);
        var temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, fullPath, true);
    }

    public static async Task<(string JsonPath, string HtmlPath)> ExportBundleAsync(
        SelfTestReport report, string directory, string baseName, CancellationToken cancellationToken = default)
    {
        var safeName = string.Concat(baseName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var jsonPath = Path.Combine(directory, safeName + ".json");
        var htmlPath = Path.Combine(directory, safeName + ".html");
        await ExportAsync(report, jsonPath, cancellationToken).ConfigureAwait(false);
        await ExportAsync(report, htmlPath, cancellationToken).ConfigureAwait(false);
        return (Path.GetFullPath(jsonPath), Path.GetFullPath(htmlPath));
    }

    private static string BuildHtml(SelfTestReport report)
    {
        static string H(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? string.Empty);
        var rows = new StringBuilder();
        foreach (var result in report.Results)
        {
            rows.Append("<tr><td>").Append(H(result.TaskId)).Append("</td><td>").Append(H(result.DisplayName))
                .Append("</td><td>").Append(H(result.Disposition)).Append("</td><td>").Append(H(result.Code))
                .Append("</td><td>").Append(H(result.Message)).AppendLine("</td></tr>");
        }
        return $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><title>WISK self-test</title>
            <style>body{font-family:Segoe UI,Arial,sans-serif;margin:32px;color:#172033}table{border-collapse:collapse;width:100%}th,td{border:1px solid #d8deea;padding:8px;text-align:left;vertical-align:top}th{background:#eef4ff}.summary{margin:16px 0;padding:14px;background:#f4f7fb}</style></head>
            <body><h1>WISK self-test</h1><div class="summary">Completed {{H(report.CompletedAt)}} · Total {{report.Total}} · Ready {{report.Ready}} · Satisfied {{report.Satisfied}} · Unavailable {{report.Unavailable}} · Failed {{report.Failed}}</div>
            <p>{{H(report.Compatibility.ProductName)}} {{H(report.Compatibility.DisplayVersion)}} · Build {{H(report.Compatibility.Build)}} · {{H(report.Compatibility.OsArchitecture)}}</p>
            <table><thead><tr><th>Task ID</th><th>Name</th><th>Result</th><th>Code</th><th>Message</th></tr></thead><tbody>{{rows}}</tbody></table></body></html>
            """;
    }
}
