/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Safety/AgentInstructions.cs, in the Application layer.
 * What: Owns ProjectInstructionFile, IProjectInstructionSource and ProjectAgentInstructions —
 *       automatic discovery/loading of agent.md / AGENTS.md files as execution constraints.
 * How: The Infrastructure source walks the workspace root (and optional nested scope) with a depth
 *      cap; this layer merges broadest-to-most-specific into one instruction block.
 * Why: The runtime — not the model — must ensure project rules are loaded before applicable writes,
 *      recorded in the Action Graph, and recalculated when task scope changes.
 * Maintenance: Keep merge order deterministic (root first); never let one file's failure break loading.
 */

using System.Text;
using Haven.Core;

namespace Haven.Application;

/// <summary>One discovered agent-instruction file.</summary>
public sealed record ProjectInstructionFile(string RelativePath, int Depth, string Content);

/// <summary>Finds agent-instruction files for a workspace/scope.</summary>
public interface IProjectInstructionSource
{
    Task<IReadOnlyList<ProjectInstructionFile>> DiscoverAsync(string workspaceRoot, string? scopeRelativeDirectory, CancellationToken cancellationToken);
}

public static class ProjectAgentInstructions
{
    /// <summary>Merges discovered files broadest-first. Empty when nothing applies.</summary>
    public static string Merge(IReadOnlyList<ProjectInstructionFile> files)
    {
        if (files.Count == 0) return string.Empty;
        var builder = new StringBuilder();
        foreach (var file in files.OrderBy(item => item.Depth).ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(file.Content)) continue;
            if (builder.Length > 0) builder.AppendLine();
            builder.Append("From ").Append(file.RelativePath).Append(":\n").Append(file.Content.Trim());
        }
        return builder.ToString();
    }

    /// <summary>Loads instructions for a workspace, returning empty text when discovery finds none.</summary>
    public static async Task<string> LoadAsync(
        IProjectInstructionSource source,
        string workspaceRoot,
        string? scopeRelativeDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var files = await source.DiscoverAsync(workspaceRoot, scopeRelativeDirectory, cancellationToken).ConfigureAwait(false);
            return Merge(files);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
