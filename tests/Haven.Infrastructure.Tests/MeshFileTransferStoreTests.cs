using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class MeshFileTransferStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-mesh-transfer-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CompleteAsync_ConfinesSenderFileNameToMeshInboxAndVerifiesHash()
    {
        Directory.CreateDirectory(_root);
        await using var store = new MeshFileTransferStore(new TempAppPaths(_root));
        var source = Guid.NewGuid();
        var transfer = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes("mesh transfer payload");

        await store.BeginAsync(source, transfer, @"..\..\outside.txt", payload.Length, CancellationToken.None);
        await store.AppendAsync(source, transfer, 0, payload, CancellationToken.None);
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        var saved = await store.CompleteAsync(source, transfer, 1, hash, CancellationToken.None);

        var inbox = Path.GetFullPath(Path.Combine(_root, "mesh-inbox")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(inbox, Path.GetFullPath(saved), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(payload, await File.ReadAllBytesAsync(saved));
        Assert.False(Path.GetFileName(saved).Contains("..", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_root, "outside.txt")));
    }

    [Fact]
    public async Task CompleteAsync_HashMismatchDeletesPartialFile()
    {
        Directory.CreateDirectory(_root);
        await using var store = new MeshFileTransferStore(new TempAppPaths(_root));
        var source = Guid.NewGuid();
        var transfer = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes("tamper me");

        await store.BeginAsync(source, transfer, "safe.txt", payload.Length, CancellationToken.None);
        await store.AppendAsync(source, transfer, 0, payload, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.CompleteAsync(source, transfer, 1, new string('0', 64), CancellationToken.None));

        var inbox = Path.Combine(_root, "mesh-inbox");
        Assert.Empty(Directory.Exists(inbox) ? Directory.GetFiles(inbox, "*.part") : []);
    }

    [Fact]
    public async Task AppendAsync_RejectsOutOfOrderChunkAndAbortRemovesPartialFile()
    {
        Directory.CreateDirectory(_root);
        await using var store = new MeshFileTransferStore(new TempAppPaths(_root));
        var source = Guid.NewGuid();
        var transfer = Guid.NewGuid();

        await store.BeginAsync(source, transfer, "ordered.bin", 3, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AppendAsync(source, transfer, 1, new byte[] { 1 }, CancellationToken.None));
        await store.AbortSourceAsync(source, CancellationToken.None);

        var inbox = Path.Combine(_root, "mesh-inbox");
        Assert.Empty(Directory.Exists(inbox) ? Directory.GetFiles(inbox, "*.part") : []);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class TempAppPaths : IAppPaths
    {
        private readonly string _root;

        public TempAppPaths(string root)
        {
            _root = root;
            DataDirectory = root;
        }

        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(_root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(_root, "browser");
        public string AttachmentsDirectory => Path.Combine(_root, "attachments");
        public string LogsDirectory => Path.Combine(_root, "logs");
        public string LegacyStatePath => Path.Combine(_root, "legacy.json");
    }
}
