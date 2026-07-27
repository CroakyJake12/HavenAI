/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/MigratingNotesServices.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns MigratingNotesRepository, MigratingNotesImportExportService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents migrating notes repository and keeps its related state and behavior together.
/// </summary>
public sealed class MigratingNotesRepository(
    VerifiedNotesRepository inner,
    INotesDocumentMigrator migrator,
    INotesDocumentValidator validator,
    IAppPaths paths,
    IProductionDiagnostics diagnostics) : INotesRepository
{
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
    public async Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureAllCurrentDocumentsMigratedAsync(cancellationToken).ConfigureAwait(false);
        return await inner.ListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs load asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await EnsureCurrentDocumentMigratedAsync(documentId, cancellationToken).ConfigureAwait(false);
        return await inner.LoadAsync(documentId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs save asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<NotesSaveResult> SaveAsync(
        NotesDocument document,
        string reason,
        CancellationToken cancellationToken) =>
        inner.SaveAsync(document, reason, cancellationToken);

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
    public async Task<NotesDocument?> LoadVersionAsync(
        Guid documentId,
        string versionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await inner.LoadVersionAsync(documentId, versionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            var path = Path.Combine(_root, documentId.ToString("D"), "Versions", versionId + ".haven-notes.json");
            if (!File.Exists(path)) return null;
            var result = await migrator.ReadAndMigrateAsync(path, cancellationToken).ConfigureAwait(false);
            EnsureValid(result.Document);
            return result.Document;
        }
    }

    /// <summary>
    /// Performs recover latest asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<NotesDocument?> RecoverLatestAsync(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            return await inner.RecoverLatestAsync(documentId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            var directory = Path.Combine(_root, documentId.ToString("D"), "Versions");
            if (!Directory.Exists(directory)) return null;
            foreach (var path in Directory.EnumerateFiles(directory, "*.haven-notes.json", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await migrator.ReadAndMigrateAsync(path, cancellationToken).ConfigureAwait(false);
                    EnsureValid(result.Document);
                    result.Document.Recovery.HasUnsavedRecovery = true;
                    result.Document.Recovery.LastRecoveredAt = DateTimeOffset.UtcNow;
                    result.Document.Recovery.RecoveryReason = "Recovered and migrated from a previous native Notes version.";
                    return result.Document;
                }
                catch (Exception candidateException) when (candidateException is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                {
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Performs search asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(
        string query,
        CancellationToken cancellationToken) =>
        SearchMigratedAsync(query, cancellationToken);

    /// <summary>
    /// Performs search migrated asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<IReadOnlyList<NotesSearchHit>> SearchMigratedAsync(
        string query,
        CancellationToken cancellationToken)
    {
        await EnsureAllCurrentDocumentsMigratedAsync(cancellationToken).ConfigureAwait(false);
        return await inner.SearchAsync(query, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs ensure all current documents migrated asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task EnsureAllCurrentDocumentsMigratedAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_root)) return;
        foreach (var directory in Directory.EnumerateDirectories(_root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(Path.GetFileName(directory), out var id)) continue;
            try
            {
                await EnsureCurrentDocumentMigratedAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                await diagnostics.WriteAsync(
                    ReliabilitySeverity.Warning,
                    "notes",
                    "document-migration-deferred",
                    "A Notes document could not be migrated during library enumeration. Other documents remain available and the normal recovery path will inspect this file.",
                    new Dictionary<string, string>
                    {
                        ["documentId"] = id.ToString("D"),
                        ["exceptionType"] = ex.GetType().Name
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Performs ensure current document migrated asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task EnsureCurrentDocumentMigratedAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Document ID cannot be empty.", nameof(documentId));
        var path = Path.Combine(_root, documentId.ToString("D"), "current.haven-notes.json");
        if (!File.Exists(path)) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var schema = await ReadSchemaVersionAsync(path, cancellationToken).ConfigureAwait(false);
            if (schema == NotesDocument.CurrentSchemaVersion) return;
            var result = await migrator.ReadAndMigrateAsync(path, cancellationToken).ConfigureAwait(false);
            result.Document.Id = documentId;
            EnsureValid(result.Document);
            await inner.SaveAsync(
                result.Document,
                $"Migrated native Notes schema {result.SourceSchemaVersion} to {result.TargetSchemaVersion}",
                cancellationToken).ConfigureAwait(false);
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "notes",
                "document-migrated",
                "A native Notes document was migrated before it was opened.",
                new Dictionary<string, string>
                {
                    ["documentId"] = documentId.ToString("D"),
                    ["sourceSchema"] = result.SourceSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["targetSchema"] = result.TargetSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["changes"] = string.Join(" | ", result.Changes.Take(20))
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs read schema version asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<int> ReadSchemaVersionAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var document = await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 64
            },
            cancellationToken).ConfigureAwait(false);
        var property = document.RootElement.EnumerateObject()
            .FirstOrDefault(candidate => candidate.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase));
        if (property.Name is null) return 0;
        return property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var version)
            ? version
            : throw new InvalidDataException("The native Notes schemaVersion must be an integer.");
    }

    /// <summary>
    /// Performs the ensure valid step owned by this component.
    /// </summary>
    private void EnsureValid(NotesDocument document)
    {
        var validation = validator.Validate(document);
        if (!validation.IsValid)
            throw new InvalidDataException(
                "Migrated Notes content failed validation: "
                + string.Join(" | ", validation.Issues.Where(issue => issue.IsError).Take(12).Select(issue => issue.Path + ": " + issue.Message)));
    }
}

/// <summary>
/// Represents migrating notes import export service and keeps its related state and behavior together.
/// </summary>
public sealed class MigratingNotesImportExportService(
    NotesImportExportService inner,
    INotesDocumentMigrator migrator,
    INotesDocumentValidator validator,
    IProductionDiagnostics diagnostics) : INotesImportExportService
{
    /// <summary>
    /// Gets or updates import extensions, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<string> ImportExtensions => inner.ImportExtensions;
    /// <summary>
    /// Gets or updates export extensions, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<string> ExportExtensions => inner.ExportExtensions;

    /// <summary>
    /// Performs import asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<NotesDocument> ImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var extension = EffectiveExtension(sourcePath);
        if (extension is not ".haven-notes.json" and not ".json")
            return await inner.ImportAsync(sourcePath, cancellationToken).ConfigureAwait(false);

        var result = await migrator.ReadAndMigrateAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var document = result.Document;
        var now = DateTimeOffset.UtcNow;
        document.Id = Guid.NewGuid();
        document.Title = string.IsNullOrWhiteSpace(document.Title)
            ? Path.GetFileNameWithoutExtension(sourcePath)
            : document.Title.Trim();
        document.CreatedAt = now;
        document.UpdatedAt = now;
        document.Version = 0;
        document.Recovery = new NotesRecoveryState();
        document.Collaboration.SyncRevision = string.Empty;
        document.Collaboration.RemoteEtag = string.Empty;
        document.Collaboration.LastSyncedAt = null;
        document.Collaboration.ConflictState = NotesConflictState.None;
        document.Collaboration.Conflicts.Clear();
        document.Revisions.Add(new NotesRevision
        {
            Kind = NotesRevisionKind.Imported,
            Summary = result.SourceSchemaVersion == result.TargetSchemaVersion
                ? "Imported native Notes document"
                : $"Imported and migrated native Notes schema {result.SourceSchemaVersion} to {result.TargetSchemaVersion}",
            Author = Environment.UserName,
            CreatedAt = now
        });
        var validation = validator.Validate(document);
        if (!validation.IsValid)
            throw new InvalidDataException(
                "Imported Notes content failed validation: "
                + string.Join(" | ", validation.Issues.Where(issue => issue.IsError).Take(12).Select(issue => issue.Path + ": " + issue.Message)));
        await diagnostics.WriteAsync(
            ReliabilitySeverity.Information,
            "notes",
            "native-document-imported",
            "A native Notes document was imported through the migration boundary.",
            new Dictionary<string, string>
            {
                ["documentId"] = document.Id.ToString("D"),
                ["sourceSchema"] = result.SourceSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["targetSchema"] = result.TargetSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return document;
    }

    /// <summary>
    /// Performs export asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> ExportAsync(
        NotesDocument document,
        string destinationPath,
        CancellationToken cancellationToken) =>
        inner.ExportAsync(document, destinationPath, cancellationToken);

    /// <summary>
    /// Performs print asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task PrintAsync(NotesDocument document, CancellationToken cancellationToken) =>
        inner.PrintAsync(document, cancellationToken);

    /// <summary>
    /// Performs the effective extension step owned by this component.
    /// </summary>
    private static string EffectiveExtension(string path) =>
        path.EndsWith(".haven-notes.json", StringComparison.OrdinalIgnoreCase)
            ? ".haven-notes.json"
            : Path.GetExtension(path).ToLowerInvariant();
}
