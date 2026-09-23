using Wisk.Platform.Windows;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class WindowsCompatibilityTests
{
    [Fact]
    public void Supported26100X64IsApplyCompatible()
    {
        var probe = new FakeProbe(26100, "x64", "x64");
        var snapshot = new WindowsCompatibility(probe).Read();

        Assert.True(snapshot.IsWindows11);
        Assert.True(snapshot.IsSupportedBuild);
        Assert.True(snapshot.IsApplySupported);
        Assert.True(snapshot.PowerShellAvailable);
        Assert.True(snapshot.WinGetAvailable);
    }

    [Fact]
    public void OlderBuildCanReportStatusButCannotApply()
    {
        var snapshot = new WindowsCompatibility(new FakeProbe(22631, "x64", "x64")).Read();

        Assert.True(snapshot.IsWindows11);
        Assert.False(snapshot.IsSupportedBuild);
        Assert.False(snapshot.IsApplySupported);
    }

    [Fact]
    public void StatusIncludesSupportingEnvironmentSignals()
    {
        var snapshot = new WindowsCompatibility(new FakeProbe(26100, "x64", "x64")).Read();

        Assert.Equal(100, snapshot.Ubr);
        Assert.Equal("Professional", snapshot.Edition);
        Assert.True(snapshot.NetworkAvailable);
        Assert.False(snapshot.ProxyConfigured);
        Assert.True(snapshot.WindowsUpdateAvailable);
    }

    [Fact]
    public void NonX64ProcessCannotApply()
    {
        var snapshot = new WindowsCompatibility(new FakeProbe(26100, "x64", "x86")).Read();

        Assert.True(snapshot.IsSupportedBuild);
        Assert.False(snapshot.IsApplySupported);
    }

    [Fact]
    public void ServerBuild26100RemainsUnsupported()
    {
        var snapshot = new WindowsCompatibility(new FakeProbe(26200, "x64", "x64", "Windows Server 2025 Datacenter")).Read();

        Assert.False(snapshot.IsWindows11);
        Assert.False(snapshot.IsSupportedBuild);
        Assert.False(snapshot.IsApplySupported);
    }

    [Fact]
    public void ModernWindowsClientUsesBuildWhenRegistryProductNameIsLegacy()
    {
        var snapshot = new WindowsCompatibility(new FakeProbe(26200, "x64", "x64", "Windows 10 Enterprise")).Read();

        Assert.Equal("Windows 11 Enterprise", snapshot.ProductName);
        Assert.True(snapshot.IsWindows11);
        Assert.True(snapshot.IsSupportedBuild);
        Assert.True(snapshot.IsApplySupported);
    }

    [Fact]
    public void Windows10DisplayNameRemainsForBuildsBelowWindows11()
    {
        var snapshot = new WindowsCompatibility(new FakeProbe(19045, "x64", "x64", "Windows 10 Enterprise")).Read();

        Assert.Equal("Windows 10 Enterprise", snapshot.ProductName);
        Assert.False(snapshot.IsWindows11);
        Assert.False(snapshot.IsApplySupported);
    }

    private sealed class FakeProbe(int build, string osArchitecture, string processArchitecture, string productName = "Windows 11 Pro") : IWindowsSystemProbe
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ProductName"] = productName,
            ["DisplayVersion"] = "24H2",
            ["CurrentBuildNumber"] = build.ToString(),
            ["UBR"] = "100",
            ["EditionID"] = "Professional"
        };

        public string OsArchitecture { get; } = osArchitecture;
        public string ProcessArchitecture { get; } = processArchitecture;
        public string? ReadCurrentVersion(string name) => _values.TryGetValue(name, out var value) ? value : null;
        public bool FileExists(string path) => path.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith("winget.exe", StringComparison.OrdinalIgnoreCase);
        public bool IsAdministrator() => true;
        public bool IsNetworkAvailable() => true;
        public bool IsProxyConfigured() => false;
        public bool IsWindowsUpdateAvailable() => true;
    }
}
