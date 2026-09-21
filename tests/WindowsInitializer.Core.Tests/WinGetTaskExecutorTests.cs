using WindowsInitializer.Contracts;
using WindowsInitializer.Core;
using WindowsInitializer.Platform.Windows;
using Xunit;

namespace WindowsInitializer.Core.Tests;

public sealed class WinGetTaskExecutorTests
{
    private const int NoApplicationsFoundExitCode = unchecked((int)0x8A150014);

    [Fact]
    public async Task CheckUsesCatalogPackageIdAndDetectsInstalledPackage()
    {
        var runner = new FakeRunner(0, "Name  Microsoft.EdgeWebView2Runtime  1.0", string.Empty);
        var executor = new WinGetTaskExecutor(new Catalog(), runner);
        var task = new PlannedTask("runtime-webview2", "2.0.0", RiskLevel.Standard, TaskSource.BuiltIn, [], false, string.Empty);

        var result = await executor.CheckAsync(task, CancellationToken.None);

        Assert.True(result.AlreadyComplete);
        Assert.Equal(new[] { "list", "--id", "Microsoft.EdgeWebView2Runtime", "--exact", "--source", "winget", "--accept-source-agreements" }, runner.Arguments);
    }

    [Fact]
    public async Task ApplyUsesFixedInstallArguments()
    {
        var runner = new FakeRunner(0, "", "");
        var executor = new WinGetTaskExecutor(new Catalog(), runner);
        var task = new PlannedTask("app-vscode", "2.0.0", RiskLevel.Standard, TaskSource.BuiltIn, [], false, string.Empty);
        var plan = new PlanBuilder(new Catalog()).Build(new ProfileDocument("2.0", "p", ["app-vscode"], new ProfileTarget(), new ExecutionPolicy(), false, false));

        var result = await executor.ApplyAsync(task, new ApplyContext("run", plan, CancellationToken.None, TimeSpan.FromSeconds(1), true));

        Assert.Equal(TaskState.Succeeded, result.State);
        Assert.Contains("install", runner.Arguments);
        Assert.Contains("--accept-package-agreements", runner.Arguments);
        Assert.DoesNotContain("powershell", runner.Arguments, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpgradeCheckRequiresInstalledPackageAndUsesReadOnlyUpgradeListing()
    {
        var runner = new FakeRunner(
            (0, "Name  Id  Version\nPowerShell  9MZ1SNWT0N5D  7.5.0", string.Empty),
            (NoApplicationsFoundExitCode, string.Empty, "No installed package found matching input criteria."));
        var executor = new WinGetTaskExecutor(new Catalog(), runner);
        var task = new PlannedTask("terminal-powershell7", "2.0.0", RiskLevel.Standard, TaskSource.BuiltIn, [], false, "upgrade");

        var result = await executor.CheckAsync(task, CancellationToken.None);

        Assert.True(result.AlreadyComplete);
        Assert.Equal(TaskState.Skipped, result.State);
        Assert.Equal(new[] { "list", "--id", "9MZ1SNWT0N5D", "--exact", "--source", "msstore", "--accept-source-agreements" }, runner.Calls[0]);
        Assert.Equal(new[] { "list", "--id", "9MZ1SNWT0N5D", "--exact", "--upgrade-available", "--source", "msstore", "--accept-source-agreements" }, runner.Calls[1]);
    }

    [Fact]
    public async Task UpgradeCheckIsReadyWhenExactPackageHasEligibleUpdate()
    {
        var runner = new FakeRunner(
            (0, "Name  Id  Version\nVisual Studio Code  Microsoft.VisualStudioCode  1.0", string.Empty),
            (0, "Name  Id  Version  Available\nVisual Studio Code  Microsoft.VisualStudioCode  1.0  1.1", string.Empty));
        var executor = new WinGetTaskExecutor(new Catalog(), runner);
        var task = new PlannedTask("app-vscode", "2.0.0", RiskLevel.Standard, TaskSource.BuiltIn, [], false, "upgrade");

        var result = await executor.CheckAsync(task, CancellationToken.None);

        Assert.Equal(TaskState.Ready, result.State);
        Assert.False(result.AlreadyComplete);
    }

    [Fact]
    public async Task UpgradeCheckRejectsPackageThatIsNotInstalled()
    {
        var runner = new FakeRunner(NoApplicationsFoundExitCode, string.Empty, "No installed package found matching input criteria.");
        var executor = new WinGetTaskExecutor(new Catalog(), runner);
        var task = new PlannedTask("app-vscode", "2.0.0", RiskLevel.Standard, TaskSource.BuiltIn, [], false, "upgrade");

        var result = await executor.CheckAsync(task, CancellationToken.None);

        Assert.Equal(TaskState.NeedsManualReview, result.State);
        Assert.False(result.CanApply);
        Assert.Equal(1, runner.RunCount);
    }

    [Fact]
    public async Task UnknownListExitCodeFailsInsteadOfTreatingPackageAsAbsent()
    {
        var runner = new FakeRunner(17, string.Empty, "unexpected failure");
        var executor = new WinGetTaskExecutor(new Catalog(), runner);
        var task = new PlannedTask("app-vscode", "2.0.0", RiskLevel.Standard, TaskSource.BuiltIn, [], false, string.Empty);

        var result = await executor.CheckAsync(task, CancellationToken.None);

        Assert.Equal(TaskState.Failed, result.State);
        Assert.Equal(ErrorCode.ProcessFailed, result.Code);
        Assert.False(result.CanApply);
    }

    [Fact]
    public async Task ApplyUpgradeUsesUpgradeCommandAndFixedStoreSource()
    {
        var runner = new FakeRunner(0, string.Empty, string.Empty);
        var executor = new WinGetTaskExecutor(new Catalog(), runner);
        var task = new PlannedTask("terminal-powershell7", "2.0.0", RiskLevel.Standard, TaskSource.BuiltIn, [], false, "upgrade");
        var plan = new PlanBuilder(new Catalog()).Build(new ProfileDocument("2.0", "p", ["terminal-powershell7"], new ProfileTarget(), new ExecutionPolicy(), false, false));

        var result = await executor.ApplyAsync(task, new ApplyContext("run", plan, CancellationToken.None, TimeSpan.FromSeconds(1), true));

        Assert.Equal(TaskState.Succeeded, result.State);
        Assert.Equal("upgrade", runner.Arguments[0]);
        Assert.Equal("9MZ1SNWT0N5D", runner.Arguments[2]);
        Assert.Contains("--source", runner.Arguments);
        Assert.Contains("msstore", runner.Arguments);
    }

    [Fact]
    public async Task InvalidWinGetActionFailsCheckAndApplyWithoutStartingProcess()
    {
        var runner = new FakeRunner(0, string.Empty, string.Empty);
        var executor = new WinGetTaskExecutor(new Catalog(), runner);
        var task = new PlannedTask("app-vscode", "2.0.0", RiskLevel.Standard, TaskSource.BuiltIn, [], false, "remove");
        var plan = new PlanBuilder(new Catalog()).Build(new ProfileDocument("2.0", "p", ["app-vscode"], new ProfileTarget(), new ExecutionPolicy(), false, false));

        var check = await executor.CheckAsync(task, CancellationToken.None);
        var apply = await executor.ApplyAsync(task, new ApplyContext("run", plan, CancellationToken.None, TimeSpan.FromSeconds(1), true));

        Assert.Equal(TaskState.NeedsManualReview, check.State);
        Assert.Equal(TaskState.NeedsManualReview, apply.State);
        Assert.Equal(0, runner.RunCount);
    }

    [Theory]
    [InlineData(0, "Code Microsoft.VisualStudioCode 1.0 2.0", false)]
    [InlineData(NoApplicationsFoundExitCode, "", true)]
    [InlineData(17, "", false)]
    public async Task UpgradeVerificationRequiresNoRemainingUpdate(int exitCode, string output, bool verified)
    {
        var runner = new FakeRunner((0, "Code Microsoft.VisualStudioCode 1.0", ""), (exitCode, output, ""));
        var task = new PlannedTask("app-vscode", "3.0.0", RiskLevel.Standard, TaskSource.BuiltIn, [], false, "upgrade");
        var result = await new WinGetTaskExecutor(new Catalog(), runner).VerifyAsync(task, CancellationToken.None);
        Assert.Equal(verified, result.Succeeded);
    }

    [Fact]
    public void LegacyHuorongIsRegisteredAsWinGetTask()
    {
        var descriptor = new Catalog().Find("legacy-huorong");

        Assert.NotNull(descriptor);
        Assert.Equal(TaskKind.Winget, descriptor!.Kind);
        Assert.Equal("XPDNH1FMW7NB40", descriptor.PackageId);
        Assert.Equal(RiskLevel.High, descriptor.Risk);
    }

    [Theory]
    [InlineData(false, true, ErrorCode.NetworkUnavailable)]
    [InlineData(true, false, ErrorCode.MissingWinGet)]
    public async Task PrerequisiteFailureDoesNotStartWinGet(bool networkAvailable, bool winGetAvailable, ErrorCode expectedCode)
    {
        var runner = new FakeRunner(0, string.Empty, string.Empty);
        var compatibility = Compatibility(networkAvailable, winGetAvailable);
        var executor = new RegisteredTaskExecutor(new Catalog(), new FakeBridge(), runner, compatibility);
        var task = new PlannedTask("app-vscode", "2.0.0", RiskLevel.Standard, TaskSource.BuiltIn, [], false, string.Empty);

        var result = await executor.CheckAsync(task, CancellationToken.None);

        Assert.Equal(TaskState.UnsupportedPrerequisite, result.State);
        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(0, runner.RunCount);
    }

    private static CompatibilitySnapshot Compatibility(bool networkAvailable, bool winGetAvailable) =>
        new("Windows 11 Pro", "24H2", 26100, 1, "Professional", "x64", "x64", true, true, true,
            true, winGetAvailable, networkAvailable, false, true, true);

    private sealed class FakeRunner : IWinGetProcessRunner
    {
        private readonly Queue<(int ExitCode, string Stdout, string Stderr)> _responses;

        public FakeRunner(int exitCode, string stdout, string stderr)
            : this([(exitCode, stdout, stderr)])
        {
        }

        public FakeRunner(params (int ExitCode, string Stdout, string Stderr)[] responses)
        {
            _responses = new Queue<(int ExitCode, string Stdout, string Stderr)>(responses);
        }

        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public int RunCount { get; private set; }
        public Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            RunCount++;
            Arguments = arguments.ToArray();
            Calls.Add(Arguments);
            if (_responses.Count == 0)
                throw new InvalidOperationException("The fake runner was called more times than expected.");
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class FakeBridge : WindowsInitializer.PowerShell.IBridgeInvoker
    {
        public Task<BridgeResponse> InvokeAsync(BridgeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The bridge must not run during a failed prerequisite check.");
    }
}
