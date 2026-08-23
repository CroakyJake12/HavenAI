using System.Collections.Concurrent;
using System.Security.Cryptography;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Streams authenticated Mesh files into a bounded app-owned inbox and never writes to sender-supplied directories.</summary>
public sealed class MeshFileTransferStore(IAppPaths paths) : IMeshFileTransferStore, IAsyncDisposable
{
    private const int MaximumConcurrentTransfers = 4;
    private const int MaximumConcurrentTransfersPerPeer = 2;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<(Guid Source, Guid Transfer), Session> _sessions = new();

    public async Task BeginAsync(Guid sourceDeviceId, Guid transferId, string fileName, long length, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceDeviceId == Guid.Empty || transferId == Guid.Empty) throw new InvalidDataException("Mesh file transfer identity is invalid.");
        if (length < 0 || length > MeshCoordinator.MaximumFileBytes) throw new InvalidDataException("Mesh file length is outside the allowed range.");
        await PruneExpiredAsync(cancellationToken).ConfigureAwait(false);
        if (_sessions.Count >= MaximumConcurrentTransfers || _sessions.Keys.Count(key => key.Source == sourceDeviceId) >= MaximumConcurrentTransfersPerPeer)
            throw new IOException("Too many Mesh file transfers are already active.");

        var inbox = Path.GetFullPath(Path.Combine(paths.DataDirectory, "mesh-inbox"));
        Directory.CreateDirectory(inbox);
        var safeName = SafeFileName(fileName);
        var tempPath = Path.Combine(inbox, $".{transferId:N}.part");
        var finalPath = Path.Combine(inbox, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{transferId:N}-{safeName}");
        var stream = new FileStream(tempPath, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            BufferSize = 64 * 1024
        });
        var session = new Session(tempPath, finalPath, length, stream, IncrementalHash.CreateHash(HashAlgorithmName.SHA256), DateTimeOffset.UtcNow);
        if (!_sessions.TryAdd((sourceDeviceId, transferId), session))
        {
            await session.DisposeAsync().ConfigureAwait(false);
            TryDelete(tempPath);
            throw new IOException("This Mesh file transfer already exists.");
        }
    }

    public async Task AppendAsync(Guid sourceDeviceId, Guid transferId, int chunkIndex, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue((sourceDeviceId, transferId), out var session)) throw new IOException("The Mesh file transfer session does not exist.");
        if (data.Length > MeshCoordinator.FileChunkBytes) throw new InvalidDataException("Mesh file chunk exceeds the allowed size.");
        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (chunkIndex != session.NextChunk) throw new InvalidDataException($"Expected Mesh file chunk {session.NextChunk}, but received {chunkIndex}.");
            if (session.Received + data.Length > session.ExpectedLength) throw new InvalidDataException("Mesh file data exceeds the declared file length.");
            await session.Stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            session.Hash.AppendData(data.Span);
            session.Received += data.Length;
            session.NextChunk++;
        }
        finally { session.Gate.Release(); }
    }

    public async Task<string> CompleteAsync(Guid sourceDeviceId, Guid transferId, int chunkCount, string sha256, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue((sourceDeviceId, transferId), out var session)) throw new IOException("The Mesh file transfer session does not exist.");
        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (chunkCount != session.NextChunk || session.Received != session.ExpectedLength) throw new InvalidDataException("Mesh file transfer completed with missing or extra chunks.");
            byte[] expected;
            try { expected = Convert.FromHexString(sha256); } catch (FormatException ex) { throw new InvalidDataException("Mesh file SHA-256 is malformed.", ex); }
            if (expected.Length != 32) throw new InvalidDataException("Mesh file SHA-256 must contain 32 bytes.");
            var actual = session.Hash.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(actual, expected)) throw new InvalidDataException("Mesh file SHA-256 verification failed.");
            await session.Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            await session.Stream.DisposeAsync().ConfigureAwait(false);
            session.Hash.Dispose();
            _sessions.TryRemove((sourceDeviceId, transferId), out _);
            File.Move(session.TempPath, session.FinalPath, overwrite: false);
            return session.FinalPath;
        }
        catch
        {
            _sessions.TryRemove((sourceDeviceId, transferId), out _);
            await session.DisposeAsync().ConfigureAwait(false);
            TryDelete(session.TempPath);
            throw;
        }
        finally
        {
            try { session.Gate.Release(); } catch (ObjectDisposedException) { }
        }
    }

    public async Task AbortAsync(Guid sourceDeviceId, Guid transferId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryRemove((sourceDeviceId, transferId), out var session)) return;
        await session.DisposeAsync().ConfigureAwait(false);
        TryDelete(session.TempPath);
    }

    public async Task AbortSourceAsync(Guid sourceDeviceId, CancellationToken cancellationToken)
    {
        foreach (var key in _sessions.Keys.Where(key => key.Source == sourceDeviceId).ToArray())
            await AbortAsync(key.Source, key.Transfer, cancellationToken).ConfigureAwait(false);
    }

    private async Task PruneExpiredAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow - SessionLifetime;
        foreach (var pair in _sessions.ToArray())
            if (pair.Value.StartedAt < cutoff) await AbortAsync(pair.Key.Source, pair.Key.Transfer, cancellationToken).ConfigureAwait(false);
    }

    private static string SafeFileName(string fileName)
    {
        var leaf = Path.GetFileName(fileName?.Trim());
        if (string.IsNullOrWhiteSpace(leaf)) leaf = "received-file";
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(leaf.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safe)) safe = "received-file";
        return safe.Length <= 120 ? safe : safe[..120];
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var pair in _sessions.ToArray())
            await AbortAsync(pair.Key.Source, pair.Key.Transfer, CancellationToken.None).ConfigureAwait(false);
    }

    private sealed class Session(string tempPath, string finalPath, long expectedLength, FileStream stream, IncrementalHash hash, DateTimeOffset startedAt) : IAsyncDisposable
    {
        public string TempPath { get; } = tempPath;
        public string FinalPath { get; } = finalPath;
        public long ExpectedLength { get; } = expectedLength;
        public FileStream Stream { get; } = stream;
        public IncrementalHash Hash { get; } = hash;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public long Received { get; set; }
        public int NextChunk { get; set; }

        public async ValueTask DisposeAsync()
        {
            try { await Stream.DisposeAsync().ConfigureAwait(false); } catch { }
            try { Hash.Dispose(); } catch { }
            try { Gate.Dispose(); } catch { }
        }
    }
}
