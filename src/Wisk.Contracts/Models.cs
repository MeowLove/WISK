using System.Collections.Immutable;

namespace Wisk.Contracts;

public enum RiskLevel { Standard, Elevated, High }

public enum TaskState
{
    Pending, Checking, Ready, Running, Applying, Verifying, Succeeded, Failed, Skipped, Cancelled,
    NeedsReboot, NeedsManualReview, UnsupportedPrerequisite, UnavailableExtension, RecoveryRequired
}

public enum TaskSource { BuiltIn, ControlledExtension }

public enum TaskRelationKind { Requires, ConflictsWith, Recommends, Supersedes, OrderAfter }

public enum RollbackSupport { Exact, Conditional, Manual }

public enum ExecutionBoundary { None, Reboot, SignOut }

public sealed record TaskRelation(string TargetTaskId, TaskRelationKind Kind, string ReasonKey);

public enum TaskKind
{
    Winget, LanguagePack, RegionalFormat, Capability, Account, SystemSetting, RegistrySetting, OfflineAsset, ManualReview
}

public enum ErrorCode
{
    None, InvalidProfile, UnknownTask, DependencyCycle, MutualExclusion,
    UnsupportedOperatingSystem, UnsupportedArchitecture, NotAdministrator, MissingPowerShell,
    MissingWinGet, NetworkUnavailable, PolicyBlocked, Timeout, Cancelled, AccessDenied,
    ProcessFailed, VerificationFailed, RetryExhausted, PlanTampered, StateCorrupt, ConcurrentRun,
    ExtensionNotTrusted, ExtensionHashMismatch, ExtensionIncompatible, ExtensionPathUnsafe,
    ExtensionAssetRejected, UnavailableExtension, ManualReviewRequired, StoppedByEarlierFailure
}

public sealed record TaskDescriptor(
    string Id,
    string DisplayName,
    string Domain,
    RiskLevel Risk,
    TaskKind Kind,
    TaskSource Source = TaskSource.BuiltIn,
    string Version = "3.0.0",
    bool DefaultSelected = false,
    bool RequiresAdministrator = false,
    bool RequiresInternet = false,
    bool RequiresReboot = false,
    bool IsAvailable = true,
    string? UnavailableReason = null,
    ImmutableArray<string> Dependencies = default,
    string? ExclusiveGroup = null,
    string? PackageId = null,
    string? LanguageTag = null,
    string? Culture = null,
    string? ExtensionPackageId = null,
    string? ExtensionPackageVersion = null,
    string? ExtensionManifestHash = null,
    string? ExtensionExecutablePath = null,
    string? ExtensionExecutableHash = null,
    string? ExtensionProtocol = null,
    ImmutableArray<TaskRelation>? Relations = null,
    ImmutableArray<string>? ResourceLocks = null,
    RollbackSupport Rollback = RollbackSupport.Manual,
    ExecutionBoundary Boundary = ExecutionBoundary.None,
    string? PackageScope = null);

public sealed record CompatibilitySnapshot(
    string ProductName,
    string DisplayVersion,
    int CurrentBuild,
    int Ubr,
    string Edition,
    string OsArchitecture,
    string ProcessArchitecture,
    bool IsWindows11,
    bool IsSupportedBuild,
    bool IsAdministrator,
    bool PowerShellAvailable,
    bool WinGetAvailable,
    bool NetworkAvailable,
    bool ProxyConfigured,
    bool WindowsUpdateAvailable,
    bool IsApplySupported)
{
    public CompatibilitySnapshot(string productName, string build, string architecture, bool isAdministrator, bool powerShellAvailable, bool winGetAvailable)
        : this(productName, string.Empty, int.TryParse(build, out var parsedBuild) ? parsedBuild : 0, 0, string.Empty,
            architecture, architecture, true, parsedBuild >= 26100, isAdministrator, powerShellAvailable,
            winGetAvailable, false, false, false, false)
    { }

    public string Build => CurrentBuild.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string Architecture => OsArchitecture;
}

public sealed record ProfileDocument(
    string SchemaVersion,
    string ProfileId,
    ImmutableArray<string> Tasks,
    ProfileTarget Target,
    ExecutionPolicy Policy,
    bool AllowElevated,
    bool AllowHighRisk,
    string? Proxy = null,
    ImmutableDictionary<string, string>? Parameters = null,
    ImmutableArray<ProfileAccount>? Accounts = null);

public sealed record PlanTemplateItem(string TaskId, string? Value = null);

public sealed record PlanTemplateDocument(
    string SchemaVersion,
    string TemplateId,
    string Name,
    DateTimeOffset CreatedAt,
    ImmutableArray<PlanTemplateItem> Items,
    ExecutionPolicy Policy,
    bool AllowElevated,
    bool AllowHighRisk);

public sealed record ProfileTarget(int MinimumBuild = 26100, string Architecture = "x64", bool RequireWindows11 = true);

public sealed record ProfileAccount(
    string Name, string? FullName, string? Description, string? GroupSid,
    bool PasswordNeverExpires, bool HideFromSignInScreen, bool AllowRemoteDesktop, string? Password);

public sealed record ExecutionPolicy(
    bool StopOnError = true, int MaxRetries = 0, TimeSpan? DefaultTimeout = null, TimeSpan? RetryBaseDelay = null)
{
    public TimeSpan EffectiveTimeout => DefaultTimeout ?? TimeSpan.FromMinutes(10);
    public TimeSpan EffectiveRetryBaseDelay => RetryBaseDelay ?? TimeSpan.FromSeconds(2);
}

public sealed record TaskCheckResult(
    string TaskId, TaskState State, ErrorCode Code, string Message,
    bool AlreadyComplete = false, bool CanApply = true, bool RequiresReboot = false, bool Retryable = false);

public sealed record PlannedTask(
    string TaskId, string Version, RiskLevel Risk, TaskSource Source,
    ImmutableArray<string> Dependencies, bool RequiresReboot, string ParameterSummary,
    TaskState CheckState = TaskState.Pending, bool RequiresAdministrator = false,
    string? ExtensionPackageId = null, string? ExtensionPackageVersion = null,
    string? ExtensionManifestHash = null, string? ExtensionExecutablePath = null,
    string? ExtensionExecutableHash = null, string? ExtensionProtocol = null,
    ImmutableArray<TaskRelation>? Relations = null, ImmutableArray<string>? ResourceLocks = null,
    RollbackSupport Rollback = RollbackSupport.Manual, ExecutionBoundary Boundary = ExecutionBoundary.None,
    string? Proxy = null, string? PackageScope = null);

public sealed record ImmutablePlan(
    string PlanId, string ProfileId, string CatalogVersion, DateTimeOffset CreatedAt,
    ImmutableArray<PlannedTask> Tasks, RiskLevel MaximumRisk, bool RequiresReboot,
    string SemanticHash, string? ExtensionPackageHash = null, ExecutionPolicy? Policy = null)
{
    public ImmutablePlan WithSemanticHash(string semanticHash) => this with { SemanticHash = semanticHash };
}

public sealed record ApplyContext(
    string RunId,
    ImmutablePlan Plan,
    CancellationToken CancellationToken,
    TimeSpan Timeout,
    bool IsElevated,
    ImmutableArray<ProfileAccount> Accounts = default);

public sealed record VerifyResult(string TaskId, bool Succeeded, ErrorCode Code, string Message, bool RebootRequired = false);

public sealed record ExecutionResult(
    string TaskId, TaskState State, string Code, string Message, bool Changed, bool RebootRequired,
    bool Retryable = false, DateTimeOffset? StartedAt = null, DateTimeOffset? CompletedAt = null,
    string? FailureStage = null, int? ProcessExitCode = null);

public sealed record RunStateSnapshot(
    string RunId,
    string PlanId,
    string PlanSemanticHash,
    TaskState State,
    ImmutableArray<ExecutionResult> Results,
    DateTimeOffset UpdatedAt,
    bool RecoveryRequired = false,
    string? SerializedPlan = null);

public sealed record BridgeRequest(
    string ProtocolVersion, string RunId, string TaskId, string Operation, ImmutableDictionary<string, string> Parameters);

public sealed record BridgeResponse(
    string ProtocolVersion, string RunId, string TaskId, TaskState Status, ErrorCode Code,
    string Message, bool Changed, bool RebootRequired, string Summary);
