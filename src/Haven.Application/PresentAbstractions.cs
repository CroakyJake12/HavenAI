using Haven.Core;

namespace Haven.Application;

public sealed record PresentDocumentSummary(
    Guid Id,
    string Title,
    DateTimeOffset UpdatedAt,
    int Version,
    int SlideCount,
    bool RecoveredFromBackup);

public sealed record PresentSaveResult(
    Guid DocumentId,
    int Version,
    DateTimeOffset SavedAt,
    string CurrentPath,
    string BackupPath);

public interface IPresentRepository
{
    Task<IReadOnlyList<PresentDocumentSummary>> ListAsync(CancellationToken cancellationToken);
    Task<PresentDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken);
    Task<PresentSaveResult> SaveAsync(
        PresentDocument document,
        string reason,
        CancellationToken cancellationToken);
    Task DeleteAsync(Guid documentId, CancellationToken cancellationToken);
}

public interface IPresentExportService
{
    IReadOnlyList<string> ExportExtensions { get; }

    Task<string> ExportAsync(
        PresentDocument document,
        string destinationPath,
        CancellationToken cancellationToken);
}
