namespace Haven.Application;

public enum MeshTransferKind { ClipboardText, File }
public enum MeshTransferStatus { Ready, Succeeded, Failed }

public sealed record MeshTransferReceipt(
    Guid TransferId,
    MeshTransferKind Kind,
    MeshTransferStatus Status,
    DateTimeOffset UpdatedAt,
    string Message)
{
    public bool Succeeded => Status == MeshTransferStatus.Succeeded;
}

public sealed record MeshIncomingClipboard(
    Guid TransferId,
    Guid SourceDeviceId,
    string SourceDeviceName,
    string Text,
    DateTimeOffset ReceivedAt);

public sealed record MeshReceivedFile(
    Guid TransferId,
    Guid SourceDeviceId,
    string SourceDeviceName,
    string FileName,
    long Length,
    string SavedPath,
    DateTimeOffset ReceivedAt);

public sealed record MeshTransferSnapshot(
    IReadOnlyList<MeshIncomingClipboard> RecentClipboards,
    IReadOnlyList<MeshReceivedFile> RecentFiles);

/// <summary>Owns bounded, controlled local storage for files arriving through an authenticated Mesh session.</summary>
public interface IMeshFileTransferStore
{
    Task BeginAsync(Guid sourceDeviceId, Guid transferId, string fileName, long length, CancellationToken cancellationToken);
    Task AppendAsync(Guid sourceDeviceId, Guid transferId, int chunkIndex, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
    Task<string> CompleteAsync(Guid sourceDeviceId, Guid transferId, int chunkCount, string sha256, CancellationToken cancellationToken);
    Task AbortAsync(Guid sourceDeviceId, Guid transferId, CancellationToken cancellationToken);
    Task AbortSourceAsync(Guid sourceDeviceId, CancellationToken cancellationToken);
}
