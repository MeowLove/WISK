using Wisk.Contracts;
using Wisk.Execution;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class HistoryRetentionTests
{
    [Fact]
    public async Task CleanupAppliesCountAndAgeAfterDryRun()
    {
        var root = TempRoot();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var store = new AtomicJsonStateStore(root);
            await Save(store, "newest", TaskState.Succeeded, now);
            await Save(store, "second", TaskState.Failed, now.AddDays(-10));
            await Save(store, "beyond-count", TaskState.Succeeded, now.AddDays(-20));
            await Save(store, "expired", TaskState.Succeeded, now.AddDays(-100));
            var policy = new HistoryRetentionPolicy(2, TimeSpan.FromDays(90));

            var preview = await store.CleanupAsync(policy, dryRun: true, now);
            Assert.Equal(2, preview.Deleted);
            Assert.Equal(4, (await store.ListAsync(CancellationToken.None)).Count);

            var result = await store.CleanupAsync(policy, dryRun: false, now);
            Assert.Equal(2, result.Deleted);
            Assert.Equal(["newest", "second"], (await store.ListAsync(CancellationToken.None)).Select(item => item.RunId));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CleanupProtectsActiveAndRecoveryFiles()
    {
        var root = TempRoot();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var store = new AtomicJsonStateStore(root);
            await Save(store, "running", TaskState.Running, now.AddYears(-1));
            await File.WriteAllTextAsync(Path.Combine(root, "corrupt.json"), "not-json");
            await Save(store, "old-complete", TaskState.Succeeded, now.AddYears(-1));

            var result = await store.CleanupAsync(new HistoryRetentionPolicy(1, TimeSpan.FromDays(1)), now: now);

            Assert.Equal(2, result.Protected);
            Assert.Equal(1, result.Deleted);
            var remaining = await store.ListAsync(CancellationToken.None);
            Assert.Contains(remaining, item => item.RunId == "running");
            Assert.Contains(remaining, item => item.RunId == "corrupt" && item.RecoveryRequired);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CleanupRejectsUnsafePolicy()
    {
        var root = TempRoot();
        try
        {
            var store = new AtomicJsonStateStore(root);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.CleanupAsync(new HistoryRetentionPolicy(0)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static Task Save(AtomicJsonStateStore store, string runId, TaskState state, DateTimeOffset updatedAt) =>
        store.SaveAsync(new RunStateSnapshot(runId, "plan", "hash", state, [], updatedAt), CancellationToken.None);

    private static string TempRoot() => Path.Combine(Path.GetTempPath(), "WISK wisk-history-" + Guid.NewGuid().ToString("N"));
}
