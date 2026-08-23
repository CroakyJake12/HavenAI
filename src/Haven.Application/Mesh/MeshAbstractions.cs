using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Application;

public sealed record MeshTransportPeer(
    Guid DeviceId,
    string DisplayName,
    MeshDeviceClass DeviceClass,
    CapabilityPlatform Platform,
    string PublicKeyFingerprint,
    string Endpoint,
    IReadOnlyList<MeshCapabilityDescriptor> Capabilities);

public sealed record MeshTransportMessage(
    Guid MessageId,
    Guid SourceDeviceId,
    string Kind,
    string Payload,
    DateTimeOffset ReceivedAt);

public interface IMeshStateStore
{
    Task<MeshPersistentState> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(MeshPersistentState state, CancellationToken cancellationToken);
}

public interface IMeshIdentitySecretStore
{
    Task<string?> GetPrivateKeyAsync(Guid deviceId, CancellationToken cancellationToken);
    Task SetPrivateKeyAsync(Guid deviceId, string privateKey, CancellationToken cancellationToken);
    Task DeletePrivateKeyAsync(Guid deviceId, CancellationToken cancellationToken);
}

public interface IMeshTransport : IAsyncDisposable
{
    event Action<MeshTransportPeer>? PeerObserved;
    event Action<MeshTransportPeer>? PairingCompleted;
    event Action<Guid, MeshConnectionState>? ConnectionChanged;
    event Action<MeshTransportMessage>? MessageReceived;

    bool IsRunning { get; }
    string? LocalEndpoint { get; }
    Task StartAsync(MeshLocalIdentity localIdentity, string privateKey, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task SetPairingChallengeAsync(MeshPairingChallenge? challenge, CancellationToken cancellationToken);
    Task SetTrustedPeersAsync(IReadOnlyList<MeshPeerRecord> peers, CancellationToken cancellationToken);
    Task<MeshTransportPeer> PairAsync(string endpoint, Guid challengeId, string verificationCode, string expectedRemoteFingerprint, CancellationToken cancellationToken);
    Task ConnectAsync(MeshPeerRecord peer, CancellationToken cancellationToken);
    Task DisconnectAsync(Guid peerDeviceId, CancellationToken cancellationToken);
    Task SendAsync(Guid peerDeviceId, string kind, string payload, CancellationToken cancellationToken);
}

public interface IMeshCapabilitySource
{
    Task<IReadOnlyList<MeshCapabilityDescriptor>> GetLocalCapabilitiesAsync(CancellationToken cancellationToken);
}

public interface IMeshDiscoveryService : IAsyncDisposable
{
    event Action<MeshDiscoveryCandidate>? CandidateObserved;
    bool IsRunning { get; }
    Task StartAsync(MeshLocalIdentity localIdentity, string localEndpoint, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface IMeshInboundDeviceActionExecutor
{
    Task<DeviceCapabilitySnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
    Task<DeviceActionResult> ExecuteAsync(string actionKey, IReadOnlyDictionary<string, string>? parameters, bool permissionGranted, CancellationToken cancellationToken);
}

public interface IMeshInboundRuntimeExecutor
{
    Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken);
    Task<string> CompleteModelAsync(string providerId, OllamaChatRequest request, CancellationToken cancellationToken);
    Task<OllamaToolResponse> ChatWithToolsAsync(string providerId, OllamaToolRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<AgentDefinition>> GetAgentsAsync(CancellationToken cancellationToken);
    Task<string> ExecuteAgentAsync(Guid agentId, string prompt, CancellationToken cancellationToken);
}

public interface IMeshInboundTaskExecutor
{
    Task<string> ExecuteAsync(MeshTaskEnvelope task, CancellationToken cancellationToken);
}

public sealed record MeshRemoteAgentDescriptor(Guid DeviceId, string DeviceName, Guid AgentId, string Name, string Description, string IconKey, string PreferredModel);

public interface IMeshResourceMergeService
{
    Task<MeshResourceSnapshot?> GetCurrentAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken);
    Task<bool> TryApplyAsync(MeshSyncMutation mutation, CancellationToken cancellationToken);
}

public sealed record MeshDashboardSnapshot(
    MeshLocalIdentity LocalDevice,
    IReadOnlyList<MeshPeerSnapshot> TrustedPeers,
    IReadOnlyList<MeshTaskReceipt> RemoteTasks,
    IReadOnlyList<MeshConflict> Conflicts,
    bool TransportRunning,
    IReadOnlyList<MeshDiscoveryCandidate>? NearbyDevices = null);
