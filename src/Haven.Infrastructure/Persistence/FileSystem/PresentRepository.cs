using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class PresentRepository : IPresentRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _root;

    public PresentRepository(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _root = Path.Combine(paths.DataDirectory, "Present", "Documents");
    }

    public async Task<IReadOnlyList<PresentDocumentSummary>> ListAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_root))
            return [];

        var summaries = new List<PresentDocumentSummary>();
        foreach (var directory in Directory.EnumerateDirectories(_root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParse(Path.GetFileName(directory), out var id))
                continue;

            var document = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
            if (document is null)
                continue;

            summaries.Add(new PresentDocumentSummary(
                document.Id,
                document.Title,
                document.UpdatedAt,
                document.Version,
                document.Slides.Count,
                document.Recovery.RecoveredFromBackup));
        }

        return summaries
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<PresentDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (currentPath, backupPath) = Paths(documentId);
        var current = await TryLoadAsync(currentPath, documentId, cancellationToken).ConfigureAwait(false);
        if (current is not null)
            return current;

        var backup = await TryLoadAsync(backupPath, documentId, cancellationToken).ConfigureAwait(false);
        if (backup is null)
            return null;

        backup.Recovery.RecoveredFromBackup = true;
        backup.Recovery.RecoveredAt = DateTimeOffset.UtcNow;
        backup.Recovery.Message = "Recovered the previous valid presentation after the current file could not be read.";
        return backup;
    }

    public async Task<PresentSaveResult> SaveAsync(
        PresentDocument document,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateForSave(document);
        document.Normalize();
        document.UpdatedAt = DateTimeOffset.UtcNow;
        document.Version = checked(document.Version + 1);
        document.Metadata["lastSaveReason"] = reason ?? string.Empty;

        var directory = DocumentDirectory(document.Id);
        Directory.CreateDirectory(directory);
        var (currentPath, backupPath) = Paths(document.Id);
        var temporaryPath = Path.Combine(directory, $"current-{Guid.NewGuid():N}.tmp");

        try
        {
            if (File.Exists(currentPath))
            {
                if (document.Recovery.RecoveredFromBackup)
                {
                    var corruptPath = Path.Combine(
                        directory,
                        $"unreadable-current-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.json");
                    File.Move(currentPath, corruptPath, overwrite: false);
                }
                else
                {
                    File.Copy(currentPath, backupPath, overwrite: true);
                }
            }

            document.Recovery.RecoveredFromBackup = false;
            document.Recovery.RecoveredAt = null;
            document.Recovery.Message = string.Empty;

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            _ = await TryLoadAsync(temporaryPath, document.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The presentation did not pass its persistence verification read.");
            File.Move(temporaryPath, currentPath, overwrite: true);

            return new PresentSaveResult(
                document.Id,
                document.Version,
                document.UpdatedAt,
                currentPath,
                backupPath);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = DocumentDirectory(documentId);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        return Task.CompletedTask;
    }

    private async Task<PresentDocument?> TryLoadAsync(
        string path,
        Guid expectedId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<PresentDocument>(
                    stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (document is null || document.Id != expectedId)
                return null;
            if (document.SchemaVersion <= 0 || document.SchemaVersion > PresentDocument.CurrentSchemaVersion)
                return null;
            document.Normalize();
            return document;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static void ValidateForSave(PresentDocument document)
    {
        if (document.Id == Guid.Empty)
            throw new InvalidDataException("A presentation must have a stable identifier.");
        if (document.SchemaVersion <= 0 || document.SchemaVersion > PresentDocument.CurrentSchemaVersion)
            throw new InvalidDataException("This presentation schema version is not supported by this Haven build.");
        if (document.Slides is null)
            throw new InvalidDataException("A presentation must contain a slide collection.");
    }

    private (string CurrentPath, string BackupPath) Paths(Guid documentId)
    {
        var directory = DocumentDirectory(documentId);
        return (Path.Combine(directory, "current.json"), Path.Combine(directory, "previous.json"));
    }

    private string DocumentDirectory(Guid documentId) =>
        Path.Combine(_root, documentId.ToString("D"));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
