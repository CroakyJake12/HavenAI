/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/WorkspaceRetrievalAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns RetrievalIndexReport, IWorkspaceRetrievalIndexer. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents retrieval index report and keeps its related state and behavior together.
/// </summary>
public sealed record RetrievalIndexReport(
    int Indexed,
    int Unchanged,
    int Removed,
    int Skipped,
    IReadOnlyList<string> Notices);

/// <summary>
/// Defines the i workspace retrieval indexer contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IWorkspaceRetrievalIndexer
{
    Task<RetrievalIndexReport> IndexProjectAsync(Guid projectId, string rootPath, CancellationToken cancellationToken);
    Task<RetrievalIndexReport> IndexSubjectAsync(ContainerDefinition subject, IReadOnlyList<Lesson> lessons, CancellationToken cancellationToken);
}
