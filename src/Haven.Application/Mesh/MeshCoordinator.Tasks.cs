using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed partial class MeshCoordinator
{
    public const string RemoteTaskCapability = "mesh-task-use";
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _inboundTaskCancellations = new();
    private IMeshInboundTaskExecutor? _inboundTasks;

    public MeshCoordinator(
        IMeshStateStore stateStore,
        IMeshIdentitySecretStore identitySecrets,
        IMeshTransport transport,
        IMeshCapabilitySource capabilities,
        IMeshResourceMergeService merge,
        IMeshInboundDeviceActionExecutor inboundDeviceActions,
        IMeshDiscoveryService discovery,
        IMeshInboundRuntimeExecutor inboundRuntime,
        IMeshInboundTaskExecutor inboundTasks)
        : this(stateStore, identitySecrets, transport, capabilities, merge, inboundDeviceActions, discovery, inboundRuntime)
    {
        _inboundTasks = inboundTasks ?? throw new ArgumentNullException(nameof(inboundTasks));
    }

    private bool TryHandleTaskMessage(MeshTransportMessage message)
    {
        if (message.Kind == "task.receipt")
        {
            try
            {
                var receipt = JsonSerializer.Deserialize<MeshTaskReceipt>(message.Payload);
                if (receipt is not null && _state.RemoteTasks.Any(task => task.TaskId == receipt.TaskId)) _ = RecordTaskAsync(receipt, CancellationToken.None);
            }
            catch (JsonException) { }
            return true;
        }
        if (message.Kind == "task.request")
        {
            _ = HandleInboundTaskRequestAsync(message);
            return true;
        }
        if (message.Kind == "task.cancel")
        {
            try
            {
                var control = JsonSerializer.Deserialize<MeshTaskControl>(message.Payload);
                if (control is not null && _inboundTaskCancellations.TryGetValue(control.TaskId, out var cancellation)) cancellation.Cancel();
            }
            catch (JsonException) { }
            return true;
        }
        return false;
    }

    private async Task HandleInboundTaskRequestAsync(MeshTransportMessage message)
    {
        MeshTaskEnvelope? task;
        try { task = JsonSerializer.Deserialize<MeshTaskEnvelope>(message.Payload); }
        catch (JsonException) { return; }
        if (task is null) return;

        MeshTaskReceipt receipt;
        try
        {
            var peer = RequireTrustedPeer(message.SourceDeviceId);
            if (task.SourceDeviceId != message.SourceDeviceId || task.TargetDeviceId != _state.LocalIdentity?.DeviceId || task.TaskId == Guid.Empty || string.IsNullOrWhiteSpace(task.IdempotencyKey))
                throw new InvalidDataException("The delegated Mesh task identity or target is invalid.");

            var existing = _state.RemoteTasks.FirstOrDefault(item => item.TaskId == task.TaskId);
            if (existing is not null)
            {
                await SendTaskReceiptAsync(message.SourceDeviceId, existing).ConfigureAwait(false);
                return;
            }

            var grants = (peer.AllowedRemoteCapabilities ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!grants.Contains(RemoteTaskCapability))
            {
                receipt = new MeshTaskReceipt(task.TaskId, MeshTaskStatus.Failed, DateTimeOffset.UtcNow, $"{peer.DisplayName} is trusted, but remote task execution is not allowed on this device.", FailureCode: "mesh-task-permission-required");
                await RecordTaskAsync(receipt, CancellationToken.None).ConfigureAwait(false);
                await SendTaskReceiptAsync(message.SourceDeviceId, receipt).ConfigureAwait(false);
                return;
            }
            var deniedCapabilities = task.RequiredCapabilities.Where(required => !grants.Contains(required)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (deniedCapabilities.Length > 0)
            {
                receipt = new MeshTaskReceipt(task.TaskId, MeshTaskStatus.Failed, DateTimeOffset.UtcNow, $"Remote task capability permission is missing: {string.Join(", ", deniedCapabilities)}.", FailureCode: "mesh-task-capability-permission-required");
                await RecordTaskAsync(receipt, CancellationToken.None).ConfigureAwait(false);
                await SendTaskReceiptAsync(message.SourceDeviceId, receipt).ConfigureAwait(false);
                return;
            }
            if (_inboundTasks is null) throw new InvalidOperationException("This Haven build has no inbound Mesh task executor.");

            receipt = new MeshTaskReceipt(task.TaskId, MeshTaskStatus.Accepted, DateTimeOffset.UtcNow, $"Accepted delegated task from {peer.DisplayName}.");
            await RecordTaskAsync(receipt, CancellationToken.None).ConfigureAwait(false);
            await SendTaskReceiptAsync(message.SourceDeviceId, receipt).ConfigureAwait(false);
            _ = ExecuteInboundTaskAsync(task, message.SourceDeviceId);
        }
        catch (Exception ex)
        {
            receipt = new MeshTaskReceipt(task.TaskId, MeshTaskStatus.Failed, DateTimeOffset.UtcNow, ex.Message, FailureCode: "mesh-task-rejected");
            await RecordTaskAsync(receipt, CancellationToken.None).ConfigureAwait(false);
            try { await SendTaskReceiptAsync(message.SourceDeviceId, receipt).ConfigureAwait(false); } catch { }
        }
    }

    private async Task ExecuteInboundTaskAsync(MeshTaskEnvelope task, Guid sourceDeviceId)
    {
        using var cancellation = new CancellationTokenSource();
        if (!_inboundTaskCancellations.TryAdd(task.TaskId, cancellation)) return;
        try
        {
            var running = new MeshTaskReceipt(task.TaskId, MeshTaskStatus.Running, DateTimeOffset.UtcNow, "Running on the target Mesh device.");
            await RecordTaskAsync(running, CancellationToken.None).ConfigureAwait(false);
            await SendTaskReceiptAsync(sourceDeviceId, running).ConfigureAwait(false);
            var result = await _inboundTasks!.ExecuteAsync(task, cancellation.Token).ConfigureAwait(false);
            var complete = new MeshTaskReceipt(task.TaskId, MeshTaskStatus.Succeeded, DateTimeOffset.UtcNow, "Delegated Mesh task completed.", result);
            await RecordTaskAsync(complete, CancellationToken.None).ConfigureAwait(false);
            await SendTaskReceiptAsync(sourceDeviceId, complete).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            var cancelled = new MeshTaskReceipt(task.TaskId, MeshTaskStatus.Cancelled, DateTimeOffset.UtcNow, "Delegated Mesh task was cancelled.");
            await RecordTaskAsync(cancelled, CancellationToken.None).ConfigureAwait(false);
            try { await SendTaskReceiptAsync(sourceDeviceId, cancelled).ConfigureAwait(false); } catch { }
        }
        catch (Exception ex)
        {
            var failed = new MeshTaskReceipt(task.TaskId, MeshTaskStatus.Failed, DateTimeOffset.UtcNow, ex.Message, FailureCode: "mesh-task-execution-failed");
            await RecordTaskAsync(failed, CancellationToken.None).ConfigureAwait(false);
            try { await SendTaskReceiptAsync(sourceDeviceId, failed).ConfigureAwait(false); } catch { }
        }
        finally
        {
            _inboundTaskCancellations.TryRemove(task.TaskId, out _);
        }
    }

    private Task SendTaskReceiptAsync(Guid peerId, MeshTaskReceipt receipt) =>
        _transport.SendAsync(peerId, "task.receipt", JsonSerializer.Serialize(receipt), CancellationToken.None);

    private sealed record MeshTaskControl(Guid TaskId);
}
