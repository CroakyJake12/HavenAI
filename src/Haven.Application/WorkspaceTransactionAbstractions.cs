namespace Haven.Application;

public sealed record WorkspaceFileMutation(string RelativePath, string Content);

public sealed record WorkspaceTransactionResult(
    Guid TransactionId,
    IReadOnlyList<string> ChangedPaths,
    int AddedCharacters,
    int RemovedCharacters);

public interface IWorkspaceTransactionService
{
    Task<WorkspaceTransactionResult> ApplyAsync(
        string workspaceRoot,
        IReadOnlyList<WorkspaceFileMutation> mutations,
        CancellationToken cancellationToken);
}
