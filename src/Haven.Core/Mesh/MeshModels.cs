namespace Haven.Core;

public enum MeshDeviceClass
{
    Desktop,
    Laptop,
    Phone,
    Tablet,
    Tv,
    Headset,
    Other
}

public enum MeshPeerTrustState
{
    Pending,
    Trusted,
    Revoked
}

public enum MeshPresenceState
{
    Available,
    Busy,
    Offline,
    Stale
}

public enum MeshConnectionState
{
    Disconnected,
    Pairing,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

public enum MeshTaskStatus
{
    Queued,
    Sending,
    Accepted,
    Running,
    Succeeded,
    Failed,
    CancelRequested,
    Cancelled
}

public enum MeshSyncOperationKind
{
    Upsert,
    Delete
}

public enum MeshSyncDecisionKind
{
    Apply,
    Duplicate,
    IgnoreStale,
    Conflict
}

public enum MeshHandoffStatus
{
    Created,
    Sent,
    Accepted,
    Rejected,
    Failed
}

public sealed record MeshLocalIdentity(
    Guid DeviceId,
    string DisplayName,
    MeshDeviceClass DeviceClass,
    CapabilityPlatform Platform,
    string PublicKeyFingerprint,
    DateTimeOffset CreatedAt);

public sealed record MeshPeerRecord(
    Guid DeviceId,
    string DisplayName,
    MeshDeviceClass DeviceClass,
    CapabilityPlatform Platform,
    string PublicKeyFingerprint,
    MeshPeerTrustState TrustState,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? TrustedAt,
    DateTimeOffset? RevokedAt,
    string? LastKnownEndpoint = null,
    IReadOnlyList<string>? AllowedRemoteCapabilities = null);

public sealed record MeshCapabilityDescriptor(
    string Key,
    string Name,
    CapabilityPlatform Platform,
    CapabilityRiskClass RiskClass,
    IReadOnlyList<string> SemanticActions,
    bool SupportsTaskDelegation = false,
    bool ProvidesModels = false,
    bool ProvidesAgents = false);

public sealed record MeshPresenceSnapshot(
    Guid DeviceId,
    MeshPresenceState Presence,
    MeshConnectionState Connection,
    DateTimeOffset ObservedAt,
    DateTimeOffset? LastSeenAt,
    IReadOnlyList<MeshCapabilityDescriptor> Capabilities,
    int ActiveRemoteTasks = 0,
    string? Activity = null);

public sealed record MeshPeerSnapshot(
    MeshPeerRecord Peer,
    MeshPresenceSnapshot Presence);

/// <summary>Nearby discovery data is intentionally untrusted until explicit pairing succeeds.</summary>
public sealed record MeshDiscoveryCandidate(
    Guid DeviceId,
    string DisplayName,
    MeshDeviceClass DeviceClass,
    CapabilityPlatform Platform,
    string PublicKeyFingerprint,
    string Endpoint,
    DateTimeOffset ObservedAt);

public sealed record MeshPairingChallenge(
    Guid Id,
    string VerificationCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string LocalFingerprint);

public sealed record MeshPairingOffer(
    Guid ChallengeId,
    string Endpoint,
    string VerificationCode,
    string DeviceFingerprint,
    DateTimeOffset ExpiresAt);

public sealed record MeshPairingEvidence(
    Guid ChallengeId,
    Guid RemoteDeviceId,
    string RemoteDisplayName,
    MeshDeviceClass RemoteDeviceClass,
    CapabilityPlatform RemotePlatform,
    string RemoteFingerprint,
    string RemoteEndpoint,
    bool VerificationCodeMatched);

public sealed record MeshPairingResult(
    bool Succeeded,
    string Message,
    MeshPeerRecord? Peer = null);

public sealed record MeshTaskReference(string Kind, string Id, string? DisplayName = null);

public sealed record MeshTaskEnvelope(
    Guid TaskId,
    Guid SourceDeviceId,
    Guid TargetDeviceId,
    string Instruction,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<MeshTaskReference> References,
    string IdempotencyKey,
    DateTimeOffset CreatedAt,
    HavenSurface SourceSurface = HavenSurface.Tasks);

public sealed record MeshTaskReceipt(
    Guid TaskId,
    MeshTaskStatus Status,
    DateTimeOffset UpdatedAt,
    string Message,
    string? Result = null,
    string? FailureCode = null);

public sealed record MeshResourceSnapshot(
    Guid ResourceId,
    string ResourceType,
    long Revision,
    string ContentHash,
    Guid OriginDeviceId,
    DateTimeOffset ModifiedAt,
    string? Payload = null);

public sealed record MeshSyncMutation(
    Guid OperationId,
    Guid ResourceId,
    string ResourceType,
    MeshSyncOperationKind Kind,
    long BaseRevision,
    long Revision,
    string ContentHash,
    Guid OriginDeviceId,
    DateTimeOffset CreatedAt,
    string? Payload = null);

public sealed record MeshSyncDecision(
    MeshSyncDecisionKind Kind,
    string Reason,
    MeshResourceSnapshot? Current,
    MeshSyncMutation Incoming);

public sealed record MeshConflict(
    Guid Id,
    Guid ResourceId,
    string ResourceType,
    MeshResourceSnapshot Current,
    MeshSyncMutation Incoming,
    DateTimeOffset DetectedAt,
    string Reason);

public sealed record MeshHandoffEnvelope(
    Guid HandoffId,
    Guid SourceDeviceId,
    Guid TargetDeviceId,
    string ResourceType,
    string ResourceId,
    HavenSurface Surface,
    string? TaskId,
    string? ActivityContext,
    IReadOnlyList<MeshTaskReference> References,
    DateTimeOffset CreatedAt);

public sealed record MeshHandoffReceipt(
    Guid HandoffId,
    MeshHandoffStatus Status,
    DateTimeOffset UpdatedAt,
    string Message);

public sealed record MeshPersistentState(
    int Version,
    MeshLocalIdentity? LocalIdentity,
    IReadOnlyList<MeshPeerRecord> TrustedPeers,
    IReadOnlyList<MeshTaskReceipt> RemoteTasks,
    IReadOnlyList<Guid> AppliedSyncOperations,
    IReadOnlyList<MeshConflict> Conflicts,
    IReadOnlyList<MeshWorkMember>? WorkMembers = null,
    IReadOnlyList<MeshWorkMessage>? WorkMessages = null,
    IReadOnlyList<MeshWorkItem>? WorkItems = null)
{
    public const int CurrentVersion = 2;

    public static MeshPersistentState Empty { get; } = new(
        CurrentVersion,
        null,
        [],
        [],
        [],
        [],
        [],
        [],
        []);
}
