using System.Text.Json;
using WindowsInitializer.Contracts;
using WindowsInitializer.Core;

namespace WindowsInitializer.Platform.Windows;

public sealed record UacPlanEnvelope(string PlanId, string SemanticHash, string SerializedPlan, DateTimeOffset CreatedAt);
public sealed record UacApplyEnvelope(UacPlanEnvelope Plan, string? ResumeRunId = null);

public static class UacPlanHandoff
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static UacPlanEnvelope Create(ImmutablePlan plan)
    {
        PlanBuilder.ValidatePlanIntegrity(plan);
        return new UacPlanEnvelope(plan.PlanId, plan.SemanticHash, JsonSerializer.Serialize(plan, JsonOptions), DateTimeOffset.UtcNow);
    }

    public static ImmutablePlan Validate(UacPlanEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.SerializedPlan)) throw new InvalidOperationException("UAC plan envelope is empty.");
        var plan = JsonSerializer.Deserialize<ImmutablePlan>(envelope.SerializedPlan, JsonOptions)
            ?? throw new InvalidOperationException("UAC plan envelope is invalid.");
        if (!string.Equals(plan.PlanId, envelope.PlanId, StringComparison.Ordinal) ||
            !string.Equals(plan.SemanticHash, envelope.SemanticHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("UAC plan identity or semantic hash changed.");
        PlanBuilder.ValidatePlanIntegrity(plan);
        return plan;
    }

    public static string WriteApplyEnvelope(ImmutablePlan plan, string root, string? resumeRunId = null)
    {
        var envelope = new UacApplyEnvelope(Create(plan), resumeRunId);
        var directory = Path.GetFullPath(root);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "uac-" + Guid.NewGuid().ToString("N") + ".json");
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(envelope, JsonOptions));
        File.Move(tempPath, path, true);
        return path;
    }

    public static UacApplyEnvelope ReadApplyEnvelope(string path, string? expectedRoot = null)
    {
        var fullPath = Path.GetFullPath(path);
        if (!string.IsNullOrWhiteSpace(expectedRoot))
        {
            var root = Path.GetFullPath(expectedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("UAC plan envelope is outside the controlled state directory.");
        }
        if (!File.Exists(fullPath)) throw new FileNotFoundException("UAC plan envelope was not found.", fullPath);
        var envelope = JsonSerializer.Deserialize<UacApplyEnvelope>(File.ReadAllText(fullPath), JsonOptions)
            ?? throw new InvalidOperationException("UAC apply envelope is invalid.");
        _ = Validate(envelope.Plan);
        return envelope;
    }
}
