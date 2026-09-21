using WindowsInitializer.Contracts;
using WindowsInitializer.Core;
using Xunit;

namespace WindowsInitializer.Core.Tests;

public sealed class PlanImpactAnalyzerTests
{
    [Fact]
    public void SummarizesOperationalImpactWithoutMutatingTheCatalog()
    {
        var catalog = new Catalog();
        var tasks = new[]
        {
            catalog.Find("setting-fast-startup")!,
            catalog.Find("app-docker-desktop")!,
            catalog.Find("registry-group-take-ownership-menus")!
        };

        var summary = PlanImpactAnalyzer.Analyze(tasks);

        Assert.Equal(3, summary.TaskCount);
        Assert.Equal(3, summary.AdministratorCount);
        Assert.Equal(0, summary.DependencyCount);
        Assert.Equal(2, summary.RecommendationCount);
        Assert.Equal(2, summary.ExactRollbackCount);
        Assert.Equal(1, summary.ConditionalRollbackCount);
        Assert.Contains("registry", summary.ResourceLocks);
        Assert.True(summary.RestorePointRecommended);
    }

    [Fact]
    public void RestorePointSuppressesRecommendation()
    {
        var catalog = new Catalog();

        var summary = PlanImpactAnalyzer.Analyze([
            catalog.Find("setting-power-plan")!,
            catalog.Find("safety-restore-point")!]);

        Assert.True(summary.IncludesRestorePoint);
        Assert.False(summary.RestorePointRecommended);
    }
}
