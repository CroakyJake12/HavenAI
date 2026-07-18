/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/WorkspaceChangeSetService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns WorkspaceChangeSetEntry, WorkspaceChangePreview, WorkspaceAppliedChange, WorkspaceChangeSetResult, WorkspaceChangeSetService, PreparedChange. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Haven.Application;

/// <summary>
/// Represents workspace change set entry and keeps its related state and behavior together.
/// </summary>
public sealed record WorkspaceChangeSetEntry(string Path, string Content, string? ExpectedSha256);
/// <summary>
/// Represents workspace change preview and keeps its related state and behavior together.
/// </summary>
public sealed record WorkspaceChangePreview(string Path, bool Existed, string BeforeSha256, string AfterSha256, int LinesAdded, int LinesRemoved);
/// <summary>
/// Represents workspace applied change and keeps its related state and behavior together.
/// </summary>
public sealed record WorkspaceAppliedChange(string Path, string Before, string After, int LinesAdded, int LinesRemoved);
/// <summary>
/// Represents workspace change set result and keeps its related state and behavior together.
/// </summary>
public sealed record WorkspaceChangeSetResult(IReadOnlyList<WorkspaceAppliedChange> Changes, string Summary);

/// <summary>
/// Represents workspace change set service and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceChangeSetService(IWorkspaceToolService tools)
{
    /// <summary>
    /// Stores max entries locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaxEntries = 50;
    /// <summary>
    /// Stores max content characters locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int MaxContentCharacters = 2_000_000;

    /// <summary>
    /// Performs preview asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<IReadOnlyList<WorkspaceChangePreview>> PreviewAsync(
        string workspaceRoot,
        string changesJson,
        CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(workspaceRoot, changesJson, cancellationToken).ConfigureAwait(false);
        return prepared.Select(item => new WorkspaceChangePreview(
            item.Entry.Path,
            item.Existed,
            Sha256(item.Before),
            Sha256(item.Entry.Content),
            item.LinesAdded,
            item.LinesRemoved)).ToArray();
    }

    /// <summary>
    /// Performs apply asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<WorkspaceChangeSetResult> ApplyAsync(
        string workspaceRoot,
        string changesJson,
        CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(workspaceRoot, changesJson, cancellationToken).ConfigureAwait(false);
        var applied = new List<PreparedChange>(prepared.Count);
        try
        {
            foreach (var item in prepared)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await tools.WriteTextAtomicAsync(workspaceRoot, item.Entry.Path, item.Entry.Content, cancellationToken).ConfigureAwait(false);
                applied.Add(item);
            }
        }
        catch
        {
            await RollBackAsync(workspaceRoot, applied).ConfigureAwait(false);
            throw;
        }

        var changes = prepared.Select(item => new WorkspaceAppliedChange(
            item.Entry.Path,
            item.Before,
            item.Entry.Content,
            item.LinesAdded,
            item.LinesRemoved)).ToArray();
        var added = changes.Sum(item => item.LinesAdded);
        var removed = changes.Sum(item => item.LinesRemoved);
        return new WorkspaceChangeSetResult(changes,
            $"Applied {changes.Length} workspace file change{(changes.Length == 1 ? string.Empty : "s")} transactionally (+{added}/-{removed} lines)." );
    }

    /// <summary>
    /// Performs prepare asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<IReadOnlyList<PreparedChange>> PrepareAsync(
        string workspaceRoot,
        string changesJson,
        CancellationToken cancellationToken)
    {
        var entries = Parse(changesJson);
        var duplicate = entries.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new InvalidOperationException($"Change set contains duplicate path '{duplicate.Key}'.");

        var prepared = new List<PreparedChange>(entries.Count);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = tools.ResolveWorkspacePath(workspaceRoot, entry.Path);
            if (Directory.Exists(resolved)) throw new InvalidOperationException($"Change-set target '{entry.Path}' is a directory.");

            string before;
            var existed = File.Exists(resolved);
            if (existed)
            {
                before = await tools.ReadTextAsync(workspaceRoot, entry.Path, cancellationToken).ConfigureAwait(false);
                if (before.IndexOf('\0') >= 0) throw new InvalidOperationException($"Change-set target '{entry.Path}' appears to be binary.");
            }
            else
            {
                before = string.Empty;
            }

            var beforeHash = Sha256(before);
            if (!string.IsNullOrWhiteSpace(entry.ExpectedSha256) &&
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(beforeHash),
                    Encoding.ASCII.GetBytes(entry.ExpectedSha256.Trim().ToLowerInvariant())))
                throw new InvalidOperationException($"Change-set target '{entry.Path}' changed after inspection; expected SHA-256 did not match.");

            var (added, removed) = CountLineChanges(before, entry.Content);
            prepared.Add(new PreparedChange(entry, resolved, existed, before, added, removed));
        }
        return prepared;
    }

    /// <summary>
    /// Performs roll back asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RollBackAsync(string workspaceRoot, IReadOnlyList<PreparedChange> applied)
    {
        List<Exception>? failures = null;
        for (var index = applied.Count - 1; index >= 0; index--)
        {
            var item = applied[index];
            try
            {
                if (item.Existed)
                    await tools.WriteTextAtomicAsync(workspaceRoot, item.Entry.Path, item.Before, CancellationToken.None).ConfigureAwait(false);
                else if (File.Exists(item.ResolvedPath))
                    File.Delete(item.ResolvedPath);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }
        if (failures is not null)
            throw new AggregateException("The change set failed and one or more rollback operations also failed.", failures);
    }

    /// <summary>
    /// Performs the parse step owned by this component.
    /// </summary>
    private static IReadOnlyList<WorkspaceChangeSetEntry> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("changes_json is required.");
        WorkspaceChangeSetEntry[]? entries;
        try
        {
            entries = JsonSerializer.Deserialize<WorkspaceChangeSetEntry[]>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("changes_json must be a JSON array of { path, content, expectedSha256? } objects.", exception);
        }
        if (entries is null || entries.Length == 0) throw new ArgumentException("A change set must contain at least one file.");
        if (entries.Length > MaxEntries) throw new InvalidOperationException($"A change set may contain at most {MaxEntries} files.");
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path)) throw new ArgumentException("Every change-set entry requires a path.");
            if (entry.Content is null) throw new ArgumentException($"Change-set entry '{entry.Path}' requires content.");
            if (entry.Content.Length > MaxContentCharacters) throw new InvalidOperationException($"Change-set entry '{entry.Path}' exceeds the {MaxContentCharacters:N0}-character limit.");
            if (!string.IsNullOrWhiteSpace(entry.ExpectedSha256) && (entry.ExpectedSha256.Length != 64 || !entry.ExpectedSha256.All(Uri.IsHexDigit)))
                throw new ArgumentException($"Change-set entry '{entry.Path}' has an invalid expected SHA-256 value.");
        }
        return entries;
    }

    /// <summary>
    /// Performs the sha256 step owned by this component.
    /// </summary>
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static (int Added, int Removed) CountLineChanges(string before, string after)
    {
        var oldLines = before.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var newLines = after.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix]) prefix++;
        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix &&
               oldLines[oldLines.Length - 1 - suffix] == newLines[newLines.Length - 1 - suffix]) suffix++;
        return (Math.Max(0, newLines.Length - prefix - suffix), Math.Max(0, oldLines.Length - prefix - suffix));
    }

    /// <summary>
    /// Represents prepared change and keeps its related state and behavior together.
    /// </summary>
    private sealed record PreparedChange(
        WorkspaceChangeSetEntry Entry,
        string ResolvedPath,
        bool Existed,
        string Before,
        int LinesAdded,
        int LinesRemoved);
}
