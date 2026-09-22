using Wisk.Contracts;
using Wisk.Core;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class CatalogSoftwareTests
{
    [Fact]
    public void StoreAdditionsExposeVerifiedWingetPackagesWithoutDefaults()
    {
        var catalog = new Catalog();
        var expected = new Dictionary<string, (string PackageId, string Domain)>(StringComparer.OrdinalIgnoreCase)
        {
            ["app-libreoffice"] = ("TheDocumentFoundation.LibreOffice", "Office and productivity"),
            ["app-thunderbird"] = ("Mozilla.Thunderbird", "Office and productivity"),
            ["app-obsidian"] = ("Obsidian.Obsidian", "Office and productivity"),
            ["app-discord"] = ("Discord.Discord", "Communication"),
            ["app-github-desktop"] = ("GitHub.GitHubDesktop", "Development tools"),
            ["app-rufus"] = ("Rufus.Rufus", "System utilities"),
            ["app-inkscape"] = ("Inkscape.Inkscape", "Graphics and media"),
            ["app-putty"] = ("PuTTY.PuTTY", "Networking"),
            ["app-winscp"] = ("WinSCP.WinSCP", "Networking")
        };

        foreach (var (taskId, expectedPackage) in expected)
        {
            var task = catalog.Find(taskId) ?? throw new Xunit.Sdk.XunitException($"Missing catalog task: {taskId}");
            Assert.Equal(TaskKind.Winget, task.Kind);
            Assert.Equal(expectedPackage.PackageId, task.PackageId);
            Assert.Equal(expectedPackage.Domain, task.Domain);
            Assert.True(task.RequiresInternet);
            Assert.False(task.DefaultSelected);
            Assert.True(task.ResourceLocks?.Contains("winget") == true);
            Assert.Equal(RollbackSupport.Conditional, task.Rollback);
        }
    }
}
