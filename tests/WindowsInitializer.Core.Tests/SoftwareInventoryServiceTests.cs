using WindowsInitializer.Core;
using WindowsInitializer.Platform.Windows;
using Xunit;

namespace WindowsInitializer.Core.Tests;

public sealed class SoftwareInventoryServiceTests
{
    [Fact]
    public async Task ReadsCatalogInventoryWithTwoWinGetCalls()
    {
        var runner = new InventoryRunner(
            (0, "Visual Studio Code  Microsoft.VisualStudioCode  1.0", ""),
            (0, "Visual Studio Code  Microsoft.VisualStudioCode  2.0", ""));

        var snapshot = await new SoftwareInventoryService(new Catalog(), runner).ReadAsync();

        Assert.Equal(SoftwareInventoryStatus.UpgradeAvailable, snapshot.Items["app-vscode"].Status);
        Assert.Equal(SoftwareInventoryStatus.NotInstalled, snapshot.Items["app-git"].Status);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal("list", runner.Calls[0][0]);
        Assert.Equal("list", runner.Calls[1][0]);
        Assert.Contains("--upgrade-available", runner.Calls[1]);
        Assert.All(runner.Calls, call => Assert.DoesNotContain("--source", call));
        Assert.DoesNotContain("--id", runner.Calls[1]);
        Assert.DoesNotContain("--all", runner.Calls[1]);
    }

    [Fact]
    public async Task UpdateDetectionFailureDoesNotClaimInstalledSoftwareIsCurrent()
    {
        var runner = new InventoryRunner(
            (0, "Visual Studio Code  Microsoft.VisualStudioCode  1.0", ""),
            (1, "", "source unavailable"));
        var snapshot = await new SoftwareInventoryService(new Catalog(), runner).ReadAsync();
        Assert.NotNull(snapshot.Error);
        Assert.Equal(SoftwareInventoryStatus.DetectionFailed, snapshot.Items["app-vscode"].Status);
    }

    [Fact]
    public async Task NoUpdatesExitCodeIsAValidEmptyUpdateList()
    {
        var runner = new InventoryRunner(
            (0, "Visual Studio Code  Microsoft.VisualStudioCode  1.0", ""),
            (unchecked((int)0x8A150014), "", ""));
        var snapshot = await new SoftwareInventoryService(new Catalog(), runner).ReadAsync();
        Assert.Null(snapshot.Error);
        Assert.Equal(SoftwareInventoryStatus.Installed, snapshot.Items["app-vscode"].Status);
    }

    [Fact]
    public async Task FailedInventoryMarksEveryCatalogPackageUnavailable()
    {
        var runner = new InventoryRunner((1, "", "failure"));

        var snapshot = await new SoftwareInventoryService(new Catalog(), runner).ReadAsync();

        Assert.NotNull(snapshot.Error);
        Assert.NotEmpty(snapshot.Items);
        Assert.All(snapshot.Items.Values, item => Assert.Equal(SoftwareInventoryStatus.DetectionFailed, item.Status));
    }

    [Fact]
    public async Task PackageIdMatchingUsesWholeTokens()
    {
        var runner = new InventoryRunner(
            (0, "Other  Microsoft.VisualStudioCode.Insiders  1.0", ""),
            (0, "", ""));

        var snapshot = await new SoftwareInventoryService(new Catalog(), runner).ReadAsync();

        Assert.Equal(SoftwareInventoryStatus.NotInstalled, snapshot.Items["app-vscode"].Status);
    }

    private sealed class InventoryRunner(params (int ExitCode, string Stdout, string Stderr)[] results) : IWinGetProcessRunner
    {
        private int _index;
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
            IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add(arguments.ToArray());
            var result = results[Math.Min(_index++, results.Length - 1)];
            return Task.FromResult(result);
        }
    }
}
