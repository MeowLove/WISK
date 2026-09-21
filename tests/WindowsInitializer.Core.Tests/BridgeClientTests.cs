using System.Collections.Immutable;
using WindowsInitializer.Contracts;
using WindowsInitializer.Core;
using WindowsInitializer.PowerShell;
using Xunit;

namespace WindowsInitializer.Core.Tests;

public sealed class BridgeClientTests
{
    [Fact]
    public async Task StructuredRequestIsCorrelatedAndParsed()
    {
        var runner = new FakeRunner(Frame("{\"protocolVersion\":\"1.0\",\"runId\":\"run\",\"taskId\":\"language-zh-cn\",\"status\":\"Ready\",\"code\":\"None\",\"message\":\"ok\",\"changed\":false,\"rebootRequired\":false,\"summary\":\"ok\"}"));
        var client = new BridgeClient(runner);
        var request = new BridgeRequest("1.0", "run", "language-zh-cn", "Check", ImmutableDictionary<string, string>.Empty);

        var response = await client.InvokeAsync(request, TimeSpan.FromSeconds(5));

        Assert.Equal(TaskState.Ready, response.Status);
        Assert.Equal("language-zh-cn", response.TaskId);
    }

    [Fact]
    public async Task SecretParameterIsRejectedBeforeProcessLaunch()
    {
        var runner = new FakeRunner("");
        var client = new BridgeClient(runner);
        var parameters = ImmutableDictionary<string, string>.Empty.Add("password", "not-used");
        var request = new BridgeRequest("1.0", "run", "accounts-local", "Check", parameters);

        var exception = await Assert.ThrowsAsync<BridgeFailureException>(() => client.InvokeAsync(request, TimeSpan.FromSeconds(5)));

        Assert.Equal(ErrorCode.InvalidProfile, exception.Code);
        Assert.False(runner.Called);
    }

    [Fact]
    public async Task UnknownParameterIsRejectedBeforeProcessLaunch()
    {
        var runner = new FakeRunner(string.Empty);
        var request = new BridgeRequest("1.0", "run", "language-zh-cn", "Check",
            ImmutableDictionary<string, string>.Empty.Add("value", "unreviewed"));

        var exception = await Assert.ThrowsAsync<BridgeFailureException>(() =>
            new BridgeClient(runner).InvokeAsync(request, TimeSpan.FromSeconds(5)));

        Assert.Equal(ErrorCode.InvalidProfile, exception.Code);
        Assert.False(runner.Called);
    }

    [Fact]
    public async Task BuiltInLanguageAndGodModeAdaptersAreEmbeddedInBridge()
    {
        var runner = new FakeRunner(Frame("{\"protocolVersion\":\"1.0\",\"runId\":\"run\",\"taskId\":\"language-zh-cn\",\"status\":\"Ready\",\"code\":\"None\",\"message\":\"ok\",\"changed\":false,\"rebootRequired\":false,\"summary\":\"ok\"}"));
        var client = new BridgeClient(runner);

        await client.InvokeAsync(new BridgeRequest("1.0", "run", "language-zh-cn", "Check", ImmutableDictionary<string, string>.Empty), TimeSpan.FromSeconds(5));

        Assert.Contains("Install-Language -Language $languageTag -ErrorAction Stop | Out-Null", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("Get-InstalledLanguage -ErrorAction Stop", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("zh-Hans-CN", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("foreach ($language in $installed)", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("$entry = $newLanguageList[0]", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("Add-WindowsCapability -Online -Name $name -ErrorAction Stop | Out-Null", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("Get-BridgeSupplementalFontCapabilities", runner.FixedCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("Install-Language -Language $languageTags[$taskId] -CopyToSettings", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains(BridgeClient.ResponseMarker, runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("Windows God Mode", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("New-LocalUser", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("ConvertTo-SecureString", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("Get-LocalGroup -SID", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("S-1-5-32-555", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("SpecialAccounts\\UserList", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("DeviceRegion", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("CultureInfo]::GetCultureInfo", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("Set-WinUILanguageOverride -Language $culture", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("Get-Command -Name Get-WinUILanguageOverride", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("Microsoft-Windows-Subsystem-Linux", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("Containers-DisposableClientVM", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("Microsoft-Hyper-V-All", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("OpenSSH.Client~~~~0.0.1.0", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("TelnetClient", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("LongPathsEnabled", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("AllowDevelopmentWithoutDevLicense", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("HideFileExt", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("powercfg.exe", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("HiberbootEnabled", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("Enable-WindowsOptionalFeature", runner.FixedCommand, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigurableRegistrySettingsUseEnabledStateParameters()
    {
        foreach (var taskId in ConfigurableRegistrySettingCatalog.All.Select(setting => setting.TaskId))
            Assert.True(BridgeClient.SupportsTask(taskId));

        var runner = new FakeRunner(Frame("{\"protocolVersion\":\"1.0\",\"runId\":\"run\",\"taskId\":\"setting-long-paths\",\"status\":\"Ready\",\"code\":\"None\",\"message\":\"ok\",\"changed\":false,\"rebootRequired\":false,\"summary\":\"ok\"}"));
        var request = new BridgeRequest("1.0", "run", "setting-long-paths", "Check", ImmutableDictionary<string, string>.Empty.Add("value", "disabled"));

        _ = await new BridgeClient(runner).InvokeAsync(request, TimeSpan.FromSeconds(5));

        Assert.Contains("ConvertFrom-BridgeBoolean", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("LongPathsEnabled", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("Enabled = 1; Disabled = 0", runner.FixedCommand, StringComparison.Ordinal);
        Assert.Contains("Set-BridgeRegistrySetting", runner.FixedCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("__CONFIGURABLE_REGISTRY_SETTINGS__", runner.FixedCommand, StringComparison.Ordinal);
        foreach (var setting in ConfigurableRegistrySettingCatalog.All)
        {
            Assert.Contains($"'{setting.TaskId}'", runner.FixedCommand, StringComparison.Ordinal);
            Assert.Contains($"Name = '{setting.ValueName}'", runner.FixedCommand, StringComparison.Ordinal);
            Assert.Contains($"Enabled = {setting.EnabledValue}; Disabled = {setting.DisabledValue}", runner.FixedCommand, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UnframedCmdletOutputCannotCorruptTheBridgeResponse()
    {
        const string json = "{\"protocolVersion\":\"1.0\",\"runId\":\"run\",\"taskId\":\"language-zh-cn\",\"status\":\"Ready\",\"code\":\"None\",\"message\":\"ok\",\"changed\":false,\"rebootRequired\":false,\"summary\":\"ok\"}";
        var runner = new FakeRunner("LanguagePack install output\r\n" + Frame(json) + "\r\nwarning output");

        var response = await new BridgeClient(runner).InvokeAsync(
            new BridgeRequest("1.0", "run", "language-zh-cn", "Check", ImmutableDictionary<string, string>.Empty),
            TimeSpan.FromSeconds(5));

        Assert.Equal(TaskState.Ready, response.Status);
    }

    [Theory]
    [InlineData("setting-taskbar-seconds", "enabled", 1)]
    [InlineData("setting-taskbar-seconds", "disabled", 0)]
    [InlineData("setting-taskbar-end-task", "enabled", 1)]
    [InlineData("setting-taskbar-end-task", "disabled", 0)]
    [InlineData("setting-taskbar-widgets", "enabled", 1)]
    [InlineData("setting-taskbar-widgets", "disabled", 0)]
    [InlineData("setting-notification-banners", "enabled", 1)]
    [InlineData("setting-notification-banners", "disabled", 0)]
    [InlineData("setting-lock-screen-notifications", "enabled", 1)]
    [InlineData("setting-lock-screen-notifications", "disabled", 0)]
    [InlineData("setting-explorer-this-pc", "enabled", 1)]
    [InlineData("setting-explorer-this-pc", "disabled", 2)]
    public async Task RegistryOptionCheckRecognizesBothConfiguredStatesWithoutReadingHostRegistry(string id, string value, int storedValue)
    {
        if (!OperatingSystem.IsWindows()) return;
        var prefix = "function Get-ItemPropertyValue { [CmdletBinding()] param($Path, $Name) return " + storedValue + " }\n";
        var response = await new BridgeClient(new PrefixingProcessRunner(prefix)).InvokeAsync(
            new BridgeRequest("1.0", "configured-check", id, "Check", ImmutableDictionary<string, string>.Empty.Add("value", value)),
            TimeSpan.FromSeconds(15));
        Assert.Equal(TaskState.Skipped, response.Status);
    }

    [Theory]
    [InlineData("{\"protocolVersion\":\"1.0\"}")]
    [InlineData(BridgeClient.ResponseMarker + "{}\n" + BridgeClient.ResponseMarker + "{}")]
    public async Task MissingOrDuplicateFramedResponseIsRejected(string output)
    {
        var exception = await Assert.ThrowsAsync<BridgeFailureException>(() => new BridgeClient(new FakeRunner(output)).InvokeAsync(
            new BridgeRequest("1.0", "run", "language-zh-cn", "Check", ImmutableDictionary<string, string>.Empty),
            TimeSpan.FromSeconds(5)));

        Assert.Equal(ErrorCode.ProcessFailed, exception.Code);
    }

    [Fact]
    public async Task LanguageCheckEnumeratesNoEnumerateCollectionsReturnedByWindowsCmdlets()
    {
        if (!OperatingSystem.IsWindows()) return;
        var powerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powerShell)) return;
        var response = await new BridgeClient(new PrefixingProcessRunner(NoEnumerateLanguagePrelude)).InvokeAsync(
            new BridgeRequest("1.0", "collection-check", "language-zh-cn", "Check", ImmutableDictionary<string, string>.Empty),
            TimeSpan.FromSeconds(15));

        Assert.Equal(TaskState.Skipped, response.Status);
    }

    [Fact]
    public void EveryBuiltInCatalogTaskHasAConcreteExecutionRoute()
    {
        var catalog = new Catalog();
        var builtIn = catalog.GetTasks().Where(task => task.Source == TaskSource.BuiltIn).ToArray();

        Assert.All(builtIn.Where(task => task.Kind == TaskKind.Winget),
            task => Assert.False(string.IsNullOrWhiteSpace(task.PackageId)));
        Assert.All(builtIn.Where(task => task.Kind is not TaskKind.Winget and not TaskKind.RegistrySetting),
            task => Assert.True(BridgeClient.SupportsTask(task.Id), $"No fixed bridge adapter exists for {task.Id}."));
        Assert.All(builtIn.Where(task => task.Kind == TaskKind.RegistrySetting),
            task => Assert.NotEmpty(RegistryOptimizationCatalog.GetTaskEntries(task.Id)));
        Assert.All(catalog.GetTasks().Where(task => task.Kind == TaskKind.OfflineAsset), task =>
        {
            Assert.Equal(TaskSource.ControlledExtension, task.Source);
            Assert.False(task.IsAvailable);
        });
    }

    [Fact]
    public async Task EmbeddedBridgeParsesAndRunsAReadOnlyCheckOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var powerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powerShell)) return;
        var request = new BridgeRequest("1.0", "syntax-check", "legacy-god-mode", "Check",
            ImmutableDictionary<string, string>.Empty);

        var response = await new BridgeClient().InvokeAsync(request, TimeSpan.FromSeconds(15));

        Assert.Equal("legacy-god-mode", response.TaskId);
        Assert.Contains(response.Status, new[] { TaskState.Ready, TaskState.Skipped });
    }

    private static string Frame(string json) => BridgeClient.ResponseMarker + json;

    [Fact]
    public async Task RunnerAcceptsScriptLargerThanWindowsCommandLineWithoutChangingRequest()
    {
        if (!OperatingSystem.IsWindows()) return;
        var executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(executable)) return;
        var script = "#" + new string('x', 50000) + "\n[Console]::Out.Write([Console]::In.ReadToEnd())";
        var result = await new PowerShellProcessRunner().RunAsync(executable, script, "{\"value\":\"unchanged\"}", TimeSpan.FromSeconds(15), CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("{\"value\":\"unchanged\"}", result.Stdout);
    }

    private const string NoEnumerateLanguagePrelude = """
function Get-InstalledLanguage {
  [CmdletBinding()] param()
  $items = [System.Collections.Generic.List[object]]::new()
  [void]$items.Add([pscustomobject]@{ LanguageId = 'zh-CN'; LanguagePacks = @('LpCab', 'LXP') })
  Write-Output -NoEnumerate $items
}
function Get-WinUserLanguageList {
  [CmdletBinding()] param()
  $items = [System.Collections.Generic.List[object]]::new()
  [void]$items.Add([pscustomobject]@{ LanguageTag = 'zh-Hans-CN' })
  Write-Output -NoEnumerate $items
}
""";

    private sealed class PrefixingProcessRunner(string prefix) : IBridgeProcessRunner
    {
        private readonly PowerShellProcessRunner _inner = new();

        public Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
            string executablePath, string fixedCommand, string requestJson, TimeSpan timeout, CancellationToken cancellationToken) =>
            _inner.RunAsync(executablePath, prefix + Environment.NewLine + fixedCommand, requestJson, timeout, cancellationToken);
    }

    private sealed class FakeRunner(string output) : IBridgeProcessRunner
    {
        public bool Called { get; private set; }
        public string? TaskId { get; private set; }
        public string FixedCommand { get; private set; } = string.Empty;
        public Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string executablePath, string fixedCommand, string requestJson, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Called = true;
            FixedCommand = fixedCommand;
            TaskId = "task";
            return Task.FromResult((0, output, string.Empty));
        }
    }
}
