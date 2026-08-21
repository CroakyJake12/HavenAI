using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class MeshReliabilityTests
{
    [Fact]
    public void DuplicateSyncOperationIsIdempotent()
    {
        var engine = new MeshSyncEngine();
        var operation = Mutation(baseRevision: 1, revision: 2);
        var current = Snapshot(revision: 1, hash: "old");

        var decision = engine.Evaluate(current, operation, new HashSet<Guid> { operation.OperationId });

        Assert.Equal(MeshSyncDecisionKind.Duplicate, decision.Kind);
    }

    [Fact]
    public void OutOfOrderOlderRevisionIsIgnoredWithoutOverwrite()
    {
        var engine = new MeshSyncEngine();
        var current = Snapshot(revision: 5, hash: "newer");
        var incoming = Mutation(baseRevision: 2, revision: 3, hash: "older");

        var decision = engine.Evaluate(current, incoming, new HashSet<Guid>());

        Assert.Equal(MeshSyncDecisionKind.IgnoreStale, decision.Kind);
        Assert.Same(current, decision.Current);
    }

    [Fact]
    public void DivergentNewerEditBecomesConflict()
    {
        var engine = new MeshSyncEngine();
        var current = Snapshot(revision: 5, hash: "local");
        var incoming = Mutation(baseRevision: 3, revision: 6, hash: "remote");

        var decision = engine.Evaluate(current, incoming, new HashSet<Guid>());

        Assert.Equal(MeshSyncDecisionKind.Conflict, decision.Kind);
        Assert.Contains("changed", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MutationWithMissingBaseBecomesConflict()
    {
        var engine = new MeshSyncEngine();
        var incoming = Mutation(baseRevision: 4, revision: 5);

        var decision = engine.Evaluate(null, incoming, new HashSet<Guid>());

        Assert.Equal(MeshSyncDecisionKind.Conflict, decision.Kind);
    }

    [Fact]
    public async Task ExpiredPairingOfferIsRejectedBeforeTransportUse()
    {
        var transport = new FakeTransport();
        await using var coordinator = CreateCoordinator(transport);
        await coordinator.InitialiseAsync(CancellationToken.None);
        var offer = new MeshPairingOffer(Guid.NewGuid(), "127.0.0.1:45001", "123456", new string('a', 64), DateTimeOffset.UtcNow.AddSeconds(-1));

        var result = await coordinator.PairAsync(offer, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("expired", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, transport.PairCalls);
    }

    [Fact]
    public async Task FingerprintAuthenticationFailureReturnsCleanPairingFailure()
    {
        var transport = new FakeTransport { PairException = new AuthenticationException("fingerprint mismatch") };
        await using var coordinator = CreateCoordinator(transport);
        await coordinator.InitialiseAsync(CancellationToken.None);
        var offer = new MeshPairingOffer(Guid.NewGuid(), "127.0.0.1:45001", "123456", new string('b', 64), DateTimeOffset.UtcNow.AddMinutes(1));

        var result = await coordinator.PairAsync(offer, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("fingerprint mismatch", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RevokedPeerCannotReconnect()
    {
        var peer = TrustedPeer() with { TrustState = MeshPeerTrustState.Revoked, RevokedAt = DateTimeOffset.UtcNow };
        var transport = new FakeTransport();
        await using var coordinator = CreateCoordinator(transport, StateWithPeer(peer));
        await coordinator.InitialiseAsync(CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => coordinator.ConnectAsync(peer.DeviceId, CancellationToken.None));
        Assert.Equal(0, transport.ConnectCalls);
    }

    [Fact]
    public async Task OfflineTaskRemainsQueuedAndIsNotSilentlySubstituted()
    {
        var peer = TrustedPeer();
        var transport = new FakeTransport();
        await using var coordinator = CreateCoordinator(transport, StateWithPeer(peer));
        await coordinator.InitialiseAsync(CancellationToken.None);

        var receipt = await coordinator.DelegateTaskAsync(peer.DeviceId, "Do the work", [], [], HavenSurface.Tasks, CancellationToken.None);

        Assert.Equal(MeshTaskStatus.Queued, receipt.Status);
        Assert.Equal("mesh-peer-offline", receipt.FailureCode);
        Assert.Equal(0, transport.SendCalls);
    }

    [Fact]
    public async Task CapabilityMismatchFailsBeforeRemoteTaskIsSent()
    {
        var peer = TrustedPeer();
        var transport = new FakeTransport();
        await using var coordinator = CreateCoordinator(transport, StateWithPeer(peer));
        await coordinator.InitialiseAsync(CancellationToken.None);
        transport.Observe(peer, [Capability("computer-device-use")]);

        var receipt = await coordinator.DelegateTaskAsync(peer.DeviceId, "Render this", ["gpu-render"], [], HavenSurface.Tasks, CancellationToken.None);

        Assert.Equal(MeshTaskStatus.Failed, receipt.Status);
        Assert.Equal("mesh-capability-mismatch", receipt.FailureCode);
        Assert.Equal(0, transport.SendCalls);
    }

    [Fact]
    public async Task ConnectionLossDuringDelegationLeavesExplicitRetryQueue()
    {
        var peer = TrustedPeer();
        var transport = new FakeTransport { SendException = new IOException("connection lost") };
        await using var coordinator = CreateCoordinator(transport, StateWithPeer(peer));
        await coordinator.InitialiseAsync(CancellationToken.None);
        transport.Observe(peer, [Capability("computer-device-use")]);

        var receipt = await coordinator.DelegateTaskAsync(peer.DeviceId, "Inspect the desktop", ["computer-device-use"], [], HavenSurface.Tasks, CancellationToken.None);

        Assert.Equal(MeshTaskStatus.Queued, receipt.Status);
        Assert.Equal("mesh-connection-lost", receipt.FailureCode);
        Assert.Contains("explicit retry", receipt.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicInternetEndpointIsRejectedBySecureTransport()
    {
        await using var transport = new SecureLanMeshTransport(new EmptyCapabilitySource());
        var (identity, key) = CreateIdentity("Public endpoint test");
        await transport.StartAsync(identity, key, CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => transport.PairAsync("8.8.8.8:443", Guid.NewGuid(), "123456", new string('a', 64), CancellationToken.None));
    }

    [Fact]
    public async Task RemoteDeviceActionRequiresTargetGrantAndSourcePermission()
    {
        var peer = TrustedPeer();
        var transport = new FakeTransport();
        var inbound = new FakeInboundDeviceExecutor();
        await using var coordinator = CreateCoordinator(transport, StateWithPeer(peer), inbound);
        await coordinator.InitialiseAsync(CancellationToken.None);
        transport.Observe(peer, [Capability("computer-device-use")]);

        var firstRequest = Guid.NewGuid();
        transport.EmitMessage(peer.DeviceId, "device.action.request", DeviceRequest(firstRequest, sourcePermission: true));
        await WaitForAsync(() => transport.SentMessages.Count >= 1);
        Assert.Equal(DeviceActionResultStatus.PermissionRequired, ReadDeviceStatus(transport.SentMessages[^1].Payload));
        Assert.Equal(0, inbound.ExecuteCalls);

        await coordinator.SetPeerCapabilityPermissionAsync(peer.DeviceId, "computer-device-use", true, CancellationToken.None);
        var secondRequest = Guid.NewGuid();
        transport.EmitMessage(peer.DeviceId, "device.action.request", DeviceRequest(secondRequest, sourcePermission: false));
        await WaitForAsync(() => transport.SentMessages.Count >= 2);
        Assert.Equal(DeviceActionResultStatus.PermissionRequired, ReadDeviceStatus(transport.SentMessages[^1].Payload));
        Assert.Equal(0, inbound.ExecuteCalls);

        var thirdRequest = Guid.NewGuid();
        transport.EmitMessage(peer.DeviceId, "device.action.request", DeviceRequest(thirdRequest, sourcePermission: true));
        await WaitForAsync(() => transport.SentMessages.Count >= 3);
        Assert.Equal(DeviceActionResultStatus.Success, ReadDeviceStatus(transport.SentMessages[^1].Payload));
        Assert.Equal(1, inbound.ExecuteCalls);
    }

    [Fact]
    public async Task LoopbackPairingPinsIdentityAndPersistsStableListeningEndpoint()
    {
        var capabilities = new EmptyCapabilitySource();
        var transportA = new SecureLanMeshTransport(capabilities);
        var transportB = new SecureLanMeshTransport(capabilities);
        await using var coordinatorA = CreateCoordinator(transportA, inbound: new FakeInboundDeviceExecutor());
        await using var coordinatorB = CreateCoordinator(transportB, inbound: new FakeInboundDeviceExecutor());
        await coordinatorA.InitialiseAsync(CancellationToken.None);
        await coordinatorB.InitialiseAsync(CancellationToken.None);

        var offer = await coordinatorB.CreatePairingOfferAsync(CancellationToken.None);
        var port = offer.Endpoint[(offer.Endpoint.LastIndexOf(':') + 1)..];
        var loopbackOffer = offer with { Endpoint = "127.0.0.1:" + port };
        var result = await coordinatorA.PairAsync(loopbackOffer, CancellationToken.None);

        Assert.True(result.Succeeded, $"{result.Message} Receiver: {transportB.LastInboundFailure ?? "no inbound diagnostic"}");
        Assert.NotNull(result.Peer);
        await WaitForAsync(async () => (await coordinatorB.GetDashboardAsync(CancellationToken.None)).TrustedPeers.Count == 1);
        var dashboardB = await coordinatorB.GetDashboardAsync(CancellationToken.None);
        var inboundPeer = Assert.Single(dashboardB.TrustedPeers);
        Assert.Equal(transportA.LocalEndpoint, inboundPeer.Peer.LastKnownEndpoint);
        Assert.Equal(result.Peer!.PublicKeyFingerprint, offer.DeviceFingerprint);
    }

    private static MeshSyncMutation Mutation(long baseRevision, long revision, string hash = "incoming") =>
        new(Guid.NewGuid(), ResourceId, "mesh-note", MeshSyncOperationKind.Upsert, baseRevision, revision, hash, RemoteDeviceId, DateTimeOffset.UtcNow, "payload");

    private static MeshResourceSnapshot Snapshot(long revision, string hash) =>
        new(ResourceId, "mesh-note", revision, hash, LocalDeviceId, DateTimeOffset.UtcNow, "payload");

    private static readonly Guid ResourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LocalDeviceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RemoteDeviceId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static MeshPeerRecord TrustedPeer() => new(
        RemoteDeviceId, "Remote PC", MeshDeviceClass.Desktop, CapabilityPlatform.Windows, new string('c', 64),
        MeshPeerTrustState.Trusted, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(-4), null, "127.0.0.1:45001");

    private static MeshCapabilityDescriptor Capability(string key) =>
        new(key, key, CapabilityPlatform.Windows, CapabilityRiskClass.Consequential, ["inspect"]);

    private static MeshPersistentState StateWithPeer(MeshPeerRecord peer)
    {
        var identity = new MeshLocalIdentity(LocalDeviceId, "Local PC", MeshDeviceClass.Desktop, CapabilityPlatform.Windows, new string('d', 64), DateTimeOffset.UtcNow.AddDays(-1));
        return new MeshPersistentState(MeshPersistentState.CurrentVersion, identity, [peer], [], [], []);
    }

    private static MeshCoordinator CreateCoordinator(IMeshTransport transport, MeshPersistentState? state = null, IMeshInboundDeviceActionExecutor? inbound = null)
    {
        var store = new InMemoryStateStore(state ?? MeshPersistentState.Empty);
        var secrets = new InMemoryIdentitySecrets();
        if (state?.LocalIdentity is { } identity) secrets.Values[identity.DeviceId] = "test-secret";
        var capabilities = new EmptyCapabilitySource();
        var merge = new InMemoryMergeService();
        return inbound is null
            ? new MeshCoordinator(store, secrets, transport, capabilities, merge)
            : new MeshCoordinator(store, secrets, transport, capabilities, merge, inbound);
    }

    private static (MeshLocalIdentity Identity, string PrivateKey) CreateIdentity(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = Convert.ToBase64String(key.ExportPkcs8PrivateKey());
        var fingerprint = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        return (new MeshLocalIdentity(Guid.NewGuid(), name, MeshDeviceClass.Desktop, CapabilityPlatform.Windows, fingerprint, DateTimeOffset.UtcNow), privateKey);
    }

    private static string DeviceRequest(Guid requestId, bool sourcePermission) => JsonSerializer.Serialize(new
    {
        RequestId = requestId,
        ActionKey = "ui.snapshot",
        CapabilityKey = "computer-device-use",
        Parameters = (IReadOnlyDictionary<string, string>?)null,
        SourcePermissionGranted = sourcePermission
    });

    private static DeviceActionResultStatus ReadDeviceStatus(string rpcPayload)
    {
        using var outer = JsonDocument.Parse(rpcPayload);
        Assert.True(outer.RootElement.GetProperty("Success").GetBoolean());
        var resultPayload = outer.RootElement.GetProperty("Payload").GetString() ?? throw new InvalidDataException("DEVICE RPC result payload was empty.");
        using var inner = JsonDocument.Parse(resultPayload);
        return (DeviceActionResultStatus)inner.RootElement.GetProperty("Status").GetInt32();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(20, timeout.Token);
        }
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed class InMemoryStateStore(MeshPersistentState initial) : IMeshStateStore
    {
        public MeshPersistentState State { get; private set; } = initial;
        public Task<MeshPersistentState> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(State);
        public Task SaveAsync(MeshPersistentState state, CancellationToken cancellationToken) { State = state; return Task.CompletedTask; }
    }

    private sealed class InMemoryIdentitySecrets : IMeshIdentitySecretStore
    {
        public Dictionary<Guid, string> Values { get; } = [];
        public Task<string?> GetPrivateKeyAsync(Guid deviceId, CancellationToken cancellationToken) => Task.FromResult(Values.TryGetValue(deviceId, out var value) ? value : null);
        public Task SetPrivateKeyAsync(Guid deviceId, string privateKey, CancellationToken cancellationToken) { Values[deviceId] = privateKey; return Task.CompletedTask; }
        public Task DeletePrivateKeyAsync(Guid deviceId, CancellationToken cancellationToken) { Values.Remove(deviceId); return Task.CompletedTask; }
    }

    private sealed class EmptyCapabilitySource : IMeshCapabilitySource
    {
        public Task<IReadOnlyList<MeshCapabilityDescriptor>> GetLocalCapabilitiesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MeshCapabilityDescriptor>>([]);
    }

    private sealed class InMemoryMergeService : IMeshResourceMergeService
    {
        private readonly Dictionary<(string Type, Guid Id), MeshResourceSnapshot> _values = [];
        public Task<MeshResourceSnapshot?> GetCurrentAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken) =>
            Task.FromResult(_values.TryGetValue((resourceType, resourceId), out var value) ? value : null);
        public Task<bool> TryApplyAsync(MeshSyncMutation mutation, CancellationToken cancellationToken)
        {
            if (mutation.Kind == MeshSyncOperationKind.Delete) _values.Remove((mutation.ResourceType, mutation.ResourceId));
            else _values[(mutation.ResourceType, mutation.ResourceId)] = new(mutation.ResourceId, mutation.ResourceType, mutation.Revision, mutation.ContentHash, mutation.OriginDeviceId, mutation.CreatedAt, mutation.Payload);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeInboundDeviceExecutor : IMeshInboundDeviceActionExecutor
    {
        public int ExecuteCalls { get; private set; }
        public Task<DeviceCapabilitySnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(new DeviceCapabilitySnapshot(
            new DeviceTargetDescriptor("current", "Local PC", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice, WindowsComputerDeviceActionProvider.NativeProviderId),
            true, DateTimeOffset.UtcNow,
            [new DeviceActionDescriptor("ui.snapshot", "Inspect", "Desktop", "computer-device-use", "device.control", WindowsComputerDeviceActionProvider.NativeProviderId, DeviceActionAvailability.PermissionRequired, [])]));
        public Task<DeviceActionResult> ExecuteAsync(string actionKey, IReadOnlyDictionary<string, string>? parameters, bool permissionGranted, CancellationToken cancellationToken)
        {
            ExecuteCalls++;
            return Task.FromResult(new DeviceActionResult(DeviceActionResultStatus.Success, actionKey, "current", "done", "snapshot"));
        }
    }

    private sealed class FakeTransport : IMeshTransport
    {
        public event Action<MeshTransportPeer>? PeerObserved;
        public event Action<MeshTransportPeer>? PairingCompleted;
        public event Action<Guid, MeshConnectionState>? ConnectionChanged;
        public event Action<MeshTransportMessage>? MessageReceived;
        public bool IsRunning { get; private set; }
        public string? LocalEndpoint { get; private set; } = "127.0.0.1:45000";
        public int PairCalls { get; private set; }
        public int ConnectCalls { get; private set; }
        public int SendCalls { get; private set; }
        public Exception? PairException { get; init; }
        public Exception? SendException { get; init; }
        public List<(Guid PeerId, string Kind, string Payload)> SentMessages { get; } = [];
        public IReadOnlyList<MeshPeerRecord> TrustedPeers { get; private set; } = [];

        public Task StartAsync(MeshLocalIdentity localIdentity, string privateKey, CancellationToken cancellationToken) { IsRunning = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) { IsRunning = false; return Task.CompletedTask; }
        public Task SetPairingChallengeAsync(MeshPairingChallenge? challenge, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetTrustedPeersAsync(IReadOnlyList<MeshPeerRecord> peers, CancellationToken cancellationToken) { TrustedPeers = peers.ToArray(); return Task.CompletedTask; }
        public Task<MeshTransportPeer> PairAsync(string endpoint, Guid challengeId, string verificationCode, string expectedRemoteFingerprint, CancellationToken cancellationToken)
        {
            PairCalls++;
            if (PairException is not null) throw PairException;
            return Task.FromResult(new MeshTransportPeer(RemoteDeviceId, "Remote PC", MeshDeviceClass.Desktop, CapabilityPlatform.Windows, expectedRemoteFingerprint, endpoint, []));
        }
        public Task ConnectAsync(MeshPeerRecord peer, CancellationToken cancellationToken) { ConnectCalls++; ConnectionChanged?.Invoke(peer.DeviceId, MeshConnectionState.Connected); return Task.CompletedTask; }
        public Task DisconnectAsync(Guid peerDeviceId, CancellationToken cancellationToken) { ConnectionChanged?.Invoke(peerDeviceId, MeshConnectionState.Disconnected); return Task.CompletedTask; }
        public Task SendAsync(Guid peerDeviceId, string kind, string payload, CancellationToken cancellationToken)
        {
            SendCalls++;
            if (SendException is not null) throw SendException;
            SentMessages.Add((peerDeviceId, kind, payload));
            return Task.CompletedTask;
        }
        public void Observe(MeshPeerRecord peer, IReadOnlyList<MeshCapabilityDescriptor> capabilities)
        {
            PeerObserved?.Invoke(new MeshTransportPeer(peer.DeviceId, peer.DisplayName, peer.DeviceClass, peer.Platform, peer.PublicKeyFingerprint, peer.LastKnownEndpoint ?? "127.0.0.1:45001", capabilities));
            ConnectionChanged?.Invoke(peer.DeviceId, MeshConnectionState.Connected);
        }
        public void EmitMessage(Guid sourceDeviceId, string kind, string payload) => MessageReceived?.Invoke(new MeshTransportMessage(Guid.NewGuid(), sourceDeviceId, kind, payload, DateTimeOffset.UtcNow));
        public void CompletePairing(MeshTransportPeer peer) => PairingCompleted?.Invoke(peer);
        public ValueTask DisposeAsync() { IsRunning = false; return ValueTask.CompletedTask; }
    }
}
