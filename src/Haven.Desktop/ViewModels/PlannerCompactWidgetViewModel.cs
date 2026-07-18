/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/PlannerCompactWidgetViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns PlannerCompactWidgetViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents planner compact widget view model and keeps its related state and behavior together.
/// </summary>
public sealed class PlannerCompactWidgetViewModel : ObservableObject
{
    /// <summary>
    /// Stores planner locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IPlannerRepository _planner;
    /// <summary>
    /// Stores proposals locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IPlannerProposalService _proposals;
    /// <summary>
    /// Stores is expanded locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isExpanded;
    /// <summary>
    /// Stores quick capture text locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _quickCaptureText = string.Empty;
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates today tasks, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<PlannerTaskItemViewModel> TodayTasks { get; }
    /// <summary>
    /// Gets or updates overdue tasks, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<PlannerTaskItemViewModel> OverdueTasks { get; }
    /// <summary>
    /// Gets or updates pending proposals, the bindable or domain state represented by this property.
    /// </summary>
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

    /// <summary>
    /// Reports whether has today tasks is true for the current state.
    /// </summary>
    public bool HasTodayTasks => TodayTasks.Count > 0;
    /// <summary>
    /// Reports whether has overdue tasks is true for the current state.
    /// </summary>
    public bool HasOverdueTasks => OverdueTasks.Count > 0;
    /// <summary>
    /// Reports whether has proposals is true for the current state.
    /// </summary>
    public bool HasProposals => PendingProposals.Count > 0;
    /// <summary>
    /// Reports whether has any content is true for the current state.
    /// </summary>
    public bool HasAnyContent => HasTodayTasks || HasOverdueTasks || HasProposals;

    /// <summary>
    /// Gets or updates toggle expand command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleExpandCommand { get; }
    /// <summary>
    /// Gets or updates quick capture command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand QuickCaptureCommand { get; }
    /// <summary>
    /// Gets or updates apply proposal command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<PlannerChangeProposal> ApplyProposalCommand { get; }
    /// <summary>
    /// Gets or updates dismiss proposal command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<PlannerChangeProposal> DismissProposalCommand { get; }
    /// <summary>
    /// Gets or updates open full planner command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand OpenFullPlannerCommand { get; }

    /// <summary>
    /// Stores open full planner requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler? OpenFullPlannerRequested;

    /// <summary>
    /// Performs refresh async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs quick capture async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs apply proposal async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ApplyProposalAsync(PlannerChangeProposal? proposal)
    {
        if (proposal is null) return;
        await _proposals.ApplyAsync(proposal, CancellationToken.None);
        PendingProposals.Remove(proposal);
        await RefreshAsync();
    }

    /// <summary>
    /// Performs the dismiss proposal step owned by this component.
    /// </summary>
    private void DismissProposal(PlannerChangeProposal? proposal)
    {
        if (proposal is null) return;
        PendingProposals.Remove(proposal);
        RaisePropertyChanged(nameof(HasProposals));
        RaisePropertyChanged(nameof(HasAnyContent));
    }
}
