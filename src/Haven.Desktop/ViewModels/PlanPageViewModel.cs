/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/PlanPageViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns PlanPageViewModel, PlannerViewOption, PlannerCollectionItemViewModel, PlannerCalendarItemViewModel, PlannerTaskItemViewModel, PlannerTaskEditorViewModel, PlannerTaskParentOption, PlannerEventItemViewModel, PlannerEventEditorViewModel, PlannerProposedChangeItemViewModel, PlannerBoardColumnViewModel, PlannerCalendarDayViewModel, PlannerCalendarEntryViewModel, CalendarProviderItemViewModel, CalendarConflictItemViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents plan page view model and keeps its related state and behavior together.
/// </summary>
public sealed class PlanPageViewModel : ObservableObject, IActivatablePage, IDisposable
{
    /// <summary>
    /// Stores repository locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IPlannerRepository _repository;
    /// <summary>
    /// Stores proposals locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IPlannerProposalService _proposals;
    /// <summary>
    /// Stores sync providers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ICalendarSyncProviderRegistry _syncProviders;
    /// <summary>
    /// Stores ollama locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _ollama;
    /// <summary>
    /// Stores calendar sync timer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly DispatcherTimer _calendarSyncTimer;
    /// <summary>
    /// Stores refresh cancellation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _refreshCancellation;
    /// <summary>
    /// Stores calendar sync cancellation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _calendarSyncCancellation;
    /// <summary>
    /// Stores calendar sync running locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _calendarSyncRunning;
    /// <summary>
    /// Stores is active locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isActive;
    /// <summary>
    /// Stores selected collection locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PlannerCollectionItemViewModel? _selectedCollection;
    /// <summary>
    /// Stores selected calendar locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PlannerCalendarItemViewModel? _selectedCalendar;
    /// <summary>
    /// Stores selected view locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PlannerViewKind _selectedView = PlannerViewKind.Today;
    /// <summary>
    /// Stores anchor date locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DateTimeOffset _anchorDate = DateTimeOffset.Now;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Loading your plan…";
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;
    /// <summary>
    /// Stores new task title locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _newTaskTitle = string.Empty;
    /// <summary>
    /// Stores new task due date locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DateTimeOffset? _newTaskDueDate = DateTimeOffset.Now.Date;
    /// <summary>
    /// Stores new task priority locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PlannerPriority _newTaskPriority = PlannerPriority.None;
    /// <summary>
    /// Stores new event title locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _newEventTitle = string.Empty;
    /// <summary>
    /// Stores new event date locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DateTimeOffset? _newEventDate = DateTimeOffset.Now.Date;
    /// <summary>
    /// Stores new event start locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private TimeSpan? _newEventStart = new(9, 0, 0);
    /// <summary>
    /// Stores new event end locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private TimeSpan? _newEventEnd = new(10, 0, 0);
    /// <summary>
    /// Stores ai prompt locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _aiPrompt = string.Empty;
    /// <summary>
    /// Stores pending proposal locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PlannerChangeProposal? _pendingProposal;
    /// <summary>
    /// Stores new collection name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _newCollectionName = string.Empty;
    /// <summary>
    /// Stores selected collection name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _selectedCollectionName = string.Empty;
    /// <summary>
    /// Stores is archive collection confirming locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isArchiveCollectionConfirming;
    /// <summary>
    /// Stores task editor locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PlannerTaskEditorViewModel? _taskEditor;
    /// <summary>
    /// Stores event editor locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PlannerEventEditorViewModel? _eventEditor;

    public PlanPageViewModel(
        IPlannerRepository repository,
        IPlannerProposalService proposals,
        ICalendarSyncProviderRegistry syncProviders,
        IOllamaClient ollama)
    {
        _repository = repository;
        _proposals = proposals;
        _syncProviders = syncProviders;
        _ollama = ollama;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SelectViewCommand = new RelayCommand<PlannerViewOption>(option => { if (option is not null) SelectedView = option.Kind; });
        PreviousPeriodCommand = new RelayCommand(() => MovePeriod(-1));
        NextPeriodCommand = new RelayCommand(() => MovePeriod(1));
        TodayCommand = new RelayCommand(() => { AnchorDate = DateTimeOffset.Now; _ = RefreshAsync(); });
        CreateTaskCommand = new AsyncRelayCommand(CreateTaskAsync, () => SelectedCollection is not null && !string.IsNullOrWhiteSpace(NewTaskTitle));
        CompleteTaskCommand = new AsyncRelayCommand<PlannerTaskItemViewModel>(CompleteTaskAsync);
        StartTaskCommand = new AsyncRelayCommand<PlannerTaskItemViewModel>(StartTaskAsync);
        DeleteTaskCommand = new AsyncRelayCommand<PlannerTaskItemViewModel>(DeleteTaskAsync);
        CreateEventCommand = new AsyncRelayCommand(CreateEventAsync, CanCreateEvent);
        DeleteEventCommand = new AsyncRelayCommand<PlannerEventItemViewModel>(DeleteEventAsync);
        AskAiCommand = new AsyncRelayCommand(AskAiAsync, () => !string.IsNullOrWhiteSpace(AiPrompt));
        ApplyProposalCommand = new AsyncRelayCommand(ApplyProposalAsync, () => PendingProposal is not null);
        DismissProposalCommand = new RelayCommand(DismissProposal);
        ConnectProviderCommand = new AsyncRelayCommand<CalendarProviderItemViewModel>(ConnectProviderAsync);
        SyncProviderCommand = new AsyncRelayCommand<CalendarProviderItemViewModel>(SyncProviderAsync);
        DisconnectProviderCommand = new AsyncRelayCommand<CalendarProviderItemViewModel>(DisconnectProviderAsync);
        KeepHavenConflictCommand = new AsyncRelayCommand<CalendarConflictItemViewModel>(item => ResolveConflictAsync(item, CalendarConflictResolution.KeepHaven));
        KeepProviderConflictCommand = new AsyncRelayCommand<CalendarConflictItemViewModel>(item => ResolveConflictAsync(item, CalendarConflictResolution.KeepProvider));
        DuplicateConflictCommand = new AsyncRelayCommand<CalendarConflictItemViewModel>(item => ResolveConflictAsync(item, CalendarConflictResolution.Duplicate));
        CreateCollectionCommand = new AsyncRelayCommand(CreateCollectionAsync, () => !string.IsNullOrWhiteSpace(NewCollectionName));
        RenameCollectionCommand = new AsyncRelayCommand(RenameCollectionAsync, () => SelectedCollection is not null && !string.IsNullOrWhiteSpace(SelectedCollectionName));
        RequestArchiveCollectionCommand = new RelayCommand(() => IsArchiveCollectionConfirming = true, () => SelectedCollection is not null && Collections.Count > 1);
        CancelArchiveCollectionCommand = new RelayCommand(() => IsArchiveCollectionConfirming = false);
        ArchiveCollectionCommand = new AsyncRelayCommand(ArchiveCollectionAsync, () => SelectedCollection is not null && Collections.Count > 1);
        MoveCollectionUpCommand = new AsyncRelayCommand<PlannerCollectionItemViewModel>(item => MoveCollectionAsync(item, -1));
        MoveCollectionDownCommand = new AsyncRelayCommand<PlannerCollectionItemViewModel>(item => MoveCollectionAsync(item, 1));
        EditTaskCommand = new RelayCommand<PlannerTaskItemViewModel>(OpenTaskEditor);
        AddSubtaskCommand = new AsyncRelayCommand<PlannerTaskItemViewModel>(AddSubtaskAsync);
        CloseTaskEditorCommand = new RelayCommand(() => TaskEditor = null);
        EditEventCommand = new RelayCommand<PlannerEventItemViewModel>(OpenEventEditor);
        CloseEventEditorCommand = new RelayCommand(() => EventEditor = null);
        foreach (var provider in _syncProviders.Providers) Providers.Add(new(provider));
        _calendarSyncTimer = new DispatcherTimer(TimeSpan.FromMinutes(5), DispatcherPriority.Background,
            async (_, _) => await SyncConnectedCalendarsAsync());
        _ = RefreshAsync();
    }

    /// <summary>
    /// Gets or updates collections, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<PlannerCollectionItemViewModel> Collections { get; } = [];
    /// <summary>
    /// Gets or updates calendars, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<PlannerCalendarItemViewModel> Calendars { get; } = [];
    /// <summary>
    /// Gets or updates tasks, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<PlannerTaskItemViewModel> Tasks { get; } = [];
    /// <summary>
    /// Gets or updates events, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<PlannerEventItemViewModel> Events { get; } = [];
    /// <summary>
    /// Gets or updates board columns, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<PlannerBoardColumnViewModel> BoardColumns { get; } = [];
    /// <summary>
    /// Gets or updates calendar days, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<PlannerCalendarDayViewModel> CalendarDays { get; } = [];
    /// <summary>
    /// Gets or updates pending changes, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<PlannerProposedChangeItemViewModel> PendingChanges { get; } = [];
    /// <summary>
    /// Gets or updates providers, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<CalendarProviderItemViewModel> Providers { get; } = [];
    /// <summary>
    /// Gets or updates conflicts, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<CalendarConflictItemViewModel> Conflicts { get; } = [];
    /// <summary>
    /// Gets or updates views, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<PlannerViewOption> Views { get; } =
    [
        new(PlannerViewKind.Today, "Today"), new(PlannerViewKind.Inbox, "Inbox"), new(PlannerViewKind.Upcoming, "Upcoming"),
        new(PlannerViewKind.List, "List"), new(PlannerViewKind.Board, "Board"), new(PlannerViewKind.Day, "Day"),
        new(PlannerViewKind.Week, "Week"), new(PlannerViewKind.Month, "Month"), new(PlannerViewKind.Agenda, "Agenda")
    ];
    /// <summary>
    /// Gets or updates priorities, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<PlannerPriority> Priorities { get; } = Enum.GetValues<PlannerPriority>();

    public PlannerCollectionItemViewModel? SelectedCollection
    {
        get => _selectedCollection;
        set
        {
            if (!SetProperty(ref _selectedCollection, value)) return;
            SelectedCollectionName = value?.Name ?? string.Empty;
            IsArchiveCollectionConfirming = false;
            CreateTaskCommand.RaiseCanExecuteChanged();
            RenameCollectionCommand.RaiseCanExecuteChanged();
            RequestArchiveCollectionCommand.RaiseCanExecuteChanged();
            ArchiveCollectionCommand.RaiseCanExecuteChanged();
            _ = RefreshAsync();
        }
    }

    public PlannerCalendarItemViewModel? SelectedCalendar
    {
        get => _selectedCalendar;
        set { if (SetProperty(ref _selectedCalendar, value)) CreateEventCommand.RaiseCanExecuteChanged(); }
    }

    public PlannerViewKind SelectedView
    {
        get => _selectedView;
        set
        {
            if (!SetProperty(ref _selectedView, value)) return;
            RaisePropertyChanged(nameof(Title));
            RaisePropertyChanged(nameof(PeriodLabel));
            RaisePropertyChanged(nameof(ShowsBoard));
            RaisePropertyChanged(nameof(ShowsCalendarGrid));
            RaisePropertyChanged(nameof(ShowsStandardList));
            _ = RefreshAsync();
        }
    }

    /// <summary>
    /// Gets or updates anchor date, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset AnchorDate { get => _anchorDate; set { if (SetProperty(ref _anchorDate, value)) RaisePropertyChanged(nameof(PeriodLabel)); } }
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => SelectedView switch { PlannerViewKind.Today => "Today", PlannerViewKind.Inbox => "Inbox", PlannerViewKind.Upcoming => "Upcoming", _ => SelectedView.ToString() };
    /// <summary>
    /// Gets or updates period label, the bindable or domain state represented by this property.
    /// </summary>
    public string PeriodLabel => SelectedView switch
    {
        PlannerViewKind.Day => AnchorDate.ToString("dddd, d MMMM yyyy"),
        PlannerViewKind.Week => $"Week of {StartOfWeek(AnchorDate):d MMMM yyyy}",
        PlannerViewKind.Month => AnchorDate.ToString("MMMM yyyy"),
        _ => DateTimeOffset.Now.ToString("dddd, d MMMM")
    };
    /// <summary>
    /// Gets or updates shows board, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowsBoard => SelectedView == PlannerViewKind.Board;
    /// <summary>
    /// Gets or updates shows calendar grid, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowsCalendarGrid => SelectedView is PlannerViewKind.Week or PlannerViewKind.Month;
    /// <summary>
    /// Gets or updates shows standard list, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowsStandardList => !ShowsBoard && !ShowsCalendarGrid;
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Reports whether busy applies to the current state.
    /// </summary>
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    /// <summary>
    /// Gets or updates new task title, the bindable or domain state represented by this property.
    /// </summary>
    public string NewTaskTitle { get => _newTaskTitle; set { if (SetProperty(ref _newTaskTitle, value)) CreateTaskCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates new task due date, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? NewTaskDueDate { get => _newTaskDueDate; set => SetProperty(ref _newTaskDueDate, value); }
    /// <summary>
    /// Gets or updates new task priority, the bindable or domain state represented by this property.
    /// </summary>
    public PlannerPriority NewTaskPriority { get => _newTaskPriority; set => SetProperty(ref _newTaskPriority, value); }
    /// <summary>
    /// Gets or updates new event title, the bindable or domain state represented by this property.
    /// </summary>
    public string NewEventTitle { get => _newEventTitle; set { if (SetProperty(ref _newEventTitle, value)) CreateEventCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates new event date, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? NewEventDate { get => _newEventDate; set { if (SetProperty(ref _newEventDate, value)) CreateEventCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates new event start, the bindable or domain state represented by this property.
    /// </summary>
    public TimeSpan? NewEventStart { get => _newEventStart; set { if (SetProperty(ref _newEventStart, value)) CreateEventCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates new event end, the bindable or domain state represented by this property.
    /// </summary>
    public TimeSpan? NewEventEnd { get => _newEventEnd; set { if (SetProperty(ref _newEventEnd, value)) CreateEventCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates ai prompt, the bindable or domain state represented by this property.
    /// </summary>
    public string AiPrompt { get => _aiPrompt; set { if (SetProperty(ref _aiPrompt, value)) AskAiCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates new collection name, the bindable or domain state represented by this property.
    /// </summary>
    public string NewCollectionName { get => _newCollectionName; set { if (SetProperty(ref _newCollectionName, value)) CreateCollectionCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates selected collection name, the bindable or domain state represented by this property.
    /// </summary>
    public string SelectedCollectionName { get => _selectedCollectionName; set { if (SetProperty(ref _selectedCollectionName, value)) RenameCollectionCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Reports whether archive collection confirming applies to the current state.
    /// </summary>
    public bool IsArchiveCollectionConfirming { get => _isArchiveCollectionConfirming; private set => SetProperty(ref _isArchiveCollectionConfirming, value); }
    /// <summary>
    /// Gets or updates task editor, the bindable or domain state represented by this property.
    /// </summary>
    public PlannerTaskEditorViewModel? TaskEditor { get => _taskEditor; private set { if (SetProperty(ref _taskEditor, value)) RaisePropertyChanged(nameof(HasTaskEditor)); } }
    /// <summary>
    /// Reports whether task editor applies to the current state.
    /// </summary>
    public bool HasTaskEditor => TaskEditor is not null;
    /// <summary>
    /// Gets or updates event editor, the bindable or domain state represented by this property.
    /// </summary>
    public PlannerEventEditorViewModel? EventEditor { get => _eventEditor; private set { if (SetProperty(ref _eventEditor, value)) RaisePropertyChanged(nameof(HasEventEditor)); } }
    /// <summary>
    /// Reports whether event editor applies to the current state.
    /// </summary>
    public bool HasEventEditor => EventEditor is not null;
    public PlannerChangeProposal? PendingProposal
    {
        get => _pendingProposal;
        private set
        {
            if (!SetProperty(ref _pendingProposal, value)) return;
            RaisePropertyChanged(nameof(HasPendingProposal));
            RaisePropertyChanged(nameof(PendingProposalSummary));
            ApplyProposalCommand.RaiseCanExecuteChanged();
        }
    }
    /// <summary>
    /// Reports whether pending proposal applies to the current state.
    /// </summary>
    public bool HasPendingProposal => PendingProposal is not null;
    /// <summary>
    /// Gets or updates pending proposal summary, the bindable or domain state represented by this property.
    /// </summary>
    public string PendingProposalSummary => PendingProposal?.Summary ?? string.Empty;
    /// <summary>
    /// Reports whether conflicts applies to the current state.
    /// </summary>
    public bool HasConflicts => Conflicts.Count > 0;

    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates select view command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<PlannerViewOption> SelectViewCommand { get; }
    /// <summary>
    /// Gets or updates previous period command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand PreviousPeriodCommand { get; }
    /// <summary>
    /// Gets or updates next period command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NextPeriodCommand { get; }
    /// <summary>
    /// Gets or updates today command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand TodayCommand { get; }
    /// <summary>
    /// Creates task command with the invariants required by its callers.
    /// </summary>
    public AsyncRelayCommand CreateTaskCommand { get; }
    /// <summary>
    /// Gets or updates complete task command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<PlannerTaskItemViewModel> CompleteTaskCommand { get; }
    /// <summary>
    /// Gets or updates start task command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<PlannerTaskItemViewModel> StartTaskCommand { get; }
    /// <summary>
    /// Gets or updates delete task command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<PlannerTaskItemViewModel> DeleteTaskCommand { get; }
    /// <summary>
    /// Creates event command with the invariants required by its callers.
    /// </summary>
    public AsyncRelayCommand CreateEventCommand { get; }
    /// <summary>
    /// Gets or updates delete event command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<PlannerEventItemViewModel> DeleteEventCommand { get; }
    /// <summary>
    /// Gets or updates ask ai command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand AskAiCommand { get; }
    /// <summary>
    /// Gets or updates apply proposal command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ApplyProposalCommand { get; }
    /// <summary>
    /// Gets or updates dismiss proposal command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand DismissProposalCommand { get; }
    /// <summary>
    /// Gets or updates connect provider command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<CalendarProviderItemViewModel> ConnectProviderCommand { get; }
    /// <summary>
    /// Gets or updates sync provider command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<CalendarProviderItemViewModel> SyncProviderCommand { get; }
    /// <summary>
    /// Gets or updates disconnect provider command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<CalendarProviderItemViewModel> DisconnectProviderCommand { get; }
    /// <summary>
    /// Gets or updates keep haven conflict command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<CalendarConflictItemViewModel> KeepHavenConflictCommand { get; }
    /// <summary>
    /// Gets or updates keep provider conflict command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<CalendarConflictItemViewModel> KeepProviderConflictCommand { get; }
    /// <summary>
    /// Gets or updates duplicate conflict command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<CalendarConflictItemViewModel> DuplicateConflictCommand { get; }
    /// <summary>
    /// Creates collection command with the invariants required by its callers.
    /// </summary>
    public AsyncRelayCommand CreateCollectionCommand { get; }
    /// <summary>
    /// Gets or updates rename collection command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RenameCollectionCommand { get; }
    /// <summary>
    /// Gets or updates request archive collection command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand RequestArchiveCollectionCommand { get; }
    /// <summary>
    /// Reports whether cancel archive collection command is true for the current state.
    /// </summary>
    public RelayCommand CancelArchiveCollectionCommand { get; }
    /// <summary>
    /// Gets or updates archive collection command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ArchiveCollectionCommand { get; }
    /// <summary>
    /// Gets or updates move collection up command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<PlannerCollectionItemViewModel> MoveCollectionUpCommand { get; }
    /// <summary>
    /// Gets or updates move collection down command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<PlannerCollectionItemViewModel> MoveCollectionDownCommand { get; }
    /// <summary>
    /// Gets or updates edit task command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<PlannerTaskItemViewModel> EditTaskCommand { get; }
    /// <summary>
    /// Gets or updates add subtask command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<PlannerTaskItemViewModel> AddSubtaskCommand { get; }
    /// <summary>
    /// Gets or updates close task editor command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand CloseTaskEditorCommand { get; }
    /// <summary>
    /// Gets or updates edit event command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<PlannerEventItemViewModel> EditEventCommand { get; }
    /// <summary>
    /// Gets or updates close event editor command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand CloseEventEditorCommand { get; }

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task RefreshAsync()
        => await RefreshAsync(CancellationToken.None);

    /// <summary>
    /// Performs activate asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        _isActive = true;
        _calendarSyncTimer.Start();
        await RefreshAsync(cancellationToken);
        _ = SyncConnectedCalendarsAsync();
    }

    /// <summary>
    /// Performs the deactivate step owned by this component.
    /// </summary>
    public void Deactivate()
    {
        _isActive = false;
        _calendarSyncTimer.Stop();
        _calendarSyncCancellation?.Cancel();
        _refreshCancellation?.Cancel();
    }

    /// <summary>
    /// Performs refresh asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshAsync(CancellationToken outerCancellationToken)
    {
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(outerCancellationToken);
        var cancellationToken = _refreshCancellation.Token;
        try
        {
            IsBusy = true;
            await _repository.EnsureDefaultsAsync(cancellationToken);
            var selectedId = SelectedCollection?.Id;
            var selectedCalendarId = SelectedCalendar?.Id;
            var collections = await _repository.GetCollectionsAsync(false, cancellationToken);
            Collections.Clear();
            foreach (var collection in collections) Collections.Add(new(collection));
            _selectedCollection = Collections.FirstOrDefault(item => item.Id == selectedId) ?? Collections.FirstOrDefault();
            RaisePropertyChanged(nameof(SelectedCollection));
            SelectedCollectionName = _selectedCollection?.Name ?? string.Empty;
            CreateTaskCommand.RaiseCanExecuteChanged();
            RenameCollectionCommand.RaiseCanExecuteChanged();
            RequestArchiveCollectionCommand.RaiseCanExecuteChanged();
            ArchiveCollectionCommand.RaiseCanExecuteChanged();

            var calendars = await _repository.GetCalendarsAsync(true, cancellationToken);
            Calendars.Clear();
            foreach (var calendar in calendars.Where(calendar => calendar.Permission == CalendarPermission.Writer))
                Calendars.Add(new(calendar));
            _selectedCalendar = Calendars.FirstOrDefault(item => item.Id == selectedCalendarId)
                                ?? Calendars.FirstOrDefault(item => item.Id == PlannerDefaults.LocalCalendarId)
                                ?? Calendars.FirstOrDefault();
            RaisePropertyChanged(nameof(SelectedCalendar));
            CreateEventCommand.RaiseCanExecuteChanged();

            var (taskQuery, eventStart, eventEnd) = CreateQuery();
            var tasks = await _repository.GetTasksAsync(taskQuery, cancellationToken);
            var events = await _repository.GetEventsAsync(eventStart, eventEnd, null, cancellationToken);
            Tasks.Clear();
            foreach (var task in tasks) Tasks.Add(new(task, Collections.FirstOrDefault(collection => collection.Id == task.CollectionId)?.Name ?? "Planner"));
            Events.Clear();
            foreach (var plannerEvent in events) Events.Add(new(plannerEvent));
            var conflicts = await _repository.GetUnresolvedConflictsAsync(cancellationToken);
            Conflicts.Clear();
            foreach (var conflict in conflicts) Conflicts.Add(new(conflict));
            RaisePropertyChanged(nameof(HasConflicts));
            var accounts = await _repository.GetCalendarAccountsAsync(cancellationToken);
            foreach (var provider in Providers)
            {
                var account = accounts.Where(account => account.Provider == provider.Provider.Kind)
                    .OrderByDescending(account => account.UpdatedAt).FirstOrDefault();
                provider.Update(account?.Status ?? (provider.IsConfigured ? CalendarSyncStatus.Disconnected : CalendarSyncStatus.NotConfigured),
                    account?.StatusMessage ?? provider.Provider.ConfigurationStatus);
            }
            BuildProjections();
            Status = $"{Tasks.Count} task{(Tasks.Count == 1 ? string.Empty : "s")} · {Events.Count} event{(Events.Count == 1 ? string.Empty : "s")}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { Status = $"Planner could not refresh: {ex.Message}"; }
        finally { if (!cancellationToken.IsCancellationRequested) IsBusy = false; }
    }

    private (PlannerTaskQuery Tasks, DateTimeOffset EventStart, DateTimeOffset EventEnd) CreateQuery()
    {
        var day = LocalDay(AnchorDate);
        var collectionId = SelectedCollection?.Id;
        return SelectedView switch
        {
            PlannerViewKind.Today => (new(collectionId, RangeEnd: day.AddDays(1)), day, day.AddDays(1)),
            PlannerViewKind.Inbox => (new(collectionId, PlannerTaskStatus.Inbox), day, day.AddDays(1)),
            PlannerViewKind.Upcoming => (new(collectionId, RangeStart: day, RangeEnd: day.AddDays(14)), day, day.AddDays(14)),
            PlannerViewKind.Day => (new(collectionId, RangeStart: day, RangeEnd: day.AddDays(1)), day, day.AddDays(1)),
            PlannerViewKind.Week => Range(StartOfWeek(day), 7, collectionId),
            PlannerViewKind.Month => Range(StartOfWeek(new DateTimeOffset(day.Year, day.Month, 1, 0, 0, 0, day.Offset)), 42, collectionId),
            PlannerViewKind.Agenda => Range(day, 30, collectionId),
            PlannerViewKind.Board => (new(collectionId, IncludeCompleted: true), day.AddDays(-7), day.AddDays(30)),
            _ => (new(collectionId), day.AddDays(-7), day.AddDays(30))
        };
    }

    private static (PlannerTaskQuery, DateTimeOffset, DateTimeOffset) Range(DateTimeOffset start, int days, Guid? collectionId) =>
        (new PlannerTaskQuery(collectionId, RangeStart: start, RangeEnd: start.AddDays(days)), start, start.AddDays(days));

    /// <summary>
    /// Creates task async with the invariants required by its callers.
    /// </summary>
    private async Task CreateTaskAsync()
    {
        if (SelectedCollection is null) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            DateTimeOffset? due = null;
            if (NewTaskDueDate is not null) due = LocalDay(NewTaskDueDate.Value).AddHours(17);
            await _repository.UpsertTaskAsync(new PlannerTask(Guid.NewGuid(), SelectedCollection.Id, null, NewTaskTitle.Trim(), string.Empty,
                NewTaskPriority, PlannerTaskStatus.Planned, "[]", null, null, due, null, null, null, Tasks.Count, now, now,
                TimeZoneInfo.Local.Id), CancellationToken.None);
            NewTaskTitle = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Task could not be created: {ex.Message}"; }
    }

    /// <summary>
    /// Performs complete task asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task CompleteTaskAsync(PlannerTaskItemViewModel? item)
    {
        if (item is null) return;
        try { await _repository.CompleteTaskAsync(item.Id, DateTimeOffset.UtcNow, CancellationToken.None); await RefreshAsync(); }
        catch (Exception ex) { Status = $"Task could not be completed: {ex.Message}"; }
    }

    /// <summary>
    /// Performs start task asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task StartTaskAsync(PlannerTaskItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            await _repository.UpsertTaskAsync(item.Definition with { Status = PlannerTaskStatus.InProgress, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Task could not be started: {ex.Message}"; }
    }

    /// <summary>
    /// Performs move task to status asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task MoveTaskToStatusAsync(Guid taskId, PlannerTaskStatus status)
    {
        try
        {
            var task = await _repository.GetTaskAsync(taskId, CancellationToken.None)
                       ?? throw new InvalidOperationException("The dragged task no longer exists.");
            if (status == PlannerTaskStatus.Completed) await _repository.CompleteTaskAsync(taskId, DateTimeOffset.UtcNow, CancellationToken.None);
            else await _repository.UpsertTaskAsync(task with { Status = status, CompletedAt = null, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Task could not be moved: {ex.Message}"; }
    }

    /// <summary>
    /// Performs reschedule task asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task RescheduleTaskAsync(Guid taskId, DateTimeOffset day)
    {
        try
        {
            var task = await _repository.GetTaskAsync(taskId, CancellationToken.None)
                       ?? throw new InvalidOperationException("The dragged task no longer exists.");
            var localDay = LocalDay(day);
            var time = task.DueAt?.ToLocalTime().TimeOfDay ?? new TimeSpan(17, 0, 0);
            var dueAt = localDay.Add(time);
            var startsAt = task.StartsAt;
            if (task.StartsAt is not null && task.DueAt is not null) startsAt = dueAt - (task.DueAt.Value - task.StartsAt.Value);
            await _repository.UpsertTaskAsync(task with { StartsAt = startsAt, DueAt = dueAt, Status = PlannerTaskStatus.Planned, CompletedAt = null, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Task could not be rescheduled: {ex.Message}"; }
    }

    /// <summary>
    /// Performs reschedule event asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task RescheduleEventAsync(Guid eventId, DateTimeOffset day)
    {
        try
        {
            var item = await _repository.GetEventAsync(eventId, CancellationToken.None)
                       ?? throw new InvalidOperationException("The dragged event no longer exists.");
            if (item.IsReadOnly) throw new InvalidOperationException("Read-only provider events cannot be moved.");
            var duration = item.EndsAt - item.StartsAt;
            var startsAt = LocalDay(day).Add(item.StartsAt.ToLocalTime().TimeOfDay);
            await _repository.UpsertEventAsync(item with { StartsAt = startsAt, EndsAt = startsAt + duration, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Event could not be rescheduled: {ex.Message}"; }
    }

    /// <summary>
    /// Performs delete task asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DeleteTaskAsync(PlannerTaskItemViewModel? item)
    {
        if (item is null) return;
        try { await _repository.DeleteTaskAsync(item.Id, CancellationToken.None); if (TaskEditor?.Definition.Id == item.Id) TaskEditor = null; await RefreshAsync(); }
        catch (Exception ex) { Status = $"Task could not be deleted: {ex.Message}"; }
    }

    /// <summary>
    /// Reports whether create event applies to the current state.
    /// </summary>
    private bool CanCreateEvent() => SelectedCalendar is not null && !string.IsNullOrWhiteSpace(NewEventTitle) && NewEventDate is not null && NewEventStart is not null && NewEventEnd is not null && NewEventEnd > NewEventStart;

    /// <summary>
    /// Creates event async with the invariants required by its callers.
    /// </summary>
    private async Task CreateEventAsync()
    {
        if (!CanCreateEvent()) return;
        try
        {
            var day = LocalDay(NewEventDate!.Value);
            var startsAt = day.Add(NewEventStart!.Value);
            var endsAt = day.Add(NewEventEnd!.Value);
            var now = DateTimeOffset.UtcNow;
            await _repository.UpsertEventAsync(new PlannerEvent(Guid.NewGuid(), SelectedCalendar!.Id, NewEventTitle.Trim(), string.Empty,
                string.Empty, startsAt, endsAt, false, null, null, false, null, null, now, now, null, TimeZoneInfo.Local.Id), CancellationToken.None);
            NewEventTitle = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Event could not be created: {ex.Message}"; }
    }

    /// <summary>
    /// Performs delete event asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DeleteEventAsync(PlannerEventItemViewModel? item)
    {
        if (item is null || item.IsReadOnly) return;
        try { await _repository.DeleteEventAsync(item.Id, DateTimeOffset.UtcNow, CancellationToken.None); if (EventEditor?.Definition.Id == item.Id) EventEditor = null; await RefreshAsync(); }
        catch (Exception ex) { Status = $"Event could not be deleted: {ex.Message}"; }
    }

    /// <summary>
    /// Performs ask ai asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task AskAiAsync()
    {
        try
        {
            IsBusy = true;
            Status = "Asking a local model to draft a plan…";
            var models = await _ollama.GetModelsAsync(CancellationToken.None);
            var model = models.FirstOrDefault(item => item.Supports(ToolCapability.Tools))
                        ?? throw new InvalidOperationException("No installed local model supports structured tools.");
            var context = JsonSerializer.Serialize(new
            {
                today = DateTimeOffset.Now.ToString("O"),
                collections = Collections.Select(item => new { id = item.Id, item.Name }),
                tasks = Tasks.Take(100).Select(item => item.Definition),
                events = Events.Take(100).Select(item => item.Definition),
                localCalendarId = PlannerDefaults.LocalCalendarId
            });
            var response = await _ollama.ChatWithToolsAsync(new OllamaToolRequest(model.Name,
                [new OllamaToolTurn("user", $"Current planner state:\n{context}\n\nUser request:\n{AiPrompt.Trim()}\n\nDraft changes with planner_propose_changes. Do not claim they were applied.")],
                [_proposals.ToolDefinition], EffortLevel.Medium,
                "You are Haven Plan's local planning assistant. Preserve existing items unless asked, use ISO-8601 timestamps, and return a reviewable proposal."), CancellationToken.None);
            var call = response.ToolCalls.FirstOrDefault(item => item.Name.Equals(_proposals.ToolDefinition.Name, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("The model did not return a planner proposal.");
            var proposal = _proposals.ParseToolCall(call.Arguments);
            var validation = _proposals.Validate(proposal);
            if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
            PendingChanges.Clear();
            foreach (var change in proposal.Changes) PendingChanges.Add(new(change));
            PendingProposal = proposal;
            Status = "Review the proposal. Nothing has changed yet.";
        }
        catch (Exception ex) { Status = $"AI planning failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Performs apply proposal asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ApplyProposalAsync()
    {
        if (PendingProposal is null) return;
        try
        {
            await _proposals.ApplyAsync(PendingProposal, CancellationToken.None);
            DismissProposal();
            AiPrompt = string.Empty;
            await RefreshAsync();
            Status = "AI proposal applied.";
        }
        catch (Exception ex) { Status = $"Proposal could not be applied: {ex.Message}"; }
    }

    /// <summary>
    /// Performs the dismiss proposal step owned by this component.
    /// </summary>
    private void DismissProposal()
    {
        PendingProposal = null;
        PendingChanges.Clear();
        Status = "Proposal dismissed. No changes were made.";
    }

    /// <summary>
    /// Performs connect provider asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ConnectProviderAsync(CalendarProviderItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            var result = await item.Provider.ConnectAsync(CancellationToken.None);
            item.Update(result.Status, result.Message);
            Status = result.Message;
            if (!result.Succeeded) return;

            var accounts = await _repository.GetCalendarAccountsAsync(CancellationToken.None);
            var account = accounts.Where(account => account.Provider == item.Provider.Kind)
                .OrderByDescending(account => account.UpdatedAt).FirstOrDefault();
            if (account is null) return;
            item.Update(CalendarSyncStatus.Syncing, "Running the initial calendar sync…");
            var sync = await item.Provider.SyncAsync(new CalendarSyncRequest(account.Id, true,
                DateTimeOffset.Now.AddMonths(-1), DateTimeOffset.Now.AddMonths(12)), CancellationToken.None);
            item.Update(sync.Status, sync.Message);
            await RefreshAsync();
            Status = sync.Message;
        }
        catch (Exception ex) { Status = $"Calendar connection failed: {ex.Message}"; }
    }

    /// <summary>
    /// Performs sync provider asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SyncProviderAsync(CalendarProviderItemViewModel? item)
    {
        if (item is null) return;
        var accounts = await _repository.GetCalendarAccountsAsync(CancellationToken.None);
        var account = accounts.FirstOrDefault(account => account.Provider == item.Provider.Kind);
        if (account is null) { Status = $"Connect {item.Name} before synchronising."; return; }
        var result = await item.Provider.SyncAsync(new CalendarSyncRequest(account.Id, false, DateTimeOffset.Now.AddMonths(-1), DateTimeOffset.Now.AddMonths(12)), CancellationToken.None);
        item.Update(result.Status, result.Message);
        await RefreshAsync();
        Status = result.Message;
    }

    /// <summary>
    /// Performs disconnect provider asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DisconnectProviderAsync(CalendarProviderItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            var accounts = await _repository.GetCalendarAccountsAsync(CancellationToken.None);
            foreach (var account in accounts.Where(account => account.Provider == item.Provider.Kind))
                await item.Provider.DisconnectAsync(account.Id, CancellationToken.None);
            item.Update(CalendarSyncStatus.Disconnected, $"{item.Name} disconnected. Local calendar data is retained.");
            Status = item.Status;
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Calendar could not be disconnected: {ex.Message}"; }
    }

    /// <summary>
    /// Performs sync connected calendars asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SyncConnectedCalendarsAsync()
    {
        if (!_isActive || _calendarSyncRunning) return;
        _calendarSyncRunning = true;
        _calendarSyncCancellation?.Cancel();
        _calendarSyncCancellation?.Dispose();
        _calendarSyncCancellation = new CancellationTokenSource();
        var cancellationToken = _calendarSyncCancellation.Token;
        try
        {
            var now = DateTimeOffset.UtcNow;
            string? lastMessage = null;
            var accounts = await _repository.GetCalendarAccountsAsync(cancellationToken);
            foreach (var account in accounts.Where(account => account.Status != CalendarSyncStatus.Disconnected
                                                               && (account.LastSyncedAt is null || account.LastSyncedAt <= now.AddMinutes(-5))))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var provider = _syncProviders.Get(account.Provider);
                if (!provider.IsConfigured) continue;
                var item = Providers.FirstOrDefault(candidate => candidate.Provider.Kind == account.Provider);
                item?.Update(CalendarSyncStatus.Syncing, "Synchronising in the background…");
                var result = await provider.SyncAsync(new CalendarSyncRequest(account.Id, false,
                    DateTimeOffset.Now.AddMonths(-1), DateTimeOffset.Now.AddMonths(12)), cancellationToken);
                item?.Update(result.Status, result.Message);
                lastMessage = result.Message;
            }
            if (_isActive) await RefreshAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(lastMessage)) Status = lastMessage;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { Status = $"Background calendar sync failed: {ex.Message}"; }
        finally { _calendarSyncRunning = false; }
    }

    /// <summary>
    /// Performs resolve conflict asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ResolveConflictAsync(CalendarConflictItemViewModel? item, CalendarConflictResolution resolution)
    {
        if (item is null) return;
        try
        {
            await _repository.ResolveConflictAsync(item.Definition.Id, resolution, DateTimeOffset.UtcNow, CancellationToken.None);
            await RefreshAsync();
            Status = resolution switch
            {
                CalendarConflictResolution.KeepHaven => "Kept Haven's version; it will be sent on the next calendar sync.",
                CalendarConflictResolution.KeepProvider => "Kept the calendar provider's version.",
                _ => "Kept the provider version and saved the Haven edit as a private local copy."
            };
        }
        catch (Exception ex) { Status = $"Calendar conflict could not be resolved: {ex.Message}"; }
    }

    /// <summary>
    /// Creates collection async with the invariants required by its callers.
    /// </summary>
    private async Task CreateCollectionAsync()
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var collection = new PlannerCollection(Guid.NewGuid(), NewCollectionName.Trim(), Collections.Count, false, now, now);
            await _repository.UpsertCollectionAsync(collection, CancellationToken.None);
            NewCollectionName = string.Empty;
            await RefreshAsync();
            SelectedCollection = Collections.FirstOrDefault(item => item.Id == collection.Id);
            Status = $"Created {collection.Name}.";
        }
        catch (Exception ex) { Status = $"Collection could not be created: {ex.Message}"; }
    }

    /// <summary>
    /// Performs rename collection asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RenameCollectionAsync()
    {
        if (SelectedCollection is null) return;
        try
        {
            await _repository.UpsertCollectionAsync(SelectedCollection.Definition with { Name = SelectedCollectionName.Trim(), UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
            await RefreshAsync();
            Status = "Collection renamed.";
        }
        catch (Exception ex) { Status = $"Collection could not be renamed: {ex.Message}"; }
    }

    /// <summary>
    /// Performs archive collection asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ArchiveCollectionAsync()
    {
        if (SelectedCollection is null || Collections.Count <= 1) return;
        try
        {
            var name = SelectedCollection.Name;
            await _repository.ArchiveCollectionAsync(SelectedCollection.Id, true, CancellationToken.None);
            IsArchiveCollectionConfirming = false;
            _selectedCollection = null;
            await RefreshAsync();
            Status = $"Archived {name}. Its tasks remain stored locally.";
        }
        catch (Exception ex) { Status = $"Collection could not be archived: {ex.Message}"; }
    }

    /// <summary>
    /// Performs move collection asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task MoveCollectionAsync(PlannerCollectionItemViewModel? item, int direction)
    {
        if (item is null) return;
        var index = Collections.IndexOf(item);
        var targetIndex = index + direction;
        if (index < 0 || targetIndex < 0 || targetIndex >= Collections.Count) return;
        try
        {
            var target = Collections[targetIndex];
            var now = DateTimeOffset.UtcNow;
            await _repository.UpsertCollectionAsync(item.Definition with { SortOrder = target.Definition.SortOrder, UpdatedAt = now }, CancellationToken.None);
            await _repository.UpsertCollectionAsync(target.Definition with { SortOrder = item.Definition.SortOrder, UpdatedAt = now }, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception ex) { Status = $"Collection could not be reordered: {ex.Message}"; }
    }

    /// <summary>
    /// Performs the open task editor step owned by this component.
    /// </summary>
    private void OpenTaskEditor(PlannerTaskItemViewModel? item)
    {
        if (item is null) return;
        EventEditor = null;
        TaskEditor = new PlannerTaskEditorViewModel(item.Definition, _repository, async () => { TaskEditor = null; await RefreshAsync(); });
    }

    /// <summary>
    /// Performs add subtask asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task AddSubtaskAsync(PlannerTaskItemViewModel? parent)
    {
        if (parent is null) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var task = new PlannerTask(Guid.NewGuid(), parent.Definition.CollectionId, parent.Id, "New subtask", string.Empty,
                parent.Definition.Priority, PlannerTaskStatus.Planned, "[]", null, null, parent.Definition.DueAt, null, null, null,
                0, now, now, parent.Definition.TimeZoneId);
            await _repository.UpsertTaskAsync(task, CancellationToken.None);
            TaskEditor = new PlannerTaskEditorViewModel(task, _repository, async () => { TaskEditor = null; await RefreshAsync(); });
            await RefreshAsync();
            Status = "Subtask created. Add its details in the editor.";
        }
        catch (Exception ex) { Status = $"Subtask could not be created: {ex.Message}"; }
    }

    /// <summary>
    /// Performs the open event editor step owned by this component.
    /// </summary>
    private void OpenEventEditor(PlannerEventItemViewModel? item)
    {
        if (item is null) return;
        TaskEditor = null;
        EventEditor = new PlannerEventEditorViewModel(item.Definition, _repository, async () => { EventEditor = null; await RefreshAsync(); });
    }

    /// <summary>
    /// Performs the move period step owned by this component.
    /// </summary>
    private void MovePeriod(int direction)
    {
        AnchorDate = SelectedView switch
        {
            PlannerViewKind.Day => AnchorDate.AddDays(direction),
            PlannerViewKind.Week => AnchorDate.AddDays(7 * direction),
            PlannerViewKind.Month => AnchorDate.AddMonths(direction),
            _ => AnchorDate.AddDays(7 * direction)
        };
        _ = RefreshAsync();
    }

    /// <summary>
    /// Builds projections from the currently available inputs.
    /// </summary>
    private void BuildProjections()
    {
        BoardColumns.Clear();
        if (ShowsBoard)
        {
            foreach (var status in new[] { PlannerTaskStatus.Inbox, PlannerTaskStatus.Planned, PlannerTaskStatus.InProgress, PlannerTaskStatus.Completed })
                BoardColumns.Add(new(status, Tasks.Where(task => task.Definition.Status == status).ToArray()));
        }

        CalendarDays.Clear();
        if (!ShowsCalendarGrid) return;
        var first = SelectedView == PlannerViewKind.Week
            ? StartOfWeek(AnchorDate)
            : StartOfWeek(new DateTimeOffset(AnchorDate.Year, AnchorDate.Month, 1, 0, 0, 0, AnchorDate.Offset));
        var count = SelectedView == PlannerViewKind.Week ? 7 : 42;
        for (var offset = 0; offset < count; offset++)
        {
            var day = first.AddDays(offset);
            var next = day.AddDays(1);
            var taskItems = Tasks.Where(task => task.Definition.DueAt >= day && task.Definition.DueAt < next)
                .Select(task => new PlannerCalendarEntryViewModel(task.Id, task.Title, task.Definition.DueAt?.ToLocalTime().ToString("HH:mm") ?? string.Empty, false));
            var eventItems = Events.Where(item => item.Definition.StartsAt < next && item.Definition.EndsAt > day)
                .Select(item => new PlannerCalendarEntryViewModel(item.Id, item.Title, item.Definition.IsAllDay ? "All day" : item.Definition.StartsAt.ToLocalTime().ToString("HH:mm"), true));
            CalendarDays.Add(new(day, day.Month == AnchorDate.Month, taskItems.Concat(eventItems).OrderBy(item => item.Time).ToArray()));
        }
    }

    /// <summary>
    /// Performs the local day step owned by this component.
    /// </summary>
    private static DateTimeOffset LocalDay(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(local.Date));
    }

    /// <summary>
    /// Performs the start of week step owned by this component.
    /// </summary>
    private static DateTimeOffset StartOfWeek(DateTimeOffset value)
    {
        var day = LocalDay(value);
        var delta = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-delta);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        _isActive = false;
        _calendarSyncTimer.Stop();
        _calendarSyncCancellation?.Cancel();
        _calendarSyncCancellation?.Dispose();
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
    }
}

/// <summary>
/// Represents planner view option and keeps its related state and behavior together.
/// </summary>
public sealed record PlannerViewOption(PlannerViewKind Kind, string Name);

/// <summary>
/// Represents planner collection item view model and keeps its related state and behavior together.
/// </summary>
public sealed class PlannerCollectionItemViewModel(PlannerCollection definition)
{
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public PlannerCollection Definition => definition;
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id => definition.Id;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => definition.Name;
}

/// <summary>
/// Represents planner calendar item view model and keeps its related state and behavior together.
/// </summary>
public sealed class PlannerCalendarItemViewModel(PlannerCalendar definition)
{
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public PlannerCalendar Definition => definition;
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id => definition.Id;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => definition.Provider == CalendarProviderKind.Local ? definition.Name : $"{definition.Name} · {definition.Provider}";
}

/// <summary>
/// Represents planner task item view model and keeps its related state and behavior together.
/// </summary>
public sealed class PlannerTaskItemViewModel(PlannerTask definition, string collectionName)
{
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public PlannerTask Definition => definition;
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id => definition.Id;
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => definition.Title;
    /// <summary>
    /// Gets or updates collection name, the bindable or domain state represented by this property.
    /// </summary>
    public string CollectionName => collectionName;
    /// <summary>
    /// Gets or updates meta, the bindable or domain state represented by this property.
    /// </summary>
    public string Meta => string.Join(" · ", new[]
    {
        definition.Priority == PlannerPriority.None ? null : definition.Priority.ToString(),
        definition.DueAt is null ? null : definition.DueAt.Value.ToLocalTime().ToString("ddd d MMM, HH:mm"),
        definition.EstimatedMinutes is null ? null : $"{definition.EstimatedMinutes} min"
    }.Where(value => value is not null));
    /// <summary>
    /// Reports whether recurring applies to the current state.
    /// </summary>
    public bool IsRecurring => !string.IsNullOrWhiteSpace(definition.RecurrenceRule);
}

/// <summary>
/// Represents planner task editor view model and keeps its related state and behavior together.
/// </summary>
public sealed class PlannerTaskEditorViewModel : ObservableObject
{
    /// <summary>
    /// Stores repository locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IPlannerRepository _repository;
    /// <summary>
    /// Stores saved locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Func<Task> _saved;
    /// <summary>
    /// Stores title locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _title;
    /// <summary>
    /// Stores notes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _notes;
    /// <summary>
    /// Stores tags locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _tags;
    /// <summary>
    /// Stores estimated minutes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _estimatedMinutes;
    /// <summary>
    /// Stores priority locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PlannerPriority _priority;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PlannerTaskStatus _status;
    /// <summary>
    /// Stores starts at locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DateTimeOffset? _startsAt;
    /// <summary>
    /// Stores due at locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DateTimeOffset? _dueAt;
    /// <summary>
    /// Stores reminder at locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DateTimeOffset? _reminderAt;
    /// <summary>
    /// Stores recurrence rule locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _recurrenceRule;
    /// <summary>
    /// Stores time zone id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _timeZoneId;
    /// <summary>
    /// Stores selected parent locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PlannerTaskParentOption? _selectedParent;
    /// <summary>
    /// Stores message locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _message = "Edit the task details, then save.";

    public PlannerTaskEditorViewModel(PlannerTask definition, IPlannerRepository repository, Func<Task> saved)
    {
        Definition = definition;
        _repository = repository;
        _saved = saved;
        _title = definition.Title;
        _notes = definition.Notes;
        _tags = ReadTags(definition.TagsJson);
        _estimatedMinutes = definition.EstimatedMinutes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        _priority = definition.Priority;
        _status = definition.Status;
        _startsAt = definition.StartsAt;
        _dueAt = definition.DueAt;
        _reminderAt = definition.ReminderAt;
        _recurrenceRule = definition.RecurrenceRule ?? string.Empty;
        _timeZoneId = definition.TimeZoneId;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !string.IsNullOrWhiteSpace(Title));
        ClearParentCommand = new RelayCommand(() => SelectedParent = Parents.FirstOrDefault(parent => parent.Id is null));
        _ = LoadParentsAsync();
    }

    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public PlannerTask Definition { get; }
    /// <summary>
    /// Gets or updates priorities, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<PlannerPriority> Priorities { get; } = Enum.GetValues<PlannerPriority>();
    /// <summary>
    /// Gets or updates statuses, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<PlannerTaskStatus> Statuses { get; } = Enum.GetValues<PlannerTaskStatus>();
    /// <summary>
    /// Gets or updates parents, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<PlannerTaskParentOption> Parents { get; } = [];
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title { get => _title; set { if (SetProperty(ref _title, value)) SaveCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates notes, the bindable or domain state represented by this property.
    /// </summary>
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    /// <summary>
    /// Gets or updates tags, the bindable or domain state represented by this property.
    /// </summary>
    public string Tags { get => _tags; set => SetProperty(ref _tags, value); }
    /// <summary>
    /// Gets or updates estimated minutes, the bindable or domain state represented by this property.
    /// </summary>
    public string EstimatedMinutes { get => _estimatedMinutes; set => SetProperty(ref _estimatedMinutes, value); }
    /// <summary>
    /// Gets or updates priority, the bindable or domain state represented by this property.
    /// </summary>
    public PlannerPriority Priority { get => _priority; set => SetProperty(ref _priority, value); }
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public PlannerTaskStatus Status { get => _status; set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates starts at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? StartsAt { get => _startsAt; set => SetProperty(ref _startsAt, value); }
    /// <summary>
    /// Gets or updates due at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? DueAt { get => _dueAt; set => SetProperty(ref _dueAt, value); }
    /// <summary>
    /// Gets or updates reminder at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? ReminderAt { get => _reminderAt; set => SetProperty(ref _reminderAt, value); }
    /// <summary>
    /// Gets or updates recurrence rule, the bindable or domain state represented by this property.
    /// </summary>
    public string RecurrenceRule { get => _recurrenceRule; set => SetProperty(ref _recurrenceRule, value); }
    /// <summary>
    /// Gets or updates time zone id, the bindable or domain state represented by this property.
    /// </summary>
    public string TimeZoneId { get => _timeZoneId; set => SetProperty(ref _timeZoneId, value); }
    /// <summary>
    /// Gets or updates selected parent, the bindable or domain state represented by this property.
    /// </summary>
    public PlannerTaskParentOption? SelectedParent { get => _selectedParent; set => SetProperty(ref _selectedParent, value); }
    /// <summary>
    /// Gets or updates message, the bindable or domain state represented by this property.
    /// </summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    /// <summary>
    /// Gets or updates save command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SaveCommand { get; }
    /// <summary>
    /// Gets or updates clear parent command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ClearParentCommand { get; }

    /// <summary>
    /// Performs load parents asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task LoadParentsAsync()
    {
        try
        {
            var tasks = await _repository.GetTasksAsync(new PlannerTaskQuery(Definition.CollectionId, IncludeCompleted: true), CancellationToken.None);
            Parents.Clear();
            Parents.Add(new(null, "No parent"));
            foreach (var task in tasks.Where(task => task.Id != Definition.Id)) Parents.Add(new(task.Id, task.Title));
            SelectedParent = Parents.FirstOrDefault(parent => parent.Id == Definition.ParentTaskId) ?? Parents[0];
        }
        catch (Exception ex) { Message = $"Parent tasks could not be loaded: {ex.Message}"; }
    }

    /// <summary>
    /// Performs save asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SaveAsync()
    {
        try
        {
            int? estimate = null;
            if (!string.IsNullOrWhiteSpace(EstimatedMinutes))
            {
                if (!int.TryParse(EstimatedMinutes.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
                    throw new InvalidOperationException("Estimate must be a non-negative number of minutes.");
                estimate = parsed;
            }
            var tags = Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var now = DateTimeOffset.UtcNow;
            var completing = Status == PlannerTaskStatus.Completed && Definition.Status != PlannerTaskStatus.Completed;
            var updated = Definition with
            {
                ParentTaskId = SelectedParent?.Id,
                Title = Title.Trim(),
                Notes = Notes.Trim(),
                TagsJson = JsonSerializer.Serialize(tags),
                EstimatedMinutes = estimate,
                Priority = Priority,
                Status = completing ? Definition.Status : Status,
                StartsAt = StartsAt,
                DueAt = DueAt,
                ReminderAt = ReminderAt,
                RecurrenceRule = string.IsNullOrWhiteSpace(RecurrenceRule) ? null : RecurrenceRule.Trim(),
                TimeZoneId = string.IsNullOrWhiteSpace(TimeZoneId) ? TimeZoneInfo.Local.Id : TimeZoneId.Trim(),
                CompletedAt = Status == PlannerTaskStatus.Completed ? Definition.CompletedAt : null,
                UpdatedAt = now
            };
            await _repository.UpsertTaskAsync(updated, CancellationToken.None);
            if (completing) await _repository.CompleteTaskAsync(updated.Id, now, CancellationToken.None);
            Message = "Task saved.";
            await _saved();
        }
        catch (Exception ex) { Message = $"Task could not be saved: {ex.Message}"; }
    }

    /// <summary>
    /// Performs the read tags step owned by this component.
    /// </summary>
    private static string ReadTags(string json)
    {
        try { return string.Join(", ", JsonSerializer.Deserialize<string[]>(json) ?? []); }
        catch (JsonException) { return string.Empty; }
    }
}

/// <summary>
/// Represents planner task parent option and keeps its related state and behavior together.
/// </summary>
public sealed record PlannerTaskParentOption(Guid? Id, string Name);

/// <summary>
/// Represents planner event item view model and keeps its related state and behavior together.
/// </summary>
public sealed class PlannerEventItemViewModel(PlannerEvent definition)
{
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public PlannerEvent Definition => definition;
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id => definition.Id;
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => definition.Title;
    /// <summary>
    /// Gets or updates time, the bindable or domain state represented by this property.
    /// </summary>
    public string Time => definition.IsAllDay ? "All day" : $"{definition.StartsAt.ToLocalTime():ddd d MMM, HH:mm}–{definition.EndsAt.ToLocalTime():HH:mm}";
    /// <summary>
    /// Gets or updates detail, the bindable or domain state represented by this property.
    /// </summary>
    public string Detail => string.IsNullOrWhiteSpace(definition.Location) ? Time : $"{Time} · {definition.Location}";
    /// <summary>
    /// Reports whether read only applies to the current state.
    /// </summary>
    public bool IsReadOnly => definition.IsReadOnly;
    /// <summary>
    /// Reports whether delete applies to the current state.
    /// </summary>
    public bool CanDelete => !definition.IsReadOnly;
}

/// <summary>
/// Represents planner event editor view model and keeps its related state and behavior together.
/// </summary>
public sealed class PlannerEventEditorViewModel : ObservableObject
{
    /// <summary>
    /// Stores repository locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IPlannerRepository _repository;
    /// <summary>
    /// Stores saved locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Func<Task> _saved;
    /// <summary>
    /// Stores title locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _title;
    /// <summary>
    /// Stores notes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _notes;
    /// <summary>
    /// Stores location locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _location;
    /// <summary>
    /// Stores date locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DateTimeOffset _date;
    /// <summary>
    /// Stores start time locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private TimeSpan? _startTime;
    /// <summary>
    /// Stores end time locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private TimeSpan? _endTime;
    /// <summary>
    /// Stores is all day locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isAllDay;
    /// <summary>
    /// Stores reminder at locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DateTimeOffset? _reminderAt;
    /// <summary>
    /// Stores recurrence rule locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _recurrenceRule;
    /// <summary>
    /// Stores time zone id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _timeZoneId;
    /// <summary>
    /// Stores message locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _message;

    public PlannerEventEditorViewModel(PlannerEvent definition, IPlannerRepository repository, Func<Task> saved)
    {
        Definition = definition;
        _repository = repository;
        _saved = saved;
        _title = definition.Title;
        _notes = definition.Notes;
        _location = definition.Location;
        _date = definition.StartsAt.ToLocalTime().Date;
        _startTime = definition.StartsAt.ToLocalTime().TimeOfDay;
        _endTime = definition.EndsAt.ToLocalTime().TimeOfDay;
        _isAllDay = definition.IsAllDay;
        _reminderAt = definition.ReminderAt;
        _recurrenceRule = definition.RecurrenceRule ?? string.Empty;
        _timeZoneId = definition.TimeZoneId;
        _message = definition.IsReadOnly ? "This provider event is read-only." : "Edit the event details, then save.";
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !definition.IsReadOnly && !string.IsNullOrWhiteSpace(Title));
    }

    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public PlannerEvent Definition { get; }
    /// <summary>
    /// Reports whether read only applies to the current state.
    /// </summary>
    public bool IsReadOnly => Definition.IsReadOnly;
    /// <summary>
    /// Reports whether save applies to the current state.
    /// </summary>
    public bool CanSave => !Definition.IsReadOnly;
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title { get => _title; set { if (SetProperty(ref _title, value)) SaveCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates notes, the bindable or domain state represented by this property.
    /// </summary>
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
    /// <summary>
    /// Gets or updates location, the bindable or domain state represented by this property.
    /// </summary>
    public string Location { get => _location; set => SetProperty(ref _location, value); }
    /// <summary>
    /// Gets or updates date, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset Date { get => _date; set => SetProperty(ref _date, value); }
    /// <summary>
    /// Gets or updates start time, the bindable or domain state represented by this property.
    /// </summary>
    public TimeSpan? StartTime { get => _startTime; set => SetProperty(ref _startTime, value); }
    /// <summary>
    /// Gets or updates end time, the bindable or domain state represented by this property.
    /// </summary>
    public TimeSpan? EndTime { get => _endTime; set => SetProperty(ref _endTime, value); }
    /// <summary>
    /// Reports whether all day applies to the current state.
    /// </summary>
    public bool IsAllDay { get => _isAllDay; set => SetProperty(ref _isAllDay, value); }
    /// <summary>
    /// Gets or updates reminder at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? ReminderAt { get => _reminderAt; set => SetProperty(ref _reminderAt, value); }
    /// <summary>
    /// Gets or updates recurrence rule, the bindable or domain state represented by this property.
    /// </summary>
    public string RecurrenceRule { get => _recurrenceRule; set => SetProperty(ref _recurrenceRule, value); }
    /// <summary>
    /// Gets or updates time zone id, the bindable or domain state represented by this property.
    /// </summary>
    public string TimeZoneId { get => _timeZoneId; set => SetProperty(ref _timeZoneId, value); }
    /// <summary>
    /// Gets or updates message, the bindable or domain state represented by this property.
    /// </summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    /// <summary>
    /// Gets or updates save command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SaveCommand { get; }

    /// <summary>
    /// Performs save asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SaveAsync()
    {
        try
        {
            if (StartTime is null || EndTime is null) throw new InvalidOperationException("Start and end times are required.");
            var localDate = Date.ToLocalTime();
            var day = new DateTimeOffset(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(localDate.Date));
            var startsAt = day.Add(StartTime.Value);
            var endsAt = day.Add(EndTime.Value);
            if (endsAt <= startsAt) endsAt = endsAt.AddDays(1);
            await _repository.UpsertEventAsync(Definition with
            {
                Title = Title.Trim(),
                Notes = Notes.Trim(),
                Location = Location.Trim(),
                StartsAt = startsAt,
                EndsAt = endsAt,
                IsAllDay = IsAllDay,
                ReminderAt = ReminderAt,
                RecurrenceRule = string.IsNullOrWhiteSpace(RecurrenceRule) ? null : RecurrenceRule.Trim(),
                TimeZoneId = string.IsNullOrWhiteSpace(TimeZoneId) ? TimeZoneInfo.Local.Id : TimeZoneId.Trim(),
                UpdatedAt = DateTimeOffset.UtcNow
            }, CancellationToken.None);
            Message = "Event saved.";
            await _saved();
        }
        catch (Exception ex) { Message = $"Event could not be saved: {ex.Message}"; }
    }
}

/// <summary>
/// Represents planner proposed change item view model and keeps its related state and behavior together.
/// </summary>
public sealed class PlannerProposedChangeItemViewModel(PlannerProposedChange definition)
{
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public string Kind => definition.Kind.ToString();
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description => definition.Description;
}

/// <summary>
/// Represents planner board column view model and keeps its related state and behavior together.
/// </summary>
public sealed class PlannerBoardColumnViewModel(PlannerTaskStatus status, IReadOnlyList<PlannerTaskItemViewModel> tasks)
{
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public PlannerTaskStatus Status => status;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => status switch { PlannerTaskStatus.InProgress => "In progress", _ => status.ToString() };
    /// <summary>
    /// Gets or updates tasks, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<PlannerTaskItemViewModel> Tasks => tasks;
    /// <summary>
    /// Gets or updates count label, the bindable or domain state represented by this property.
    /// </summary>
    public string CountLabel => tasks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Represents planner calendar day view model and keeps its related state and behavior together.
/// </summary>
public sealed class PlannerCalendarDayViewModel(DateTimeOffset date, bool isCurrentMonth, IReadOnlyList<PlannerCalendarEntryViewModel> entries)
{
    /// <summary>
    /// Gets or updates date, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset Date => date;
    /// <summary>
    /// Gets or updates day name, the bindable or domain state represented by this property.
    /// </summary>
    public string DayName => date.ToString("ddd");
    /// <summary>
    /// Gets or updates day number, the bindable or domain state represented by this property.
    /// </summary>
    public string DayNumber => date.Day.ToString(System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>
    /// Reports whether current month applies to the current state.
    /// </summary>
    public bool IsCurrentMonth => isCurrentMonth;
    /// <summary>
    /// Reports whether today applies to the current state.
    /// </summary>
    public bool IsToday => date.Date == DateTimeOffset.Now.Date;
    /// <summary>
    /// Gets or updates entries, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<PlannerCalendarEntryViewModel> Entries => entries;
}

/// <summary>
/// Represents planner calendar entry view model and keeps its related state and behavior together.
/// </summary>
public sealed record PlannerCalendarEntryViewModel(Guid Id, string Title, string Time, bool IsEvent);

/// <summary>
/// Represents calendar provider item view model and keeps its related state and behavior together.
/// </summary>
public sealed class CalendarProviderItemViewModel : ObservableObject
{
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status;
    /// <summary>
    /// Stores sync status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CalendarSyncStatus _syncStatus;

    public CalendarProviderItemViewModel(ICalendarSyncProvider provider)
    {
        Provider = provider;
        _status = provider.ConfigurationStatus;
        _syncStatus = provider.IsConfigured ? CalendarSyncStatus.Disconnected : CalendarSyncStatus.NotConfigured;
    }

    /// <summary>
    /// Gets or updates provider, the bindable or domain state represented by this property.
    /// </summary>
    public ICalendarSyncProvider Provider { get; }
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => Provider.Kind + " Calendar";
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates sync status, the bindable or domain state represented by this property.
    /// </summary>
    public CalendarSyncStatus SyncStatus { get => _syncStatus; private set => SetProperty(ref _syncStatus, value); }
    /// <summary>
    /// Reports whether configured applies to the current state.
    /// </summary>
    public bool IsConfigured => Provider.IsConfigured;

    /// <summary>
    /// Performs the update step owned by this component.
    /// </summary>
    public void Update(CalendarSyncStatus status, string message) { SyncStatus = status; Status = message; }
}

/// <summary>
/// Represents calendar conflict item view model and keeps its related state and behavior together.
/// </summary>
public sealed class CalendarConflictItemViewModel
{
    /// <summary>
    /// Stores haven locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly PlannerEvent? _haven;
    /// <summary>
    /// Stores provider locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly PlannerEvent? _provider;

    public CalendarConflictItemViewModel(CalendarConflict definition)
    {
        Definition = definition;
        try { _haven = JsonSerializer.Deserialize<PlannerEvent>(definition.HavenSnapshotJson); } catch (JsonException) { }
        try { _provider = JsonSerializer.Deserialize<PlannerEvent>(definition.ProviderSnapshotJson); } catch (JsonException) { }
    }

    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public CalendarConflict Definition { get; }
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => _haven?.Title ?? _provider?.Title ?? "Calendar event conflict";
    /// <summary>
    /// Gets or updates detected, the bindable or domain state represented by this property.
    /// </summary>
    public string Detected => $"Detected {Definition.DetectedAt.ToLocalTime():g}";
    /// <summary>
    /// Gets or updates haven version, the bindable or domain state represented by this property.
    /// </summary>
    public string HavenVersion => _haven is null
        ? "Haven version unavailable"
        : $"Haven: {_haven.StartsAt.ToLocalTime():g} · {_haven.Title}";
    /// <summary>
    /// Gets or updates provider version, the bindable or domain state represented by this property.
    /// </summary>
    public string ProviderVersion => _provider is null
        ? "Provider version unavailable"
        : _provider.DeletedAt is not null
            ? "Provider: event was deleted"
            : $"Provider: {_provider.StartsAt.ToLocalTime():g} · {_provider.Title}";
}
