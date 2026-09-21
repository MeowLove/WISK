using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsInitializer.Contracts;
using WindowsInitializer.Execution;

namespace WindowsInitializer.Platform.Windows;

public interface IControlledExtensionProcessRunner
{
    Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string executablePath, string requestJson, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class ControlledExtensionTaskExecutor(IControlledExtensionProcessRunner? runner = null) : IInitializerTaskExecutor
{
    private const int MaxOutputCharacters = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly IControlledExtensionProcessRunner _runner = runner ?? new ControlledExtensionProcessRunner();

    public async Task<TaskCheckResult> CheckAsync(PlannedTask task, CancellationToken cancellationToken)
    {
        try
        {
            var response = await InvokeAsync(task, "Check", Guid.NewGuid().ToString("N"), TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            return new TaskCheckResult(task.TaskId, response.Status, response.Code, response.Message,
                AlreadyComplete: response.Status == TaskState.Skipped,
                CanApply: response.Status is TaskState.Ready or TaskState.Succeeded,
                RequiresReboot: response.RebootRequired,
                Retryable: response.Code == ErrorCode.Timeout);
        }
        catch (ControlledExtensionProcessException exception)
        {
            return new TaskCheckResult(task.TaskId,
                exception.Code == ErrorCode.Cancelled ? TaskState.Cancelled :
                exception.Code == ErrorCode.UnavailableExtension ? TaskState.UnavailableExtension : TaskState.Failed,
                exception.Code, exception.Message, CanApply: false, Retryable: exception.Code == ErrorCode.Timeout);
        }
    }

    public async Task<ExecutionResult> ApplyAsync(PlannedTask task, ApplyContext context)
    {
        try
        {
            var response = await InvokeAsync(task, "Apply", context.RunId, context.Timeout, context.CancellationToken).ConfigureAwait(false);
            return new ExecutionResult(task.TaskId, response.Status, response.Code.ToString(), response.Message,
                response.Changed, response.RebootRequired, Retryable: response.Code == ErrorCode.Timeout);
        }
        catch (ControlledExtensionProcessException exception)
        {
            return new ExecutionResult(task.TaskId,
                exception.Code == ErrorCode.Cancelled ? TaskState.Cancelled :
                exception.Code == ErrorCode.UnavailableExtension ? TaskState.UnavailableExtension : TaskState.Failed,
                exception.Code.ToString(), exception.Message, false, task.RequiresReboot,
                Retryable: exception.Code == ErrorCode.Timeout);
        }
    }

    public async Task<VerifyResult> VerifyAsync(PlannedTask task, CancellationToken cancellationToken)
    {
        try
        {
            var response = await InvokeAsync(task, "Verify", Guid.NewGuid().ToString("N"), TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            return new VerifyResult(task.TaskId, response.Status is TaskState.Succeeded or TaskState.Skipped,
                response.Code, response.Message, response.RebootRequired);
        }
        catch (ControlledExtensionProcessException exception)
        {
            return new VerifyResult(task.TaskId, false, exception.Code, exception.Message, task.RequiresReboot);
        }
    }

    private async Task<BridgeResponse> InvokeAsync(
        PlannedTask task, string operation, string runId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var executablePath = await ValidateEntryPointAsync(task, cancellationToken).ConfigureAwait(false);
        var request = new BridgeRequest("1.0", runId, task.TaskId, operation,
            System.Collections.Immutable.ImmutableDictionary<string, string>.Empty);
        var requestJson = JsonSerializer.Serialize(request, JsonOptions);
        var result = await _runner.RunAsync(executablePath, requestJson, timeout, cancellationToken).ConfigureAwait(false);
        if (result.Stdout.Length > MaxOutputCharacters || result.Stderr.Length > 8 * 1024)
            throw Failure(ErrorCode.ProcessFailed, "Extension process output exceeded the safety limit.");
        if (result.ExitCode != 0)
            throw Failure(result.ExitCode == 5 ? ErrorCode.AccessDenied : ErrorCode.ProcessFailed, "Extension process failed.");
        BridgeResponse? response;
        try
        {
            ValidateNoAmbiguousProperties(result.Stdout);
            response = JsonSerializer.Deserialize<BridgeResponse>(result.Stdout, JsonOptions);
        }
        catch (JsonException)
        {
            throw Failure(ErrorCode.ProcessFailed, "Extension process returned invalid JSON.");
        }
        if (response is null || response.ProtocolVersion != "1.0" || response.RunId != runId || response.TaskId != task.TaskId)
            throw Failure(ErrorCode.ProcessFailed, "Extension process response failed correlation validation.");
        return response with
        {
            Message = SensitiveDataRedactor.Redact(response.Message ?? string.Empty),
            Summary = SensitiveDataRedactor.Redact(response.Summary ?? string.Empty)
        };
    }

    private static async Task<string> ValidateEntryPointAsync(PlannedTask task, CancellationToken cancellationToken)
    {
        if (task.Source != TaskSource.ControlledExtension || task.ExtensionProtocol != "stdio-json-v1" ||
            string.IsNullOrWhiteSpace(task.ExtensionPackageId) || string.IsNullOrWhiteSpace(task.ExtensionManifestHash) ||
            string.IsNullOrWhiteSpace(task.ExtensionExecutablePath) || string.IsNullOrWhiteSpace(task.ExtensionExecutableHash) ||
            !Path.IsPathFullyQualified(task.ExtensionExecutablePath) ||
            !Path.GetExtension(task.ExtensionExecutablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(task.ExtensionExecutablePath))
            throw Failure(ErrorCode.UnavailableExtension, "The controlled extension entry point is unavailable.");
        try
        {
            await using var stream = File.OpenRead(task.ExtensionExecutablePath);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            if (!hash.Equals(task.ExtensionExecutableHash, StringComparison.OrdinalIgnoreCase))
                throw Failure(ErrorCode.ExtensionHashMismatch, "The controlled extension entry point hash changed after planning.");
            return Path.GetFullPath(task.ExtensionExecutablePath);
        }
        catch (UnauthorizedAccessException)
        {
            throw Failure(ErrorCode.AccessDenied, "Access to the controlled extension entry point was denied.");
        }
        catch (IOException)
        {
            throw Failure(ErrorCode.UnavailableExtension, "The controlled extension entry point could not be read.");
        }
    }

    private static void ValidateNoAmbiguousProperties(string json)
    {
        using var document = JsonDocument.Parse(json);
        Visit(document.RootElement);
        static void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new JsonException("Duplicate or ambiguous JSON property.");
                    Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) Visit(item);
        }
    }

    private static ControlledExtensionProcessException Failure(ErrorCode code, string message) => new(message, code);
}

public sealed class ControlledExtensionProcessRunner : IControlledExtensionProcessRunner
{
    public async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string executablePath, string requestJson, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        process.StartInfo.ArgumentList.Add("--windows-initializer-extension-v1");
        var retainedEnvironment = new[] { "SystemRoot", "WINDIR", "TEMP", "TMP" }
            .Select(name => (Name: name, Value: Environment.GetEnvironmentVariable(name)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Value)).ToArray();
        process.StartInfo.Environment.Clear();
        foreach (var item in retainedEnvironment) process.StartInfo.Environment[item.Name] = item.Value!;
        try
        {
            if (!process.Start()) throw Failure(ErrorCode.UnavailableExtension, "The controlled extension process could not be started.");
            await process.StandardInput.WriteAsync(requestJson.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            var stdout = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            return (process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw Failure(cancellationToken.IsCancellationRequested ? ErrorCode.Cancelled : ErrorCode.Timeout,
                "The controlled extension process timed out or was cancelled.");
        }
        catch (Win32Exception exception)
        {
            throw Failure(exception.NativeErrorCode == 5 ? ErrorCode.AccessDenied : ErrorCode.UnavailableExtension,
                "The controlled extension process is unavailable.");
        }
    }

    private static ControlledExtensionProcessException Failure(ErrorCode code, string message) => new(message, code);
}

public sealed class ControlledExtensionProcessException(string message, ErrorCode code) : Exception(message)
{
    public ErrorCode Code { get; } = code;
}
