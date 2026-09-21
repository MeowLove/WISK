using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsInitializer.Contracts;
using WindowsInitializer.Core;
using WindowsInitializer.Execution;
using WindowsInitializer.Platform.Windows;
using WindowsInitializer.PowerShell;

namespace WindowsInitializer.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> Main(string[] args)
    {
        var options = new CliOptions();
        try
        {
            options = Parse(args);
            if (options.Help)
            {
                PrintUsage();
                return 0;
            }

            var compatibility = new WindowsCompatibility().Read();
            if (options.Command.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                WriteStatus(options.Json, compatibility);
                return 0;
            }
            var stateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsInitializer", "state");
            var store = new AtomicJsonStateStore(stateRoot);
            if (options.Command.Equals("history", StringComparison.OrdinalIgnoreCase))
            {
                WriteHistory(options.Json, await store.ListAsync(CancellationToken.None).ConfigureAwait(false));
                return 0;
            }
            if (options.Command.Equals("diagnostics", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(options.RunId) || string.IsNullOrWhiteSpace(options.OutputPath))
                    throw new CliUsageException("diagnostics requires --run-id and --output.");
                var diagnosticSnapshot = await store.LoadAsync(options.RunId, CancellationToken.None).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Run state was not found.");
                var outputPath = Path.GetFullPath(options.OutputPath);
                await DiagnosticExporter.ExportAsync(diagnosticSnapshot, outputPath).ConfigureAwait(false);
                if (options.Json) Write(true, new { state = "Succeeded", runId = diagnosticSnapshot.RunId, output = outputPath });
                else Console.WriteLine($"Redacted diagnostics exported to {outputPath}");
                return 0;
            }
            var extensionValidator = ExtensionTrustPolicy.CreateValidator();
            var extensionInstaller = new ControlledExtensionInstaller(extensionValidator);
            var installedExtensions = await extensionInstaller.ListAsync(ExtensionPaths.UserRoot, compatibility.CurrentBuild,
                compatibility.OsArchitecture, CancellationToken.None).ConfigureAwait(false);
            var catalog = new Catalog(installedExtensions);
            if (options.Command.Equals("catalog", StringComparison.OrdinalIgnoreCase))
            {
                WriteCatalog(options.Json, catalog.GetTasks());
                return 0;
            }
            if (options.Command.Equals("self-test", StringComparison.OrdinalIgnoreCase))
            {
                ProfileDocument? selfTestProfile = null;
                IReadOnlyCollection<string>? selfTestTaskIds = null;
                IReadOnlyDictionary<string, string>? selfTestParameters = null;
                if (!string.IsNullOrWhiteSpace(options.ProfilePath))
                {
                    var profileJson = await File.ReadAllTextAsync(Path.GetFullPath(options.ProfilePath)).ConfigureAwait(false);
                    selfTestProfile = ProfileJson.Deserialize(profileJson, catalog);
                    selfTestTaskIds = selfTestProfile.Tasks;
                    selfTestParameters = selfTestProfile.Parameters;
                }

                var selfTestExecutor = new RegisteredTaskExecutor(catalog, new BridgeClient(), compatibility: compatibility);
                var report = await new SelfTestRunner(catalog, selfTestExecutor).RunAsync(compatibility, selfTestTaskIds, selfTestParameters).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(options.OutputPath))
                {
                    var outputPath = Path.GetFullPath(options.OutputPath);
                    await SelfTestReportExporter.ExportAsync(report, outputPath).ConfigureAwait(false);
                    if (!options.Json) Console.WriteLine($"Self-test report exported to {outputPath}");
                }
                else
                {
                    WriteSelfTest(options.Json, report);
                }
                return report.Failed > 0 ? 4 : report.Unavailable > 0 ? 3 : 0;
            }
            if (options.Command.Equals("extensions", StringComparison.OrdinalIgnoreCase))
            {
                if (options.Json) Write(true, installedExtensions);
                else if (installedExtensions.Count == 0) Console.WriteLine("No trusted controlled extensions are installed.");
                else foreach (var extension in installedExtensions) Console.WriteLine($"{extension.PackageId} {extension.Version} | {extension.Publisher} | {extension.Tasks.Length} task(s)");
                return 0;
            }
            if (options.Command.Equals("install-extension", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(options.PackagePath)) throw new CliUsageException("install-extension requires --package path.");
                var protectedRoots = new[] { AppContext.BaseDirectory, stateRoot };
                var installed = await extensionInstaller.InstallAsync(Path.GetFullPath(options.PackagePath), ExtensionPaths.UserRoot,
                    protectedRoots, compatibility.CurrentBuild, compatibility.OsArchitecture, CancellationToken.None).ConfigureAwait(false);
                if (options.Json) Write(true, installed);
                else Console.WriteLine($"Installed trusted extension {installed.PackageId} {installed.Version}.");
                return 0;
            }

            ProfileDocument? profile = null;
            ImmutablePlan plan;
            if (!string.IsNullOrWhiteSpace(options.ProfilePath))
            {
                var profileJson = await File.ReadAllTextAsync(Path.GetFullPath(options.ProfilePath)).ConfigureAwait(false);
                var parsedProfile = ProfileJson.Deserialize(profileJson, catalog);
                if (parsedProfile.Accounts is { IsDefaultOrEmpty: false })
                    ProfileSecurityValidator.ValidatePrivateFile(options.ProfilePath);
                profile = parsedProfile with
                {
                    AllowElevated = options.AllowElevated || parsedProfile.AllowElevated,
                    AllowHighRisk = options.AllowHighRisk || parsedProfile.AllowHighRisk
                };
                plan = new PlanBuilder(catalog).Build(profile, options.Command.Equals("apply", StringComparison.OrdinalIgnoreCase) || options.Command.Equals("resume", StringComparison.OrdinalIgnoreCase) ? compatibility : null);
            }
            else if (options.Command.Equals("resume", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(options.RunId)) throw new CliUsageException("--run-id is required when --profile is omitted for resume.");
                var persisted = await store.LoadAsync(options.RunId, CancellationToken.None).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Run state was not found.");
                if (string.IsNullOrWhiteSpace(persisted.SerializedPlan)) throw new PlanValidationException("Run state has no persisted immutable plan.", ErrorCode.StateCorrupt);
                plan = JsonSerializer.Deserialize<ImmutablePlan>(persisted.SerializedPlan, JsonOptions)
                    ?? throw new PlanValidationException("Persisted run plan is invalid.", ErrorCode.StateCorrupt);
                PlanBuilder.ValidatePlanIntegrity(plan);
            }
            else
            {
                throw new CliUsageException("--profile is required for plan and apply.");
            }

            if (options.Command.Equals("plan", StringComparison.OrdinalIgnoreCase))
            {
                WritePlan(options.Json, plan);
                return 0;
            }

            if (!options.Command.Equals("apply", StringComparison.OrdinalIgnoreCase) && !options.Command.Equals("resume", StringComparison.OrdinalIgnoreCase))
                throw new CliUsageException($"Unknown command '{options.Command}'.");
            if (!compatibility.IsApplySupported) return WriteFailure(options.Json, ErrorCode.UnsupportedOperatingSystem, "Apply is supported only on Windows 11 build 26100+ x64.");
            if (!compatibility.IsAdministrator) return WriteFailure(options.Json, ErrorCode.NotAdministrator, "Apply requires an administrator context.");

            var engine = new ExecutionEngine(new RegisteredTaskExecutor(catalog, new BridgeClient(), compatibility: compatibility), store);
            var coordinator = new RunCoordinator(engine, stateRoot);
            RunStateSnapshot snapshot;
            if (options.Command.Equals("resume", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(options.RunId)) throw new CliUsageException("--run-id is required for resume.");
                snapshot = await coordinator.ResumeAsync(options.RunId, plan, plan.Policy ?? profile?.Policy ?? new ExecutionPolicy(), compatibility.IsAdministrator, CancellationToken.None, profile?.Accounts).ConfigureAwait(false);
            }
            else
            {
                snapshot = await coordinator.ExecuteAsync(plan, plan.Policy ?? profile?.Policy ?? new ExecutionPolicy(), compatibility.IsAdministrator, CancellationToken.None, profile?.Accounts).ConfigureAwait(false);
            }
            WriteSnapshot(options.Json, snapshot);
            return snapshot.State is TaskState.Succeeded or TaskState.NeedsReboot ? 0 : 4;
        }
        catch (CliUsageException exception)
        {
            Console.Error.WriteLine(exception.Message);
            PrintUsage();
            return 5;
        }
        catch (ProfileValidationException exception)
        {
            Console.Error.WriteLine(SensitiveDataRedactor.Redact(exception.Message));
            return 2;
        }
        catch (PlanValidationException exception)
        {
            Console.Error.WriteLine(SensitiveDataRedactor.Redact(exception.Message));
            return (int)exception.Code;
        }
        catch (ConcurrentRunException exception)
        {
            return WriteFailure(options.Json, exception.Code, exception.Message);
        }
        catch (ExtensionInstallException exception)
        {
            if (options.Json) Write(true, new { state = TaskState.Failed, code = exception.Code, message = SensitiveDataRedactor.Redact(exception.Message) });
            else Console.Error.WriteLine($"Failed: {exception.Code} - {SensitiveDataRedactor.Redact(exception.Message)}");
            return 4;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine(SensitiveDataRedactor.Redact(exception.Message));
            return 4;
        }
    }

    private static CliOptions Parse(string[] args)
    {
        var command = "plan";
        var options = new CliOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "-h" or "--help")
            {
                options.Help = true;
                continue;
            }
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (!command.Equals("plan", StringComparison.OrdinalIgnoreCase)) throw new CliUsageException("Only one command may be specified.");
                command = arg;
                continue;
            }
            switch (arg)
            {
                case "--help": options.Help = true; break;
                case "--json": options.Json = true; break;
                case "--allow-elevated": options.AllowElevated = true; break;
                case "--allow-high-risk": options.AllowHighRisk = true; break;
                case "--profile": options.ProfilePath = NextValue(args, ref index, arg); break;
                case "--run-id": options.RunId = NextValue(args, ref index, arg); break;
                case "--output": options.OutputPath = NextValue(args, ref index, arg); break;
                case "--package": options.PackagePath = NextValue(args, ref index, arg); break;
                default: throw new CliUsageException($"Unknown option '{arg}'.");
            }
        }
        options.Command = command;
        return options;
    }

    private static string NextValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal)) throw new CliUsageException($"{option} requires a value.");
        return args[index];
    }

    private static int WriteFailure(bool json, ErrorCode code, string message)
    {
        var safeMessage = SensitiveDataRedactor.Redact(message);
        if (json) Write(true, new { state = TaskState.Failed, code, message = safeMessage });
        else Console.Error.WriteLine($"Failed: {code} - {safeMessage}");
        return (int)code;
    }

    private static void WriteStatus(bool json, CompatibilitySnapshot snapshot)
    {
        if (json) { Write(true, snapshot); return; }
        Console.WriteLine($"{snapshot.ProductName} {snapshot.DisplayVersion} (Build {snapshot.Build}, UBR {snapshot.Ubr})");
        Console.WriteLine($"Edition: {snapshot.Edition}; OS: {snapshot.OsArchitecture}; Process: {snapshot.ProcessArchitecture}");
        Console.WriteLine($"Windows 11: {snapshot.IsWindows11}; Apply supported: {snapshot.IsApplySupported}; Administrator: {snapshot.IsAdministrator}");
        Console.WriteLine($"PowerShell: {snapshot.PowerShellAvailable}; WinGet: {snapshot.WinGetAvailable}; Network: {snapshot.NetworkAvailable}; Proxy: {snapshot.ProxyConfigured}; Windows Update/FoD: {snapshot.WindowsUpdateAvailable}");
    }

    private static void WritePlan(bool json, ImmutablePlan plan)
    {
        var value = new { plan.PlanId, plan.ProfileId, plan.CatalogVersion, plan.MaximumRisk, plan.RequiresReboot, plan.SemanticHash, Tasks = plan.Tasks.Select(task => new { task.TaskId, task.Version, task.Risk, task.Source, task.RequiresReboot, task.ParameterSummary }) };
        if (json) { Write(true, value); return; }
        Console.WriteLine($"Plan {plan.PlanId} | Profile {plan.ProfileId} | Maximum risk {plan.MaximumRisk} | Reboot required {plan.RequiresReboot}");
        Console.WriteLine($"Semantic hash: {plan.SemanticHash}");
        foreach (var task in plan.Tasks) Console.WriteLine($"- {task.TaskId} [{task.Risk}] {task.Source}{(task.RequiresReboot ? " (reboot)" : string.Empty)}");
    }

    private static void WriteCatalog(bool json, IReadOnlyList<TaskDescriptor> tasks)
    {
        if (json)
        {
            Write(true, tasks.Select(task => new
            {
                task.Id,
                task.DisplayName,
                task.Domain,
                task.Risk,
                task.Kind,
                task.Source,
                task.Version,
                task.DefaultSelected,
                task.RequiresAdministrator,
                task.RequiresInternet,
                task.RequiresReboot,
                task.IsAvailable,
                task.UnavailableReason,
                Dependencies = task.Dependencies.IsDefault ? Array.Empty<string>() : task.Dependencies.ToArray(),
                task.ExclusiveGroup,
                task.PackageId,
                task.LanguageTag,
                task.Culture,
                task.ExtensionPackageId,
                task.ExtensionPackageVersion,
                task.ExtensionProtocol
            }));
            return;
        }
        foreach (var task in tasks)
            Console.WriteLine($"{task.Id} | {task.Risk} | {task.Source} | {(task.IsAvailable ? "Available" : task.UnavailableReason)}");
    }

    private static void WriteHistory(bool json, IReadOnlyList<RunStateSnapshot> history)
    {
        if (json) { Write(true, history); return; }
        if (history.Count == 0) { Console.WriteLine("No persisted runs found."); return; }
        foreach (var run in history) Console.WriteLine($"{run.UpdatedAt:O} {run.RunId} {run.State}{(run.RecoveryRequired ? " RecoveryRequired" : string.Empty)}");
    }

    private static void WriteSnapshot(bool json, RunStateSnapshot snapshot)
    {
        if (json) { Write(true, snapshot); return; }
        Console.WriteLine($"Run {snapshot.RunId}: {snapshot.State}");
        foreach (var result in snapshot.Results) Console.WriteLine($"- {result.TaskId}: {result.State} ({result.Code}) {SensitiveDataRedactor.Redact(result.Message)}");
    }

    private static void Write(bool json, object value)
    {
        if (json) Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
        else Console.WriteLine(value);
    }

    private static void WriteSelfTest(bool json, SelfTestReport report)
    {
        if (json)
        {
            Write(true, report);
            return;
        }
        Console.WriteLine($"Self-test: {report.Total} total, {report.Ready} ready, {report.Satisfied} satisfied, {report.Unavailable} unavailable, {report.Failed} failed.");
        foreach (var result in report.Results)
            Console.WriteLine($"- {result.TaskId}: {result.Disposition} ({result.Code}) {SensitiveDataRedactor.Redact(result.Message)}");
    }

    private static void PrintUsage() => Console.Error.WriteLine("WindowsInitializer.Cli status|catalog|self-test|plan|apply|resume|history|diagnostics|extensions|install-extension [--profile path] [--run-id id] [--output path] [--package path] [--allow-elevated] [--allow-high-risk] [--json]");

    private sealed record CliOptions
    {
        public string Command { get; set; } = "plan";
        public string? ProfilePath { get; set; }
        public string? RunId { get; set; }
        public string? OutputPath { get; set; }
        public string? PackagePath { get; set; }
        public bool AllowElevated { get; set; }
        public bool AllowHighRisk { get; set; }
        public bool Json { get; set; }
        public bool Help { get; set; }
    }

    private sealed class CliUsageException(string message) : Exception(message);
}
