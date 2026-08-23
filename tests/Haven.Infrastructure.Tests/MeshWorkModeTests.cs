using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class MeshWorkModeTests
{
    [Fact]
    public async Task LegacyMeshStateMigratesToWorkModeWithoutLosingPeerData()
    {
        var peer = Peer(PeerOne, "Desktop");
        var store = Store(version: 1, [peer]);
        var transport = new AutoRuntimeTransport();
        await using var coordinator = CreateCoordinator(store, transport);

        await coordinator.InitialiseAsync(CancellationToken.None);

        Assert.Equal(MeshPersistentState.CurrentVersion, store.State.Version);
        Assert.Single(store.State.TrustedPeers);
        Assert.NotNull(store.State.WorkMembers);
        Assert.NotNull(store.State.WorkMessages);
        Assert.NotNull(store.State.WorkItems);
    }

    [Fact]
    public async Task FriendlyNameUpdatePreservesIdentityAndCoordinatorIsUnique()
    {
        var store = Store(peers: [Peer(PeerOne, "Desktop"), Peer(PeerTwo, "Laptop")]);
        var transport = new AutoRuntimeTransport();
        await using var coordinator = CreateCoordinator(store, transport);
        await coordinator.InitialiseAsync(CancellationToken.None);

        var mike = await coordinator.ConfigureModelWorkerAsync("Mike", PeerOne, "ollama", "coder", null, "coding", ["code"], true, CancellationToken.None);
        var updatedMike = await coordinator.ConfigureModelWorkerAsync("mike", PeerOne, "ollama", "coder-v2", null, "coding", ["code"], true, CancellationToken.None);
        var sarah = await coordinator.ConfigureModelWorkerAsync("Sarah", PeerTwo, "ollama", "researcher", null, "research", ["research"], true, CancellationToken.None);
        var snapshot = await coordinator.GetWorkModeAsync(CancellationToken.None);

        Assert.Equal(mike.WorkerId, updatedMike.WorkerId);
        Assert.Equal(sarah.WorkerId, snapshot.Coordinator?.WorkerId);
        Assert.Single(snapshot.Members, member => member.Member.IsCoordinator);
        Assert.Equal("coder-v2", snapshot.Members.Single(member => member.Member.WorkerId == mike.WorkerId).Member.ModelName);
    }

    [Fact]
    public async Task CheckUpReportsRealPresenceAndNaturalCommandUsesFriendlyName()
    {
        var peer = Peer(PeerOne, "Desktop");
        var store = Store(peers: [peer]);
        var transport = new AutoRuntimeTransport();
        await using var coordinator = CreateCoordinator(store, transport);
        await coordinator.InitialiseAsync(CancellationToken.None);
        await coordinator.ConfigureModelWorkerAsync("Mike", PeerOne, "ollama", "coder", null, null, [], false, CancellationToken.None);

        var offline = await coordinator.CheckUpAsync("Mike", CancellationToken.None);
        Assert.Equal(MeshPresenceState.Offline, offline.Presence);

        transport.Observe(peer);
        var result = await coordinator.ExecuteWorkCommandAsync("check up on Mike", CancellationToken.None);

        Assert.Contains("Mike is available", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.NewWork);
    }

    [Fact]
    public async Task OfflineDirectMessageFailsWithoutReroutingToAnotherWorker()
    {
        var peerOne = Peer(PeerOne, "Desktop");
        var peerTwo = Peer(PeerTwo, "Laptop");
        var store = Store(peers: [peerOne, peerTwo]);
        var transport = new AutoRuntimeTransport();
        await using var coordinator = CreateCoordinator(store, transport);
        await coordinator.InitialiseAsync(CancellationToken.None);
        var mike = await coordinator.ConfigureModelWorkerAsync("Mike", PeerOne, "ollama", "coder", null, null, [], false, CancellationToken.None);
        await coordinator.ConfigureModelWorkerAsync("Sarah", PeerTwo, "ollama", "research", null, null, [], false, CancellationToken.None);
        transport.Observe(peerTwo);

        var reply = await coordinator.SendWorkMessageAsync(mike.WorkerId, "Handle this", CancellationToken.None);

        Assert.Equal(MeshWorkMessageStatus.Failed, reply.Status);
        Assert.Equal("mesh-worker-offline", reply.Error);
        Assert.Empty(transport.ModelRequests);
    }

    [Fact]
    public async Task SharedPoolCarriesEarlierWorkerReplyIntoLaterWorkerContext()
    {
        var peerOne = Peer(PeerOne, "Desktop");
        var peerTwo = Peer(PeerTwo, "Laptop");
        var store = Store(peers: [peerOne, peerTwo]);
        var transport = new AutoRuntimeTransport(request => "answer-from-" + request.Model);
        await using var coordinator = CreateCoordinator(store, transport);
        await coordinator.InitialiseAsync(CancellationToken.None);
        await coordinator.ConfigureModelWorkerAsync("Mike", PeerOne, "ollama", "coder", null, null, [], false, CancellationToken.None);
        await coordinator.ConfigureModelWorkerAsync("Sarah", PeerTwo, "ollama", "researcher", null, null, [], false, CancellationToken.None);
        transport.Observe(peerOne);
        transport.Observe(peerTwo);

        var replies = await coordinator.PostSharedPoolAsync("Ideas for this project?", null, CancellationToken.None);

        Assert.Equal(2, replies.Count);
        Assert.Equal(2, transport.ModelRequests.Count);
        Assert.Contains("Mike: answer-from-coder", transport.ModelRequests[1].Prompt, StringComparison.Ordinal);
        var snapshot = await coordinator.GetWorkModeAsync(CancellationToken.None);
        Assert.Contains(snapshot.RecentMessages, message => message.Channel == MeshWorkChannelKind.SharedPool && message.SenderWorkerId is not null);
    }

    [Fact]
    public async Task CoordinatorPlansDelegatesReviewsAndSynthesises()
    {
        var peers = new[] { Peer(PeerOne, "Coordinator PC"), Peer(PeerTwo, "Coding PC"), Peer(PeerThree, "Review PC") };
        var store = Store(peers: peers);
        var transport = new AutoRuntimeTransport(request => request.Model switch
        {
            "coordinator" when request.Prompt.Contains("Return ONLY JSON", StringComparison.Ordinal) => "{\"summary\":\"Split and review\",\"assignments\":[{\"workerName\":\"Mike\",\"task\":\"Implement the parser\",\"reviewerName\":\"Sarah\"}]}",
            "coordinator" => "final synthesis",
            "coder" => "parser implementation",
            "reviewer" => "review passed",
            _ => "unexpected"
        });
        await using var coordinator = CreateCoordinator(store, transport);
        await coordinator.InitialiseAsync(CancellationToken.None);
        await coordinator.ConfigureModelWorkerAsync("Alex", PeerOne, "ollama", "coordinator", null, "coordinator", ["planning"], true, CancellationToken.None);
        await coordinator.ConfigureModelWorkerAsync("Mike", PeerTwo, "ollama", "coder", null, "developer", ["code"], false, CancellationToken.None);
        await coordinator.ConfigureModelWorkerAsync("Sarah", PeerThree, "ollama", "reviewer", null, "reviewer", ["review"], false, CancellationToken.None);
        foreach (var peer in peers) transport.Observe(peer);

        var result = await coordinator.CoordinateWorkAsync("Build the parser", CancellationToken.None);

        Assert.True(result.Plan.UsedCoordinator);
        Assert.Single(result.WorkItems);
        Assert.Equal(MeshWorkItemStatus.Succeeded, result.WorkItems[0].Status);
        Assert.Equal("parser implementation", result.WorkItems[0].Result);
        Assert.Equal("review passed", result.WorkItems[0].Review);
        Assert.Equal("final synthesis", result.Summary);
    }

    [Fact]
    public async Task MalformedCoordinatorPlanFallsBackToBestMatchingAvailableWorker()
    {
        var peers = new[] { Peer(PeerOne, "Coordinator PC"), Peer(PeerTwo, "Coding PC"), Peer(PeerThree, "Research PC") };
        var store = Store(peers: peers);
        var transport = new AutoRuntimeTransport(request => request.Model switch
        {
            "coordinator" when request.Prompt.Contains("Return ONLY JSON", StringComparison.Ordinal) => "not-json",
            "coordinator" => "fallback synthesis",
            "coder" => "coded",
            "researcher" => "researched",
            _ => "result"
        });
        await using var coordinator = CreateCoordinator(store, transport);
        await coordinator.InitialiseAsync(CancellationToken.None);
        await coordinator.ConfigureModelWorkerAsync("Alex", PeerOne, "ollama", "coordinator", null, "coordinator", [], true, CancellationToken.None);
        await coordinator.ConfigureModelWorkerAsync("Mike", PeerTwo, "ollama", "coder", null, "developer", ["code", "parser"], false, CancellationToken.None);
        await coordinator.ConfigureModelWorkerAsync("Sarah", PeerThree, "ollama", "researcher", null, "researcher", ["research"], false, CancellationToken.None);
        foreach (var peer in peers) transport.Observe(peer);

        var result = await coordinator.CoordinateWorkAsync("code the parser", CancellationToken.None);

        Assert.False(result.Plan.UsedCoordinator);
        Assert.Equal("Mike", Assert.Single(result.Plan.Assignments).WorkerName);
        Assert.Equal("coded", Assert.Single(result.WorkItems).Result);
    }

    [Fact]
    public async Task AskEveryoneNaturalCommandUsesSharedTeamPool()
    {
        var peers = new[] { Peer(PeerOne, "Desktop"), Peer(PeerTwo, "Laptop") };
        var store = Store(peers: peers);
        var transport = new AutoRuntimeTransport(request => "idea-" + request.Model);
        await using var coordinator = CreateCoordinator(store, transport);
        await coordinator.InitialiseAsync(CancellationToken.None);
        await coordinator.ConfigureModelWorkerAsync("Mike", PeerOne, "ollama", "coder", null, null, [], false, CancellationToken.None);
        await coordinator.ConfigureModelWorkerAsync("Sarah", PeerTwo, "ollama", "researcher", null, null, [], false, CancellationToken.None);
        foreach (var peer in peers) transport.Observe(peer);

        var result = await coordinator.ExecuteWorkCommandAsync("ask everyone for ideas", CancellationToken.None);

        Assert.Contains("Mike: idea-coder", result.Message, StringComparison.Ordinal);
        Assert.Contains("Sarah: idea-researcher", result.Message, StringComparison.Ordinal);
        Assert.Contains(result.NewMessages, message => message.Channel == MeshWorkChannelKind.SharedPool);
    }

    private static readonly Guid LocalId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PeerOne = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PeerTwo = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid PeerThree = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private static MeshPeerRecord Peer(Guid id, string name) => new(id, name, MeshDeviceClass.Desktop, CapabilityPlatform.Windows, new string(id == PeerOne ? 'b' : id == PeerTwo ? 'c' : 'd', 64), MeshPeerTrustState.Trusted, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(-50), null, "127.0.0.1:45001");

    private static MemoryStateStore Store(int version = MeshPersistentState.CurrentVersion, IReadOnlyList<MeshPeerRecord>? peers = null)
    {
        var identity = new MeshLocalIdentity(LocalId, "Local", MeshDeviceClass.Desktop, CapabilityPlatform.Windows, new string('a', 64), DateTimeOffset.UtcNow.AddDays(-1));
        return new MemoryStateStore(new MeshPersistentState(version, identity, peers ?? [], [], [], []));
    }

    private static MeshCoordinator CreateCoordinator(MemoryStateStore store, AutoRuntimeTransport transport)
    {
        var secrets = new MemorySecrets();
        secrets.Values[LocalId] = "test-secret";
        return new MeshCoordinator(store, secrets, transport, new EmptyCapabilities(), new EmptyMerge(), new EmptyDeviceExecutor(), new EmptyDiscovery(), new EmptyRuntime(), new EmptyTaskExecutor());
    }

    private sealed record RuntimeRequest(Guid DeviceId, string Model, string Prompt);

    private sealed class AutoRuntimeTransport(Func<RuntimeRequest, string>? respond = null) : IMeshTransport
    {
        private readonly Func<RuntimeRequest, string> _respond = respond ?? (_ => "ok");
        public event Action<MeshTransportPeer>? PeerObserved;
        public event Action<MeshTransportPeer>? PairingCompleted { add { } remove { } }
        public event Action<Guid, MeshConnectionState>? ConnectionChanged;
        public event Action<MeshTransportMessage>? MessageReceived;
        public bool IsRunning { get; private set; }
        public string? LocalEndpoint { get; private set; } = "127.0.0.1:45000";
        public List<RuntimeRequest> ModelRequests { get; } = [];
        public Task StartAsync(MeshLocalIdentity localIdentity, string privateKey, CancellationToken cancellationToken) { IsRunning = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) { IsRunning = false; return Task.CompletedTask; }
        public Task SetPairingChallengeAsync(MeshPairingChallenge? challenge, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetTrustedPeersAsync(IReadOnlyList<MeshPeerRecord> peers, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MeshTransportPeer> PairAsync(string endpoint, Guid challengeId, string verificationCode, string expectedRemoteFingerprint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ConnectAsync(MeshPeerRecord peer, CancellationToken cancellationToken) { Observe(peer); return Task.CompletedTask; }
        public Task DisconnectAsync(Guid peerDeviceId, CancellationToken cancellationToken) { ConnectionChanged?.Invoke(peerDeviceId, MeshConnectionState.Disconnected); return Task.CompletedTask; }
        public Task SendAsync(Guid peerDeviceId, string kind, string payload, CancellationToken cancellationToken)
        {
            if (kind == "runtime.model.complete.request")
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                var requestId = root.GetProperty("RequestId").GetGuid();
                var request = root.GetProperty("Request");
                var model = request.GetProperty("Model").GetString() ?? string.Empty;
                var prompt = request.GetProperty("Messages")[0].GetProperty("Content").GetString() ?? string.Empty;
                var runtimeRequest = new RuntimeRequest(peerDeviceId, model, prompt);
                ModelRequests.Add(runtimeRequest);
                var response = JsonSerializer.Serialize(new { RequestId = requestId, Success = true, Payload = _respond(runtimeRequest), Error = (string?)null });
                MessageReceived?.Invoke(new MeshTransportMessage(Guid.NewGuid(), peerDeviceId, "runtime.rpc.response", response, DateTimeOffset.UtcNow));
            }
            return Task.CompletedTask;
        }
        public void Observe(MeshPeerRecord peer)
        {
            PeerObserved?.Invoke(new MeshTransportPeer(peer.DeviceId, peer.DisplayName, peer.DeviceClass, peer.Platform, peer.PublicKeyFingerprint, peer.LastKnownEndpoint ?? "127.0.0.1:45001", []));
            ConnectionChanged?.Invoke(peer.DeviceId, MeshConnectionState.Connected);
        }
        public ValueTask DisposeAsync() { IsRunning = false; return ValueTask.CompletedTask; }
    }

    private sealed class MemoryStateStore(MeshPersistentState state) : IMeshStateStore
    {
        public MeshPersistentState State { get; private set; } = state;
        public Task<MeshPersistentState> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(State);
        public Task SaveAsync(MeshPersistentState state, CancellationToken cancellationToken) { State = state; return Task.CompletedTask; }
    }

    private sealed class MemorySecrets : IMeshIdentitySecretStore
    {
        public Dictionary<Guid, string> Values { get; } = [];
        public Task<string?> GetPrivateKeyAsync(Guid deviceId, CancellationToken cancellationToken) => Task.FromResult(Values.TryGetValue(deviceId, out var value) ? value : null);
        public Task SetPrivateKeyAsync(Guid deviceId, string privateKey, CancellationToken cancellationToken) { Values[deviceId] = privateKey; return Task.CompletedTask; }
        public Task DeletePrivateKeyAsync(Guid deviceId, CancellationToken cancellationToken) { Values.Remove(deviceId); return Task.CompletedTask; }
    }

    private sealed class EmptyCapabilities : IMeshCapabilitySource
    {
        public Task<IReadOnlyList<MeshCapabilityDescriptor>> GetLocalCapabilitiesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MeshCapabilityDescriptor>>([]);
    }

    private sealed class EmptyMerge : IMeshResourceMergeService
    {
        public Task<MeshResourceSnapshot?> GetCurrentAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken) => Task.FromResult<MeshResourceSnapshot?>(null);
        public Task<bool> TryApplyAsync(MeshSyncMutation mutation, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class EmptyDeviceExecutor : IMeshInboundDeviceActionExecutor
    {
        public Task<DeviceCapabilitySnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(new DeviceCapabilitySnapshot(new DeviceTargetDescriptor("current", "Local", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice), true, DateTimeOffset.UtcNow, []));
        public Task<DeviceActionResult> ExecuteAsync(string actionKey, IReadOnlyDictionary<string, string>? parameters, bool permissionGranted, CancellationToken cancellationToken) => Task.FromResult(new DeviceActionResult(DeviceActionResultStatus.Unsupported, actionKey, "current", "unused"));
    }

    private sealed class EmptyDiscovery : IMeshDiscoveryService
    {
        public event Action<MeshDiscoveryCandidate>? CandidateObserved { add { } remove { } }
        public bool IsRunning { get; private set; }
        public Task StartAsync(MeshLocalIdentity localIdentity, string localEndpoint, CancellationToken cancellationToken) { IsRunning = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) { IsRunning = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { IsRunning = false; return ValueTask.CompletedTask; }
    }

    private sealed class EmptyRuntime : IMeshInboundRuntimeExecutor
    {
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]);
        public Task<string> CompleteModelAsync(string providerId, OllamaChatRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(string providerId, OllamaToolRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentDefinition>> GetAgentsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AgentDefinition>>([]);
        public Task<string> ExecuteAgentAsync(Guid agentId, string prompt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EmptyTaskExecutor : IMeshInboundTaskExecutor
    {
        public Task<string> ExecuteAsync(MeshTaskEnvelope task, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
