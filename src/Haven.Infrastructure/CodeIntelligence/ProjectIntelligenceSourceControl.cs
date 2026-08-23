using System.Text.RegularExpressions;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class ProjectIntelligenceService
{
    public async Task<ProjectSourceControlSnapshot> GetSourceControlAsync(string root, CancellationToken cancellationToken)
    {
        var canonicalRoot = ResolveGitRoot(root);
        var repositoryCheck = await RunGitAsync(canonicalRoot, "rev-parse --is-inside-work-tree", TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
        if (repositoryCheck.ExitCode != 0 || !repositoryCheck.StandardOutput.Contains("true", StringComparison.OrdinalIgnoreCase))
            return new ProjectSourceControlSnapshot(false, "No Git repository", [], [], [], [], [], string.Empty, string.Empty, "Initialize Git to enable source control.", DateTimeOffset.UtcNow);

        var statusResult = await RunGitAsync(canonicalRoot, "status --porcelain=v1 -z --untracked-files=all", TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        EnsureGitSucceeded(statusResult, "read Git status");
        var branch = await GitTextAsync(canonicalRoot, "branch --show-current", cancellationToken).ConfigureAwait(false);
        var branches = await ReadBranchesAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
        var stashes = await ReadStashesAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
        var worktrees = await ReadWorktreesAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
        var history = await ReadHistoryAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
        var workingDiff = await GitTextAsync(canonicalRoot, "diff --no-ext-diff --unified=3 --", cancellationToken).ConfigureAwait(false);
        var stagedDiff = await GitTextAsync(canonicalRoot, "diff --cached --no-ext-diff --unified=3 --", cancellationToken).ConfigureAwait(false);
        var changes = ParsePorcelainStatus(statusResult.StandardOutput);
        var current = string.IsNullOrWhiteSpace(branch) ? "Detached HEAD" : branch;
        var message = changes.Count == 0 ? "Working tree clean." : $"{changes.Count} changed path(s): {changes.Count(item => item.IsStaged)} staged, {changes.Count(item => item.HasWorkingTreeChange)} unstaged/untracked.";
        return new ProjectSourceControlSnapshot(true, current, changes, branches, stashes, worktrees, history, TruncateGitOutput(workingDiff), TruncateGitOutput(stagedDiff), message, DateTimeOffset.UtcNow);
    }

    public async Task<ProjectSourceControlSnapshot> StageAsync(string root, string relativePath, CancellationToken cancellationToken)
    {
        var canonicalRoot = ResolveGitRoot(root);
        var path = ValidateGitPath(canonicalRoot, relativePath);
        var result = await RunGitAsync(canonicalRoot, $"add -- {QuoteGitArgument(path)}", TimeSpan.FromSeconds(40), cancellationToken).ConfigureAwait(false);
        EnsureGitSucceeded(result, $"stage {path}");
        return await GetSourceControlAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectSourceControlSnapshot> UnstageAsync(string root, string relativePath, CancellationToken cancellationToken)
    {
        var canonicalRoot = ResolveGitRoot(root);
        var path = ValidateGitPath(canonicalRoot, relativePath);
        var result = await RunGitAsync(canonicalRoot, $"restore --staged -- {QuoteGitArgument(path)}", TimeSpan.FromSeconds(40), cancellationToken).ConfigureAwait(false);
        EnsureGitSucceeded(result, $"unstage {path}");
        return await GetSourceControlAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectSourceControlSnapshot> CheckoutBranchAsync(string root, string branchName, CancellationToken cancellationToken)
    {
        var canonicalRoot = ResolveGitRoot(root);
        var requested = ValidateBranchName(branchName);
        var current = await GetSourceControlAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
        if (!current.IsRepository) throw new InvalidOperationException("This project is not a Git repository.");
        if (current.Changes.Count != 0) throw new InvalidOperationException("Safe branch checkout requires a clean working tree. Commit or stash current changes first.");
        if (!current.Branches.Any(item => item.Name.Equals(requested, StringComparison.Ordinal))) throw new ArgumentException("Choose an existing local branch from the source-control branch list.", nameof(branchName));
        if (current.CurrentBranch.Equals(requested, StringComparison.Ordinal)) return current;
        var result = await RunGitAsync(canonicalRoot, $"switch {QuoteGitArgument(requested)}", TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
        EnsureGitSucceeded(result, $"switch to branch {requested}");
        return await GetSourceControlAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectSourceControlSnapshot> CreateStashAsync(string root, string message, CancellationToken cancellationToken)
    {
        var canonicalRoot = ResolveGitRoot(root);
        var current = await GetSourceControlAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
        if (!current.IsRepository) throw new InvalidOperationException("This project is not a Git repository.");
        if (current.Changes.Count == 0) throw new InvalidOperationException("There are no working-tree changes to stash.");
        var label = string.IsNullOrWhiteSpace(message) ? $"Haven stash {DateTimeOffset.Now:yyyy-MM-dd HH:mm}" : message.Trim();
        if (label.Length > 200 || label.Any(char.IsControl) || label.Contains('\"')) throw new ArgumentException("Stash messages must be 200 characters or fewer and cannot contain quotes or control characters.", nameof(message));
        var result = await RunGitAsync(canonicalRoot, $"stash push -u -m {QuoteGitArgument(label)}", TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
        EnsureGitSucceeded(result, "create stash");
        return await GetSourceControlAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectSourceControlSnapshot> ApplyStashAsync(string root, string stashReference, CancellationToken cancellationToken)
    {
        var canonicalRoot = ResolveGitRoot(root);
        var reference = stashReference.Trim();
        if (!Regex.IsMatch(reference, @"^stash@\{\d+\}$", RegexOptions.CultureInvariant)) throw new ArgumentException("Choose a stash from the current source-control stash list.", nameof(stashReference));
        var current = await GetSourceControlAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
        if (current.Changes.Count != 0) throw new InvalidOperationException("Safe stash apply requires a clean working tree.");
        if (!current.Stashes.Any(item => item.Reference.Equals(reference, StringComparison.Ordinal))) throw new InvalidOperationException("That stash no longer exists. Refresh source control.");
        var result = await RunGitAsync(canonicalRoot, $"stash apply {QuoteGitArgument(reference)}", TimeSpan.FromMinutes(3), cancellationToken).ConfigureAwait(false);
        EnsureGitSucceeded(result, $"apply {reference}");
        return await GetSourceControlAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
    }

    internal static IReadOnlyList<ProjectSourceControlChange> ParsePorcelainStatus(string output)
    {
        if (string.IsNullOrEmpty(output)) return [];
        var tokens = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<ProjectSourceControlChange>();
        for (var index = 0; index < tokens.Length; index++)
        {
            var entry = tokens[index];
            if (entry.Length < 4) continue;
            var x = entry[0];
            var y = entry[1];
            var path = entry[3..];
            var renameOrCopy = x is 'R' or 'C' || y is 'R' or 'C';
            if (renameOrCopy && index + 1 < tokens.Length) index++; // -z puts the destination path first; the following token is the source path.
            var untracked = x == '?' && y == '?';
            var conflicted = IsConflictCode(x, y);
            var staged = !untracked && x != ' ';
            var working = untracked || y != ' ';
            result.Add(new ProjectSourceControlChange(path, StatusName(x), StatusName(y), staged, working, untracked, conflicted, StatusSummary(x, y)));
        }
        return result;
    }

    private async Task<IReadOnlyList<ProjectGitBranch>> ReadBranchesAsync(string root, CancellationToken cancellationToken)
    {
        var text = await GitTextAsync(root, "for-each-ref --format=\"%(refname:short)%09%(HEAD)%09%(upstream:short)%09%(upstream:track)\" refs/heads", cancellationToken).ConfigureAwait(false);
        var output = new List<ProjectGitBranch>();
        foreach (var line in Lines(text))
        {
            var fields = line.Split('\t');
            if (fields.Length == 0 || string.IsNullOrWhiteSpace(fields[0])) continue;
            var track = fields.Length > 3 ? fields[3] : string.Empty;
            output.Add(new ProjectGitBranch(fields[0], fields.Length > 1 && fields[1].Contains('*'), fields.Length > 2 && fields[2].Length > 0 ? fields[2] : null, TrackCount(track, "ahead"), TrackCount(track, "behind")));
        }
        return output.OrderByDescending(item => item.IsCurrent).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<IReadOnlyList<ProjectGitStash>> ReadStashesAsync(string root, CancellationToken cancellationToken)
    {
        var text = await GitTextAsync(root, "stash list --format=%gd%x09%gs", cancellationToken).ConfigureAwait(false);
        var output = new List<ProjectGitStash>();
        foreach (var line in Lines(text))
        {
            var split = line.IndexOf('\t');
            if (split <= 0) continue;
            var reference = line[..split];
            var match = Regex.Match(reference, @"^stash@\{(?<index>\d+)\}$", RegexOptions.CultureInvariant);
            if (match.Success && int.TryParse(match.Groups["index"].Value, out var index)) output.Add(new ProjectGitStash(index, reference, line[(split + 1)..]));
        }
        return output;
    }

    private async Task<IReadOnlyList<ProjectGitWorktree>> ReadWorktreesAsync(string root, CancellationToken cancellationToken)
    {
        var text = await GitTextAsync(root, "worktree list --porcelain", cancellationToken).ConfigureAwait(false);
        var output = new List<ProjectGitWorktree>();
        string? path = null; string head = string.Empty; string? branch = null; var detached = false; var locked = false; string? lockReason = null;
        void Flush()
        {
            if (path is null) return;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            output.Add(new ProjectGitWorktree(path, head, branch, Path.GetFullPath(path).Equals(Path.GetFullPath(root), comparison), detached, locked, lockReason));
            path = null; head = string.Empty; branch = null; detached = false; locked = false; lockReason = null;
        }
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.Length == 0) { Flush(); continue; }
            if (line.StartsWith("worktree ", StringComparison.Ordinal)) path = line[9..];
            else if (line.StartsWith("HEAD ", StringComparison.Ordinal)) head = line[5..];
            else if (line.StartsWith("branch ", StringComparison.Ordinal)) branch = line[7..].Replace("refs/heads/", string.Empty, StringComparison.Ordinal);
            else if (line.Equals("detached", StringComparison.Ordinal)) detached = true;
            else if (line.StartsWith("locked", StringComparison.Ordinal)) { locked = true; lockReason = line.Length > 7 ? line[7..] : null; }
        }
        Flush();
        return output;
    }

    private async Task<IReadOnlyList<ProjectGitCommit>> ReadHistoryAsync(string root, CancellationToken cancellationToken)
    {
        var text = await GitTextAsync(root, "log -n 30 --date=iso-strict --pretty=format:%H%x09%h%x09%an%x09%aI%x09%s", cancellationToken).ConfigureAwait(false);
        var output = new List<ProjectGitCommit>();
        foreach (var line in Lines(text))
        {
            var fields = line.Split('\t', 5);
            if (fields.Length < 5) continue;
            output.Add(new ProjectGitCommit(fields[0], fields[1], fields[2], DateTimeOffset.TryParse(fields[3], out var date) ? date : null, fields[4]));
        }
        return output;
    }

    private static string ResolveGitRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var canonical = Path.GetFullPath(root);
        if (!Directory.Exists(canonical)) throw new DirectoryNotFoundException(canonical);
        return canonical;
    }

    private static string ValidateGitPath(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsWithin(full, root)) throw new UnauthorizedAccessException("Git operations cannot target paths outside the project root.");
        var relative = Path.GetRelativePath(root, full);
        if (relative.Equals(".", StringComparison.Ordinal) || relative.StartsWith("..", StringComparison.Ordinal)) throw new UnauthorizedAccessException("Choose a project file, not a parent path.");
        return relative.Replace('\\', '/');
    }

    private static string ValidateBranchName(string branchName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        var value = branchName.Trim();
        if (value.Length > 255 || value.Any(char.IsControl) || value.Contains('\"') || value.StartsWith('-') || value.Contains("..", StringComparison.Ordinal) || value.Contains("@{", StringComparison.Ordinal) || value.EndsWith('.') || value.EndsWith('/') || value.Contains("//", StringComparison.Ordinal) || value.Any(character => character is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\')) throw new ArgumentException("Choose a valid existing local Git branch name.", nameof(branchName));
        return value;
    }

    private static string QuoteGitArgument(string value)
    {
        if (value.Any(char.IsControl) || value.Contains('\"')) throw new ArgumentException("Git argument contains unsupported control or quote characters.", nameof(value));
        return $"\"{value}\"";
    }

    private static void EnsureGitSucceeded(ProcessResult result, string operation)
    {
        if (result.ExitCode == 0) return;
        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        throw new InvalidOperationException($"Git could not {operation}: {TruncateGitOutput(detail, 1200)}");
    }

    private static IReadOnlyList<string> Lines(string value) => value.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static int TrackCount(string value, string name) { var match = Regex.Match(value, $@"\b{name} (?<count>\d+)\b", RegexOptions.CultureInvariant); return match.Success && int.TryParse(match.Groups["count"].Value, out var count) ? count : 0; }
    private static bool IsConflictCode(char x, char y) => (x, y) is ('D', 'D') or ('A', 'U') or ('U', 'D') or ('U', 'A') or ('D', 'U') or ('A', 'A') or ('U', 'U');
    private static string StatusName(char value) => value switch { ' ' => "None", '?' => "Untracked", 'M' => "Modified", 'A' => "Added", 'D' => "Deleted", 'R' => "Renamed", 'C' => "Copied", 'U' => "Unmerged", 'T' => "Type changed", _ => value.ToString() };
    private static string StatusSummary(char x, char y) { if (x == '?' && y == '?') return "Untracked"; if (IsConflictCode(x, y)) return "Conflict"; if (x != ' ' && y != ' ') return $"Staged {StatusName(x).ToLowerInvariant()}, working tree {StatusName(y).ToLowerInvariant()}"; if (x != ' ') return $"Staged {StatusName(x).ToLowerInvariant()}"; return StatusName(y); }
    private static string TruncateGitOutput(string value, int maximum = 80_000) => value.Length <= maximum ? value : value[..maximum] + "\n[output truncated by Haven]";
}
