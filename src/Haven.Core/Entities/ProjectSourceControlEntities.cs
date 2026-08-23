namespace Haven.Core;

public sealed record ProjectSourceControlChange(
    string Path,
    string IndexStatus,
    string WorkTreeStatus,
    bool IsStaged,
    bool HasWorkingTreeChange,
    bool IsUntracked,
    bool IsConflicted,
    string Summary);

public sealed record ProjectGitBranch(
    string Name,
    bool IsCurrent,
    string? Upstream,
    int Ahead,
    int Behind);

public sealed record ProjectGitStash(int Index, string Reference, string Message);

public sealed record ProjectGitWorktree(
    string Path,
    string Head,
    string? Branch,
    bool IsCurrent,
    bool IsDetached,
    bool IsLocked,
    string? LockReason);

public sealed record ProjectGitCommit(
    string Sha,
    string ShortSha,
    string Author,
    DateTimeOffset? AuthoredAt,
    string Subject);

public sealed record ProjectSourceControlSnapshot(
    bool IsRepository,
    string CurrentBranch,
    IReadOnlyList<ProjectSourceControlChange> Changes,
    IReadOnlyList<ProjectGitBranch> Branches,
    IReadOnlyList<ProjectGitStash> Stashes,
    IReadOnlyList<ProjectGitWorktree> Worktrees,
    IReadOnlyList<ProjectGitCommit> History,
    string WorkingDiff,
    string StagedDiff,
    string Message,
    DateTimeOffset CapturedAt);
