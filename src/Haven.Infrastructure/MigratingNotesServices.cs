using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class MigratingNotesRepository(
    VerifiedNotesRepository inner,
    INotesDocumentMigrator migrator,
    INotesDocumentValidator validator,
    IAppPaths paths,
    IProductionDiagnostics diagnostics) : INotesRepository
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _root = Path.Combine(paths.DataDirectory, "Notes", "Documents");

    public async Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureAllCurrentDocumentsMigratedAsync(cancellationToken).ConfigureAwait(false);
        return await inner.ListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await EnsureCurrentDocumentMigratedAsync(documentId, cancellationToken).ConfigureAwait(false);
        return await inner.LoadAsync(documentId, cancellationToken).ConfigureAwait(false);
    }

    public Task<NotesSaveResult> SaveAsync(
        NotesDocument document,
        string reason,
        CancellationToken cancellationToken) =>
        inner.SaveAsync(document, reason, cancellationToken);

    public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken) =>
        inner.DeleteAsync(documentId, cancellationToken);

    public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(
        Guid documentId,
        CancellationToken cancellationToken) =>
        inner.GetVersionsAsync(documentId, cancellationToken);

    public async Task<NotesDocument?> LoadVersionAsync(
        Guid documentId,
        string versionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await inner.LoadVersionAsync(documentId, versionId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            var path = Path.Combine(_root, documentId.ToString("D"), "versions", versionId + ".haven-notes.json");
            if (!File.Exists(path)) return null;
            var result = await migrator.ReadAndMigrateAsync(path, cancellationToken).ConfigureAwait(false);
            EnsureValid(result.Document);
            return result.Document;
        }
    }

    public async Task<NotesDocument?> RecoverLatestAsync(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            return await inner.RecoverLatestAsync(documentId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            var directory = Path.Combine(_root, documentId.ToString("D"), "versions");
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
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                {
                }
            }
            return null;
        }
    }

    public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(
        string query,
        CancellationToken cancellationToken) =>
        SearchMigratedAsync(query, cancellationToken);

    private async Task<IReadOnlyList<NotesSearchHit>> SearchMigratedAsync(
        string query,
        CancellationToken cancellationToken)
    {
        await EnsureAllCurrentDocumentsMigratedAsync(cancellationToken).ConfigureAwait(false);
        return await inner.SearchAsync(query, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAllCurrentDocumentsMigratedAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_root)) return;
        foreach (var directory in Directory.EnumerateDirectories(_root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Guid.TryParse(Path.GetFileName(directory), out var id))
                await EnsureCurrentDocumentMigratedAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }

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
            new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 },
            cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("schemaVersion", out var value)) return 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var version)
            ? version
            : throw new InvalidDataException("The native Notes schemaVersion must be an integer.");
    }

    private void EnsureValid(NotesDocument document)
    {
        var validation = validator.Validate(document);
        if (!validation.IsValid)
            throw new InvalidDataException(
                "Migrated Notes content failed validation: "
                + string.Join(" | ", validation.Issues.Where(issue => issue.IsError).Take(12).Select(issue => issue.Path + ": " + issue.Message)));
    }
}

public sealed class MigratingNotesImportExportService(
    NotesImportExportService inner,
    INotesDocumentMigrator migrator,
    INotesDocumentValidator validator,
    IProductionDiagnostics diagnostics) : INotesImportExportService
{
    public IReadOnlyList<string> ImportExtensions => inner.ImportExtensions;
    public IReadOnlyList<string> ExportExtensions => inner.ExportExtensions;

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

    public Task<string> ExportAsync(
        NotesDocument document,
        string destinationPath,
        CancellationToken cancellationToken) =>
        inner.ExportAsync(document, destinationPath, cancellationToken);

    public Task PrintAsync(NotesDocument document, CancellationToken cancellationToken) =>
        inner.PrintAsync(document, cancellationToken);

    private static string EffectiveExtension(string path) =>
        path.EndsWith(".haven-notes.json", StringComparison.OrdinalIgnoreCase)
            ? ".haven-notes.json"
            : Path.GetExtension(path).ToLowerInvariant();
}
