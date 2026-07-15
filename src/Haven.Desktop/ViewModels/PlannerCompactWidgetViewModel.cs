using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class PlannerCompactWidgetViewModel : ObservableObject
{
    private readonly IPlannerRepository _planner;
    private readonly IPlannerProposalService _proposals;
    private bool _isExpanded;
    private string _quickCaptureText = string.Empty;
    private bool _isBusy;

    public PlannerCompactWidgetViewModel(
        IPlannerRepository planner,
        IPlannerProposalService proposals)
    {
        _planner = planner;
        _proposals = proposals;
        TodayTasks = [];
        OverdueTasks = [];
        PendingProposals = [];
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        QuickCaptureCommand = new AsyncRelayCommand(QuickCaptureAsync, () => !string.IsNullOrWhiteSpace(QuickCaptureText) && !IsBusy);
        ApplyProposalCommand = new AsyncRelayCommand<PlannerChangeProposal>(ApplyProposalAsync);
        DismissProposalCommand = new RelayCommand<PlannerChangeProposal>(_ => RaisePropertyChanged(nameof(HasAnyContent)));
        OpenFullPlannerCommand = new RelayCommand(() => OpenFullPlannerRequested?.Invoke(this, EventArgs.Empty));
    }

    public ObservableCollection<PlannerTaskItemViewModel> TodayTasks { get; }
    public ObservableCollection<PlannerTaskItemViewModel> OverdueTasks { get; }
    public ObservableCollection<PlannerChangeProposal> PendingProposals { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;
            if (value) _ = RefreshAsync();
        }
    }

    public string QuickCaptureText
    {
        get => _quickCaptureText;
        set
        {
            if (SetProperty(ref _quickCaptureText, value))
                QuickCaptureCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
                QuickCaptureCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasTodayTasks => TodayTasks.Count > 0;
    public bool HasOverdueTasks => OverdueTasks.Count > 0;
    public bool HasProposals => PendingProposals.Count > 0;
    public bool HasAnyContent => HasTodayTasks || HasOverdueTasks || HasProposals;

    public RelayCommand ToggleExpandCommand { get; }
    public AsyncRelayCommand QuickCaptureCommand { get; }
    public AsyncRelayCommand<PlannerChangeProposal> ApplyProposalCommand { get; }
    public RelayCommand<PlannerChangeProposal> DismissProposalCommand { get; }
    public RelayCommand OpenFullPlannerCommand { get; }

    public event EventHandler? OpenFullPlannerRequested;

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var defaults = await _planner.GetCollectionsAsync(false, CancellationToken.None);
            var personalId = defaults.FirstOrDefault()?.Id ?? Guid.Empty;

            var tasks = await _planner.GetTasksAsync(new PlannerTaskQuery(personalId, RangeStart: now.Date, RangeEnd: now.Date.AddDays(1)), CancellationToken.None);
            TodayTasks.Clear();
            foreach (var t in tasks.Where(t => t.Status != PlannerTaskStatus.Completed && t.Status != PlannerTaskStatus.Cancelled))
                TodayTasks.Add(new PlannerTaskItemViewModel(t, defaults.FirstOrDefault()?.Name ?? "General"));

            var allTasks = await _planner.GetTasksAsync(new PlannerTaskQuery(personalId), CancellationToken.None);
            OverdueTasks.Clear();
            foreach (var t in allTasks.Where(t => t.DueAt.HasValue && t.DueAt.Value < now && t.Status != PlannerTaskStatus.Completed))
                OverdueTasks.Add(new PlannerTaskItemViewModel(t, defaults.FirstOrDefault()?.Name ?? "General"));

            RaisePropertyChanged(nameof(HasTodayTasks));
            RaisePropertyChanged(nameof(HasOverdueTasks));
            RaisePropertyChanged(nameof(HasAnyContent));
        }
        catch { }
        finally { IsBusy = false; }
    }

    private async Task QuickCaptureAsync()
    {
        if (string.IsNullOrWhiteSpace(QuickCaptureText) || IsBusy) return;
        IsBusy = true;
        try
        {
            var defaults = await _planner.GetCollectionsAsync(false, CancellationToken.None);
            var personalId = defaults.FirstOrDefault()?.Id ?? Guid.Empty;
            var now = DateTimeOffset.UtcNow;
            var task = new PlannerTask(
                Guid.NewGuid(), personalId, null, QuickCaptureText.Trim(), string.Empty,
                PlannerPriority.None, PlannerTaskStatus.Inbox, "[]", null,
                null, null, null, null, null, 0, now, now, "UTC");
            await _planner.UpsertTaskAsync(task, CancellationToken.None);
            QuickCaptureText = string.Empty;
            await RefreshAsync();
        }
        catch { }
        finally { IsBusy = false; }
    }

    private async Task ApplyProposalAsync(PlannerChangeProposal? proposal)
    {
        if (proposal is null) return;
        await _proposals.ApplyAsync(proposal, CancellationToken.None);
        PendingProposals.Remove(proposal);
        await RefreshAsync();
    }

    private void DismissProposal(PlannerChangeProposal? proposal)
    {
        if (proposal is null) return;
        PendingProposals.Remove(proposal);
        RaisePropertyChanged(nameof(HasProposals));
        RaisePropertyChanged(nameof(HasAnyContent));
    }
}
