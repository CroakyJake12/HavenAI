using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

public sealed class MeshRuntimeAndTaskTests
{
    [Fact]
    public void RemoteModelRouteRoundTripsAndRejectsMeshRecursion()
    {
        var deviceId = Guid.NewGuid();
        var encoded = MeshRemoteModelProvider.EncodeRoute(deviceId, "ollama", "qwen3.5:8b|tools");
        var decoded = MeshRemoteModelProvider.DecodeRoute(encoded);

        Assert.Equal(deviceId, decoded.DeviceId);
        Assert.Equal("ollama", decoded.ProviderId);
        Assert.Equal("qwen3.5:8b|tools", decoded.ModelName);
        Assert.Throws<ArgumentException>(() => MeshRemoteModelProvider.EncodeRoute(deviceId, MeshRemoteModelProvider.MeshProviderId, "recursive"));
    }

    [Fact]
    public async Task ModelInventoryRequiresExplicitTargetGrant()
    {
        var peer = Peer();
        var transport = new FakeTransport();
        var runtime = new FakeRuntime();
        await using var coordinator = CreateFullCoordinator(transport, peer, runtime, new FakeTaskExecutor());
        await coordinator.InitialiseAsync(CancellationToken.None);
        transport.Observe(peer);

        var firstId = Guid.NewGuid();
        transport.Emit(peer.DeviceId, "runtime.models.request", JsonSerializer.Serialize(new { RequestId = firstId }));
        await WaitForAsync(() => transport.Sent.Any(item => item.Kind == "runtime.rpc.response"));
        Assert.False(ReadRpcSuccess(transport.Sent.Last(item => item.Kind == "runtime.rpc.response").Payload));
        Assert.Equal(0, runtime.ModelInventoryCalls);

        await coordinator.SetPeerCapabilityPermissionAsync(peer.DeviceId, MeshCoordinator.RemoteModelCapability, true, CancellationToken.None);
        transport.Sent.Clear();
        var secondId = Guid.NewGuid();
        transport.Emit(peer.DeviceId, "runtime.models.request", JsonSerializer.Serialize(new { RequestId = secondId }));
        await WaitForAsync(() => transport.Sent.Any(item => item.Kind == "runtime.rpc.response"));
        Assert.True(ReadRpcSuccess(transport.Sent.Last(item => item.Kind == "runtime.rpc.response").Payload));
        Assert.Equal(1, runtime.ModelInventoryCalls);
    }

    [Fact]
    public async Task AgentInventoryUsesSeparateExplicitTargetGrant()
    {
        var peer = Peer();
        var transport = new FakeTransport();
        var runtime = new FakeRuntime();
        await using var coordinator = CreateFullCoordinator(transport, peer, runtime, new FakeTaskExecutor());
        await coordinator.InitialiseAsync(CancellationToken.None);
        transport.Observe(peer);
        await coordinator.SetPeerCapabilityPermissionAsync(peer.DeviceId, MeshCoordinator.RemoteModelCapability, true, CancellationToken.None);

        transport.Emit(peer.DeviceId, "runtime.agents.request", JsonSerializer.Serialize(new { RequestId = Guid.NewGuid() }));
        await WaitForAsync(() => transport.Sent.Any(item => item.Kind == "runtime.rpc.response"));

        Assert.False(ReadRpcSuccess(transport.Sent.Last(item => item.Kind == "runtime.rpc.response").Payload));
        Assert.Equal(0, runtime.AgentInventoryCalls);
    }

    [Fact]
    public async Task InboundTaskRequiresGeneralGrantAndDuplicateDoesNotExecuteTwice()
    {
        var peer = Peer();
        var transport = new FakeTransport();
        var taskExecutor = new FakeTaskExecutor();
        await using var coordinator = CreateFullCoordinator(transport, peer, new FakeRuntime(), taskExecutor);
        await coordinator.InitialiseAsync(CancellationToken.None);
        transport.Observe(peer);
        var task = Envelope(peer.DeviceId);

        transport.Emit(peer.DeviceId, "task.request", JsonSerializer.Serialize(task));
        await WaitForAsync(() => transport.Sent.Any(item => item.Kind == "task.receipt"));
        Assert.Equal(MeshTaskStatus.Failed, ReadReceipt(transport.Sent.Last(item => item.Kind == "task.receipt").Payload).Status);
        Assert.Equal(0, taskExecutor.Calls);

        await coordinator.SetPeerCapabilityPermissionAsync(peer.DeviceId, MeshCoordinator.RemoteTaskCapability, true, CancellationToken.None);
        var secondTask = Envelope(peer.DeviceId);
        transport.Sent.Clear();
        transport.Emit(peer.DeviceId, "task.request", JsonSerializer.Serialize(secondTask));
        await WaitForAsync(() => transport.Sent.Count(item => item.Kind == "task.receipt") >= 3);
        Assert.Equal(1, taskExecutor.Calls);
        Assert.Equal(MeshTaskStatus.Succeeded, ReadReceipt(transport.Sent.Last(item => item.Kind == "task.receipt").Payload).Status);

        transport.Emit(peer.DeviceId, "task.request", JsonSerializer.Serialize(secondTask));
        await WaitForAsync(() => transport.Sent.Count(item => item.Kind == "task.receipt") >= 4);
        Assert.Equal(1, taskExecutor.Calls);
        Assert.Equal(MeshTaskStatus.Succeeded, ReadReceipt(transport.Sent.Last(item => item.Kind == "task.receipt").Payload).Status);
    }

    [Fact]
    public async Task OutboundTaskIsNotMarkedAcceptedBeforeTargetReceipt()
    {
        var peer = Peer();
        var transport = new FakeTransport();
        await using var coordinator = CreateFullCoordinator(transport, peer, new FakeRuntime(), new FakeTaskExecutor());
        await coordinator.InitialiseAsync(CancellationToken.None);
        transport.Observe(peer);

        var sent = await coordinator.DelegateTaskAsync(peer.DeviceId, "Summarise this reasoning task", [], [], HavenSurface.Tasks, CancellationToken.None);
        Assert.Equal(MeshTaskStatus.Sending, sent.Status);
        Assert.Contains("waiting", sent.Message, StringComparison.OrdinalIgnoreCase);

        var completed = new MeshTaskReceipt(sent.TaskId, MeshTaskStatus.Succeeded, DateTimeOffset.UtcNow, "done", "result");
        transport.Emit(peer.DeviceId, "task.receipt", JsonSerializer.Serialize(completed));
        await WaitForAsync(async () => (await coordinator.GetDashboardAsync(CancellationToken.None)).RemoteTasks.Any(task => task.TaskId == sent.TaskId && task.Status == MeshTaskStatus.Succeeded));
    }

    [Fact]
    public async Task CapabilityBearingInboundTaskIsRefusedByReasoningOnlyExecutor()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var executor = new MeshInboundTaskExecutor(provider);
        var task = Envelope(Guid.NewGuid()) with { RequiredCapabilities = ["run-command"] };

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => executor.ExecuteAsync(task, CancellationToken.None));
        Assert.Contains("without tools", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Guid LocalId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PeerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static MeshPeerRecord Peer() => new(PeerId, "Remote PC", MeshDeviceClass.Desktop, CapabilityPlatform.Windows, new string('c', 64), MeshPeerTrustState.Trusted, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(-4), null, "127.0.0.1:45001");

    private static MeshTaskEnvelope Envelope(Guid sourceId) => new(Guid.NewGuid(), sourceId, LocalId, "Reason about this", [], [], $"test:{Guid.NewGuid():N}", DateTimeOffset.UtcNow, HavenSurface.Tasks);

    private static MeshCoordinator CreateFullCoordinator(FakeTransport transport, MeshPeerRecord peer, FakeRuntime runtime, FakeTaskExecutor taskExecutor)
    {
        var identity = new MeshLocalIdentity(LocalId, "Local PC", MeshDeviceClass.Desktop, CapabilityPlatform.Windows, new string('d', 64), DateTimeOffset.UtcNow.AddDays(-1));
        var state = new MeshPersistentState(MeshPersistentState.CurrentVersion, identity, [peer], [], [], []);
        var store = new MemoryStateStore(state);
        var secrets = new MemorySecrets();
        secrets.Values[LocalId] = "test-secret";
        return new MeshCoordinator(store, secrets, transport, new EmptyCapabilities(), new EmptyMerge(), new FakeDeviceExecutor(), new FakeDiscovery(), runtime, taskExecutor);
    }

    private static bool ReadRpcSuccess(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("Success").GetBoolean();
    }

    private static MeshTaskReceipt ReadReceipt(string payload) => JsonSerializer.Deserialize<MeshTaskReceipt>(payload) ?? throw new InvalidDataException("Task receipt payload was empty.");

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) { timeout.Token.ThrowIfCancellationRequested(); await Task.Delay(20, timeout.Token); }
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await condition()) { timeout.Token.ThrowIfCancellationRequested(); await Task.Delay(20, timeout.Token); }
    }

    private sealed class FakeRuntime : IMeshInboundRuntimeExecutor
    {
        public int ModelInventoryCalls { get; private set; }
        public int AgentInventoryCalls { get; private set; }
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
        {
            ModelInventoryCalls++;
            ModelDescriptor model = new("local-model", 1, "test", "1B", "Q4", new HashSet<ToolCapability>(), DateTimeOffset.UtcNow);
            return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([new("ollama", true, model)]);
        }
        public Task<string> CompleteModelAsync(string providerId, OllamaChatRequest request, CancellationToken cancellationToken) => Task.FromResult("complete");
        public Task<OllamaToolResponse> ChatWithToolsAsync(string providerId, OllamaToolRequest request, CancellationToken cancellationToken) => Task.FromResult(new OllamaToolResponse("tool", []));
        public Task<IReadOnlyList<AgentDefinition>> GetAgentsAsync(CancellationToken cancellationToken)
        {
            AgentInventoryCalls++;
            return Task.FromResult<IReadOnlyList<AgentDefinition>>([new(Guid.NewGuid(), "Agent", "desc", "instructions", "agent", "local-model", null, "", "{}", false, true, DateTimeOffset.UtcNow)]);
        }
        public Task<string> ExecuteAgentAsync(Guid agentId, string prompt, CancellationToken cancellationToken) => Task.FromResult("agent result");
    }

    private sealed class FakeTaskExecutor : IMeshInboundTaskExecutor
    {
        public int Calls { get; private set; }
        public Task<string> ExecuteAsync(MeshTaskEnvelope task, CancellationToken cancellationToken) { Calls++; return Task.FromResult("task result"); }
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

    private sealed class FakeDeviceExecutor : IMeshInboundDeviceActionExecutor
    {
        public Task<DeviceCapabilitySnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(new DeviceCapabilitySnapshot(new DeviceTargetDescriptor("current", "Local", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice), true, DateTimeOffset.UtcNow, []));
        public Task<DeviceActionResult> ExecuteAsync(string actionKey, IReadOnlyDictionary<string, string>? parameters, bool permissionGranted, CancellationToken cancellationToken) => Task.FromResult(new DeviceActionResult(DeviceActionResultStatus.Success, actionKey, "current", "done"));
    }

    private sealed class FakeDiscovery : IMeshDiscoveryService
    {
        public event Action<MeshDiscoveryCandidate>? CandidateObserved { add { } remove { } }
        public bool IsRunning { get; private set; }
        public Task StartAsync(MeshLocalIdentity localIdentity, string localEndpoint, CancellationToken cancellationToken) { IsRunning = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) { IsRunning = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { IsRunning = false; return ValueTask.CompletedTask; }
    }

    private sealed class FakeTransport : IMeshTransport
    {
        public event Action<MeshTransportPeer>? PeerObserved;
        public event Action<MeshTransportPeer>? PairingCompleted { add { } remove { } }
        public event Action<Guid, MeshConnectionState>? ConnectionChanged;
        public event Action<MeshTransportMessage>? MessageReceived;
        public bool IsRunning { get; private set; }
        public string? LocalEndpoint { get; private set; } = "127.0.0.1:45000";
        public List<(Guid PeerId, string Kind, string Payload)> Sent { get; } = [];
        public Task StartAsync(MeshLocalIdentity localIdentity, string privateKey, CancellationToken cancellationToken) { IsRunning = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) { IsRunning = false; return Task.CompletedTask; }
        public Task SetPairingChallengeAsync(MeshPairingChallenge? challenge, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetTrustedPeersAsync(IReadOnlyList<MeshPeerRecord> peers, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MeshTransportPeer> PairAsync(string endpoint, Guid challengeId, string verificationCode, string expectedRemoteFingerprint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ConnectAsync(MeshPeerRecord peer, CancellationToken cancellationToken) { ConnectionChanged?.Invoke(peer.DeviceId, MeshConnectionState.Connected); return Task.CompletedTask; }
        public Task DisconnectAsync(Guid peerDeviceId, CancellationToken cancellationToken) { ConnectionChanged?.Invoke(peerDeviceId, MeshConnectionState.Disconnected); return Task.CompletedTask; }
        public Task SendAsync(Guid peerDeviceId, string kind, string payload, CancellationToken cancellationToken) { Sent.Add((peerDeviceId, kind, payload)); return Task.CompletedTask; }
        public void Observe(MeshPeerRecord peer)
        {
            PeerObserved?.Invoke(new MeshTransportPeer(peer.DeviceId, peer.DisplayName, peer.DeviceClass, peer.Platform, peer.PublicKeyFingerprint, peer.LastKnownEndpoint ?? "127.0.0.1:45001", []));
            ConnectionChanged?.Invoke(peer.DeviceId, MeshConnectionState.Connected);
        }
        public void Emit(Guid sourceDeviceId, string kind, string payload) => MessageReceived?.Invoke(new MeshTransportMessage(Guid.NewGuid(), sourceDeviceId, kind, payload, DateTimeOffset.UtcNow));
        public ValueTask DisposeAsync() { IsRunning = false; return ValueTask.CompletedTask; }
    }
}
