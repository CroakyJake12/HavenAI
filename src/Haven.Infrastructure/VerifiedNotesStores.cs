/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/VerifiedNotesStores.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns VerifiedNotesRepository, NotesIntegrityManifest, SecureNotesAttachmentStore. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents verified notes repository and keeps its related state and behavior together.
/// </summary>
public sealed class VerifiedNotesRepository(
    NotesRepository inner,
    IAppPaths paths,
    IProductionDiagnostics diagnostics) : INotesRepository
{
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root = Path.Combine(paths.DataDirectory, "Notes", "Documents");

    /// <summary>
    /// Performs list asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken) =>
        inner.ListAsync(cancellationToken);

    /// <summary>
    /// Performs load asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs save asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs delete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken) =>
        inner.DeleteAsync(documentId, cancellationToken);

    /// <summary>
    /// Retrieves versions async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(
        Guid documentId,
        CancellationToken cancellationToken) =>
        inner.GetVersionsAsync(documentId, cancellationToken);

    /// <summary>
    /// Performs load version asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<NotesDocument?> LoadVersionAsync(
        Guid documentId,
        string versionId,
        CancellationToken cancellationToken) =>
        inner.LoadVersionAsync(documentId, versionId, cancellationToken);

    /// <summary>
    /// Performs recover latest asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<NotesDocument?> RecoverLatestAsync(Guid documentId, CancellationToken cancellationToken) =>
        inner.RecoverLatestAsync(documentId, cancellationToken);

    /// <summary>
    /// Performs search asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(
        string query,
        CancellationToken cancellationToken) =>
        inner.SearchAsync(query, cancellationToken);

    /// <summary>
    /// Performs the manifest path step owned by this component.
    /// </summary>
    private string ManifestPath(Guid documentId) =>
        Path.Combine(_root, documentId.ToString("D"), "current.integrity.json");

    /// <summary>
    /// Performs read manifest asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs write manifest atomic asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs compute sha256 asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Attempts to delete and reports the result without using failure for normal control flow.
    /// </summary>
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

    /// <summary>
    /// Represents notes integrity manifest and keeps its related state and behavior together.
    /// </summary>
    private sealed record NotesIntegrityManifest(
        int Version,
        Guid DocumentId,
        long DocumentVersion,
        string Sha256,
        long SizeBytes,
        DateTimeOffset CreatedAt)
    {
        /// <summary>
        /// Gets or updates version number, the bindable or domain state represented by this property.
        /// </summary>
        public long VersionNumber => DocumentVersion;
    }
}

/// <summary>
/// Represents secure notes attachment store and keeps its related state and behavior together.
/// </summary>
public sealed class SecureNotesAttachmentStore(
    NotesAttachmentStore inner,
    IAppPaths paths) : INotesAttachmentStore
{
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root = Path.GetFullPath(
        Path.Combine(paths.DataDirectory, "Notes", "Attachments"));

    /// <summary>
    /// Performs import asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<NotesMediaData> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken) =>
        inner.ImportAsync(sourcePath, cancellationToken);

    /// <summary>
    /// Performs resolve path asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs delete unreferenced asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteUnreferencedAsync(
        IReadOnlyCollection<Guid> referencedAttachmentIds,
        CancellationToken cancellationToken) =>
        inner.DeleteUnreferencedAsync(referencedAttachmentIds, cancellationToken);
}
