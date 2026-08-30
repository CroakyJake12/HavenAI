using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views;

internal sealed partial class ProjectHavenScene
{
    private Container? _sourceControlPanel;
    private Container? _sourceControlChanges;
    private Container? _sourceControlBranches;
    private Container? _sourceControlStashes;
    private HavenText? _sourceControlSummary;
    private HavenText? _sourceControlWorktrees;
    private HavenText? _sourceControlHistory;
    private HavenText? _sourceControlWorkingDiff;
    private HavenText? _sourceControlStagedDiff;
    private Input? _sourceControlStashMessage;

    public event EventHandler? SourceControlRefreshRequested;
    public event Action<ProjectSourceControlChange>? SourceControlStageRequested;
    public event Action<ProjectSourceControlChange>? SourceControlUnstageRequested;
    public event Action<ProjectGitBranch>? SourceControlCheckoutRequested;
    public event Action<string>? SourceControlCreateStashRequested;
    public event Action<ProjectGitStash>? SourceControlApplyStashRequested;

    public void SyncSourceControl(ProjectSourceControlSnapshot? sourceControl)
    {
        EnsureSourceControlSurface();
        Clear(_sourceControlChanges!);
        Clear(_sourceControlBranches!);
        Clear(_sourceControlStashes!);
        if (sourceControl is null)
        {
            _sourceControlSummary!.Content = "Source control not inspected yet.";
            _sourceControlWorktrees!.Content = "Worktrees · unavailable";
            _sourceControlHistory!.Content = "History · unavailable";
            _sourceControlWorkingDiff!.Content = "Working diff · unavailable";
            _sourceControlStagedDiff!.Content = "Staged diff · unavailable";
            return;
        }
        if (!sourceControl.IsRepository)
        {
            _sourceControlSummary!.Content = sourceControl.Message;
            _sourceControlWorktrees!.Content = "Worktrees · no Git repository";
            _sourceControlHistory!.Content = "History · no Git repository";
            _sourceControlWorkingDiff!.Content = "Working diff · none";
            _sourceControlStagedDiff!.Content = "Staged diff · none";
            return;
        }

        _sourceControlSummary!.Content = $"{sourceControl.CurrentBranch} · {sourceControl.Message}";
        foreach (var change in sourceControl.Changes.Take(10))
        {
            var row = Wrap("Project.SourceControl.Change." + Id(change.Path), 5);
            var label = Muted(string.Empty, $"{change.Summary} · {change.Path}");
            label.SetValue(HavenProperties.MaxWidth, HavenLength.Px(560));
            row.Add(label);
            if (change.HasWorkingTreeChange)
            {
                var stage = Ghost(string.Empty, "Stage", "plus");
                Wire(stage, () => SourceControlStageRequested?.Invoke(change));
                row.Add(stage);
            }
            if (change.IsStaged)
            {
                var unstage = Ghost(string.Empty, "Unstage", "undo");
                Wire(unstage, () => SourceControlUnstageRequested?.Invoke(change));
                row.Add(unstage);
            }
            _sourceControlChanges!.Add(row);
        }
        if (sourceControl.Changes.Count == 0) _sourceControlChanges!.Add(Muted(string.Empty, "No changed paths."));
        else if (sourceControl.Changes.Count > 10) _sourceControlChanges!.Add(Muted(string.Empty, $"+ {sourceControl.Changes.Count - 10} more changed path(s)"));

        foreach (var branch in sourceControl.Branches.Take(8))
        {
            var row = Wrap("Project.SourceControl.Branch." + Id(branch.Name), 5);
            row.Add(Muted(string.Empty, $"{(branch.IsCurrent ? "● " : string.Empty)}{branch.Name}{(branch.Ahead > 0 || branch.Behind > 0 ? $" · ↑{branch.Ahead} ↓{branch.Behind}" : string.Empty)}"));
            if (!branch.IsCurrent)
            {
                var checkout = Ghost(string.Empty, "Checkout", "git-branch");
                Wire(checkout, () => SourceControlCheckoutRequested?.Invoke(branch));
                row.Add(checkout);
            }
            _sourceControlBranches!.Add(row);
        }

        foreach (var stash in sourceControl.Stashes.Take(5))
        {
            var row = Wrap("Project.SourceControl.Stash." + stash.Index, 5);
            row.Add(Muted(string.Empty, $"{stash.Reference} · {stash.Message}"));
            var apply = Ghost(string.Empty, "Apply", "download");
            Wire(apply, () => SourceControlApplyStashRequested?.Invoke(stash));
            row.Add(apply);
            _sourceControlStashes!.Add(row);
        }
        if (sourceControl.Stashes.Count == 0) _sourceControlStashes!.Add(Muted(string.Empty, "No stashes."));

        _sourceControlWorktrees!.Content = sourceControl.Worktrees.Count == 0 ? "Worktrees · none" : "Worktrees · " + string.Join(" | ", sourceControl.Worktrees.Take(4).Select(item => $"{(item.IsCurrent ? "current: " : string.Empty)}{item.Branch ?? "detached"} @ {item.Path}{(item.IsLocked ? " [locked]" : string.Empty)}"));
        _sourceControlHistory!.Content = sourceControl.History.Count == 0 ? "History · no commits" : "History\n" + string.Join("\n", sourceControl.History.Take(6).Select(item => $"{item.ShortSha} · {item.Subject} · {item.Author}"));
        _sourceControlWorkingDiff!.Content = "Working diff\n" + PreviewDiff(sourceControl.WorkingDiff);
        _sourceControlStagedDiff!.Content = "Staged diff\n" + PreviewDiff(sourceControl.StagedDiff);
    }

    private void EnsureSourceControlSurface()
    {
        if (_sourceControlPanel is not null) return;
        var panel = Card("Project.SourceControl");
        panel.SetValue(HavenProperties.MaxHeight, HavenLength.Px(360));
        panel.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        var header = Wrap("Project.SourceControl.Header", 6);
        header.Add(Label("Project.SourceControl.Title", "Source control", TextLevel.H3));
        var refresh = Ghost("Project.SourceControl.Refresh", "Refresh Git", "refresh");
        Wire(refresh, () => SourceControlRefreshRequested?.Invoke(this, EventArgs.Empty));
        header.Add(refresh);
        panel.Add(header);
        _sourceControlSummary = Muted("Project.SourceControl.Summary", "Source control not inspected yet."); panel.Add(_sourceControlSummary);
        panel.Add(Caption("CHANGES")); _sourceControlChanges = Vertical("Project.SourceControl.Changes", 4); panel.Add(_sourceControlChanges);
        panel.Add(Caption("LOCAL BRANCHES")); _sourceControlBranches = Vertical("Project.SourceControl.Branches", 4); panel.Add(_sourceControlBranches);
        panel.Add(Caption("STASHES"));
        var stashCreate = Wrap("Project.SourceControl.StashCreate", 5);
        _sourceControlStashMessage = InputField("Project.SourceControl.StashMessage", "Optional stash message");
        _sourceControlStashMessage.SetValue(HavenProperties.MaxWidth, HavenLength.Px(420));
        stashCreate.Add(_sourceControlStashMessage);
        var createStash = Ghost("Project.SourceControl.CreateStash", "Stash changes", "archive");
        Wire(createStash, () => SourceControlCreateStashRequested?.Invoke(_sourceControlStashMessage.Text));
        stashCreate.Add(createStash); panel.Add(stashCreate);
        _sourceControlStashes = Vertical("Project.SourceControl.Stashes", 4); panel.Add(_sourceControlStashes);
        _sourceControlWorktrees = Muted("Project.SourceControl.Worktrees", string.Empty); panel.Add(_sourceControlWorktrees);
        _sourceControlHistory = Muted("Project.SourceControl.History", string.Empty); panel.Add(_sourceControlHistory);
        _sourceControlWorkingDiff = Muted("Project.SourceControl.WorkingDiff", string.Empty); _sourceControlWorkingDiff.SetValue(HavenProperties.FontFamily, "Cascadia Mono, Consolas, monospace"); panel.Add(_sourceControlWorkingDiff);
        _sourceControlStagedDiff = Muted("Project.SourceControl.StagedDiff", string.Empty); _sourceControlStagedDiff.SetValue(HavenProperties.FontFamily, "Cascadia Mono, Consolas, monospace"); panel.Add(_sourceControlStagedDiff);
        ToolDock.Add(panel);
        _sourceControlPanel = panel;
    }

    private static void Clear(Container container)
    {
        foreach (var child in container.Children.ToArray()) container.Remove(child);
    }

    private static string PreviewDiff(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "No changes.";
        var normalized = value.Trim();
        return normalized.Length <= 2400 ? normalized : normalized[..2400] + "\n[diff preview truncated]";
    }
}
