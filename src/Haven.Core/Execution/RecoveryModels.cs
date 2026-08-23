namespace Haven.Core;

public enum RecoveryClass
{
    Autonomous = 0,
    UserInformationOrPermission = 1,
    RiskyOrDestructive = 2
}

public enum RecoveryStage
{
    SafeAutomaticRetry = 0,
    SafeAutonomousRepair = 1,
    SafeAlternative = 2,
    AskUserForRequiredInformation = 3,
    RequestRiskyApproval = 4,
    Exhausted = 5
}

public sealed record RecoveryRiskAssessment(
    bool InsideAuthorisedScope,
    bool Reversible,
    bool AltersUserData,
    bool HasExternalImpact,
    bool ExpandsPermissions,
    bool RequiresUnknownCredential,
    bool Destructive,
    double Confidence)
{
    public bool MayRecoverAutonomously =>
        InsideAuthorisedScope && Reversible && !AltersUserData && !HasExternalImpact &&
        !ExpandsPermissions && !RequiresUnknownCredential && !Destructive && Confidence >= 0.75;
}

public sealed record RecoveryAttempt(
    Guid Id,
    Guid ExecutionId,
    Guid FailedActionId,
    RecoveryClass Classification,
    RecoveryStage Stage,
    string FailureSignature,
    int Attempt,
    int MaximumAttempts,
    RecoveryRiskAssessment Risk,
    string SafeDiagnosis,
    string? SafeRepair,
    DateTimeOffset CreatedAt);

public static class RecoveryPolicyDefaults
{
    public const int MaximumEquivalentFailures = 3;
    public const int MaximumAutomaticAttempts = 3;
    public static readonly TimeSpan InitialUserInteractionTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaximumInteractiveWait = TimeSpan.FromMinutes(5);
}

public enum ToolFailureKind
{
    Unknown = 0,
    Transient = 1,
    PermissionRequired = 2,
    CredentialRequired = 3,
    ResourceUnavailable = 4,
    InvalidInput = 5,
    ExternalFailure = 6
}

/// <summary>Structured, safe failure metadata supplied by a tool runtime. Raw tool arguments and secrets never belong here.</summary>
public sealed record ToolFailureDescriptor(
    string Code,
    ToolFailureKind Kind,
    string SafeMessage,
    string ComponentId,
    string ComponentName,
    RecoveryRiskAssessment Risk,
    bool Retryable,
    RemediationType? SuggestedRemediation = null,
    string? ProviderName = null,
    IReadOnlyList<RemediationInput>? RequiredInputs = null,
    string? SecretProviderId = null,
    string? SecretName = null);

public enum RemediationType
{
    SecretInput = 0,
    OAuthReconnect = 1,
    PermissionRequest = 2,
    ResourceSelection = 3,
    Confirmation = 4,
    Configuration = 5,
    UserChoice = 6,
    ManualAction = 7
}

public enum RemediationSensitivity { Normal = 0, Sensitive = 1, Secret = 2 }
public enum RemediationState { Waiting = 0, InProgress = 1, Suspended = 2, Completed = 3, Cancelled = 4, Expired = 5, Failed = 6 }

public sealed record RemediationInput(
    string Key,
    string Label,
    string InputType,
    bool Required,
    RemediationSensitivity Sensitivity,
    string? HelpText = null);

/// <summary>Host-rendered remediation request. Secret values are never stored in this record.</summary>
public sealed record RemediationRequest(
    Guid Id,
    Guid ExecutionId,
    Guid ActionId,
    RemediationType Type,
    string Title,
    string Explanation,
    string RequestingComponentId,
    string RequestingComponentName,
    string? ProviderName,
    IReadOnlyList<RemediationInput> RequiredInputs,
    IReadOnlyList<string> AllowedActions,
    RemediationSensitivity Sensitivity,
    bool CanRetry,
    bool CanResume,
    TimeSpan IdleTimeout,
    TimeSpan MaximumWait,
    RemediationState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset? ExpiresAt = null,
    string? CredentialReference = null,
    string? SecretProviderId = null,
    string? SecretName = null);
