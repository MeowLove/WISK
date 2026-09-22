using System.Collections.Immutable;
using WindowsInitializer.Contracts;
using WindowsInitializer.Platform.Windows;
using WindowsInitializer.PowerShell;
using Xunit;

namespace WindowsInitializer.Core.Tests;

public sealed class WindowsTaskExecutorTests
{
    [Fact]
    public async Task ExecutorMapsFixedBridgeResponses()
    {
        var bridge = new FakeBridge();
        var executor = new WindowsTaskExecutor(bridge);
        var task = new PlannedTask("runtime-webview2", "2.0.0", RiskLevel.Standard, TaskSource.BuiltIn, [], false, string.Empty);

        var check = await executor.CheckAsync(task, CancellationToken.None);
        var apply = await executor.ApplyAsync(task, new ApplyContext("run", new WindowsInitializer.Core.PlanBuilder(new WindowsInitializer.Core.Catalog()).Build(new ProfileDocument("2.0", "p", ["runtime-webview2"], new ProfileTarget(), new ExecutionPolicy(), false, false)), CancellationToken.None, TimeSpan.FromSeconds(1), true));

        Assert.Equal(TaskState.Ready, check.State);
        Assert.Equal(TaskState.Succeeded, apply.State);
        Assert.Equal("Apply", bridge.LastOperation);
    }

    [Fact]
    public async Task SystemSettingForwardsOnlyWhitelistedPlanValue()
    {
        var bridge = new FakeBridge();
        var executor = new WindowsTaskExecutor(bridge, new WindowsInitializer.Core.Catalog());
        var task = new PlannedTask("computer-name", "2.0.0", RiskLevel.Elevated, TaskSource.BuiltIn, [], true, "safe-host");

        await executor.CheckAsync(task, CancellationToken.None);

        Assert.Equal("safe-host", bridge.LastParameters["value"]);
    }

    [Fact]
    public async Task ConfigurableRegistrySettingForwardsExplicitState()
    {
        var bridge = new FakeBridge();
        var executor = new WindowsTaskExecutor(bridge, new WindowsInitializer.Core.Catalog());
        var task = new PlannedTask("setting-fast-startup", "3.0.0", RiskLevel.Elevated, TaskSource.BuiltIn, [], false, "disabled");

        await executor.CheckAsync(task, CancellationToken.None);

        Assert.Equal("disabled", bridge.LastParameters["value"]);
    }

    [Fact]
    public async Task AccountContextIsSentOnlyAsStructuredBridgePayload()
    {
        var bridge = new FakeBridge();
        var executor = new WindowsTaskExecutor(bridge, new WindowsInitializer.Core.Catalog());
        var task = new PlannedTask("accounts-local", "2.0.0", RiskLevel.Elevated, TaskSource.BuiltIn, [], false, "initializer");
        var plan = new WindowsInitializer.Core.PlanBuilder(new WindowsInitializer.Core.Catalog()).Build(new ProfileDocument("2.0", "p", ["accounts-local"], new ProfileTarget(), new ExecutionPolicy(), true, false));
        var context = new ApplyContext("run", plan, CancellationToken.None, TimeSpan.FromSeconds(1), true,
            [new ProfileAccount("initializer", null, null, "S-1-5-32-545", false, false, false, "placeholder")]);

        await executor.ApplyAsync(task, context);

        Assert.Contains("accountsJson", bridge.LastParameters.Keys);
        Assert.Contains("initializer", bridge.LastParameters["accountsJson"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountContextIsNotSentToUnrelatedTask()
    {
        var bridge = new FakeBridge();
        var executor = new WindowsTaskExecutor(bridge, new WindowsInitializer.Core.Catalog());
        var task = new PlannedTask("language-ui-preference", "2.0.0", RiskLevel.Elevated, TaskSource.BuiltIn, [], true, "en-US");
        var plan = new WindowsInitializer.Core.PlanBuilder(new WindowsInitializer.Core.Catalog()).Build(new ProfileDocument("2.0", "p", ["language-ui-preference"], new ProfileTarget(), new ExecutionPolicy(), true, false,
            Parameters: ImmutableDictionary<string, string>.Empty.Add("language-ui-preference", "en-US")));
        var context = new ApplyContext("run", plan, CancellationToken.None, TimeSpan.FromSeconds(1), true,
            [new ProfileAccount("initializer", null, null, null, false, false, false, "placeholder")]);

        await executor.ApplyAsync(task, context);

        Assert.DoesNotContain("accountsJson", bridge.LastParameters.Keys);
    }

    [Theory]
    [InlineData(ErrorCode.Timeout, TaskState.Failed, true)]
    [InlineData(ErrorCode.Cancelled, TaskState.Cancelled, false)]
    [InlineData(ErrorCode.MissingPowerShell, TaskState.UnsupportedPrerequisite, false)]
    public async Task CheckPreservesBridgeFailureClassification(ErrorCode code, TaskState expectedState, bool retryable)
    {
        var executor = new WindowsTaskExecutor(new FailingBridge(code));
        var task = new PlannedTask("language-zh-cn", "2.0.0", RiskLevel.Standard, TaskSource.BuiltIn, [], false, string.Empty);

        var result = await executor.CheckAsync(task, CancellationToken.None);

        Assert.Equal(expectedState, result.State);
        Assert.Equal(code, result.Code);
        Assert.Equal(retryable, result.Retryable);
    }

    private sealed class FakeBridge : IBridgeInvoker
    {
        public string? LastOperation { get; private set; }
        public IReadOnlyDictionary<string, string> LastParameters { get; private set; } = new Dictionary<string, string>();
        public Task<BridgeResponse> InvokeAsync(BridgeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastOperation = request.Operation;
            LastParameters = request.Parameters;
            return Task.FromResult(new BridgeResponse("1.0", request.RunId, request.TaskId,
                request.Operation == "Check" ? TaskState.Ready : TaskState.Succeeded, ErrorCode.None, "ok", true, false, "ok"));
        }
    }

    private sealed class FailingBridge(ErrorCode code) : IBridgeInvoker
    {
        public Task<BridgeResponse> InvokeAsync(BridgeRequest request, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new BridgeFailureException("classified bridge failure", code);
    }
}
