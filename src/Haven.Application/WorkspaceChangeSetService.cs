using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Haven.Application;

public sealed record WorkspaceChangeSetEntry(string Path, string Content, string? ExpectedSha256);
public sealed record WorkspaceChangePreview(string Path, bool Existed, string BeforeSha256, string AfterSha256, int LinesAdded, int LinesRemoved);
public sealed record WorkspaceAppliedChange(string Path, string Before, string After, int LinesAdded, int LinesRemoved);
public sealed record WorkspaceChangeSetResult(IReadOnlyList<WorkspaceAppliedChange> Changes, string Summary);

public sealed class WorkspaceChangeSetService(IWorkspaceToolService tools)
{
    private const int MaxEntries = 50;
    private const int MaxContentCharacters = 2_000_000;

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

    private sealed record PreparedChange(
        WorkspaceChangeSetEntry Entry,
        string ResolvedPath,
        bool Existed,
        string Before,
        int LinesAdded,
        int LinesRemoved);
}
