using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Application;

public sealed partial class MeshCoordinator
{
    private static readonly TimeSpan DeviceRpcTimeout = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<MeshDeviceRpcResponse>> _pendingDeviceRpcs = new();
    private IMeshInboundDeviceActionExecutor? _inboundDeviceActions;

    public MeshCoordinator(
        IMeshStateStore stateStore,
        IMeshIdentitySecretStore identitySecrets,
        IMeshTransport transport,
        IMeshCapabilitySource capabilities,
        IMeshResourceMergeService merge,
        IMeshInboundDeviceActionExecutor inboundDeviceActions)
        : this(stateStore, identitySecrets, transport, capabilities, merge)
    {
        _inboundDeviceActions = inboundDeviceActions ?? throw new ArgumentNullException(nameof(inboundDeviceActions));
    }

    public async Task SetPeerCapabilityPermissionAsync(Guid deviceId, string capabilityKey, bool allowed, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(capabilityKey)) throw new ArgumentException("Capability key is required.", nameof(capabilityKey));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var peer = RequireTrustedPeer(deviceId);
            var grants = (peer.AllowedRemoteCapabilities ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (allowed) grants.Add(capabilityKey.Trim()); else grants.Remove(capabilityKey.Trim());
            await ReplacePeerAsync(peer with { AllowedRemoteCapabilities = grants.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray() }, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        StateChanged?.Invoke();
    }

    public async Task<DeviceCapabilitySnapshot> GetRemoteDeviceSnapshotAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        var peer = RequireTrustedPeer(deviceId);
        var presence = GetPresence(peer);
        if (presence.Connection != MeshConnectionState.Connected)
            return new DeviceCapabilitySnapshot(new DeviceTargetDescriptor(deviceId.ToString("N"), peer.DisplayName, peer.Platform, DeviceTargetKind.MeshDevice, MeshDeviceActionProvider.MeshProviderId), false, DateTimeOffset.UtcNow, []);
        var requestId = Guid.NewGuid();
        var response = await SendDeviceRpcAsync(deviceId, "device.snapshot.request", new MeshDeviceSnapshotRequest(requestId), requestId, cancellationToken).ConfigureAwait(false);
        if (!response.Success) throw new IOException(response.Error ?? "Remote device snapshot failed.");
        return JsonSerializer.Deserialize<DeviceCapabilitySnapshot>(response.Payload) ?? throw new InvalidDataException("Remote device returned an empty capability snapshot.");
    }

    public async Task<DeviceActionResult> ExecuteRemoteDeviceActionAsync(
        Guid deviceId,
        string actionKey,
        string capabilityKey,
        IReadOnlyDictionary<string, string>? parameters,
        bool sourcePermissionGranted,
        CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        _ = RequireTrustedPeer(deviceId);
        if (string.IsNullOrWhiteSpace(actionKey)) throw new ArgumentException("Device action key is required.", nameof(actionKey));
        if (string.IsNullOrWhiteSpace(capabilityKey)) throw new ArgumentException("Device capability key is required.", nameof(capabilityKey));
        var requestId = Guid.NewGuid();
        var request = new MeshDeviceActionRequest(requestId, actionKey.Trim(), capabilityKey.Trim(), parameters, sourcePermissionGranted);
        var response = await SendDeviceRpcAsync(deviceId, "device.action.request", request, requestId, cancellationToken).ConfigureAwait(false);
        if (!response.Success)
            return new DeviceActionResult(DeviceActionResultStatus.ConnectionLost, actionKey, deviceId.ToString("N"), response.Error ?? "Remote device action failed.");
        return JsonSerializer.Deserialize<DeviceActionResult>(response.Payload) ?? new DeviceActionResult(DeviceActionResultStatus.PlatformError, actionKey, deviceId.ToString("N"), "Remote device returned an empty action result.");
    }

    private async Task<MeshDeviceRpcResponse> SendDeviceRpcAsync(Guid deviceId, string kind, object request, Guid requestId, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<MeshDeviceRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingDeviceRpcs.TryAdd(requestId, completion)) throw new InvalidOperationException("Duplicate Mesh DEVICE RPC identifier.");
        try
        {
            await _transport.SendAsync(deviceId, kind, JsonSerializer.Serialize(request), cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DeviceRpcTimeout);
            return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new MeshDeviceRpcResponse(requestId, false, string.Empty, "The remote device did not answer before the Mesh request timeout.");
        }
        catch (IOException ex)
        {
            return new MeshDeviceRpcResponse(requestId, false, string.Empty, ex.Message);
        }
        finally { _pendingDeviceRpcs.TryRemove(requestId, out _); }
    }

    private bool TryHandleDeviceMessage(MeshTransportMessage message)
    {
        if (message.Kind == "device.rpc.response")
        {
            try
            {
                var response = JsonSerializer.Deserialize<MeshDeviceRpcResponse>(message.Payload);
                if (response is not null && _pendingDeviceRpcs.TryGetValue(response.RequestId, out var pending)) pending.TrySetResult(response);
            }
            catch (JsonException) { }
            return true;
        }
        if (message.Kind == "device.snapshot.request")
        {
            _ = HandleDeviceSnapshotRequestAsync(message);
            return true;
        }
        if (message.Kind == "device.action.request")
        {
            _ = HandleDeviceActionRequestAsync(message);
            return true;
        }
        return false;
    }

    private async Task HandleDeviceSnapshotRequestAsync(MeshTransportMessage message)
    {
        MeshDeviceSnapshotRequest? request = null;
        try { request = JsonSerializer.Deserialize<MeshDeviceSnapshotRequest>(message.Payload); } catch (JsonException) { }
        if (request is null) return;
        try
        {
            _ = RequireTrustedPeer(message.SourceDeviceId);
            if (_inboundDeviceActions is null) throw new InvalidOperationException("This Haven build has no inbound DEVICE executor.");
            var snapshot = await _inboundDeviceActions.GetSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
            await ReplyDeviceRpcAsync(message.SourceDeviceId, new MeshDeviceRpcResponse(request.RequestId, true, JsonSerializer.Serialize(snapshot), null)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ReplyDeviceRpcAsync(message.SourceDeviceId, new MeshDeviceRpcResponse(request.RequestId, false, string.Empty, ex.Message)).ConfigureAwait(false);
        }
    }

    private async Task HandleDeviceActionRequestAsync(MeshTransportMessage message)
    {
        MeshDeviceActionRequest? request = null;
        try { request = JsonSerializer.Deserialize<MeshDeviceActionRequest>(message.Payload); } catch (JsonException) { }
        if (request is null) return;
        try
        {
            var peer = RequireTrustedPeer(message.SourceDeviceId);
            var allowed = (peer.AllowedRemoteCapabilities ?? []).Contains(request.CapabilityKey, StringComparer.OrdinalIgnoreCase);
            if (!request.SourcePermissionGranted)
            {
                var denied = new DeviceActionResult(DeviceActionResultStatus.PermissionRequired, request.ActionKey, _state.LocalIdentity!.DeviceId.ToString("N"), "The source Haven session has not granted DEVICE permission for this remote action.");
                await ReplyDeviceRpcAsync(message.SourceDeviceId, new MeshDeviceRpcResponse(request.RequestId, true, JsonSerializer.Serialize(denied), null)).ConfigureAwait(false);
                return;
            }
            if (!allowed)
            {
                var denied = new DeviceActionResult(DeviceActionResultStatus.PermissionRequired, request.ActionKey, _state.LocalIdentity!.DeviceId.ToString("N"), $"{peer.DisplayName} is trusted, but remote capability '{request.CapabilityKey}' is not allowed on this device.");
                await ReplyDeviceRpcAsync(message.SourceDeviceId, new MeshDeviceRpcResponse(request.RequestId, true, JsonSerializer.Serialize(denied), null)).ConfigureAwait(false);
                return;
            }
            if (_inboundDeviceActions is null) throw new InvalidOperationException("This Haven build has no inbound DEVICE executor.");
            var result = await _inboundDeviceActions.ExecuteAsync(request.ActionKey, request.Parameters, permissionGranted: true, CancellationToken.None).ConfigureAwait(false);
            await ReplyDeviceRpcAsync(message.SourceDeviceId, new MeshDeviceRpcResponse(request.RequestId, true, JsonSerializer.Serialize(result), null)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await ReplyDeviceRpcAsync(message.SourceDeviceId, new MeshDeviceRpcResponse(request.RequestId, false, string.Empty, ex.Message)).ConfigureAwait(false);
        }
    }

    private Task ReplyDeviceRpcAsync(Guid peerId, MeshDeviceRpcResponse response) =>
        _transport.SendAsync(peerId, "device.rpc.response", JsonSerializer.Serialize(response), CancellationToken.None);

    private sealed record MeshDeviceSnapshotRequest(Guid RequestId);
    private sealed record MeshDeviceActionRequest(Guid RequestId, string ActionKey, string CapabilityKey, IReadOnlyDictionary<string, string>? Parameters, bool SourcePermissionGranted);
    private sealed record MeshDeviceRpcResponse(Guid RequestId, bool Success, string Payload, string? Error);
}
