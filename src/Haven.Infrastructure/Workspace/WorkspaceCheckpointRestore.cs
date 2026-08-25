/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/Workspace/WorkspaceCheckpointRestore.cs, in the Infrastructure layer.
 * What: Owns WorkspaceCheckpointRestorer and ProjectInstructionFileSource — confined restore writes
 *       and depth-capped filesystem discovery of agent.md / AGENTS.md instruction files.
 * How: Restore writes are atomic (temp + move), reject path traversal, and stay inside the root.
 *      Discovery matches agent.md/AGENT.md/AGENTS.md case-insensitively from the root downward.
 * Why: Recovery must work in non-Git directories; project rules must load before applicable writes.
 * Maintenance: Never widen path acceptance; keep the depth cap and deterministic ordering.
 */

using System.Text;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class WorkspaceCheckpointRestorer(IWorkspaceToolService workspaceTools) : ICheckpointRestorer
{
    public async Task<IReadOnlyList<string>> RestoreAsync(string workspaceRoot, CheckpointRestorePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspaceTools);
        var restored = new List<string>();
        foreach (var (relativePath, beforeContent) in plan.PathToBeforeContent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await workspaceTools.WriteTextAtomicAsync(workspaceRoot, relativePath, beforeContent, cancellationToken).ConfigureAwait(false);
                restored.Add(relativePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A path that can no longer be written is reported honestly by omission.
            }
        }
        return restored;
    }
}

public sealed class ProjectInstructionFileSource : IProjectInstructionSource
{
    private const int MaxDepth = 6;
    private static readonly HashSet<string> InstructionNames = new(StringComparer.OrdinalIgnoreCase)
    { "agent.md", "agents.md" };

    public Task<IReadOnlyList<ProjectInstructionFile>> DiscoverAsync(string workspaceRoot, string? scopeRelativeDirectory, CancellationToken cancellationToken)
        => Task.Run<IReadOnlyList<ProjectInstructionFile>>(() =>
        {
            var results = new List<ProjectInstructionFile>();
            var fullRoot = Path.GetFullPath(workspaceRoot);
            if (!Directory.Exists(fullRoot)) return results;

            Visit(fullRoot, string.Empty, 0);
            if (!string.IsNullOrWhiteSpace(scopeRelativeDirectory))
            {
                // Scope-relative discovery walks toward the scope so deeper rules still apply.
                var scopeFull = Path.GetFullPath(Path.Combine(fullRoot, scopeRelativeDirectory));
                if (scopeFull.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(scopeFull))
                {
                    var relative = Path.GetRelativePath(fullRoot, scopeFull);
                    var parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                    var accumulated = string.Empty;
                    for (var index = 0; index < parts.Length && index < MaxDepth; index++)
                    {
                        accumulated = accumulated.Length == 0 ? parts[index] : Path.Combine(accumulated, parts[index]);
                        Visit(Path.Combine(fullRoot, accumulated), accumulated, index + 1);
                    }
                }
            }
            return results;

            void Visit(string directory, string relativeDirectory, int depth)
            {
                if (depth > MaxDepth) return;
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var file in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file);
                    if (!InstructionNames.Contains(name)) continue;
                    try
                    {
                        var content = File.ReadAllText(file);
                        var relativePath = relativeDirectory.Length == 0 ? name : $"{relativeDirectory}/{name}";
                        results.Add(new ProjectInstructionFile(relativePath, depth, content));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // One unreadable instruction file must not break loading of the others.
                    }
                }
            }
        }, cancellationToken);
}
