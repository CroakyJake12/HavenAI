using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed partial class MeshCoordinator
{
    public const string RemoteClipboardCapability = "mesh-clipboard-receive";
    public const string RemoteFileCapability = "mesh-file-receive";
    public const int MaximumClipboardBytes = 256 * 1024;
    public const int FileChunkBytes = 256 * 1024;
    public const long MaximumFileBytes = 64L * 1024 * 1024;

    private static readonly TimeSpan TransferRpcTimeout = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<Guid, PendingTransferReceipt> _pendingTransferReceipts = new();
    private readonly ConcurrentDictionary<Guid, PendingFileStart> _pendingFileStarts = new();
    private readonly ConcurrentQueue<MeshIncomingClipboard> _incomingClipboards = new();
    private readonly ConcurrentQueue<MeshReceivedFile> _receivedFiles = new();
    private readonly object _transferQueueGate = new();
    private Task _transferQueue = Task.CompletedTask;
    private IMeshFileTransferStore? _fileTransfers;

    public MeshCoordinator(
        IMeshStateStore stateStore,
        IMeshIdentitySecretStore identitySecrets,
        IMeshTransport transport,
        IMeshCapabilitySource capabilities,
        IMeshResourceMergeService merge,
        IMeshInboundDeviceActionExecutor inboundDeviceActions,
        IMeshDiscoveryService discovery,
        IMeshInboundRuntimeExecutor inboundRuntime,
        IMeshInboundTaskExecutor inboundTasks,
        IMeshFileTransferStore fileTransfers)
        : this(stateStore, identitySecrets, transport, capabilities, merge, inboundDeviceActions, discovery, inboundRuntime, inboundTasks)
    {
        _fileTransfers = fileTransfers ?? throw new ArgumentNullException(nameof(fileTransfers));
    }

    public async Task<MeshTransferReceipt> SendClipboardTextAsync(Guid targetDeviceId, string text, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        var peer = RequireTrustedPeer(targetDeviceId);
        EnsureTransferConnected(peer);
        text ??= string.Empty;
        if (Encoding.UTF8.GetByteCount(text) > MaximumClipboardBytes)
            throw new ArgumentOutOfRangeException(nameof(text), $"Mesh clipboard text is limited to {MaximumClipboardBytes / 1024} KiB.");

        var transferId = Guid.NewGuid();
        var completion = NewTransferCompletion(transferId, targetDeviceId);
        try
        {
            await _transport.SendAsync(targetDeviceId, "transfer.clipboard", JsonSerializer.Serialize(new MeshClipboardTransfer(transferId, text)), cancellationToken).ConfigureAwait(false);
            return await WaitForTransferReceiptAsync(transferId, completion, MeshTransferKind.ClipboardText, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return Failed(transferId, MeshTransferKind.ClipboardText, $"Clipboard transfer failed: {ex.Message}");
        }
        finally { _pendingTransferReceipts.TryRemove(transferId, out _); }
    }

    public async Task<MeshTransferReceipt> SendFileAsync(Guid targetDeviceId, string fileName, Stream content, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        var peer = RequireTrustedPeer(targetDeviceId);
        EnsureTransferConnected(peer);
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead || !content.CanSeek) throw new ArgumentException("Mesh file transfer requires a readable, seekable stream.", nameof(content));
        var startPosition = content.Position;
        var length = content.Length - startPosition;
        if (length < 0 || length > MaximumFileBytes) throw new ArgumentOutOfRangeException(nameof(content), $"Mesh files are limited to {MaximumFileBytes / 1024 / 1024} MiB.");
        var safeDisplayName = Path.GetFileName(fileName?.Trim());
        if (string.IsNullOrWhiteSpace(safeDisplayName)) throw new ArgumentException("A file name is required.", nameof(fileName));

        var transferId = Guid.NewGuid();
        var readyCompletion = new TaskCompletionSource<MeshFileReady>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiptCompletion = NewTransferCompletion(transferId, targetDeviceId);
        if (!_pendingFileStarts.TryAdd(transferId, new PendingFileStart(targetDeviceId, readyCompletion))) throw new InvalidOperationException("Duplicate Mesh file transfer identifier.");
        var accepted = false;
        try
        {
            await _transport.SendAsync(targetDeviceId, "transfer.file.start", JsonSerializer.Serialize(new MeshFileStart(transferId, safeDisplayName, length)), cancellationToken).ConfigureAwait(false);
            var ready = await WaitForFileReadyAsync(readyCompletion, cancellationToken).ConfigureAwait(false);
            if (!ready.Accepted) return Failed(transferId, MeshTransferKind.File, ready.Message);
            accepted = true;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[FileChunkBytes];
            var total = 0L;
            var chunkIndex = 0;
            while (total < length)
            {
                var requested = (int)Math.Min(buffer.Length, length - total);
                var read = await content.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
                if (read <= 0) break;
                hash.AppendData(buffer.AsSpan(0, read));
                var chunk = new MeshFileChunk(transferId, chunkIndex++, Convert.ToBase64String(buffer, 0, read));
                await _transport.SendAsync(targetDeviceId, "transfer.file.chunk", JsonSerializer.Serialize(chunk), cancellationToken).ConfigureAwait(false);
                total += read;
                if (receiptCompletion.Task.IsCompleted)
                    return await receiptCompletion.Task.ConfigureAwait(false);
            }
            if (total != length) return Failed(transferId, MeshTransferKind.File, "The selected file ended before the declared transfer length.");

            var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            await _transport.SendAsync(targetDeviceId, "transfer.file.complete", JsonSerializer.Serialize(new MeshFileComplete(transferId, chunkIndex, digest)), cancellationToken).ConfigureAwait(false);
            return await WaitForTransferReceiptAsync(transferId, receiptCompletion, MeshTransferKind.File, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            return Failed(transferId, MeshTransferKind.File, $"File transfer failed: {ex.Message}");
        }
        finally
        {
            _pendingFileStarts.TryRemove(transferId, out _);
            _pendingTransferReceipts.TryRemove(transferId, out _);
            if (accepted && !receiptCompletion.Task.IsCompletedSuccessfully)
            {
                try { await _transport.SendAsync(targetDeviceId, "transfer.file.abort", JsonSerializer.Serialize(new MeshFileAbort(transferId)), CancellationToken.None).ConfigureAwait(false); } catch { }
            }
        }
    }

    public async Task<MeshTransferSnapshot> GetTransferSnapshotAsync(CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        return new(_incomingClipboards.Reverse().Take(20).ToArray(), _receivedFiles.Reverse().Take(20).ToArray());
    }

    private bool TryHandleTransferMessage(MeshTransportMessage message)
    {
        if (message.Kind == "transfer.receipt")
        {
            var receipt = TryDeserialize<MeshTransferReceipt>(message.Payload);
            if (receipt is not null
                && _pendingTransferReceipts.TryGetValue(receipt.TransferId, out var pending)
                && pending.PeerId == message.SourceDeviceId)
                pending.Completion.TrySetResult(receipt);
            return true;
        }
        if (message.Kind == "transfer.file.ready")
        {
            var ready = TryDeserialize<MeshFileReady>(message.Payload);
            if (ready is not null
                && _pendingFileStarts.TryGetValue(ready.TransferId, out var pending)
                && pending.PeerId == message.SourceDeviceId)
                pending.Completion.TrySetResult(ready);
            return true;
        }
        if (message.Kind is not ("transfer.clipboard" or "transfer.file.start" or "transfer.file.chunk" or "transfer.file.complete" or "transfer.file.abort")) return false;
        lock (_transferQueueGate)
        {
            _transferQueue = _transferQueue.ContinueWith(_ => HandleInboundTransferMessageAsync(message), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
        }
        return true;
    }

    private async Task HandleInboundTransferMessageAsync(MeshTransportMessage message)
    {
        switch (message.Kind)
        {
            case "transfer.clipboard": await HandleInboundClipboardAsync(message).ConfigureAwait(false); break;
            case "transfer.file.start": await HandleInboundFileStartAsync(message).ConfigureAwait(false); break;
            case "transfer.file.chunk": await HandleInboundFileChunkAsync(message).ConfigureAwait(false); break;
            case "transfer.file.complete": await HandleInboundFileCompleteAsync(message).ConfigureAwait(false); break;
            case "transfer.file.abort": await HandleInboundFileAbortAsync(message).ConfigureAwait(false); break;
        }
    }

    private async Task HandleInboundClipboardAsync(MeshTransportMessage message)
    {
        var request = TryDeserialize<MeshClipboardTransfer>(message.Payload);
        if (request is null || request.TransferId == Guid.Empty) return;
        try
        {
            var peer = RequireTransferGrant(message.SourceDeviceId, RemoteClipboardCapability);
            if (Encoding.UTF8.GetByteCount(request.Text ?? string.Empty) > MaximumClipboardBytes) throw new InvalidDataException("Incoming Mesh clipboard text exceeds the size limit.");
            _incomingClipboards.Enqueue(new(request.TransferId, peer.DeviceId, peer.DisplayName, request.Text ?? string.Empty, DateTimeOffset.UtcNow));
            TrimQueue(_incomingClipboards, 20);
            StateChanged?.Invoke();
            await SendTransferReceiptAsync(message.SourceDeviceId, new(request.TransferId, MeshTransferKind.ClipboardText, MeshTransferStatus.Succeeded, DateTimeOffset.UtcNow, "Clipboard text arrived and is waiting for the user to copy it on the target device.")).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await SendTransferReceiptAsync(message.SourceDeviceId, Failed(request.TransferId, MeshTransferKind.ClipboardText, ex.Message)).ConfigureAwait(false);
        }
    }

    private async Task HandleInboundFileStartAsync(MeshTransportMessage message)
    {
        var request = TryDeserialize<MeshFileStart>(message.Payload);
        if (request is null || request.TransferId == Guid.Empty) return;
        var begun = false;
        try
        {
            _ = RequireTransferGrant(message.SourceDeviceId, RemoteFileCapability);
            if (_fileTransfers is null) throw new InvalidOperationException("This Haven build has no Mesh file inbox.");
            if (request.Length < 0 || request.Length > MaximumFileBytes) throw new InvalidDataException($"Incoming Mesh files are limited to {MaximumFileBytes / 1024 / 1024} MiB.");
            await _fileTransfers.BeginAsync(message.SourceDeviceId, request.TransferId, request.FileName, request.Length, CancellationToken.None).ConfigureAwait(false);
            begun = true;
            await _transport.SendAsync(message.SourceDeviceId, "transfer.file.ready", JsonSerializer.Serialize(new MeshFileReady(request.TransferId, true, "Target accepted the bounded file transfer.")), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (begun && _fileTransfers is not null)
            {
                try { await _fileTransfers.AbortAsync(message.SourceDeviceId, request.TransferId, CancellationToken.None).ConfigureAwait(false); } catch { }
            }
            try
            {
                await _transport.SendAsync(message.SourceDeviceId, "transfer.file.ready", JsonSerializer.Serialize(new MeshFileReady(request.TransferId, false, ex.Message)), CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
        }
    }

    private async Task HandleInboundFileChunkAsync(MeshTransportMessage message)
    {
        var chunk = TryDeserialize<MeshFileChunk>(message.Payload);
        if (chunk is null || chunk.TransferId == Guid.Empty || _fileTransfers is null) return;
        try
        {
            _ = RequireTransferGrant(message.SourceDeviceId, RemoteFileCapability);
            if (chunk.Data.Length > ((FileChunkBytes + 2) / 3 * 4) + 16) throw new InvalidDataException("Mesh file chunk exceeds the encoded size limit.");
            var bytes = Convert.FromBase64String(chunk.Data);
            if (bytes.Length > FileChunkBytes) throw new InvalidDataException("Mesh file chunk exceeds the binary size limit.");
            await _fileTransfers.AppendAsync(message.SourceDeviceId, chunk.TransferId, chunk.Index, bytes, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try { await _fileTransfers.AbortAsync(message.SourceDeviceId, chunk.TransferId, CancellationToken.None).ConfigureAwait(false); } catch { }
            await SendTransferReceiptAsync(message.SourceDeviceId, Failed(chunk.TransferId, MeshTransferKind.File, ex.Message)).ConfigureAwait(false);
        }
    }

    private async Task HandleInboundFileCompleteAsync(MeshTransportMessage message)
    {
        var complete = TryDeserialize<MeshFileComplete>(message.Payload);
        if (complete is null || complete.TransferId == Guid.Empty || _fileTransfers is null) return;
        try
        {
            var peer = RequireTransferGrant(message.SourceDeviceId, RemoteFileCapability);
            var path = await _fileTransfers.CompleteAsync(message.SourceDeviceId, complete.TransferId, complete.ChunkCount, complete.Sha256, CancellationToken.None).ConfigureAwait(false);
            var info = new FileInfo(path);
            _receivedFiles.Enqueue(new(complete.TransferId, peer.DeviceId, peer.DisplayName, info.Name, info.Length, path, DateTimeOffset.UtcNow));
            TrimQueue(_receivedFiles, 20);
            StateChanged?.Invoke();
            await SendTransferReceiptAsync(message.SourceDeviceId, new(complete.TransferId, MeshTransferKind.File, MeshTransferStatus.Succeeded, DateTimeOffset.UtcNow, "File received, SHA-256 verified, and saved in the target device's Mesh inbox.")).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try { await _fileTransfers.AbortAsync(message.SourceDeviceId, complete.TransferId, CancellationToken.None).ConfigureAwait(false); } catch { }
            await SendTransferReceiptAsync(message.SourceDeviceId, Failed(complete.TransferId, MeshTransferKind.File, ex.Message)).ConfigureAwait(false);
        }
    }

    private async Task HandleInboundFileAbortAsync(MeshTransportMessage message)
    {
        var abort = TryDeserialize<MeshFileAbort>(message.Payload);
        if (abort is null || _fileTransfers is null) return;
        await _fileTransfers.AbortAsync(message.SourceDeviceId, abort.TransferId, CancellationToken.None).ConfigureAwait(false);
    }

    private MeshPeerRecord RequireTransferGrant(Guid sourceDeviceId, string capability)
    {
        var peer = RequireTrustedPeer(sourceDeviceId);
        if (!(peer.AllowedRemoteCapabilities ?? []).Contains(capability, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"{peer.DisplayName} is trusted, but {capability} is not allowed on this device.");
        return peer;
    }

    private void EnsureTransferConnected(MeshPeerRecord peer)
    {
        var presence = GetPresence(peer);
        if (presence.Connection != MeshConnectionState.Connected || presence.Presence is MeshPresenceState.Offline or MeshPresenceState.Stale)
            throw new IOException($"{peer.DisplayName} is not currently connected.");
    }

    private TaskCompletionSource<MeshTransferReceipt> NewTransferCompletion(Guid transferId, Guid peerId)
    {
        var completion = new TaskCompletionSource<MeshTransferReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingTransferReceipts.TryAdd(transferId, new PendingTransferReceipt(peerId, completion))) throw new InvalidOperationException("Duplicate Mesh transfer identifier.");
        return completion;
    }

    private static async Task<MeshFileReady> WaitForFileReadyAsync(TaskCompletionSource<MeshFileReady> completion, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TransferRpcTimeout);
        try { return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(Guid.Empty, false, "The target device did not accept the file transfer before the timeout."); }
    }

    private static async Task<MeshTransferReceipt> WaitForTransferReceiptAsync(Guid transferId, TaskCompletionSource<MeshTransferReceipt> completion, MeshTransferKind kind, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TransferRpcTimeout);
        try { return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Failed(transferId, kind, "The target device did not confirm the transfer before the timeout."); }
    }

    private Task SendTransferReceiptAsync(Guid peerId, MeshTransferReceipt receipt) =>
        _transport.SendAsync(peerId, "transfer.receipt", JsonSerializer.Serialize(receipt), CancellationToken.None);

    private static MeshTransferReceipt Failed(Guid transferId, MeshTransferKind kind, string message) =>
        new(transferId, kind, MeshTransferStatus.Failed, DateTimeOffset.UtcNow, message);

    private static void TrimQueue<T>(ConcurrentQueue<T> queue, int limit)
    {
        while (queue.Count > limit) queue.TryDequeue(out _);
    }

    private sealed record PendingTransferReceipt(Guid PeerId, TaskCompletionSource<MeshTransferReceipt> Completion);
    private sealed record PendingFileStart(Guid PeerId, TaskCompletionSource<MeshFileReady> Completion);
    private sealed record MeshClipboardTransfer(Guid TransferId, string Text);
    private sealed record MeshFileStart(Guid TransferId, string FileName, long Length);
    private sealed record MeshFileReady(Guid TransferId, bool Accepted, string Message);
    private sealed record MeshFileChunk(Guid TransferId, int Index, string Data);
    private sealed record MeshFileComplete(Guid TransferId, int ChunkCount, string Sha256);
    private sealed record MeshFileAbort(Guid TransferId);
}
