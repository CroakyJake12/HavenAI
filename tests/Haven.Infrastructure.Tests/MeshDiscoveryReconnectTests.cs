using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class MeshDiscoveryReconnectTests
{
    [Fact]
    public async Task TrustedDiscoveryCandidateReconnectsUsingPinnedIdentityAndPersistsVerifiedEndpoint()
    {
        var peer = TrustedPeer() with { LastKnownEndpoint = "127.0.0.1:41001" };
        var store = new InMemoryStateStore(StateWithPeer(peer));
        var transport = new VerifyingTransport();
        var discovery = new FakeDiscovery();
        await using var coordinator = CreateCoordinator(store, transport, discovery);

        await coordinator.InitialiseAsync(CancellationToken.None);
        discovery.Observe(Candidate(peer, "127.0.0.1:42002"));

        await WaitForAsync(() => transport.ConnectCalls == 1 && store.State.TrustedPeers.Single().LastKnownEndpoint == "127.0.0.1:42002");

        Assert.Equal("127.0.0.1:42002", transport.LastConnectPeer?.LastKnownEndpoint);
        Assert.Equal(peer.PublicKeyFingerprint, transport.LastConnectPeer?.PublicKeyFingerprint);
        var dashboard = await coordinator.GetDashboardAsync(CancellationToken.None);
        Assert.Equal(MeshConnectionState.Connected, Assert.Single(dashboard.TrustedPeers).Presence.Connection);
    }

    [Fact]
    public async Task SpoofedDiscoveryFingerprintCannotRedirectTrustedPeer()
    {
        var peer = TrustedPeer() with { LastKnownEndpoint = "127.0.0.1:41001" };
        var store = new InMemoryStateStore(StateWithPeer(peer));
        var transport = new VerifyingTransport();
        var discovery = new FakeDiscovery();
        await using var coordinator = CreateCoordinator(store, transport, discovery);

        await coordinator.InitialiseAsync(CancellationToken.None);
        discovery.Observe(Candidate(peer, "127.0.0.1:42002") with { PublicKeyFingerprint = new string('f', 64) });
        await Task.Delay(100);

        Assert.Equal(0, transport.ConnectCalls);
        Assert.Equal("127.0.0.1:41001", store.State.TrustedPeers.Single().LastKnownEndpoint);
    }

    [Fact]
    public async Task RevokedPeerDiscoveryIsIgnoredAndNeverReturnsToNearbyPairing()
    {
        var peer = TrustedPeer() with
        {
            TrustState = MeshPeerTrustState.Revoked,
            RevokedAt = DateTimeOffset.UtcNow,
            LastKnownEndpoint = "127.0.0.1:41001"
        };
        var store = new InMemoryStateStore(StateWithPeer(peer));
        var transport = new VerifyingTransport();
        var discovery = new FakeDiscovery();
        await using var coordinator = CreateCoordinator(store, transport, discovery);

        await coordinator.InitialiseAsync(CancellationToken.None);
        discovery.Observe(Candidate(peer, "127.0.0.1:42002"));
        await Task.Delay(100);

        Assert.Equal(0, transport.ConnectCalls);
        var dashboard = await coordinator.GetDashboardAsync(CancellationToken.None);
        Assert.Empty(dashboard.TrustedPeers);
        Assert.DoesNotContain(dashboard.NearbyDevices ?? [], item => item.DeviceId == peer.DeviceId);
    }

    private static MeshCoordinator CreateCoordinator(InMemoryStateStore store, VerifyingTransport transport, FakeDiscovery discovery) =>
        new(
            store,
            new InMemoryIdentitySecrets(store.State.LocalIdentity!),
            transport,
            new EmptyCapabilities(),
            new EmptyMerge(),
            new EmptyInboundDeviceActions(),
            discovery);

    private static MeshDiscoveryCandidate Candidate(MeshPeerRecord peer, string endpoint) =>
        new(peer.DeviceId, peer.DisplayName, peer.DeviceClass, peer.Platform, peer.PublicKeyFingerprint, endpoint, DateTimeOffset.UtcNow);

    private static MeshPeerRecord TrustedPeer() =>
        new(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "Remote PC",
            MeshDeviceClass.Desktop,
            CapabilityPlatform.Windows,
            new string('c', 64),
            MeshPeerTrustState.Trusted,
            DateTimeOffset.UtcNow.AddDays(-2),
            DateTimeOffset.UtcNow.AddDays(-1),
            null,
            "127.0.0.1:41001");

    private static MeshPersistentState StateWithPeer(MeshPeerRecord peer)
    {
        var identity = new MeshLocalIdentity(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Local PC",
            MeshDeviceClass.Desktop,
            CapabilityPlatform.Windows,
            new string('d', 64),
            DateTimeOffset.UtcNow.AddDays(-3));
        return new MeshPersistentState(MeshPersistentState.CurrentVersion, identity, [peer], [], [], [], [], [], []);
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

    private sealed class InMemoryStateStore(MeshPersistentState initial) : IMeshStateStore
    {
        public MeshPersistentState State { get; private set; } = initial;
        public Task<MeshPersistentState> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(State);
        public Task SaveAsync(MeshPersistentState state, CancellationToken cancellationToken)
        {
            State = state;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryIdentitySecrets(MeshLocalIdentity identity) : IMeshIdentitySecretStore
    {
        private readonly Dictionary<Guid, string> _values = new() { [identity.DeviceId] = "test-secret" };
        public Task<string?> GetPrivateKeyAsync(Guid deviceId, CancellationToken cancellationToken) =>
            Task.FromResult(_values.TryGetValue(deviceId, out var value) ? value : null);
        public Task SetPrivateKeyAsync(Guid deviceId, string privateKey, CancellationToken cancellationToken)
        {
            _values[deviceId] = privateKey;
            return Task.CompletedTask;
        }
        public Task DeletePrivateKeyAsync(Guid deviceId, CancellationToken cancellationToken)
        {
            _values.Remove(deviceId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDiscovery : IMeshDiscoveryService
    {
        public event Action<MeshDiscoveryCandidate>? CandidateObserved;
        public bool IsRunning { get; private set; }

        public Task StartAsync(MeshLocalIdentity localIdentity, string localEndpoint, CancellationToken cancellationToken)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void Observe(MeshDiscoveryCandidate candidate) => CandidateObserved?.Invoke(candidate);
        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class VerifyingTransport : IMeshTransport
    {
        public event Action<MeshTransportPeer>? PeerObserved;
        public event Action<MeshTransportPeer>? PairingCompleted { add { } remove { } }
        public event Action<Guid, MeshConnectionState>? ConnectionChanged;
        public event Action<MeshTransportMessage>? MessageReceived { add { } remove { } }

        public bool IsRunning { get; private set; }
        public string? LocalEndpoint { get; private set; } = "127.0.0.1:40000";
        public int ConnectCalls { get; private set; }
        public MeshPeerRecord? LastConnectPeer { get; private set; }

        public Task StartAsync(MeshLocalIdentity localIdentity, string privateKey, CancellationToken cancellationToken)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public Task SetPairingChallengeAsync(MeshPairingChallenge? challenge, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetTrustedPeersAsync(IReadOnlyList<MeshPeerRecord> peers, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MeshTransportPeer> PairAsync(string endpoint, Guid challengeId, string verificationCode, string expectedRemoteFingerprint, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ConnectAsync(MeshPeerRecord peer, CancellationToken cancellationToken)
        {
            ConnectCalls++;
            LastConnectPeer = peer;
            var observed = new MeshTransportPeer(peer.DeviceId, peer.DisplayName, peer.DeviceClass, peer.Platform, peer.PublicKeyFingerprint, peer.LastKnownEndpoint!, []);
            PeerObserved?.Invoke(observed);
            ConnectionChanged?.Invoke(peer.DeviceId, MeshConnectionState.Connected);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(Guid peerDeviceId, CancellationToken cancellationToken)
        {
            ConnectionChanged?.Invoke(peerDeviceId, MeshConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public Task SendAsync(Guid peerDeviceId, string kind, string payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyCapabilities : IMeshCapabilitySource
    {
        public Task<IReadOnlyList<MeshCapabilityDescriptor>> GetLocalCapabilitiesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MeshCapabilityDescriptor>>([]);
    }

    private sealed class EmptyMerge : IMeshResourceMergeService
    {
        public Task<MeshResourceSnapshot?> GetCurrentAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken) =>
            Task.FromResult<MeshResourceSnapshot?>(null);
        public Task<bool> TryApplyAsync(MeshSyncMutation mutation, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class EmptyInboundDeviceActions : IMeshInboundDeviceActionExecutor
    {
        public Task<DeviceCapabilitySnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceCapabilitySnapshot(
                new DeviceTargetDescriptor("current", "Local PC", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice, "test"),
                true,
                DateTimeOffset.UtcNow,
                []));

        public Task<DeviceActionResult> ExecuteAsync(
            string actionKey,
            IReadOnlyDictionary<string, string>? parameters,
            bool permissionGranted,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceActionResult(DeviceActionResultStatus.Unsupported, actionKey, "current", "not used"));
    }
}
