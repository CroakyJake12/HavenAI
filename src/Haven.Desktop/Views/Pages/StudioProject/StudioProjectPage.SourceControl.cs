using Haven.Core;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.StudioProject;

public sealed partial class StudioProjectPage
{
    private ProjectSourceControlSnapshot? _sourceControl;
    private string _stashMessage = string.Empty;
    private AsyncRelayCommand? _refreshSourceControlCommand;
    private AsyncRelayCommand<ProjectSourceControlChange>? _stageSourceControlCommand;
    private AsyncRelayCommand<ProjectSourceControlChange>? _unstageSourceControlCommand;
    private AsyncRelayCommand<ProjectGitBranch>? _checkoutSourceControlBranchCommand;
    private AsyncRelayCommand? _createSourceControlStashCommand;
    private AsyncRelayCommand<ProjectGitStash>? _applySourceControlStashCommand;

    public ProjectSourceControlSnapshot? SourceControl { get => _sourceControl; private set => SetProperty(ref _sourceControl, value); }
    public string StashMessage { get => _stashMessage; set => SetProperty(ref _stashMessage, value); }

    public AsyncRelayCommand RefreshSourceControlCommand => _refreshSourceControlCommand ??= new AsyncRelayCommand(RefreshSourceControlAsync);
    public AsyncRelayCommand<ProjectSourceControlChange> StageSourceControlCommand => _stageSourceControlCommand ??= new AsyncRelayCommand<ProjectSourceControlChange>(StageSourceControlAsync);
    public AsyncRelayCommand<ProjectSourceControlChange> UnstageSourceControlCommand => _unstageSourceControlCommand ??= new AsyncRelayCommand<ProjectSourceControlChange>(UnstageSourceControlAsync);
    public AsyncRelayCommand<ProjectGitBranch> CheckoutSourceControlBranchCommand => _checkoutSourceControlBranchCommand ??= new AsyncRelayCommand<ProjectGitBranch>(CheckoutSourceControlBranchAsync);
    public AsyncRelayCommand CreateSourceControlStashCommand => _createSourceControlStashCommand ??= new AsyncRelayCommand(CreateSourceControlStashAsync);
    public AsyncRelayCommand<ProjectGitStash> ApplySourceControlStashCommand => _applySourceControlStashCommand ??= new AsyncRelayCommand<ProjectGitStash>(ApplySourceControlStashAsync);

    private async Task RefreshSourceControlAsync()
    {
        if (!HasRoot) { SourceControl = null; return; }
        try
        {
            SourceControl = await _intelligence.GetSourceControlAsync(RootPath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Status = "Source control refresh failed: " + ex.Message;
        }
    }

    private async Task StageSourceControlAsync(ProjectSourceControlChange? change)
    {
        if (change is null || !HasRoot) return;
        try { SourceControl = await _intelligence.StageAsync(RootPath, change.Path, CancellationToken.None); Status = $"Staged {change.Path}."; await RefreshAsync(); }
        catch (Exception ex) { Status = "Could not stage file: " + ex.Message; }
    }

    private async Task UnstageSourceControlAsync(ProjectSourceControlChange? change)
    {
        if (change is null || !HasRoot) return;
        try { SourceControl = await _intelligence.UnstageAsync(RootPath, change.Path, CancellationToken.None); Status = $"Unstaged {change.Path}."; await RefreshAsync(); }
        catch (Exception ex) { Status = "Could not unstage file: " + ex.Message; }
    }

    private async Task CheckoutSourceControlBranchAsync(ProjectGitBranch? branch)
    {
        if (branch is null || branch.IsCurrent || !HasRoot) return;
        try { SourceControl = await _intelligence.CheckoutBranchAsync(RootPath, branch.Name, CancellationToken.None); Status = $"Checked out {branch.Name} through the safe Git path."; await RefreshAsync(); }
        catch (Exception ex) { Status = "Branch was not changed: " + ex.Message; }
    }

    private async Task CreateSourceControlStashAsync()
    {
        if (!HasRoot) return;
        try { SourceControl = await _intelligence.CreateStashAsync(RootPath, StashMessage, CancellationToken.None); StashMessage = string.Empty; Status = "Created a Git stash without deleting it from history."; await RefreshAsync(); }
        catch (Exception ex) { Status = "Stash was not created: " + ex.Message; }
    }

    private async Task ApplySourceControlStashAsync(ProjectGitStash? stash)
    {
        if (stash is null || !HasRoot) return;
        try { SourceControl = await _intelligence.ApplyStashAsync(RootPath, stash.Reference, CancellationToken.None); Status = $"Applied {stash.Reference}. The stash was retained for recovery."; await RefreshAsync(); }
        catch (Exception ex) { Status = "Stash was not applied: " + ex.Message; }
    }
}
