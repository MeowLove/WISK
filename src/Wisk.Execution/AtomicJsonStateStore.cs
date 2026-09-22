using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wisk.Contracts;

namespace Wisk.Execution;

public sealed class AtomicJsonStateStore : IRunStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AtomicJsonStateStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(RunStateSnapshot snapshot, CancellationToken cancellationToken)
    {
        var finalPath = GetPath(snapshot.RunId);
        var tempPath = finalPath + ".tmp";
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, finalPath, true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            _gate.Release();
        }
    }

    public async Task<RunStateSnapshot?> LoadAsync(string runId, CancellationToken cancellationToken)
    {
        var path = GetPath(runId);
        if (!File.Exists(path)) return null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = File.OpenRead(path);
            try
            {
                var snapshot = await JsonSerializer.DeserializeAsync<RunStateSnapshot>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                return IsValidSnapshot(snapshot, runId) ? snapshot : RecoverySnapshot(runId, path);
            }
            catch (JsonException)
            {
                return RecoverySnapshot(runId, path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<RunStateSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await ReadEntriesUnsafeAsync(cancellationToken).ConfigureAwait(false))
                .Select(entry => entry.Snapshot).OrderByDescending(item => item.UpdatedAt).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<HistoryCleanupReport> CleanupAsync(
        HistoryRetentionPolicy policy, bool dryRun = false, DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = (await ReadEntriesUnsafeAsync(cancellationToken).ConfigureAwait(false))
                .OrderByDescending(entry => entry.Snapshot.UpdatedAt).ToArray();
            var protectedEntries = entries.Where(entry => IsProtected(entry.Snapshot)).ToArray();
            var eligible = entries.Where(entry => !IsProtected(entry.Snapshot)).ToArray();
            var cutoff = (now ?? DateTimeOffset.UtcNow) - policy.EffectiveMaxAge;
            var selected = eligible.Where((entry, index) => index >= policy.MaxRuns || entry.Snapshot.UpdatedAt < cutoff).ToArray();
            if (!dryRun)
                foreach (var entry in selected) File.Delete(entry.Path);
            return new HistoryCleanupReport(entries.Length, entries.Length - selected.Length, protectedEntries.Length,
                selected.Length, dryRun, selected.Select(entry => entry.Snapshot.RunId).ToImmutableArray());
        }
        finally { _gate.Release(); }
    }

    private async Task<List<StateEntry>> ReadEntriesUnsafeAsync(CancellationToken cancellationToken)
    {
        var entries = new List<StateEntry>();
        foreach (var path in Directory.EnumerateFiles(_root, "*.json", SearchOption.TopDirectoryOnly))
        {
            var fileRunId = Path.GetFileNameWithoutExtension(path);
            try
            {
                await using var stream = File.OpenRead(path);
                var snapshot = await JsonSerializer.DeserializeAsync<RunStateSnapshot>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                var usableSnapshot = IsValidSnapshot(snapshot, fileRunId) ? snapshot! : RecoverySnapshot(fileRunId, path);
                entries.Add(new StateEntry(path, usableSnapshot));
            }
            catch (JsonException)
            {
                entries.Add(new StateEntry(path, RecoverySnapshot(fileRunId, path)));
            }
        }
        return entries;
    }

    private static RunStateSnapshot RecoverySnapshot(string runId, string path) => new(runId, string.Empty, string.Empty,
        TaskState.RecoveryRequired, [], new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero), RecoveryRequired: true);

    private static bool IsValidSnapshot(RunStateSnapshot? snapshot, string expectedRunId) =>
        snapshot is not null && !string.IsNullOrWhiteSpace(snapshot.RunId) &&
        snapshot.RunId.Equals(expectedRunId, StringComparison.Ordinal);

    private static bool IsProtected(RunStateSnapshot snapshot) => snapshot.RecoveryRequired || snapshot.State is
        TaskState.Pending or TaskState.Checking or TaskState.Ready or TaskState.Running or TaskState.RecoveryRequired;

    private sealed record StateEntry(string Path, RunStateSnapshot Snapshot);

    private string GetPath(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId) || runId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("RunId is not a safe file name.", nameof(runId));
        return Path.Combine(_root, runId + ".json");
    }
}

public sealed class RunLease : IDisposable
{
    private readonly FileStream _stream;

    private RunLease(FileStream stream) => _stream = stream;

    public static RunLease Acquire(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "run.lock");
        try
        {
            return new RunLease(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException exception)
        {
            throw new ConcurrentRunException("Another initializer run is active.", exception);
        }
    }

    public void Dispose() => _stream.Dispose();
}

public sealed class ConcurrentRunException(string message, Exception innerException) : InvalidOperationException(message, innerException)
{
    public ErrorCode Code => ErrorCode.ConcurrentRun;
}
