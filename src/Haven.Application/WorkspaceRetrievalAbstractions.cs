using Haven.Core;

namespace Haven.Application;

public sealed record RetrievalIndexReport(
    int Indexed,
    int Unchanged,
    int Removed,
    int Skipped,
    IReadOnlyList<string> Notices);

public interface IWorkspaceRetrievalIndexer
{
    Task<RetrievalIndexReport> IndexProjectAsync(Guid projectId, string rootPath, CancellationToken cancellationToken);
    Task<RetrievalIndexReport> IndexSubjectAsync(ContainerDefinition subject, IReadOnlyList<LessonDefinition> lessons, CancellationToken cancellationToken);
}
