/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/WorkspaceTransactionAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns WorkspaceFileMutation, WorkspaceTransactionResult, IWorkspaceTransactionService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Application;

/// <summary>
/// Represents workspace file mutation and keeps its related state and behavior together.
/// </summary>
public sealed record WorkspaceFileMutation(string RelativePath, string Content);

/// <summary>
/// Represents workspace transaction result and keeps its related state and behavior together.
/// </summary>
public sealed record WorkspaceTransactionResult(
    Guid TransactionId,
    IReadOnlyList<string> ChangedPaths,
    int AddedCharacters,
    int RemovedCharacters);

/// <summary>
/// Defines the i workspace transaction service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IWorkspaceTransactionService
{
    Task<WorkspaceTransactionResult> ApplyAsync(
        string workspaceRoot,
        IReadOnlyList<WorkspaceFileMutation> mutations,
        CancellationToken cancellationToken);
}
