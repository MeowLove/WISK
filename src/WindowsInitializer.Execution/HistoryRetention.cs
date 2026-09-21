using System.Collections.Immutable;

namespace WindowsInitializer.Execution;

public sealed record HistoryRetentionPolicy(int MaxRuns = 50, TimeSpan? MaxAge = null)
{
    public static HistoryRetentionPolicy Default { get; } = new(50, TimeSpan.FromDays(90));
    public TimeSpan EffectiveMaxAge => MaxAge ?? TimeSpan.FromDays(90);

    public void Validate()
    {
        if (MaxRuns < 1) throw new ArgumentOutOfRangeException(nameof(MaxRuns), "At least one completed run must be retained.");
        if (EffectiveMaxAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(MaxAge), "Retention age must be positive.");
    }
}

public sealed record HistoryCleanupReport(
    int Examined,
    int Retained,
    int Protected,
    int Deleted,
    bool DryRun,
    ImmutableArray<string> DeletedRunIds);
