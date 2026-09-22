using System.IO;
using Wisk.Core;
using Wisk.Platform.Windows;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class UacPlanHandoffTests
{
    [Fact]
    public void PlanSurvivesUacEnvelopeRoundTrip()
    {
        var plan = new PlanBuilder(new Catalog()).Build(new Wisk.Contracts.ProfileDocument(
            "2.0", "uac", ["runtime-webview2"], new Wisk.Contracts.ProfileTarget(), new Wisk.Contracts.ExecutionPolicy(), false, false));

        var restored = UacPlanHandoff.Validate(UacPlanHandoff.Create(plan));

        Assert.Equal(plan.PlanId, restored.PlanId);
        Assert.Equal(plan.SemanticHash, restored.SemanticHash);
    }

    [Fact]
    public void TamperedEnvelopeIsRejected()
    {
        var plan = new PlanBuilder(new Catalog()).Build(new Wisk.Contracts.ProfileDocument(
            "2.0", "uac", ["runtime-webview2"], new Wisk.Contracts.ProfileTarget(), new Wisk.Contracts.ExecutionPolicy(), false, false));
        var envelope = UacPlanHandoff.Create(plan) with { SemanticHash = "tampered" };

        Assert.Throws<InvalidOperationException>(() => UacPlanHandoff.Validate(envelope));
    }

    [Fact]
    public void ApplyEnvelopePersistsAndRestoresPlanAndPolicy()
    {
        var plan = new PlanBuilder(new Catalog()).Build(new Wisk.Contracts.ProfileDocument(
            "2.0", "uac", ["runtime-webview2"], new Wisk.Contracts.ProfileTarget(),
            new Wisk.Contracts.ExecutionPolicy(MaxRetries: 2, DefaultTimeout: TimeSpan.FromMinutes(3)), false, false));
        var root = Path.Combine(Path.GetTempPath(), "WiskTests", Guid.NewGuid().ToString("N"));

        try
        {
            var path = UacPlanHandoff.WriteApplyEnvelope(plan, root, "resume-1");
            Assert.True(File.Exists(path));

            var restored = UacPlanHandoff.ReadApplyEnvelope(path);
            var restoredPlan = UacPlanHandoff.Validate(restored.Plan);
            Assert.Equal(plan.PlanId, restoredPlan.PlanId);
            Assert.Equal(plan.SemanticHash, restoredPlan.SemanticHash);
            Assert.Equal(2, restoredPlan.Policy!.MaxRetries);
            Assert.Equal(TimeSpan.FromMinutes(3), restoredPlan.Policy.DefaultTimeout);
            Assert.Equal("resume-1", restored.ResumeRunId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingApplyEnvelopeIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "WiskTests", Guid.NewGuid().ToString("N"), "missing.json");
        Assert.Throws<FileNotFoundException>(() => UacPlanHandoff.ReadApplyEnvelope(path));
    }

    [Fact]
    public void ApplyEnvelopeOutsideControlledRootIsRejected()
    {
        var plan = new PlanBuilder(new Catalog()).Build(new Wisk.Contracts.ProfileDocument(
            "2.0", "uac", ["runtime-webview2"], new Wisk.Contracts.ProfileTarget(), new Wisk.Contracts.ExecutionPolicy(), false, false));
        var root = Path.Combine(Path.GetTempPath(), "WiskTests", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "WiskTests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = UacPlanHandoff.WriteApplyEnvelope(plan, outside);
            Assert.Throws<InvalidOperationException>(() => UacPlanHandoff.ReadApplyEnvelope(path, root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }
}
