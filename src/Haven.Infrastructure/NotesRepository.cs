/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/NotesRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns NotesRepository, NotesVersionManifest, NotesAttachmentStore. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents notes repository and keeps its related state and behavior together.
/// </summary>
public sealed class NotesRepository(
    IAppPaths paths,
    INotesDocumentValidator validator,
    IProductionDiagnostics diagnostics) : INotesRepository
{
    /// <summary>
    /// Stores maximum versions per document locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaximumVersionsPerDocument = 100;
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root = Path.Combine(paths.DataDirectory, "Notes", "Documents");
    /// <summary>
    /// Stores trash locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _trash = Path.Combine(paths.DataDirectory, "Notes", "Trash");

    /// <summary>
    /// Performs list asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_root)) return [];
            var result = new List<NotesDocumentSummary>();
            foreach (var directory in Directory.EnumerateDirectories(_root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Guid.TryParse(Path.GetFileName(directory), out var id)) continue;
                try
                {
                    var document = await LoadCoreAsync(id, allowRecovery: true, cancellationToken).ConfigureAwait(false);
                    if (document is not null) result.Add(Summarize(document));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                {
                    await diagnostics.WriteAsync(
                        ReliabilitySeverity.Warning,
                        "notes",
                        "document-list-skip",
                        "A Notes document could not be included in the library.",
                        new Dictionary<string, string>
                        {
                            ["documentId"] = id.ToString("D"),
                            ["exceptionType"] = ex.GetType().Name
                        },
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
            return result.OrderByDescending(item => item.UpdatedAt).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs load asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Document ID cannot be empty.", nameof(documentId));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await LoadCoreAsync(documentId, allowRecovery: true, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Performs save asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var validation = validator.Validate(document);
        if (!validation.IsValid)
            throw new InvalidDataException("Notes document validation failed: " + string.Join(" | ", validation.Issues.Where(issue => issue.IsError).Take(12).Select(issue => issue.Path + ": " + issue.Message)));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = DocumentDirectory(document.Id);
            var versions = VersionsDirectory(document.Id);
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(versions);
            var currentPath = CurrentPath(document.Id);
            var backupPath = BackupPath(document.Id);
            var previousVersion = document.Version;
            var now = DateTimeOffset.UtcNow;
            document.Version = checked(document.Version + 1);
            document.UpdatedAt = now;
            document.Recovery.LastAutosaveAt = now;
            document.Recovery.HasUnsavedRecovery = false;
            document.Recovery.RecoveryReason = string.Empty;

            var temporary = currentPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await WriteJsonDurablyAsync(temporary, document, cancellationToken).ConfigureAwait(false);
                var hash = await ComputeSha256Async(temporary, cancellationToken).ConfigureAwait(false);
                document.Recovery.LastValidSha256 = hash;
                await WriteJsonDurablyAsync(temporary, document, cancellationToken, overwrite: true).ConfigureAwait(false);
                hash = await ComputeSha256Async(temporary, cancellationToken).ConfigureAwait(false);

                if (File.Exists(currentPath))
                {
                    await PreservePreviousVersionAsync(document.Id, currentPath, previousVersion, reason, cancellationToken).ConfigureAwait(false);
                    File.Replace(temporary, currentPath, backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporary, currentPath);
                }

                var versionId = VersionFileName(document.Version, now);
                var versionPath = Path.Combine(versions, versionId + ".haven-notes.json");
                await CopyDurablyAsync(currentPath, versionPath, cancellationToken).ConfigureAwait(false);
                await WriteJsonDurablyAsync(
                    Path.Combine(versions, versionId + ".meta.json"),
                    new NotesVersionManifest(document.Version, now, NormalizeReason(reason), new FileInfo(versionPath).Length, hash),
                    cancellationToken).ConfigureAwait(false);
                ApplyRetention(versions);

                await diagnostics.WriteAsync(
                    ReliabilitySeverity.Information,
                    "notes",
                    "document-saved",
                    "A Haven Notes document was written atomically and versioned.",
                    new Dictionary<string, string>
                    {
                        ["documentId"] = document.Id.ToString("D"),
                        ["version"] = document.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["sha256"] = hash,
                        ["reason"] = NormalizeReason(reason)
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return new NotesSaveResult(document.Id, document.Version, now, hash, currentPath, versionPath);
            }
            catch
            {
                document.Version = previousVersion;
                document.UpdatedAt = now;
                document.Recovery.HasUnsavedRecovery = true;
                document.Recovery.RecoveryReason = "The most recent save did not complete.";
                throw;
            }
            finally
            {
                TryDelete(temporary);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs delete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Document ID cannot be empty.", nameof(documentId));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = DocumentDirectory(documentId);
            if (!Directory.Exists(directory)) return;
            Directory.CreateDirectory(_trash);
            var destination = Path.Combine(_trash, documentId + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
            Directory.Move(directory, destination);
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "notes",
                "document-trashed",
                "A Haven Notes document was moved to recoverable trash.",
                new Dictionary<string, string> { ["documentId"] = documentId.ToString("D") },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Retrieves versions async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await GetVersionsCoreAsync(documentId, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Performs load version asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<NotesDocument?> LoadVersionAsync(Guid documentId, string versionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(versionId) || versionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || versionId.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("A managed Notes version ID is required.", nameof(versionId));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = Path.GetFullPath(VersionsDirectory(documentId)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(root, versionId + ".haven-notes.json"));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
            return await ReadAndValidateAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs recover latest asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<NotesDocument?> RecoverLatestAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await RecoverCoreAsync(documentId, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Performs search asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var normalized = string.IsNullOrWhiteSpace(query) ? string.Empty : query.Trim();
        if (normalized.Length < 2) return [];
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_root)) return [];
            var result = new List<NotesSearchHit>();
            foreach (var directory in Directory.EnumerateDirectories(_root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Guid.TryParse(Path.GetFileName(directory), out var id)) continue;
                NotesDocument? document;
                try { document = await LoadCoreAsync(id, allowRecovery: false, cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { continue; }
                if (document is null) continue;
                foreach (var section in document.Sections)
                foreach (var page in section.Pages)
                foreach (var block in page.Blocks)
                {
                    var searchable = BlockSearchText(block);
                    var offset = searchable.IndexOf(normalized, StringComparison.CurrentCultureIgnoreCase);
                    if (offset < 0) continue;
                    result.Add(new NotesSearchHit(
                        document.Id,
                        document.Title,
                        section.Id,
                        page.Id,
                        block.Id,
                        block.Kind.ToString(),
                        Snippet(searchable, offset, normalized.Length),
                        offset));
                    if (result.Count >= 500) return result;
                }
            }
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs load core asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<NotesDocument?> LoadCoreAsync(Guid documentId, bool allowRecovery, CancellationToken cancellationToken)
    {
        var path = CurrentPath(documentId);
        if (!File.Exists(path)) return allowRecovery ? await RecoverCoreAsync(documentId, cancellationToken).ConfigureAwait(false) : null;
        try { return await ReadAndValidateAsync(path, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (allowRecovery && ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Quarantine(path, "corrupt-current");
            var recovered = await RecoverCoreAsync(documentId, cancellationToken).ConfigureAwait(false);
            await diagnostics.WriteAsync(
                recovered is null ? ReliabilitySeverity.Critical : ReliabilitySeverity.Warning,
                "notes",
                recovered is null ? "recovery-failed" : "document-recovered",
                recovered is null ? "A corrupt Notes document had no valid recovery copy." : "A corrupt Notes document was recovered from its last valid copy.",
                new Dictionary<string, string>
                {
                    ["documentId"] = documentId.ToString("D"),
                    ["exceptionType"] = ex.GetType().Name
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return recovered;
        }
    }

    /// <summary>
    /// Performs recover core asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<NotesDocument?> RecoverCoreAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var candidates = new List<string>();
        var backup = BackupPath(documentId);
        if (File.Exists(backup)) candidates.Add(backup);
        var versions = VersionsDirectory(documentId);
        if (Directory.Exists(versions))
            candidates.AddRange(Directory.EnumerateFiles(versions, "*.haven-notes.json", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc));

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var document = await ReadAndValidateAsync(candidate, cancellationToken).ConfigureAwait(false);
                document.Recovery.HasUnsavedRecovery = true;
                document.Recovery.LastRecoveredAt = DateTimeOffset.UtcNow;
                document.Recovery.RecoveryReason = "Recovered after the current file failed validation.";
                document.Revisions.Add(new NotesRevision
                {
                    Kind = NotesRevisionKind.Restored,
                    Summary = "Recovered the last valid Notes version",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Author = "Haven recovery"
                });
                return document;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { }
        }
        return null;
    }

    /// <summary>
    /// Performs read and validate asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<NotesDocument> ReadAndValidateAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<NotesDocument>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidDataException("The Notes document was empty.");
        var validation = validator.Validate(document);
        if (!validation.IsValid)
            throw new InvalidDataException("The Notes document failed validation: " + string.Join(" | ", validation.Issues.Where(issue => issue.IsError).Take(8).Select(issue => issue.Path + ": " + issue.Message)));
        return document;
    }

    /// <summary>
    /// Performs preserve previous version asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task PreservePreviousVersionAsync(Guid documentId, string currentPath, long version, string reason, CancellationToken cancellationToken)
    {
        if (version <= 0 || !File.Exists(currentPath)) return;
        var createdAt = File.GetLastWriteTimeUtc(currentPath);
        var versionId = VersionFileName(version, createdAt);
        var path = Path.Combine(VersionsDirectory(documentId), versionId + ".haven-notes.json");
        if (!File.Exists(path)) await CopyDurablyAsync(currentPath, path, cancellationToken).ConfigureAwait(false);
        var hash = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        var meta = Path.Combine(VersionsDirectory(documentId), versionId + ".meta.json");
        if (!File.Exists(meta))
            await WriteJsonDurablyAsync(meta, new NotesVersionManifest(version, createdAt, NormalizeReason(reason), new FileInfo(path).Length, hash), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves versions core async for the current operation.
    /// </summary>
    private async Task<IReadOnlyList<NotesVersionInfo>> GetVersionsCoreAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var directory = VersionsDirectory(documentId);
        if (!Directory.Exists(directory)) return [];
        var result = new List<NotesVersionInfo>();
        foreach (var meta in Directory.EnumerateFiles(directory, "*.meta.json", SearchOption.TopDirectoryOnly).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(meta);
                var manifest = await JsonSerializer.DeserializeAsync<NotesVersionManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (manifest is null) continue;
                result.Add(new NotesVersionInfo(Path.GetFileName(meta)[..^".meta.json".Length], manifest.Version, manifest.CreatedAt, manifest.Reason, manifest.SizeBytes, manifest.Sha256));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return result;
    }

    /// <summary>
    /// Performs the apply retention step owned by this component.
    /// </summary>
    private void ApplyRetention(string versionsDirectory)
    {
        var files = Directory.EnumerateFiles(versionsDirectory, "*.haven-notes.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        foreach (var file in files.Skip(MaximumVersionsPerDocument))
        {
            TryDelete(file);
            TryDelete(Path.Combine(versionsDirectory, Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file)) + ".meta.json"));
        }
        foreach (var temporary in Directory.EnumerateFiles(versionsDirectory, "*.tmp-*", SearchOption.TopDirectoryOnly)) TryDelete(temporary);
    }

    /// <summary>
    /// Performs the summarize step owned by this component.
    /// </summary>
    private static NotesDocumentSummary Summarize(NotesDocument document)
    {
        var blocks = document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).ToArray();
        return new NotesDocumentSummary(
            document.Id,
            document.Title,
            document.UpdatedAt,
            document.Version,
            document.Sections.Count,
            blocks.Length,
            NotesTextStatistics.Calculate(document).Words,
            document.Recovery.HasUnsavedRecovery);
    }

    /// <summary>
    /// Performs the block search text step owned by this component.
    /// </summary>
    private static string BlockSearchText(NotesBlock block)
    {
        var builder = new StringBuilder(block.PlainText);
        if (block.List is not null) foreach (var item in block.List.Items) builder.AppendLine(item.Text);
        if (block.Table is not null) foreach (var cell in block.Table.Rows.SelectMany(row => row.Cells)) builder.AppendLine(cell.Text);
        if (block.Media is not null) builder.AppendLine(block.Media.Caption).AppendLine(block.Media.AltText);
        if (block.Equation is not null) builder.AppendLine(block.Equation.Source).AppendLine(block.Equation.AccessibleAlternative);
        if (block.Html is not null) builder.AppendLine(block.Html.FallbackText).AppendLine(block.Html.HtmlSource);
        if (block.Flashcard is not null) builder.AppendLine(block.Flashcard.Front).AppendLine(block.Flashcard.Back).AppendLine(block.Flashcard.Hint);
        return builder.ToString();
    }

    /// <summary>
    /// Performs the snippet step owned by this component.
    /// </summary>
    private static string Snippet(string text, int offset, int length)
    {
        var start = Math.Max(0, offset - 70);
        var end = Math.Min(text.Length, offset + length + 110);
        return (start > 0 ? "…" : string.Empty) + text[start..end].ReplaceLineEndings(" ") + (end < text.Length ? "…" : string.Empty);
    }

    /// <summary>
    /// Performs the document directory step owned by this component.
    /// </summary>
    private string DocumentDirectory(Guid id) => Path.Combine(_root, id.ToString("D"));
    /// <summary>
    /// Performs the current path step owned by this component.
    /// </summary>
    private string CurrentPath(Guid id) => Path.Combine(DocumentDirectory(id), "current.haven-notes.json");
    /// <summary>
    /// Performs the backup path step owned by this component.
    /// </summary>
    private string BackupPath(Guid id) => Path.Combine(DocumentDirectory(id), "backup.haven-notes.json");
    /// <summary>
    /// Performs the versions directory step owned by this component.
    /// </summary>
    private string VersionsDirectory(Guid id) => Path.Combine(DocumentDirectory(id), "Versions");
    /// <summary>
    /// Performs the version file name step owned by this component.
    /// </summary>
    private static string VersionFileName(long version, DateTimeOffset createdAt) => $"v{version:D10}-{createdAt.UtcDateTime:yyyyMMdd-HHmmssfff}";
    /// <summary>
    /// Performs the normalize reason step owned by this component.
    /// </summary>
    private static string NormalizeReason(string reason) => string.IsNullOrWhiteSpace(reason) ? "Save" : reason.Trim()[..Math.Min(reason.Trim().Length, 160)];

    private static async Task WriteJsonDurablyAsync<T>(string path, T value, CancellationToken cancellationToken, bool overwrite = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Performs copy durably asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task CopyDurablyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Performs compute sha256 asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    /// <summary>
    /// Performs the quarantine step owned by this component.
    /// </summary>
    private static void Quarantine(string path, string reason)
    {
        if (!File.Exists(path)) return;
        var destination = path + "." + reason + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        try { File.Move(path, destination); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Attempts to delete and reports the result without using failure for normal control flow.
    /// </summary>
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Represents notes version manifest and keeps its related state and behavior together.
    /// </summary>
    private sealed record NotesVersionManifest(long Version, DateTimeOffset CreatedAt, string Reason, long SizeBytes, string Sha256);
}

/// <summary>
/// Represents notes attachment store and keeps its related state and behavior together.
/// </summary>
public sealed class NotesAttachmentStore(IAppPaths paths, IProductionDiagnostics diagnostics) : INotesAttachmentStore
{
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root = Path.Combine(paths.DataDirectory, "Notes", "Attachments");
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Performs import asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<NotesMediaData> ImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) throw new FileNotFoundException("The selected attachment does not exist.", sourcePath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_root);
            var id = Guid.NewGuid();
            var extension = Path.GetExtension(sourcePath);
            var fileName = id.ToString("N") + (extension.Length <= 12 ? extension.ToLowerInvariant() : string.Empty);
            var destination = Path.Combine(_root, fileName);
            var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
                File.Move(temporary, destination);
                var hash = await ComputeAsync(destination, cancellationToken).ConfigureAwait(false);
                return new NotesMediaData
                {
                    AttachmentId = id,
                    OriginalName = Path.GetFileName(sourcePath),
                    StoredPath = fileName,
                    MediaType = GuessMediaType(extension),
                    SizeBytes = new FileInfo(destination).Length,
                    Sha256 = hash,
                    AltText = Path.GetFileNameWithoutExtension(sourcePath)
                };
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs resolve path asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> ResolvePathAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_root)) throw new FileNotFoundException("The Notes attachment store does not exist.");
        var prefix = attachmentId.ToString("N");
        var match = Directory.EnumerateFiles(_root, prefix + ".*", SearchOption.TopDirectoryOnly).FirstOrDefault();
        return match is null ? throw new FileNotFoundException("The Notes attachment was not found.") : Task.FromResult(match);
    }

    /// <summary>
    /// Performs delete unreferenced asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteUnreferencedAsync(IReadOnlyCollection<Guid> referencedAttachmentIds, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_root)) return;
            var keep = referencedAttachmentIds.Select(id => id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stem = Path.GetFileNameWithoutExtension(path);
                if (keep.Contains(stem)) continue;
                try { File.Delete(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await diagnostics.WriteAsync(ReliabilitySeverity.Warning, "notes", "attachment-cleanup-failed", "An unreferenced Notes attachment could not be removed.", cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs compute asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<string> ComputeAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    /// <summary>
    /// Performs the guess media type step owned by this component.
    /// </summary>
    private static string GuessMediaType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".m4a" => "audio/mp4",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };
}
