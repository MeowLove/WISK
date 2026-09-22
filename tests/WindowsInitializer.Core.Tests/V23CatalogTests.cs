using WindowsInitializer.Contracts;
using WindowsInitializer.Core;
using Xunit;

namespace WindowsInitializer.Core.Tests;

public sealed class V23CatalogTests
{
    [Fact]
    public void PriorityWindowsFeaturesHaveConcreteBuiltInRoutes()
    {
        var catalog = new Catalog();
        var ids = new[] { "feature-wsl", "feature-virtual-machine-platform", "feature-sandbox", "feature-hyper-v", "capability-openssh-client", "feature-telnet-client" };

        Assert.All(ids, id =>
        {
            var task = Assert.IsType<TaskDescriptor>(catalog.Find(id));
            Assert.Equal(TaskKind.Capability, task.Kind);
            Assert.True(task.RequiresAdministrator);
        });
    }

    [Fact]
    public void WslBootstrapRequiresNetwork()
    {
        var task = Assert.IsType<TaskDescriptor>(new Catalog().Find("feature-wsl"));

        Assert.True(task.RequiresInternet);
        Assert.True(task.RequiresReboot);
    }

    [Fact]
    public void LegacyMachineScopedPackagesKeepTheirInstallScope()
    {
        var catalog = new Catalog();
        var ids = new[]
        {
            "runtime-edge", "runtime-webview2", "app-vscode", "app-everything", "app-chocolatey", "app-peazip",
            "app-localsend", "app-syncthing", "app-syncthingtray", "app-docker-desktop", "app-obs",
            "app-advanced-ip-scanner", "app-wsl-manager", "app-openvpn", "app-sandboxie-plus", "app-fxsound", "app-escrcpy"
        };

        Assert.All(ids, id => Assert.Equal("machine", Assert.IsType<TaskDescriptor>(catalog.Find(id)).PackageScope));
    }

    [Fact]
    public void CatalogNeverSelectsTasksByDefault()
    {
        Assert.All(new Catalog().GetTasks(), task => Assert.False(task.DefaultSelected, task.Id));
        Assert.Empty(new Catalog().GetDefaultTasks());
    }

    [Theory]
    [InlineData("setting-long-paths")]
    [InlineData("setting-developer-mode")]
    [InlineData("setting-show-file-extensions")]
    [InlineData("setting-hibernation")]
    [InlineData("setting-fast-startup")]
    public void PriorityWindowsSettingsHaveConcreteBuiltInRoutes(string taskId)
    {
        var task = Assert.IsType<TaskDescriptor>(new Catalog().Find(taskId));
        Assert.Equal(TaskKind.SystemSetting, task.Kind);
        Assert.Equal("3.0.0", task.Version);
    }

    [Theory]
    [InlineData("runtime-vcredist-2015-x64", "Microsoft.VCRedist.2015+.x64")]
    [InlineData("runtime-vcredist-2015-x86", "Microsoft.VCRedist.2015+.x86")]
    [InlineData("runtime-dotnet-10", "Microsoft.DotNet.DesktopRuntime.10")]
    [InlineData("app-7zip", "7zip.7zip")]
    [InlineData("app-git", "Git.Git")]
    [InlineData("terminal-windows", "Microsoft.WindowsTerminal")]
    [InlineData("app-powertoys", "Microsoft.PowerToys")]
    public void PrioritySoftwareUsesFixedWingetIds(string taskId, string packageId)
    {
        var task = Assert.IsType<TaskDescriptor>(new Catalog().Find(taskId));
        Assert.Equal(TaskKind.Winget, task.Kind);
        Assert.Equal(packageId, task.PackageId);
        Assert.False(task.DefaultSelected);
    }

    [Theory]
    [InlineData("runtime-python-313", "Python.Python.3.13")]
    [InlineData("runtime-nodejs-lts", "OpenJS.NodeJS.LTS")]
    [InlineData("app-github-cli", "GitHub.cli")]
    [InlineData("runtime-dotnet-sdk-10", "Microsoft.DotNet.SDK.10")]
    [InlineData("runtime-temurin-17", "EclipseAdoptium.Temurin.17.JDK")]
    [InlineData("runtime-temurin-21", "EclipseAdoptium.Temurin.21.JDK")]
    [InlineData("app-sumatra-pdf", "SumatraPDF.SumatraPDF")]
    [InlineData("app-keepassxc", "KeePassXCTeam.KeePassXC")]
    [InlineData("app-sharex", "ShareX.ShareX")]
    [InlineData("app-sysinternals-suite", "Microsoft.Sysinternals.Suite")]
    public void PhaseSevenSoftwareUsesVerifiedWingetIds(string taskId, string packageId)
    {
        var task = Assert.IsType<TaskDescriptor>(new Catalog().Find(taskId));
        Assert.Equal(packageId, task.PackageId);
        Assert.Equal(TaskKind.Winget, task.Kind);
        Assert.Equal("3.0.0", task.Version);
        Assert.False(task.DefaultSelected);
    }

    [Theory]
    [InlineData("safety-restore-point")]
    [InlineData("setting-windows-update-mode")]
    [InlineData("setting-power-plan")]
    [InlineData("setting-sleep-timeouts")]
    public void PhaseSevenSystemTasksHaveConcreteBuiltInRoutes(string taskId)
    {
        var task = Assert.IsType<TaskDescriptor>(new Catalog().Find(taskId));
        Assert.Equal(TaskKind.SystemSetting, task.Kind);
        Assert.True(task.RequiresAdministrator);
        Assert.True(WindowsInitializer.PowerShell.BridgeClient.SupportsTask(taskId));
    }

    [Fact]
    public void SupportedRegistryTasksAdvertiseExactRollback()
    {
        var tasks = new Catalog().GetTasks().Where(task => task.Kind == TaskKind.RegistrySetting && task.IsAvailable).ToArray();

        Assert.NotEmpty(tasks);
        Assert.All(tasks, task => Assert.Equal(RollbackSupport.Exact, task.Rollback));
        Assert.All(tasks, task => Assert.Contains("registry", task.ResourceLocks ?? []));
    }
}
