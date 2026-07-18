/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/WorkspaceTransactionService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns WorkspaceTransactionService, ResolvedMutation. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Represents workspace transaction service and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceTransactionService(IWorkspaceToolService workspaceTools) : IWorkspaceTransactionService
{
    /// <summary>
    /// Performs apply async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<WorkspaceTransactionResult> ApplyAsync(
        string workspaceRoot,
        IReadOnlyList<WorkspaceFileMutation> mutations,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(mutations);
        if (mutations.Count == 0)
            throw new ArgumentException("At least one file mutation is required.", nameof(mutations));

        var root = workspaceTools.ResolveWorkspacePath(workspaceRoot, ".");
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var resolved = new List<ResolvedMutation>(mutations.Count);
        var seen = new HashSet<string>(comparer);

        foreach (var mutation in mutations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(mutation.RelativePath))
                throw new ArgumentException("Every file mutation must have a relative path.", nameof(mutations));

            var targetPath = workspaceTools.ResolveWorkspacePath(root, mutation.RelativePath);
            if (!seen.Add(targetPath))
                throw new ArgumentException($"The transaction contains the same target more than once: {mutation.RelativePath}", nameof(mutations));
            if (Directory.Exists(targetPath))
                throw new IOException($"The target path is a directory: {mutation.RelativePath}");

            var previousContent = File.Exists(targetPath)
                ? await File.ReadAllTextAsync(targetPath, cancellationToken).ConfigureAwait(false)
                : null;
            resolved.Add(new ResolvedMutation(mutation.RelativePath, targetPath, mutation.Content ?? string.Empty, previousContent));
        }

        var transactionId = Guid.NewGuid();
        var stagingRoot = Path.Combine(root, ".haven", "transactions", transactionId.ToString("N"));
        var staged = Path.Combine(stagingRoot, "staged");
        var backups = Path.Combine(stagingRoot, "backups");
        Directory.CreateDirectory(staged);
        Directory.CreateDirectory(backups);

        try
        {
            for (var index = 0; index < resolved.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stagedPath = Path.Combine(staged, index.ToString("D6") + ".tmp");
                await File.WriteAllTextAsync(stagedPath, resolved[index].NewContent, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                resolved[index] = resolved[index] with { StagedPath = stagedPath };

                if (resolved[index].PreviousContent is not null)
                {
                    var backupPath = Path.Combine(backups, index.ToString("D6") + ".bak");
                    await File.WriteAllTextAsync(backupPath, resolved[index].PreviousContent, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                    resolved[index] = resolved[index] with { BackupPath = backupPath };
                }
            }

            var appliedCount = 0;
            try
            {
                foreach (var item in resolved)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(Path.GetDirectoryName(item.TargetPath)!);
                    File.Move(item.StagedPath!, item.TargetPath, true);
                    appliedCount++;
                }
            }
            catch
            {
                RollBack(resolved, appliedCount);
                throw;
            }

            var added = resolved.Sum(item => Math.Max(0, item.NewContent.Length - (item.PreviousContent?.Length ?? 0)));
            var removed = resolved.Sum(item => Math.Max(0, (item.PreviousContent?.Length ?? 0) - item.NewContent.Length));
            return new WorkspaceTransactionResult(transactionId, resolved.Select(item => item.RelativePath).ToArray(), added, removed);
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    /// <summary>
    /// Performs the roll back step owned by this component.
    /// </summary>
    private static void RollBack(IReadOnlyList<ResolvedMutation> mutations, int appliedCount)
    {
        List<Exception>? failures = null;
        for (var index = appliedCount - 1; index >= 0; index--)
        {
            var item = mutations[index];
            try
            {
                if (item.BackupPath is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(item.TargetPath)!);
                    File.Copy(item.BackupPath, item.TargetPath, true);
                }
                else if (File.Exists(item.TargetPath))
                {
                    File.Delete(item.TargetPath);
                }
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
            throw new AggregateException("The workspace transaction failed and one or more files could not be restored.", failures);
    }

    /// <summary>
    /// Attempts to delete directory and reports the result without using failure for normal control flow.
    /// </summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Represents resolved mutation and keeps its related state and behavior together.
    /// </summary>
    private sealed record ResolvedMutation(
        string RelativePath,
        string TargetPath,
        string NewContent,
        string? PreviousContent,
        string? StagedPath = null,
        string? BackupPath = null);
}
