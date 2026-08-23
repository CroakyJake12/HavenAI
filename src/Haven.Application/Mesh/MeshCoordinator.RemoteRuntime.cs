using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed partial class MeshCoordinator
{
    public const string RemoteModelCapability = "mesh-model-use";
    public const string RemoteAgentCapability = "mesh-agent-use";
    private static readonly TimeSpan RuntimeRpcTimeout = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<MeshRuntimeRpcResponse>> _pendingRuntimeRpcs = new();
    private IMeshInboundRuntimeExecutor? _inboundRuntime;

    public MeshCoordinator(
        IMeshStateStore stateStore,
        IMeshIdentitySecretStore identitySecrets,
        IMeshTransport transport,
        IMeshCapabilitySource capabilities,
        IMeshResourceMergeService merge,
        IMeshInboundDeviceActionExecutor inboundDeviceActions,
        IMeshDiscoveryService discovery,
        IMeshInboundRuntimeExecutor inboundRuntime)
        : this(stateStore, identitySecrets, transport, capabilities, merge, inboundDeviceActions, discovery)
    {
        _inboundRuntime = inboundRuntime ?? throw new ArgumentNullException(nameof(inboundRuntime));
    }

    public async Task<IReadOnlyList<ProviderModelDescriptor>> GetRemoteModelsAsync(CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        var models = new List<ProviderModelDescriptor>();
        foreach (var peer in ConnectedTrustedPeers())
        {
            var requestId = Guid.NewGuid();
            var response = await SendRuntimeRpcAsync(peer.DeviceId, "runtime.models.request", new MeshRuntimeInventoryRequest(requestId), requestId, cancellationToken).ConfigureAwait(false);
            if (!response.Success) continue;
            MeshModelInventoryItem[] inventory;
            try { inventory = JsonSerializer.Deserialize<MeshModelInventoryItem[]>(response.Payload) ?? []; }
            catch (JsonException) { continue; }
            foreach (var item in inventory.Where(item => !string.Equals(item.ProviderId, MeshRemoteModelProvider.MeshProviderId, StringComparison.OrdinalIgnoreCase)))
            {
                var route = MeshRemoteModelProvider.EncodeRoute(peer.DeviceId, item.ProviderId, item.Name);
                var descriptor = new ModelDescriptor(route, item.SizeBytes, $"{peer.DisplayName} / {item.Family}", item.ParameterSize, item.Quantization, item.Capabilities.ToHashSet(), item.ModifiedAt);
                models.Add(new ProviderModelDescriptor(MeshRemoteModelProvider.MeshProviderId, true, descriptor, item.ContextWindow, $"{peer.DisplayName} · {item.DisplayName ?? item.Name}"));
            }
        }
        return models;
    }

    public async Task<string> CompleteRemoteModelAsync(string route, OllamaChatRequest request, CancellationToken cancellationToken)
    {
        var target = MeshRemoteModelProvider.DecodeRoute(route);
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        _ = RequireTrustedPeer(target.DeviceId);
        var requestId = Guid.NewGuid();
        var payload = new MeshModelCompleteRequest(requestId, target.ProviderId, request with { Model = target.ModelName });
        var response = await SendRuntimeRpcAsync(target.DeviceId, "runtime.model.complete.request", payload, requestId, cancellationToken).ConfigureAwait(false);
        if (!response.Success) throw new IOException(response.Error ?? "Remote model completion failed.");
        return response.Payload;
    }

    public async Task<OllamaToolResponse> ChatWithRemoteModelToolsAsync(string route, OllamaToolRequest request, CancellationToken cancellationToken)
    {
        var target = MeshRemoteModelProvider.DecodeRoute(route);
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        _ = RequireTrustedPeer(target.DeviceId);
        var requestId = Guid.NewGuid();
        var payload = new MeshModelToolsRequest(requestId, target.ProviderId, request with { Model = target.ModelName });
        var response = await SendRuntimeRpcAsync(target.DeviceId, "runtime.model.tools.request", payload, requestId, cancellationToken).ConfigureAwait(false);
        if (!response.Success) throw new IOException(response.Error ?? "Remote model tool request failed.");
        return JsonSerializer.Deserialize<OllamaToolResponse>(response.Payload) ?? throw new InvalidDataException("Remote model returned an empty tool response.");
    }

    public async Task<IReadOnlyList<MeshRemoteAgentDescriptor>> GetRemoteAgentsAsync(CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        var agents = new List<MeshRemoteAgentDescriptor>();
        foreach (var peer in ConnectedTrustedPeers())
        {
            var requestId = Guid.NewGuid();
            var response = await SendRuntimeRpcAsync(peer.DeviceId, "runtime.agents.request", new MeshRuntimeInventoryRequest(requestId), requestId, cancellationToken).ConfigureAwait(false);
            if (!response.Success) continue;
            MeshAgentInventoryItem[] inventory;
            try { inventory = JsonSerializer.Deserialize<MeshAgentInventoryItem[]>(response.Payload) ?? []; }
            catch (JsonException) { continue; }
            agents.AddRange(inventory.Select(item => new MeshRemoteAgentDescriptor(peer.DeviceId, peer.DisplayName, item.Id, item.Name, item.Description, item.IconKey, item.PreferredModel)));
        }
        return agents;
    }

    public async Task<string> ExecuteRemoteAgentAsync(Guid deviceId, Guid agentId, string prompt, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        _ = RequireTrustedPeer(deviceId);
        if (agentId == Guid.Empty) throw new ArgumentException("Remote agent ID is required.", nameof(agentId));
        if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("Remote agent prompt is required.", nameof(prompt));
        var requestId = Guid.NewGuid();
        var response = await SendRuntimeRpcAsync(deviceId, "runtime.agent.execute.request", new MeshAgentExecuteRequest(requestId, agentId, prompt.Trim()), requestId, cancellationToken).ConfigureAwait(false);
        if (!response.Success) throw new IOException(response.Error ?? "Remote agent execution failed.");
        return response.Payload;
    }

    private IEnumerable<MeshPeerRecord> ConnectedTrustedPeers() => _state.TrustedPeers
        .Where(peer => peer.TrustState == MeshPeerTrustState.Trusted && GetPresence(peer).Connection == MeshConnectionState.Connected);

    private async Task<MeshRuntimeRpcResponse> SendRuntimeRpcAsync(Guid deviceId, string kind, object request, Guid requestId, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<MeshRuntimeRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRuntimeRpcs.TryAdd(requestId, completion)) throw new InvalidOperationException("Duplicate Mesh runtime RPC identifier.");
        try
        {
            await _transport.SendAsync(deviceId, kind, JsonSerializer.Serialize(request), cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RuntimeRpcTimeout);
            return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MeshRuntimeRpcResponse(requestId, false, string.Empty, "The remote Mesh runtime did not answer before the request timeout.");
        }
        catch (IOException ex) { return new MeshRuntimeRpcResponse(requestId, false, string.Empty, ex.Message); }
        finally { _pendingRuntimeRpcs.TryRemove(requestId, out _); }
    }

    private bool TryHandleRuntimeMessage(MeshTransportMessage message)
    {
        if (message.Kind == "runtime.rpc.response")
        {
            try
            {
                var response = JsonSerializer.Deserialize<MeshRuntimeRpcResponse>(message.Payload);
                if (response is not null && _pendingRuntimeRpcs.TryGetValue(response.RequestId, out var pending)) pending.TrySetResult(response);
            }
            catch (JsonException) { }
            return true;
        }
        if (message.Kind == "runtime.models.request") { _ = HandleModelInventoryRequestAsync(message); return true; }
        if (message.Kind == "runtime.model.complete.request") { _ = HandleModelCompleteRequestAsync(message); return true; }
        if (message.Kind == "runtime.model.tools.request") { _ = HandleModelToolsRequestAsync(message); return true; }
        if (message.Kind == "runtime.agents.request") { _ = HandleAgentInventoryRequestAsync(message); return true; }
        if (message.Kind == "runtime.agent.execute.request") { _ = HandleAgentExecuteRequestAsync(message); return true; }
        return false;
    }

    private async Task HandleModelInventoryRequestAsync(MeshTransportMessage message)
    {
        var request = TryDeserialize<MeshRuntimeInventoryRequest>(message.Payload);
        if (request is null) return;
        await ReplyRuntimeGuardedAsync(message.SourceDeviceId, request.RequestId, RemoteModelCapability, async () =>
        {
            if (_inboundRuntime is null) throw new InvalidOperationException("This Haven build has no inbound model runtime.");
            var models = await _inboundRuntime.GetModelsAsync(CancellationToken.None).ConfigureAwait(false);
            var inventory = models.Where(model => !string.Equals(model.ProviderId, MeshRemoteModelProvider.MeshProviderId, StringComparison.OrdinalIgnoreCase))
                .Select(model => new MeshModelInventoryItem(model.ProviderId, model.Name, model.Model.SizeBytes, model.Model.Family, model.Model.ParameterSize, model.Model.Quantization, model.Capabilities.ToArray(), model.Model.ModifiedAt, model.ContextWindow, model.DisplayName)).ToArray();
            return JsonSerializer.Serialize(inventory);
        }).ConfigureAwait(false);
    }

    private async Task HandleModelCompleteRequestAsync(MeshTransportMessage message)
    {
        var request = TryDeserialize<MeshModelCompleteRequest>(message.Payload);
        if (request is null) return;
        await ReplyRuntimeGuardedAsync(message.SourceDeviceId, request.RequestId, RemoteModelCapability, async () =>
        {
            if (_inboundRuntime is null) throw new InvalidOperationException("This Haven build has no inbound model runtime.");
            if (string.Equals(request.ProviderId, MeshRemoteModelProvider.MeshProviderId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Mesh model recursion is not allowed.");
            return await _inboundRuntime.CompleteModelAsync(request.ProviderId, request.Request, CancellationToken.None).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private async Task HandleModelToolsRequestAsync(MeshTransportMessage message)
    {
        var request = TryDeserialize<MeshModelToolsRequest>(message.Payload);
        if (request is null) return;
        await ReplyRuntimeGuardedAsync(message.SourceDeviceId, request.RequestId, RemoteModelCapability, async () =>
        {
            if (_inboundRuntime is null) throw new InvalidOperationException("This Haven build has no inbound model runtime.");
            if (string.Equals(request.ProviderId, MeshRemoteModelProvider.MeshProviderId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Mesh model recursion is not allowed.");
            return JsonSerializer.Serialize(await _inboundRuntime.ChatWithToolsAsync(request.ProviderId, request.Request, CancellationToken.None).ConfigureAwait(false));
        }).ConfigureAwait(false);
    }

    private async Task HandleAgentInventoryRequestAsync(MeshTransportMessage message)
    {
        var request = TryDeserialize<MeshRuntimeInventoryRequest>(message.Payload);
        if (request is null) return;
        await ReplyRuntimeGuardedAsync(message.SourceDeviceId, request.RequestId, RemoteAgentCapability, async () =>
        {
            if (_inboundRuntime is null) throw new InvalidOperationException("This Haven build has no inbound agent runtime.");
            var agents = await _inboundRuntime.GetAgentsAsync(CancellationToken.None).ConfigureAwait(false);
            return JsonSerializer.Serialize(agents.Where(agent => agent.IsEnabled).Select(agent => new MeshAgentInventoryItem(agent.Id, agent.Name, agent.Description, agent.IconKey, agent.PreferredModel)).ToArray());
        }).ConfigureAwait(false);
    }

    private async Task HandleAgentExecuteRequestAsync(MeshTransportMessage message)
    {
        var request = TryDeserialize<MeshAgentExecuteRequest>(message.Payload);
        if (request is null) return;
        await ReplyRuntimeGuardedAsync(message.SourceDeviceId, request.RequestId, RemoteAgentCapability, async () =>
        {
            if (_inboundRuntime is null) throw new InvalidOperationException("This Haven build has no inbound agent runtime.");
            return await _inboundRuntime.ExecuteAgentAsync(request.AgentId, request.Prompt, CancellationToken.None).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private async Task ReplyRuntimeGuardedAsync(Guid sourceDeviceId, Guid requestId, string requiredGrant, Func<Task<string>> action)
    {
        try
        {
            var peer = RequireTrustedPeer(sourceDeviceId);
            if (!(peer.AllowedRemoteCapabilities ?? []).Contains(requiredGrant, StringComparer.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"Remote capability '{requiredGrant}' is not allowed for {peer.DisplayName} on this device.");
            var payload = await action().ConfigureAwait(false);
            await ReplyRuntimeRpcAsync(sourceDeviceId, new MeshRuntimeRpcResponse(requestId, true, payload, null)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ReplyRuntimeRpcAsync(sourceDeviceId, new MeshRuntimeRpcResponse(requestId, false, string.Empty, ex.Message)).ConfigureAwait(false);
        }
    }

    private Task ReplyRuntimeRpcAsync(Guid peerId, MeshRuntimeRpcResponse response) =>
        _transport.SendAsync(peerId, "runtime.rpc.response", JsonSerializer.Serialize(response), CancellationToken.None);

    private static T? TryDeserialize<T>(string payload) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(payload); } catch (JsonException) { return null; }
    }

    private sealed record MeshRuntimeInventoryRequest(Guid RequestId);
    private sealed record MeshModelCompleteRequest(Guid RequestId, string ProviderId, OllamaChatRequest Request);
    private sealed record MeshModelToolsRequest(Guid RequestId, string ProviderId, OllamaToolRequest Request);
    private sealed record MeshAgentExecuteRequest(Guid RequestId, Guid AgentId, string Prompt);
    private sealed record MeshRuntimeRpcResponse(Guid RequestId, bool Success, string Payload, string? Error);
    private sealed record MeshModelInventoryItem(string ProviderId, string Name, long SizeBytes, string Family, string ParameterSize, string Quantization, ToolCapability[] Capabilities, DateTimeOffset ModifiedAt, int? ContextWindow, string? DisplayName);
    private sealed record MeshAgentInventoryItem(Guid Id, string Name, string Description, string IconKey, string PreferredModel);
}
