using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class VerifiedNotesRepository(
    NotesRepository inner,
    IAppPaths paths,
    IProductionDiagnostics diagnostics) : INotesRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _root = Path.Combine(paths.DataDirectory, "Notes", "Documents");

    public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken) =>
        inner.ListAsync(cancellationToken);

    public async Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await inner.LoadAsync(documentId, cancellationToken).ConfigureAwait(false);
        if (document is null) return null;
        var current = Path.Combine(_root, documentId.ToString("D"), "current.haven-notes.json");
        var manifest = ManifestPath(documentId);
        if (!File.Exists(current) || !File.Exists(manifest)) return document;

        try
        {
            var expected = await ReadManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
            var actual = await ComputeSha256Async(current, cancellationToken).ConfigureAwait(false);
            if (expected is not null
                && expected.DocumentId == documentId
                && expected.Version == document.Version
                && expected.Sha256.Equals(actual, StringComparison.OrdinalIgnoreCase))
            {
                document.Recovery.LastValidSha256 = actual;
                return document;
            }

            await diagnostics.WriteAsync(
                ReliabilitySeverity.Critical,
                "notes",
                "integrity-mismatch",
                "A Haven Notes document did not match its durable integrity manifest and was not returned as current data.",
                new Dictionary<string, string>
                {
                    ["documentId"] = documentId.ToString("D"),
                    ["expected"] = expected?.Sha256 ?? "missing",
                    ["actual"] = actual
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var recovered = await inner.RecoverLatestAsync(documentId, cancellationToken).ConfigureAwait(false);
            if (recovered is not null)
            {
                recovered.Recovery.HasUnsavedRecovery = true;
                recovered.Recovery.RecoveryReason = "Recovered because the current document failed its integrity manifest check.";
            }
            return recovered;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Warning,
                "notes",
                "integrity-check-failed",
                "Haven Notes could not verify the document integrity sidecar and entered recovery.",
                new Dictionary<string, string>
                {
                    ["documentId"] = documentId.ToString("D"),
                    ["exceptionType"] = ex.GetType().Name
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return await inner.RecoverLatestAsync(documentId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<NotesSaveResult> SaveAsync(
        NotesDocument document,
        string reason,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await inner.SaveAsync(document, reason, cancellationToken).ConfigureAwait(false);
            var actual = await ComputeSha256Async(result.CurrentPath, cancellationToken).ConfigureAwait(false);
            var manifest = new NotesIntegrityManifest(
                1,
                document.Id,
                result.Version,
                actual,
                new FileInfo(result.CurrentPath).Length,
                result.SavedAt);
            await WriteManifestAtomicAsync(ManifestPath(document.Id), manifest, cancellationToken).ConfigureAwait(false);
            document.Recovery.LastValidSha256 = actual;
            return result with { Sha256 = actual };
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken) =>
        inner.DeleteAsync(documentId, cancellationToken);

    public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(
        Guid documentId,
        CancellationToken cancellationToken) =>
        inner.GetVersionsAsync(documentId, cancellationToken);

    public Task<NotesDocument?> LoadVersionAsync(
        Guid documentId,
        string versionId,
        CancellationToken cancellationToken) =>
        inner.LoadVersionAsync(documentId, versionId, cancellationToken);

    public Task<NotesDocument?> RecoverLatestAsync(Guid documentId, CancellationToken cancellationToken) =>
        inner.RecoverLatestAsync(documentId, cancellationToken);

    public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(
        string query,
        CancellationToken cancellationToken) =>
        inner.SearchAsync(query, cancellationToken);

    private string ManifestPath(Guid documentId) =>
        Path.Combine(_root, documentId.ToString("D"), "current.integrity.json");

    private static async Task<NotesIntegrityManifest?> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<NotesIntegrityManifest>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteManifestAtomicAsync(
        string path,
        NotesIntegrityManifest manifest,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    manifest,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
            else File.Move(temporary, path);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
            .ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record NotesIntegrityManifest(
        int Version,
        Guid DocumentId,
        long DocumentVersion,
        string Sha256,
        long SizeBytes,
        DateTimeOffset CreatedAt)
    {
        public long VersionNumber => DocumentVersion;
    }
}

public sealed class SecureNotesAttachmentStore(
    NotesAttachmentStore inner,
    IAppPaths paths) : INotesAttachmentStore
{
    private readonly string _root = Path.GetFullPath(
        Path.Combine(paths.DataDirectory, "Notes", "Attachments"));

    public Task<NotesMediaData> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken) =>
        inner.ImportAsync(sourcePath, cancellationToken);

    public Task<string> ResolvePathAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (attachmentId == Guid.Empty)
            throw new ArgumentException("Attachment ID cannot be empty.", nameof(attachmentId));
        if (!Directory.Exists(_root))
            throw new FileNotFoundException("The Notes attachment store does not exist.");
        var prefix = attachmentId.ToString("N");
        var rootWithSeparator = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var match = Directory.EnumerateFiles(_root, prefix + "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFullPath)
            .FirstOrDefault(path =>
                path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0);
        return match is null
            ? throw new FileNotFoundException("The Notes attachment was not found in the managed store.")
            : Task.FromResult(match);
    }

    public Task DeleteUnreferencedAsync(
        IReadOnlyCollection<Guid> referencedAttachmentIds,
        CancellationToken cancellationToken) =>
        inner.DeleteUnreferencedAsync(referencedAttachmentIds, cancellationToken);
}
