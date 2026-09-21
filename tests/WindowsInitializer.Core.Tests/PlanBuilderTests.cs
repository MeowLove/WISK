using System.Collections.Immutable;
using Xunit;
using WindowsInitializer.Contracts;
using WindowsInitializer.Core;

namespace WindowsInitializer.Core.Tests;

public sealed class PlanBuilderTests
{
    [Fact]
    public void InstalledExtensionActivatesMatchingOfflineTaskAndPinsItInPlan()
    {
        var installPath = Path.Combine(Path.GetTempPath(), "windows-initializer-extension", "demo-1.0.0");
        var installed = new InstalledExtension("demo", "1.0.0", "Contoso", installPath, new string('A', 64),
            DateTimeOffset.UtcNow, [new ExtensionFileEntry("worker.exe", new string('B', 64), true)],
            [new ExtensionTaskEntry("runtime-ms-bundle", "worker.exe")]);
        var catalog = new Catalog([installed]);
        var profile = new ProfileDocument("2.0", "extension", ["runtime-ms-bundle"], new ProfileTarget(),
            new ExecutionPolicy(), false, true);

        var plan = new PlanBuilder(catalog).Build(profile);

        var task = Assert.Single(plan.Tasks);
        Assert.Equal(TaskSource.ControlledExtension, task.Source);
        Assert.Equal("demo", task.ExtensionPackageId);
        Assert.Equal(Path.Combine(installPath, "worker.exe"), task.ExtensionExecutablePath);
        Assert.NotNull(plan.ExtensionPackageHash);
        PlanBuilder.ValidatePlanIntegrity(plan);
    }

    [Fact]
    public void CatalogContainsStableTasksWithoutAutomaticDefaults()
    {
        var catalog = new Catalog();
        var defaults = catalog.GetDefaultTasks().Select(task => task.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Empty(defaults);
        Assert.Equal(RiskLevel.High, catalog.Find("system-rdp-wrapper")!.Risk);
        Assert.Equal(RiskLevel.Elevated, catalog.Find("runtime-dotnet-6")!.Risk);
        Assert.Equal(RiskLevel.Elevated, catalog.Find("app-everything")!.Risk);
        Assert.Equal("voidtools.Everything.Alpha", catalog.Find("app-everything")!.PackageId);
        Assert.True(catalog.Find("legacy-god-mode")!.IsAvailable);
        Assert.Equal(TaskSource.BuiltIn, catalog.Find("legacy-god-mode")!.Source);
        Assert.Equal(TaskKind.SystemSetting, catalog.Find("legacy-god-mode")!.Kind);
        Assert.False(catalog.Find("runtime-ms-bundle")!.IsAvailable);
        Assert.Equal(TaskSource.ControlledExtension, catalog.Find("runtime-ms-bundle")!.Source);
        var requiredIds = new[]
        {
            "computer-name", "accounts-local", "language-zh-cn", "language-zh-tw", "language-en-us", "language-en-gb",
            "language-zh-sg", "language-zh-hk", "language-ui-preference", "font-supplements-cjk-indic-europe", "feature-wireless-display",
            "device-setup-region", "runtime-ms-bundle", "runtime-dotnet-8", "runtime-dotnet-6", "runtime-edge", "runtime-webview2",
            "terminal-powershell7", "app-vscode", "app-pixpin-beta", "app-everything", "app-chocolatey", "app-peazip", "app-localsend", "app-syncthing",
            "app-syncthingtray", "app-obs", "app-advanced-ip-scanner", "app-wsl-manager", "app-wsa-toolbox", "app-sandboxie-plus", "app-fxsound",
            "app-escrcpy", "app-nanabox", "app-docker-desktop", "app-vmware-workstation", "app-openvpn", "app-energy-star-x", "legacy-god-mode",
            "legacy-cursor-macos", "system-rdp-wrapper", "legacy-dismpp", "legacy-glary-utilities", "legacy-windows11-easy-settings",
            "legacy-asus-oled-screensaver", "legacy-careueyes", "legacy-netsetman", "system-dnscrypt-proxy", "legacy-miniupnp",
            "legacy-edge-installer", "legacy-chrome-installer", "legacy-purecodec", "legacy-2345-pic", "legacy-imfile", "legacy-desktop-shortcuts",
            "legacy-bandizip", "legacy-imgdrive", "legacy-huorong", "legacy-sogou-input", "legacy-flclash"
        };
        Assert.All(requiredIds, id => Assert.NotNull(catalog.Find(id)));
    }

    [Fact]
    public void BuildOrdersDependenciesAndHashesSemanticContent()
    {
        var catalog = new Catalog();
        var builder = new PlanBuilder(catalog);
        var profile = new ProfileDocument(
            "2.0", "test-profile", ["runtime-dotnet-8", "runtime-webview2"],
            new ProfileTarget(), new ExecutionPolicy(), false, false,
            Parameters: ImmutableDictionary<string, string>.Empty.Add("runtime-dotnet-8", "install"));

        var plan = builder.Build(profile);

        Assert.Equal(2, plan.Tasks.Length);
        Assert.Equal(plan.SemanticHash, PlanBuilder.ComputeSemanticHash(plan));
        PlanBuilder.ValidatePlanIntegrity(plan);
    }

    [Fact]
    public void FastStartupCanBePlannedWithoutSeparateHibernationTask()
    {
        var builder = new PlanBuilder(new Catalog());
        var profile = new ProfileDocument("2.0", "fast-startup", ["setting-fast-startup"], new ProfileTarget(),
            new ExecutionPolicy(), true, false,
            Parameters: ImmutableDictionary<string, string>.Empty.Add("setting-fast-startup", "enabled"));

        var plan = builder.Build(profile);

        var task = Assert.Single(plan.Tasks);
        Assert.Equal("setting-fast-startup", task.TaskId);
        Assert.Empty(task.Dependencies);
    }

    [Fact]
    public void SoftwareActionMustBeExplicitlySupported()
    {
        var profile = new ProfileDocument("2.0", "invalid-action", ["app-vscode"], new ProfileTarget(),
            new ExecutionPolicy(), true, false,
            Parameters: ImmutableDictionary<string, string>.Empty.Add("app-vscode", "uninstall"));
        var error = Assert.Throws<PlanValidationException>(() => new PlanBuilder(new Catalog()).Build(profile));
        Assert.Equal(ErrorCode.InvalidProfile, error.Code);
    }

    [Fact]
    public void RelationPlannerResolvesHardDependenciesAndKeepsRecommendationsOptional()
    {
        var catalog = new Catalog();

        var closure = TaskRelationPlanner.RequiredClosure(catalog, "app-wsl-manager");
        var recommendations = TaskRelationPlanner.Recommendations(catalog.Find("app-docker-desktop")!);
        var dependents = TaskRelationPlanner.Dependents(catalog, closure.Select(task => task.Id), "feature-wsl");

        Assert.Equal(["feature-wsl", "app-wsl-manager"], closure.Select(task => task.Id));
        Assert.Equal(2, recommendations.Length);
        Assert.Equal("app-wsl-manager", Assert.Single(dependents).Id);
    }

    [Fact]
    public void ExecutionPolicyIsPartOfImmutablePlanIntegrity()
    {
        var profile = new ProfileDocument("2.0", "policy", ["runtime-webview2"], new ProfileTarget(),
            new ExecutionPolicy(StopOnError: false, MaxRetries: 2, DefaultTimeout: TimeSpan.FromMinutes(4)), false, false);
        var plan = new PlanBuilder(new Catalog()).Build(profile);

        Assert.Equal(profile.Policy, plan.Policy);
        Assert.Throws<PlanValidationException>(() => PlanBuilder.ValidatePlanIntegrity(plan with
        {
            Policy = plan.Policy! with { MaxRetries = 5 }
        }));
    }

    [Fact]
    public void ProgrammaticProfileCannotBypassExecutionPolicyBounds()
    {
        var profile = new ProfileDocument("2.0", "policy", ["runtime-webview2"], new ProfileTarget(),
            new ExecutionPolicy(DefaultTimeout: TimeSpan.FromDays(1)), false, false);

        var exception = Assert.Throws<PlanValidationException>(() => new PlanBuilder(new Catalog()).Build(profile));

        Assert.Equal(ErrorCode.InvalidProfile, exception.Code);
    }

    [Fact]
    public void HighRiskTaskRequiresExplicitAuthorization()
    {
        var builder = new PlanBuilder(new Catalog());
        var profile = new ProfileDocument("2.0", "high", ["system-rdp-wrapper"], new ProfileTarget(), new ExecutionPolicy(), false, false);

        var exception = Assert.Throws<PlanValidationException>(() => builder.Build(profile));

        Assert.Equal(ErrorCode.PolicyBlocked, exception.Code);
    }

    [Fact]
    public void ExclusiveRegionalFormatsCannotBeSelectedTogether()
    {
        var builder = new PlanBuilder(new Catalog());
        var profile = new ProfileDocument("2.0", "regional", ["language-zh-sg", "language-zh-hk"], new ProfileTarget(), new ExecutionPolicy(), false, false);

        var exception = Assert.Throws<PlanValidationException>(() => builder.Build(profile));

        Assert.Equal(ErrorCode.MutualExclusion, exception.Code);
    }

    [Fact]
    public void ParameterSummaryRedactsSecretKeys()
    {
        var builder = new PlanBuilder(new Catalog());
        var parameters = ImmutableDictionary<string, string>.Empty.Add("accounts-local", "password=never-log-this");
        var profile = new ProfileDocument("2.0", "redaction", ["accounts-local"], new ProfileTarget(), new ExecutionPolicy(), true, false, Parameters: parameters);

        var plan = builder.Build(profile);

        Assert.Equal("[REDACTED]", plan.Tasks[0].ParameterSummary);
    }

    [Fact]
    public void UnavailableExtensionTaskCannotEnterPlan()
    {
        var builder = new PlanBuilder(new Catalog());
        var profile = new ProfileDocument("2.0", "offline", ["runtime-ms-bundle"], new ProfileTarget(), new ExecutionPolicy(), false, true);

        var exception = Assert.Throws<PlanValidationException>(() => builder.Build(profile));

        Assert.Equal(ErrorCode.UnavailableExtension, exception.Code);
    }

    [Fact]
    public void BuiltInGodModeTaskCanEnterAStandardPlan()
    {
        var profile = new ProfileDocument("2.0", "god-mode", ["legacy-god-mode"], new ProfileTarget(),
            new ExecutionPolicy(), false, false);

        var plan = new PlanBuilder(new Catalog()).Build(profile);

        var task = Assert.Single(plan.Tasks);
        Assert.Equal(TaskSource.BuiltIn, task.Source);
        Assert.Equal(RiskLevel.Standard, task.Risk);
    }

    [Fact]
    public void AccountPlanContainsNamesButNeverPasswords()
    {
        var account = new ProfileAccount("initializer", "Initializer", null, "S-1-5-32-545", false, false, false, "placeholder");
        var profile = new ProfileDocument("2.0", "accounts", ["accounts-local"], new ProfileTarget(), new ExecutionPolicy(), true, false, Accounts: [account]);

        var plan = new PlanBuilder(new Catalog()).Build(profile);

        Assert.Equal("initializer", plan.Tasks[0].ParameterSummary);
        Assert.DoesNotContain("placeholder", plan.Tasks[0].ParameterSummary, StringComparison.Ordinal);
    }
}
