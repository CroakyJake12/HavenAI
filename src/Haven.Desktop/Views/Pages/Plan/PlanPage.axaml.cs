using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Components.Buttons;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.Plan;

/// <summary>
/// Plan page. Manages tasks, events, collections, calendar sync, and AI planning
/// directly from repositories with HavenEventBus for event wiring.
/// </summary>
public sealed partial class PlanPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly IPlannerRepository _repository;
    private readonly IPlannerProposalService _proposals;
    private readonly ICalendarSyncProviderRegistry _syncProviders;
    private readonly IOllamaClient _ollama;

    private readonly DispatcherTimer _calendarSyncTimer;
    private CancellationTokenSource? _refreshCancellation;
    private CancellationTokenSource? _calendarSyncCancellation;
    private bool _calendarSyncRunning;
    private bool _isRefreshing;
    private bool _refreshQueued;
    private bool _isActive;
    private bool _compactViewsOpen;
    private bool _compactInspectorOpen;

    private PlannerCollectionItemViewModel? _selectedCollection;
    private PlannerCalendarItemViewModel? _selectedCalendar;
    private PlannerViewKind _selectedView = PlannerViewKind.Today;
    private DateTimeOffset _anchorDate = DateTimeOffset.Now;
    private PlannerTaskEditorViewModel? _taskEditor;
    private PlannerEventEditorViewModel? _eventEditor;
    private PlannerChangeProposal? _pendingProposal;
    private readonly ObservableCollection<PlannerCollectionItemViewModel> _collections = [];
    private readonly ObservableCollection<PlannerCalendarItemViewModel> _calendars = [];
    private readonly ObservableCollection<PlannerTaskItemViewModel> _tasks = [];
    private readonly ObservableCollection<PlannerEventItemViewModel> _events = [];
    private readonly ObservableCollection<PlannerBoardColumnViewModel> _boardColumns = [];
    private readonly ObservableCollection<PlannerCalendarDayViewModel> _calendarDays = [];
    private readonly ObservableCollection<PlannerProposedChangeItemViewModel> _pendingChanges = [];
    private readonly ObservableCollection<CalendarProviderItemViewModel> _providers = [];
    private readonly ObservableCollection<CalendarConflictItemViewModel> _conflicts = [];
    private readonly Dictionary<PlannerViewKind, HavenNavigationButton> _viewButtons = [];

    private static readonly IReadOnlyList<PlannerViewOption> Views =
    [
        new(PlannerViewKind.Today, "Today"), new(PlannerViewKind.Inbox, "Inbox"), new(PlannerViewKind.Upcoming, "Upcoming"),
        new(PlannerViewKind.List, "List"), new(PlannerViewKind.Board, "Board"), new(PlannerViewKind.Day, "Day"),
        new(PlannerViewKind.Week, "Week"), new(PlannerViewKind.Month, "Month"), new(PlannerViewKind.Agenda, "Agenda")
    ];

    private static readonly IReadOnlyList<PlannerPriority> Priorities = Enum.GetValues<PlannerPriority>();

    public PlanPage(
        HavenEventBus bus,
        IPlannerRepository repository,
        IPlannerProposalService proposals,
        ICalendarSyncProviderRegistry syncProviders,
        IOllamaClient ollama)
    {
        _bus = bus;
        _repository = repository;
        _proposals = proposals;
        _syncProviders = syncProviders;
        _ollama = ollama;

        InitializeComponent();
        WireEvents();
        PopulateStaticCombos();
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        CompactViewsButton.Click += (_, _) =>
        {
            _compactViewsOpen = !_compactViewsOpen;
            _compactInspectorOpen = false;
            ApplyResponsiveLayout(Bounds.Width);
        };
        CompactInspectorButton.Click += (_, _) =>
        {
            _compactInspectorOpen = !_compactInspectorOpen;
            _compactViewsOpen = false;
            ApplyResponsiveLayout(Bounds.Width);
        };

        _calendarSyncTimer = new DispatcherTimer(TimeSpan.FromMinutes(5), DispatcherPriority.Background,
            async (_, _) => await SyncConnectedCalendarsAsync());
        foreach (var provider in _syncProviders.Providers)
            _providers.Add(new CalendarProviderItemViewModel(provider));
    }

    public async Task ActivateAsync(CancellationToken ct)
    {
        _isActive = true;
        _calendarSyncTimer.Start();
        await RefreshAsync(ct);
        _ = SyncConnectedCalendarsAsync();
    }

    public void Deactivate()
    {
        _isActive = false;
        _calendarSyncTimer.Stop();
        _calendarSyncCancellation?.Cancel();
        _refreshCancellation?.Cancel();
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;
        var medium = width < 1180;
        var narrow = width < 900;
        CompactViewsButton.IsVisible = medium;
        CompactInspectorButton.IsVisible = medium;

        if (!medium)
        {
            ViewsRail.IsVisible = true;
            PlannerInspector.IsVisible = true;
            RootGrid.ColumnDefinitions = new ColumnDefinitions("240,*,360");
            return;
        }

        ViewsRail.IsVisible = _compactViewsOpen;
        PlannerInspector.IsVisible = !narrow || _compactInspectorOpen;
        RootGrid.ColumnDefinitions = new ColumnDefinitions(
            $"{(_compactViewsOpen ? 240 : 0)},*,{(PlannerInspector.IsVisible ? (narrow ? 320 : 340) : 0)}");
    }

    // ============================================================
    //  EVENT WIRING
    // ============================================================

    private void WireEvents()
    {
        WireViewButtons();
        WireCollectionButtons();
        WireTaskCreation();
        WireEventCreation();
        WireTaskEditor();
        WireEventEditor();
        WireAiSection();
        WireCalendarProviders();
        WireDragDrop();
    }

    private void WireViewButtons()
    {
        for (int i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            var button = new HavenNavigationButton
            {
                Content = view.Name,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _viewButtons[view.Kind] = button;
            var qName = $"Plan.Views.Item{i}";
            button.RegisterWithEvents(qName, _bus);
            button.Click += (_, _) =>
            {
                _bus.Fire($"{qName}.Click");
                SelectedView = view.Kind;
            };
            ViewsList.Items.Add(button);
        }
    }

    private void WireCollectionButtons()
    {
        CreateCollectionButton.Click += async (_, _) =>
        {
            _bus.Fire("Plan.Actions.CreateCollection");
            await CreateCollectionAsync();
        };

        RenameCollectionButton.Click += async (_, _) =>
        {
            _bus.Fire("Plan.Actions.RenameCollection");
            await RenameCollectionAsync();
        };

        ArchiveCollectionRequestButton.Click += (_, _) =>
        {
            _bus.Fire("Plan.Actions.RequestArchiveCollection");
            ArchiveConfirmPanel.IsVisible = true;
        };

        CancelArchiveButton.Click += (_, _) =>
        {
            _bus.Fire("Plan.Actions.CancelArchiveCollection");
            ArchiveConfirmPanel.IsVisible = false;
        };

        ConfirmArchiveButton.Click += async (_, _) =>
        {
            _bus.Fire("Plan.Actions.ConfirmArchiveCollection");
            await ArchiveCollectionAsync();
        };

        _bus.RegisterElement("Plan.Actions.CreateCollection", CreateCollectionButton);
        _bus.RegisterElement("Plan.Actions.RenameCollection", RenameCollectionButton);
        _bus.RegisterElement("Plan.Actions.RequestArchiveCollection", ArchiveCollectionRequestButton);
        _bus.RegisterElement("Plan.Actions.CancelArchiveCollection", CancelArchiveButton);
        _bus.RegisterElement("Plan.Actions.ConfirmArchiveCollection", ConfirmArchiveButton);
    }

    private void WireTaskCreation()
    {
        CreateTaskButton.Click += async (_, _) =>
        {
            _bus.Fire("Plan.Actions.CreateTask");
            await CreateTaskAsync();
        };
        _bus.RegisterElement("Plan.Actions.CreateTask", CreateTaskButton);
        _bus.WirePointerEvents("Plan.Actions.CreateTask", CreateTaskButton);
    }

    private void WireEventCreation()
    {
        CreateEventButton.Click += async (_, _) =>
        {
            _bus.Fire("Plan.Actions.CreateEvent");
            await CreateEventAsync();
        };
        _bus.RegisterElement("Plan.Actions.CreateEvent", CreateEventButton);
        _bus.WirePointerEvents("Plan.Actions.CreateEvent", CreateEventButton);
    }

    private void WireTaskEditor()
    {
        CloseTaskEditorButton.Click += (_, _) =>
        {
            _bus.Fire("Plan.Actions.CloseTaskEditor");
            TaskEditorPanel.IsVisible = false;
            _taskEditor = null;
        };
        _bus.RegisterElement("Plan.Actions.CloseTaskEditor", CloseTaskEditorButton);

        SaveTaskButton.Click += async (_, _) =>
        {
            _bus.Fire("Plan.Actions.SaveTask");
            await SaveTaskEditorAsync();
        };
        _bus.RegisterElement("Plan.Actions.SaveTask", SaveTaskButton);
        _bus.WirePointerEvents("Plan.Actions.SaveTask", SaveTaskButton);
    }

    private void WireEventEditor()
    {
        CloseEventEditorButton.Click += (_, _) =>
        {
            _bus.Fire("Plan.Actions.CloseEventEditor");
            EventEditorPanel.IsVisible = false;
            _eventEditor = null;
        };
        _bus.RegisterElement("Plan.Actions.CloseEventEditor", CloseEventEditorButton);

        SaveEventButton.Click += async (_, _) =>
        {
            _bus.Fire("Plan.Actions.SaveEvent");
            await SaveEventEditorAsync();
        };
        _bus.RegisterElement("Plan.Actions.SaveEvent", SaveEventButton);
        _bus.WirePointerEvents("Plan.Actions.SaveEvent", SaveEventButton);
    }

    private void WireAiSection()
    {
        AskAiButton.Click += async (_, _) =>
        {
            _bus.Fire("Plan.Actions.AskAi");
            await AskAiAsync();
        };
        _bus.RegisterElement("Plan.Actions.AskAi", AskAiButton);
        _bus.WirePointerEvents("Plan.Actions.AskAi", AskAiButton);

        DismissProposalButton.Click += (_, _) =>
        {
            _bus.Fire("Plan.Actions.DismissProposal");
            DismissProposal();
        };
        _bus.RegisterElement("Plan.Actions.DismissProposal", DismissProposalButton);

        ApplyProposalButton.Click += async (_, _) =>
        {
            _bus.Fire("Plan.Actions.ApplyProposal");
            await ApplyProposalAsync();
        };
        _bus.RegisterElement("Plan.Actions.ApplyProposal", ApplyProposalButton);
        _bus.WirePointerEvents("Plan.Actions.ApplyProposal", ApplyProposalButton);
    }

    private void WireCalendarProviders()
    {
        _bus.RegisterElement("Plan.Actions.PreviousPeriod", PreviousPeriodButton);
        _bus.WirePointerEvents("Plan.Actions.PreviousPeriod", PreviousPeriodButton);
        PreviousPeriodButton.Click += (_, _) =>
        {
            _bus.Fire("Plan.Actions.PreviousPeriod");
            MovePeriod(-1);
        };

        _bus.RegisterElement("Plan.Actions.NextPeriod", NextPeriodButton);
        _bus.WirePointerEvents("Plan.Actions.NextPeriod", NextPeriodButton);
        NextPeriodButton.Click += (_, _) =>
        {
            _bus.Fire("Plan.Actions.NextPeriod");
            MovePeriod(1);
        };

        _bus.RegisterElement("Plan.Actions.Today", TodayButton);
        _bus.WirePointerEvents("Plan.Actions.Today", TodayButton);
        TodayButton.Click += async (_, _) =>
        {
            _bus.Fire("Plan.Actions.Today");
            _anchorDate = DateTimeOffset.Now;
            await RefreshAsync(CancellationToken.None);
        };
    }

    private void WireDragDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void PopulateStaticCombos()
    {
        foreach (var priority in Priorities)
            NewTaskPriorityCombo.Items.Add(priority);
        NewTaskPriorityCombo.SelectedIndex = 0;
    }

    // ============================================================
    //  SELECTED VIEW
    // ============================================================

    private PlannerViewKind SelectedView
    {
        get => _selectedView;
        set
        {
            _selectedView = value;
            foreach (var (kind, button) in _viewButtons)
                button.Classes.Set("selected", kind == value);
            if (Bounds.Width is > 0 and < 1180)
            {
                _compactViewsOpen = false;
                ApplyResponsiveLayout(Bounds.Width);
            }
            TitleText.Text = value switch
            {
                PlannerViewKind.Today => "Today",
                PlannerViewKind.Inbox => "Inbox",
                PlannerViewKind.Upcoming => "Upcoming",
                _ => value.ToString()
            };
            UpdatePeriodLabel();
            UpdateContentVisibility();
            _ = RefreshAsync(CancellationToken.None);
        }
    }

    private void UpdatePeriodLabel()
    {
        PeriodText.Text = _selectedView switch
        {
            PlannerViewKind.Day => _anchorDate.ToString("dddd, d MMMM yyyy"),
            PlannerViewKind.Week => $"Week of {StartOfWeek(_anchorDate):d MMMM yyyy}",
            PlannerViewKind.Month => _anchorDate.ToString("MMMM yyyy"),
            _ => DateTimeOffset.Now.ToString("dddd, d MMMM")
        };
    }

    private void UpdateContentVisibility()
    {
        var showsBoard = _selectedView == PlannerViewKind.Board;
        var showsCalendar = _selectedView is PlannerViewKind.Week or PlannerViewKind.Month;
        StandardListPanel.IsVisible = !showsBoard && !showsCalendar;
        BoardColumnsPanel.IsVisible = showsBoard;
        CalendarGridPanel.IsVisible = showsCalendar;
    }

    // ============================================================
    //  PERIOD NAVIGATION
    // ============================================================

    private void MovePeriod(int direction)
    {
        _anchorDate = _selectedView switch
        {
            PlannerViewKind.Day => _anchorDate.AddDays(direction),
            PlannerViewKind.Week => _anchorDate.AddDays(7 * direction),
            PlannerViewKind.Month => _anchorDate.AddMonths(direction),
            _ => _anchorDate.AddDays(7 * direction)
        };
        UpdatePeriodLabel();
        _ = RefreshAsync(CancellationToken.None);
    }

    // ============================================================
    //  REFRESH
    // ============================================================

    private async Task RefreshAsync(CancellationToken outerCt)
    {
        if (_isRefreshing)
        {
            _refreshQueued = true;
            return;
        }

        _isRefreshing = true;
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        _refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        var ct = _refreshCancellation.Token;

        try
        {
            await _repository.EnsureDefaultsAsync(ct);
            var selectedId = _selectedCollection?.Id;

            var collections = await _repository.GetCollectionsAsync(false, ct);
            _collections.Clear();
            foreach (var c in collections) _collections.Add(new(c));
            _selectedCollection = _collections.FirstOrDefault(item => item.Id == selectedId) ?? _collections.FirstOrDefault();
            RebuildCollectionsList();

            var calendars = await _repository.GetCalendarsAsync(true, ct);
            _calendars.Clear();
            foreach (var cal in calendars.Where(c => c.Permission == CalendarPermission.Writer))
                _calendars.Add(new(cal));
            _selectedCalendar = _calendars.FirstOrDefault(item => item.Id == _selectedCalendar?.Id)
                                ?? _calendars.FirstOrDefault(item => item.Id == PlannerDefaults.LocalCalendarId)
                                ?? _calendars.FirstOrDefault();
            RebuildCalendarCombo();

            var (taskQuery, eventStart, eventEnd) = CreateQuery();
            var tasks = await _repository.GetTasksAsync(taskQuery, ct);
            var events = await _repository.GetEventsAsync(eventStart, eventEnd, null, ct);
            _tasks.Clear();
            foreach (var t in tasks) _tasks.Add(new(t, _collections.FirstOrDefault(c => c.Id == t.CollectionId)?.Name ?? "Planner"));
            _events.Clear();
            foreach (var e in events) _events.Add(new(e));

            var conflicts = await _repository.GetUnresolvedConflictsAsync(ct);
            _conflicts.Clear();
            foreach (var conflict in conflicts) _conflicts.Add(new(conflict));
            ConflictsPanel.IsVisible = _conflicts.Count > 0;
            RebuildConflictsPanel();

            var accounts = await _repository.GetCalendarAccountsAsync(ct);
            foreach (var provider in _providers)
            {
                var account = accounts.Where(a => a.Provider == provider.Provider.Kind)
                    .OrderByDescending(a => a.UpdatedAt).FirstOrDefault();
                provider.Update(account?.Status ?? (provider.IsConfigured ? CalendarSyncStatus.Disconnected : CalendarSyncStatus.NotConfigured),
                    account?.StatusMessage ?? provider.Provider.ConfigurationStatus);
            }
            RebuildCalendarProvidersPanel();

            TaskStatusText.Text = $"{_tasks.Count} task{(_tasks.Count == 1 ? "" : "s")} · {_events.Count} event{(_events.Count == 1 ? "" : "s")}";
            BuildProjections();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { TaskStatusText.Text = $"Refresh failed: {ex.Message}"; }
        finally
        {
            _isRefreshing = false;
            if (_refreshQueued)
            {
                _refreshQueued = false;
                _ = RefreshAsync(CancellationToken.None);
            }
        }
    }

    private (PlannerTaskQuery Tasks, DateTimeOffset EventStart, DateTimeOffset EventEnd) CreateQuery()
    {
        var day = LocalDay(_anchorDate);
        var collectionId = _selectedCollection?.Id;
        return _selectedView switch
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

    private static (PlannerTaskQuery, DateTimeOffset, DateTimeOffset) Range(DateTimeOffset start, int days, Guid? collectionId)
        => (new PlannerTaskQuery(collectionId, RangeStart: start, RangeEnd: start.AddDays(days)), start, start.AddDays(days));

    // ============================================================
    //  BUILD PROJECTIONS
    // ============================================================

    private void BuildProjections()
    {
        BuildBoardColumns();
        BuildCalendarGrid();
        RebuildTaskItemsPanel();
        RebuildEventItemsPanel();
    }

    private void BuildBoardColumns()
    {
        _boardColumns.Clear();
        using (UiBatcher.BeginBatch())
        {
            BoardColumnsPanel.Items.Clear();
            if (_selectedView != PlannerViewKind.Board) return;

            foreach (var status in new[] { PlannerTaskStatus.Inbox, PlannerTaskStatus.Planned, PlannerTaskStatus.InProgress, PlannerTaskStatus.Completed })
            {
                var column = new PlannerBoardColumnViewModel(status, _tasks.Where(t => t.Definition.Status == status).ToArray());
                _boardColumns.Add(column);
                BoardColumnsPanel.Items.Add(CreateBoardColumnCard(column));
            }
        }
    }

    private Border CreateBoardColumnCard(PlannerBoardColumnViewModel column)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock { Text = column.Name, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        var countBlock = new TextBlock { Text = column.CountLabel, Classes = { "muted" } };
        Grid.SetColumn(countBlock, 1);
        header.Children.Add(countBlock);

        var tasksPanel = new StackPanel { Spacing = 8 };
        foreach (var task in column.Tasks)
            tasksPanel.Children.Add(CreateBoardTaskCard(task, column));

        var stack = new StackPanel { Children = { header, tasksPanel } };
        var border = new HavenAdaptiveSurface
        {
            Classes = { "card" },
            Margin = new Thickness(4),
            Padding = new Thickness(12),
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = stack
        };
        DragDrop.SetAllowDrop(border, true);
        border.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        border.AddHandler(DragDrop.DropEvent, OnBoardColumnDrop);
        border.DataContext = column;
        return border;
    }

    private Border CreateBoardTaskCard(PlannerTaskItemViewModel task, PlannerBoardColumnViewModel column)
    {
        var qName = $"Plan.Board.Task{_tasks.IndexOf(task)}";

        var titleBlock = new TextBlock { Text = task.Title, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        var dragHandle = new HavenAdaptiveSurface
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(5),
            Background = Brush("HavenPanel3Brush"),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeAll),
            Child = new HavenIcon { IconKey = "more", Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }
        };
        dragHandle.PointerPressed += async (_, e) => await StartDragAsync(e, $"haven-plan:task:{task.Id:D}");
        dragHandle.RegisterWithEvents($"{qName}.Drag", _bus);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(titleBlock);
        Grid.SetColumn(dragHandle, 1);
        header.Children.Add(dragHandle);

        var metaBlock = new TextBlock { Text = task.Meta, Classes = { "muted" }, FontSize = 10, Margin = new Thickness(0, 3, 0, 7) };

        var editButton = new HavenButton { Content = "Edit", Classes = { "compact" } };
        var startButton = new HavenButton { Content = "Start", Classes = { "compact" } };
        var doneButton = new HavenButton { Content = "Done", Classes = { "compact" } };

        editButton.RegisterWithEvents($"{qName}.Edit", _bus);
        editButton.Click += (_, _) =>
        {
            _bus.Fire($"{qName}.Edit");
            OpenTaskEditor(task);
        };

        startButton.RegisterWithEvents($"{qName}.Start", _bus);
        startButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Start");
            await StartTaskAsync(task);
        };

        doneButton.RegisterWithEvents($"{qName}.Done", _bus);
        doneButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Done");
            await CompleteTaskAsync(task);
        };

        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 5 };
        buttons.Children.Add(editButton);
        Grid.SetColumn(startButton, 1);
        buttons.Children.Add(startButton);
        Grid.SetColumn(doneButton, 2);
        buttons.Children.Add(doneButton);

        var stack = new StackPanel { Children = { header, metaBlock, buttons } };
        var border = new HavenAdaptiveSurface
        {
            Background = Brush("HavenPanel2Brush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = stack
        };
        border.PointerEntered += (_, _) => _bus.Fire($"{qName}.Hover");
        border.PointerExited += (_, _) => _bus.Fire($"{qName}.Leave");
        return border;
    }

    private void BuildCalendarGrid()
    {
        _calendarDays.Clear();
        using (UiBatcher.BeginBatch())
        {
            CalendarGridPanel.Items.Clear();
            CalendarGridPanel.ItemsPanel = new FuncTemplate<Panel?>(() => new Avalonia.Controls.Primitives.UniformGrid { Columns = 7 });

            if (_selectedView is not (PlannerViewKind.Week or PlannerViewKind.Month)) return;

            var first = _selectedView == PlannerViewKind.Week
                ? StartOfWeek(_anchorDate)
                : StartOfWeek(new DateTimeOffset(_anchorDate.Year, _anchorDate.Month, 1, 0, 0, 0, _anchorDate.Offset));
            var count = _selectedView == PlannerViewKind.Week ? 7 : 42;

            for (var offset = 0; offset < count; offset++)
            {
                var day = first.AddDays(offset);
                var next = day.AddDays(1);
                var taskEntries = _tasks.Where(t => t.Definition.DueAt >= day && t.Definition.DueAt < next)
                    .Select(t => new PlannerCalendarEntryViewModel(t.Id, t.Title, t.Definition.DueAt?.ToLocalTime().ToString("HH:mm") ?? "", false));
                var eventEntries = _events.Where(e => e.Definition.StartsAt < next && e.Definition.EndsAt > day)
                    .Select(e => new PlannerCalendarEntryViewModel(e.Id, e.Title, e.Definition.IsAllDay ? "All day" : e.Definition.StartsAt.ToLocalTime().ToString("HH:mm"), true));
                var entries = taskEntries.Concat(eventEntries).OrderBy(e => e.Time).ToArray();
                var dayVm = new PlannerCalendarDayViewModel(day, day.Month == _anchorDate.Month, entries);
                _calendarDays.Add(dayVm);
                CalendarGridPanel.Items.Add(CreateCalendarDayCard(dayVm));
            }
        }
    }

    private Border CreateCalendarDayCard(PlannerCalendarDayViewModel dayVm)
    {
        var dayNameBlock = new TextBlock { Text = dayVm.DayName, Classes = { "muted" }, FontSize = 10 };
        var dayNumBlock = new TextBlock { Text = dayVm.DayNumber, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 7) };
        header.Children.Add(dayNameBlock);
        Grid.SetColumn(dayNumBlock, 1);
        header.Children.Add(dayNumBlock);

        var entriesPanel = new StackPanel();
        foreach (var entry in dayVm.Entries)
        {
            var entryBorder = new HavenAdaptiveSurface
            {
                Background = Brush("HavenAccentSoftBrush"),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(5, 3),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeAll),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = entry.Time, FontSize = 9, Foreground = Brush("HavenAccentBrush") },
                        new TextBlock { Text = entry.Title, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, FontSize = 10 }
                    }
                }
            };
            entryBorder.PointerPressed += async (_, e) =>
            {
                var kind = entry.IsEvent ? "event" : "task";
                await StartDragAsync(e, $"haven-plan:{kind}:{entry.Id:D}");
            };
            var qName = $"Plan.Calendar.Entry{entriesPanel.Children.Count}";
            entryBorder.RegisterWithEvents(qName, _bus);
            entriesPanel.Children.Add(entryBorder);
        }

        var stack = new StackPanel { Children = { header, entriesPanel } };
        var border = new HavenAdaptiveSurface
        {
            Classes = { "card" },
            Margin = new Thickness(3),
            Padding = new Thickness(9),
            MinHeight = 124,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = stack,
            DataContext = dayVm
        };
        DragDrop.SetAllowDrop(border, true);
        border.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        border.AddHandler(DragDrop.DropEvent, OnCalendarDayDrop);
        return border;
    }

    private void RebuildTaskItemsPanel()
    {
        using (UiBatcher.BeginBatch())
        {
            TaskItemsPanel.Items.Clear();
            foreach (var task in _tasks)
                TaskItemsPanel.Items.Add(CreateTaskCard(task));
        }
    }

    private Border CreateTaskCard(PlannerTaskItemViewModel task)
    {
        var index = _tasks.IndexOf(task);
        var qName = $"Plan.Tasks.Item{index}";

        var dragHandle = new HavenAdaptiveSurface
        {
            Width = 24, Height = 34, CornerRadius = new CornerRadius(6),
            Background = Brush("HavenPanel2Brush"),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeAll),
            Child = new HavenIcon { IconKey = "more", Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } }
        };
        dragHandle.PointerPressed += async (_, e) => await StartDragAsync(e, $"haven-plan:task:{task.Id:D}");
        dragHandle.RegisterWithEvents($"{qName}.Drag", _bus);

        var completeButton = new HavenButton
        {
            Classes = { "icon" },
            Width = 34, Height = 34,
            Margin = new Thickness(6, 0, 0, 0),
            Content = new HavenIcon { IconKey = "check", Width = 14, Height = 14 }
        };
        ToolTip.SetTip(completeButton, "Complete task");
        completeButton.RegisterWithEvents($"{qName}.Complete", _bus);
        completeButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Complete");
            await CompleteTaskAsync(task);
        };

        var titleBlock = new TextBlock { Text = task.Title, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var metaBlock = new TextBlock { Text = task.Meta, Classes = { "muted" }, FontSize = 11, Margin = new Thickness(0, 3, 0, 0) };
        var textStack = new StackPanel { Children = { titleBlock, metaBlock }, Margin = new Thickness(12, 0) };

        var editButton = new HavenButton { Content = "Edit", Margin = new Thickness(0, 0, 6, 0) };
        var subtaskButton = new HavenButton { Content = "Subtask", Margin = new Thickness(0, 0, 6, 0) };
        var startButton = new HavenButton { Content = "Start", Margin = new Thickness(0, 0, 6, 0) };
        var deleteButton = new HoldToConfirmButton { Content = "Delete" };

        editButton.RegisterWithEvents($"{qName}.Edit", _bus);
        editButton.Click += (_, _) => { _bus.Fire($"{qName}.Edit"); OpenTaskEditor(task); };

        subtaskButton.RegisterWithEvents($"{qName}.Subtask", _bus);
        subtaskButton.Click += async (_, _) => { _bus.Fire($"{qName}.Subtask"); await AddSubtaskAsync(task); };

        startButton.RegisterWithEvents($"{qName}.Start", _bus);
        startButton.Click += async (_, _) => { _bus.Fire($"{qName}.Start"); await StartTaskAsync(task); };

        deleteButton.RegisterWithEvents($"{qName}.Delete", _bus);
        deleteButton.Click += async (_, _) => { _bus.Fire($"{qName}.Delete"); await DeleteTaskAsync(task); };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto,Auto") };
        grid.Children.Add(dragHandle);
        Grid.SetColumn(completeButton, 1);
        grid.Children.Add(completeButton);
        Grid.SetColumn(textStack, 2);
        grid.Children.Add(textStack);
        Grid.SetColumn(editButton, 3);
        grid.Children.Add(editButton);
        Grid.SetColumn(subtaskButton, 4);
        grid.Children.Add(subtaskButton);
        Grid.SetColumn(startButton, 5);
        grid.Children.Add(startButton);
        Grid.SetColumn(deleteButton, 6);
        grid.Children.Add(deleteButton);

        var border = new HavenAdaptiveSurface { Classes = { "card" }, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(14), Child = grid };
        border.PointerEntered += (_, _) => _bus.Fire($"{qName}.Hover");
        border.PointerExited += (_, _) => _bus.Fire($"{qName}.Leave");
        return border;
    }

    private void RebuildEventItemsPanel()
    {
        using (UiBatcher.BeginBatch())
        {
            EventItemsPanel.Items.Clear();
            foreach (var plannerEvent in _events)
                EventItemsPanel.Items.Add(CreateEventCard(plannerEvent));
        }
    }

    private Border CreateEventCard(PlannerEventItemViewModel plannerEvent)
    {
        var index = _events.IndexOf(plannerEvent);
        var qName = $"Plan.Events.Item{index}";

        var accentBar = new HavenAdaptiveSurface { Background = Brush("HavenAccentBrush"), CornerRadius = new CornerRadius(3) };
        var titleBlock = new TextBlock { Text = plannerEvent.Title, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        var detailBlock = new TextBlock { Text = plannerEvent.Detail, Classes = { "muted" }, FontSize = 11, Margin = new Thickness(0, 3, 0, 0) };
        var textStack = new StackPanel { Children = { titleBlock, detailBlock }, Margin = new Thickness(12, 0) };

        var editButton = new HavenButton { Content = "Edit", Margin = new Thickness(0, 0, 6, 0) };
        var deleteButton = new HoldToConfirmButton { Content = "Delete", IsVisible = plannerEvent.CanDelete };

        editButton.RegisterWithEvents($"{qName}.Edit", _bus);
        editButton.Click += (_, _) => { _bus.Fire($"{qName}.Edit"); OpenEventEditor(plannerEvent); };

        deleteButton.RegisterWithEvents($"{qName}.Delete", _bus);
        deleteButton.Click += async (_, _) => { _bus.Fire($"{qName}.Delete"); await DeleteEventAsync(plannerEvent); };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("6,*,Auto,Auto") };
        grid.Children.Add(accentBar);
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);
        Grid.SetColumn(editButton, 2);
        grid.Children.Add(editButton);
        Grid.SetColumn(deleteButton, 3);
        grid.Children.Add(deleteButton);

        var border = new HavenAdaptiveSurface { Classes = { "card" }, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(14), Child = grid };
        border.PointerEntered += (_, _) => _bus.Fire($"{qName}.Hover");
        border.PointerExited += (_, _) => _bus.Fire($"{qName}.Leave");
        return border;
    }

    private void RebuildCollectionsList()
    {
        using (UiBatcher.BeginBatch())
        {
            CollectionsList.Items.Clear();
            foreach (var collection in _collections)
            {
                var moveUpButton = new HavenButton
                {
                    Classes = { "icon", "compact" },
                    Content = new HavenIcon { IconKey = "chevron-down", Width = 13, Height = 13 },
                    RenderTransform = new Avalonia.Media.RotateTransform(180)
                };
                ToolTip.SetTip(moveUpButton, "Move collection up");
                var moveDownButton = new HavenButton
                {
                    Classes = { "icon", "compact" },
                    Content = new HavenIcon { IconKey = "chevron-down", Width = 13, Height = 13 }
                };
                ToolTip.SetTip(moveDownButton, "Move collection down");

                var idx = _collections.IndexOf(collection);
                var upQName = $"Plan.Collections.Item{idx}.MoveUp";
                var downQName = $"Plan.Collections.Item{idx}.MoveDown";
                moveUpButton.RegisterWithEvents(upQName, _bus);
                moveDownButton.RegisterWithEvents(downQName, _bus);

                moveUpButton.Click += async (_, _) => { _bus.Fire(upQName); await MoveCollectionAsync(collection, -1); };
                moveDownButton.Click += async (_, _) => { _bus.Fire(downQName); await MoveCollectionAsync(collection, 1); };

                var itemGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
                var nameBlock = new TextBlock { Text = collection.Name, Margin = new Thickness(8, 7), VerticalAlignment = VerticalAlignment.Center };
                nameBlock.PointerPressed += (_, _) => { CollectionsList.SelectedItem = collection; };
                itemGrid.Children.Add(nameBlock);
                Grid.SetColumn(moveUpButton, 1);
                itemGrid.Children.Add(moveUpButton);
                Grid.SetColumn(moveDownButton, 2);
                itemGrid.Children.Add(moveDownButton);

                var listItem = new HavenListBoxItem { Content = itemGrid, DataContext = collection };
                listItem.PointerEntered += (_, _) => _bus.Fire($"Plan.Collections.Item{idx}.Hover");
                listItem.PointerExited += (_, _) => _bus.Fire($"Plan.Collections.Item{idx}.Leave");
                CollectionsList.Items.Add(listItem);
            }
        }
    }

    private void RebuildCalendarCombo()
    {
        NewEventCalendarCombo.Items.Clear();
        foreach (var cal in _calendars)
            NewEventCalendarCombo.Items.Add(cal);
        if (_selectedCalendar is not null)
            NewEventCalendarCombo.SelectedItem = _selectedCalendar;
    }

    private void RebuildCalendarProvidersPanel()
    {
        using (UiBatcher.BeginBatch())
        {
            CalendarProvidersPanel.Items.Clear();
            foreach (var provider in _providers)
            {
                var nameBlock = new TextBlock { Text = provider.Name, FontWeight = Avalonia.Media.FontWeight.SemiBold };
                var statusBlock = new TextBlock { Text = provider.Status, Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 4, 0, 8) };

                var connectButton = new HavenPrimaryButton { Content = "Connect calendar" };
                var syncButton = new HavenSecondaryButton { Content = "Sync now" };
                var disconnectButton = new HavenNegativeButton { Content = "Disconnect" };

                var qName = $"Plan.Calendar.Provider{_providers.IndexOf(provider)}";
                connectButton.RegisterWithEvents($"{qName}.Connect", _bus);
                syncButton.RegisterWithEvents($"{qName}.Sync", _bus);
                disconnectButton.RegisterWithEvents($"{qName}.Disconnect", _bus);

                connectButton.Click += async (_, _) => { _bus.Fire($"{qName}.Connect"); await ConnectProviderAsync(provider); };
                syncButton.Click += async (_, _) => { _bus.Fire($"{qName}.Sync"); await SyncProviderAsync(provider); };
                disconnectButton.Click += async (_, _) => { _bus.Fire($"{qName}.Disconnect"); await DisconnectProviderAsync(provider); };

                var buttons = new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,Auto"),
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    ColumnSpacing = 8,
                    RowSpacing = 8
                };
                Grid.SetColumnSpan(connectButton, 2);
                buttons.Children.Add(connectButton);
                Grid.SetRow(syncButton, 1);
                buttons.Children.Add(syncButton);
                Grid.SetRow(disconnectButton, 1);
                Grid.SetColumn(disconnectButton, 1);
                buttons.Children.Add(disconnectButton);

                var stack = new StackPanel { Children = { nameBlock, statusBlock, buttons } };
                var border = new HavenAdaptiveSurface { Classes = { "card" }, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12), Child = stack };
                CalendarProvidersPanel.Items.Add(border);
            }
        }
    }

    private void RebuildConflictsPanel()
    {
        using (UiBatcher.BeginBatch())
        {
            ConflictsItemsPanel.Items.Clear();
            foreach (var conflict in _conflicts)
            {
                var titleBlock = new TextBlock { Text = conflict.Title, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                var detectedBlock = new TextBlock { Text = conflict.Detected, Classes = { "muted" }, FontSize = 10, Margin = new Thickness(0, 2, 0, 8) };
                var havenBlock = new TextBlock { Text = conflict.HavenVersion, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11 };
                var providerBlock = new TextBlock { Text = conflict.ProviderVersion, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 3, 0, 10) };

                var keepHavenButton = new HavenButton { Content = "Keep Haven" };
                var keepProviderButton = new HavenButton { Content = "Keep provider" };
                var duplicateButton = new HavenButton { Content = "Duplicate" };

                var idx = _conflicts.IndexOf(conflict);
                keepHavenButton.RegisterWithEvents($"Plan.Conflict{idx}.KeepHaven", _bus);
                keepProviderButton.RegisterWithEvents($"Plan.Conflict{idx}.KeepProvider", _bus);
                duplicateButton.RegisterWithEvents($"Plan.Conflict{idx}.Duplicate", _bus);

                keepHavenButton.Click += async (_, _) => { _bus.Fire($"Plan.Conflict{idx}.KeepHaven"); await ResolveConflictAsync(conflict, CalendarConflictResolution.KeepHaven); };
                keepProviderButton.Click += async (_, _) => { _bus.Fire($"Plan.Conflict{idx}.KeepProvider"); await ResolveConflictAsync(conflict, CalendarConflictResolution.KeepProvider); };
                duplicateButton.Click += async (_, _) => { _bus.Fire($"Plan.Conflict{idx}.Duplicate"); await ResolveConflictAsync(conflict, CalendarConflictResolution.Duplicate); };

                var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 5 };
                buttons.Children.Add(keepHavenButton);
                Grid.SetColumn(keepProviderButton, 1);
                buttons.Children.Add(keepProviderButton);
                Grid.SetColumn(duplicateButton, 2);
                buttons.Children.Add(duplicateButton);

                var stack = new StackPanel { Children = { titleBlock, detectedBlock, havenBlock, providerBlock, buttons } };
                var border = new HavenAdaptiveSurface { Classes = { "card" }, Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(12), Child = stack };
                ConflictsItemsPanel.Items.Add(border);
            }
        }
    }

    // ============================================================
    //  TASK CRUD
    // ============================================================

    private async Task CreateTaskAsync()
    {
        if (_selectedCollection is null) return;
        var title = NewTaskTitleBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title)) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            DateTimeOffset? due = null;
            if (NewTaskDueDatePicker.SelectedDate is { } d) due = LocalDay(d).AddHours(17);
            var priority = NewTaskPriorityCombo.SelectedItem is PlannerPriority p ? p : PlannerPriority.None;
            await _repository.UpsertTaskAsync(new PlannerTask(Guid.NewGuid(), _selectedCollection.Id, null, title, string.Empty,
                priority, PlannerTaskStatus.Planned, "[]", null, null, due, null, null, null, _tasks.Count, now, now,
                TimeZoneInfo.Local.Id), CancellationToken.None);
            NewTaskTitleBox.Text = string.Empty;
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) { TaskStatusText.Text = $"Task could not be created: {ex.Message}"; }
    }

    private async Task CompleteTaskAsync(PlannerTaskItemViewModel item)
    {
        try { await _repository.CompleteTaskAsync(item.Id, DateTimeOffset.UtcNow, CancellationToken.None); await RefreshAsync(CancellationToken.None); }
        catch (Exception ex) { TaskStatusText.Text = $"Task could not be completed: {ex.Message}"; }
    }

    private async Task StartTaskAsync(PlannerTaskItemViewModel item)
    {
        try
        {
            await _repository.UpsertTaskAsync(item.Definition with { Status = PlannerTaskStatus.InProgress, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) { TaskStatusText.Text = $"Task could not be started: {ex.Message}"; }
    }

    private async Task DeleteTaskAsync(PlannerTaskItemViewModel item)
    {
        try
        {
            await _repository.DeleteTaskAsync(item.Id, CancellationToken.None);
            if (_taskEditor?.Definition.Id == item.Id) { _taskEditor = null; TaskEditorPanel.IsVisible = false; }
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) { TaskStatusText.Text = $"Task could not be deleted: {ex.Message}"; }
    }

    private async Task AddSubtaskAsync(PlannerTaskItemViewModel parent)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var task = new PlannerTask(Guid.NewGuid(), parent.Definition.CollectionId, parent.Id, "New subtask", string.Empty,
                parent.Definition.Priority, PlannerTaskStatus.Planned, "[]", null, null, parent.Definition.DueAt, null, null, null,
                0, now, now, parent.Definition.TimeZoneId);
            await _repository.UpsertTaskAsync(task, CancellationToken.None);
            OpenTaskEditor(new PlannerTaskItemViewModel(task, parent.CollectionName));
            await RefreshAsync(CancellationToken.None);
            TaskStatusText.Text = "Subtask created. Add its details in the editor.";
        }
        catch (Exception ex) { TaskStatusText.Text = $"Subtask could not be created: {ex.Message}"; }
    }

    public async Task MoveTaskToStatusAsync(Guid taskId, PlannerTaskStatus status)
    {
        try
        {
            var task = await _repository.GetTaskAsync(taskId, CancellationToken.None)
                       ?? throw new InvalidOperationException("The dragged task no longer exists.");
            if (status == PlannerTaskStatus.Completed) await _repository.CompleteTaskAsync(taskId, DateTimeOffset.UtcNow, CancellationToken.None);
            else await _repository.UpsertTaskAsync(task with { Status = status, CompletedAt = null, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) { TaskStatusText.Text = $"Task could not be moved: {ex.Message}"; }
    }

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
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) { TaskStatusText.Text = $"Task could not be rescheduled: {ex.Message}"; }
    }

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
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) { TaskStatusText.Text = $"Event could not be rescheduled: {ex.Message}"; }
    }

    // ============================================================
    //  EVENT CRUD
    // ============================================================

    private async Task CreateEventAsync()
    {
        if (_selectedCalendar is null) return;
        var title = NewEventTitleBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title)) return;
        if (NewEventDatePicker.SelectedDate is null) return;
        try
        {
            var day = LocalDay(NewEventDatePicker.SelectedDate.Value);
            var start = NewEventStartPicker.SelectedTime ?? new TimeSpan(9, 0, 0);
            var end = NewEventEndPicker.SelectedTime ?? new TimeSpan(10, 0, 0);
            var startsAt = day.Add(start);
            var endsAt = day.Add(end);
            var now = DateTimeOffset.UtcNow;
            await _repository.UpsertEventAsync(new PlannerEvent(Guid.NewGuid(), _selectedCalendar.Id, title, string.Empty,
                string.Empty, startsAt, endsAt, false, null, null, false, null, null, now, now, null, TimeZoneInfo.Local.Id), CancellationToken.None);
            NewEventTitleBox.Text = string.Empty;
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) { TaskStatusText.Text = $"Event could not be created: {ex.Message}"; }
    }

    private async Task DeleteEventAsync(PlannerEventItemViewModel item)
    {
        if (item.IsReadOnly) return;
        try
        {
            await _repository.DeleteEventAsync(item.Id, DateTimeOffset.UtcNow, CancellationToken.None);
            if (_eventEditor?.Definition.Id == item.Id) { _eventEditor = null; EventEditorPanel.IsVisible = false; }
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) { TaskStatusText.Text = $"Event could not be deleted: {ex.Message}"; }
    }

    // ============================================================
    //  TASK / EVENT EDITORS
    // ============================================================

    private void OpenTaskEditor(PlannerTaskItemViewModel item)
    {
        _eventEditor = null;
        EventEditorPanel.IsVisible = false;

        var def = item.Definition;
        TaskEditorPanel.IsVisible = true;
        TaskEditorTitleBox.Text = def.Title;
        TaskEditorNotesBox.Text = def.Notes;
        TaskEditorTagsBox.Text = ReadTags(def.TagsJson);
        TaskEditorEstimateBox.Text = def.EstimatedMinutes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        TaskEditorRecurrenceBox.Text = def.RecurrenceRule ?? "";
        TaskEditorTimeZoneBox.Text = def.TimeZoneId;
        TaskEditorStartsAtPicker.SelectedDate = def.StartsAt?.LocalDateTime;
        TaskEditorDueAtPicker.SelectedDate = def.DueAt?.LocalDateTime;
        TaskEditorReminderAtPicker.SelectedDate = def.ReminderAt?.LocalDateTime;

        TaskEditorPriorityCombo.Items.Clear();
        foreach (var p in Priorities) TaskEditorPriorityCombo.Items.Add(p);
        TaskEditorPriorityCombo.SelectedItem = def.Priority;

        TaskEditorStatusCombo.Items.Clear();
        foreach (var s in Enum.GetValues<PlannerTaskStatus>()) TaskEditorStatusCombo.Items.Add(s);
        TaskEditorStatusCombo.SelectedItem = def.Status;

        _ = LoadParentOptionsAsync(def);

        TaskEditorMessageText.Text = "Edit the task details, then save.";
        _taskEditor = new PlannerTaskEditorViewModel(def, _repository, async () =>
        {
            TaskEditorPanel.IsVisible = false;
            _taskEditor = null;
            await RefreshAsync(CancellationToken.None);
        });
    }

    private async Task LoadParentOptionsAsync(PlannerTask current)
    {
        TaskEditorParentCombo.Items.Clear();
        TaskEditorParentCombo.Items.Add(new PlannerTaskParentOption(null, "No parent"));
        try
        {
            var tasks = await _repository.GetTasksAsync(new PlannerTaskQuery(current.CollectionId, IncludeCompleted: true), CancellationToken.None);
            foreach (var t in tasks.Where(t => t.Id != current.Id))
                TaskEditorParentCombo.Items.Add(new PlannerTaskParentOption(t.Id, t.Title));
            TaskEditorParentCombo.SelectedItem = TaskEditorParentCombo.Items.OfType<PlannerTaskParentOption>()
                .FirstOrDefault(p => p.Id == current.ParentTaskId) ?? TaskEditorParentCombo.Items[0];
        }
        catch { }
    }

    private async Task SaveTaskEditorAsync()
    {
        if (_taskEditor is null) return;
        try
        {
            var def = _taskEditor.Definition;
            var title = TaskEditorTitleBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(title)) { TaskEditorMessageText.Text = "Title is required."; return; }

            int? estimate = null;
            var estimateText = TaskEditorEstimateBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(estimateText))
            {
                if (int.TryParse(estimateText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
                    estimate = parsed;
            }

            var parentOption = TaskEditorParentCombo.SelectedItem as PlannerTaskParentOption;
            var now = DateTimeOffset.UtcNow;
            var priority = TaskEditorPriorityCombo.SelectedItem is PlannerPriority p ? p : def.Priority;
            var status = TaskEditorStatusCombo.SelectedItem is PlannerTaskStatus s ? s : def.Status;
            var completing = status == PlannerTaskStatus.Completed && def.Status != PlannerTaskStatus.Completed;

            var tags = (TaskEditorTagsBox.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            var updated = def with
            {
                ParentTaskId = parentOption?.Id,
                Title = title,
                Notes = TaskEditorNotesBox.Text?.Trim() ?? "",
                TagsJson = PlannerStudyAssignmentTags.ReplaceUserTags(def.TagsJson, tags),
                EstimatedMinutes = estimate,
                Priority = priority,
                Status = completing ? def.Status : status,
                StartsAt = TaskEditorStartsAtPicker.SelectedDate,
                DueAt = TaskEditorDueAtPicker.SelectedDate,
                ReminderAt = TaskEditorReminderAtPicker.SelectedDate,
                RecurrenceRule = string.IsNullOrWhiteSpace(TaskEditorRecurrenceBox.Text) ? null : TaskEditorRecurrenceBox.Text.Trim(),
                TimeZoneId = string.IsNullOrWhiteSpace(TaskEditorTimeZoneBox.Text) ? TimeZoneInfo.Local.Id : TaskEditorTimeZoneBox.Text.Trim(),
                CompletedAt = status == PlannerTaskStatus.Completed ? def.CompletedAt : null,
                UpdatedAt = now
            };
            await _repository.UpsertTaskAsync(updated, CancellationToken.None);
            if (completing) await _repository.CompleteTaskAsync(updated.Id, now, CancellationToken.None);
            TaskEditorMessageText.Text = "Task saved.";
            TaskEditorPanel.IsVisible = false;
            _taskEditor = null;
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) { TaskEditorMessageText.Text = $"Task could not be saved: {ex.Message}"; }
    }

    private void OpenEventEditor(PlannerEventItemViewModel item)
    {
        _taskEditor = null;
        TaskEditorPanel.IsVisible = false;

        var def = item.Definition;
        EventEditorPanel.IsVisible = true;
        EventEditorTitleBox.Text = def.Title;
        EventEditorNotesBox.Text = def.Notes;
        EventEditorLocationBox.Text = def.Location;
        EventEditorDatePicker.SelectedDate = def.StartsAt.LocalDateTime;
        EventEditorStartPicker.SelectedTime = def.StartsAt.ToLocalTime().TimeOfDay;
        EventEditorEndPicker.SelectedTime = def.EndsAt.ToLocalTime().TimeOfDay;
        EventEditorAllDayCheck.IsChecked = def.IsAllDay;
        EventEditorReminderPicker.SelectedDate = def.ReminderAt?.LocalDateTime;
        EventEditorRecurrenceBox.Text = def.RecurrenceRule ?? "";
        EventEditorTimeZoneBox.Text = def.TimeZoneId;
        EventEditorMessageText.Text = def.IsReadOnly ? "This provider event is read-only." : "Edit the event details, then save.";
        SaveEventButton.IsEnabled = !def.IsReadOnly;

        _eventEditor = new PlannerEventEditorViewModel(def, _repository, async () =>
        {
            EventEditorPanel.IsVisible = false;
            _eventEditor = null;
            await RefreshAsync(CancellationToken.None);
        });
    }

    private async Task SaveEventEditorAsync()
    {
        if (_eventEditor is null) return;
        try
        {
            var def = _eventEditor.Definition;
            if (def.IsReadOnly) return;
            var title = EventEditorTitleBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(title)) { EventEditorMessageText.Text = "Title is required."; return; }
            if (EventEditorStartPicker.SelectedTime is null || EventEditorEndPicker.SelectedTime is null)
            { EventEditorMessageText.Text = "Start and end times are required."; return; }

            var localDate = (EventEditorDatePicker.SelectedDate ?? def.StartsAt).ToLocalTime();
            var day = new DateTimeOffset(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(localDate.Date));
            var startsAt = day.Add(EventEditorStartPicker.SelectedTime.Value);
            var endsAt = day.Add(EventEditorEndPicker.SelectedTime.Value);
            if (endsAt <= startsAt) endsAt = endsAt.AddDays(1);

            await _repository.UpsertEventAsync(def with
            {
                Title = title,
                Notes = EventEditorNotesBox.Text?.Trim() ?? "",
                Location = EventEditorLocationBox.Text?.Trim() ?? "",
                StartsAt = startsAt,
                EndsAt = endsAt,
                IsAllDay = EventEditorAllDayCheck.IsChecked == true,
                ReminderAt = EventEditorReminderPicker.SelectedDate,
                RecurrenceRule = string.IsNullOrWhiteSpace(EventEditorRecurrenceBox.Text) ? null : EventEditorRecurrenceBox.Text.Trim(),
                TimeZoneId = string.IsNullOrWhiteSpace(EventEditorTimeZoneBox.Text) ? TimeZoneInfo.Local.Id : EventEditorTimeZoneBox.Text.Trim(),
                UpdatedAt = DateTimeOffset.UtcNow
            }, CancellationToken.None);
            EventEditorMessageText.Text = "Event saved.";
            EventEditorPanel.IsVisible = false;
            _eventEditor = null;
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) { EventEditorMessageText.Text = $"Event could not be saved: {ex.Message}"; }
    }

    // ============================================================
    //  COLLECTIONS
    // ============================================================

    private async Task CreateCollectionAsync()
    {
        var name = NewCollectionNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var collection = new PlannerCollection(Guid.NewGuid(), name, _collections.Count, false, now, now);
            await _repository.UpsertCollectionAsync(collection, CancellationToken.None);
            NewCollectionNameBox.Text = string.Empty;
            await RefreshAsync(CancellationToken.None);
            _selectedCollection = _collections.FirstOrDefault(item => item.Id == collection.Id);
            RebuildCollectionsList();
            TaskStatusText.Text = $"Created {collection.Name}.";
        }
        catch (Exception ex) { TaskStatusText.Text = $"Collection could not be created: {ex.Message}"; }
    }

    private async Task RenameCollectionAsync()
    {
        if (_selectedCollection is null) return;
        var name = SelectedCollectionNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            await _repository.UpsertCollectionAsync(_selectedCollection.Definition with { Name = name, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
            await RefreshAsync(CancellationToken.None);
            TaskStatusText.Text = "Collection renamed.";
        }
        catch (Exception ex) { TaskStatusText.Text = $"Collection could not be renamed: {ex.Message}"; }
    }

    private async Task ArchiveCollectionAsync()
    {
        if (_selectedCollection is null || _collections.Count <= 1) return;
        try
        {
            var collectionName = _selectedCollection.Name;
            await _repository.ArchiveCollectionAsync(_selectedCollection.Id, true, CancellationToken.None);
            ArchiveConfirmPanel.IsVisible = false;
            _selectedCollection = null;
            await RefreshAsync(CancellationToken.None);
            TaskStatusText.Text = $"Archived {collectionName}. Its tasks remain stored locally.";
        }
        catch (Exception ex) { TaskStatusText.Text = $"Collection could not be archived: {ex.Message}"; }
    }

    private async Task MoveCollectionAsync(PlannerCollectionItemViewModel item, int direction)
    {
        var index = _collections.IndexOf(item);
        var targetIndex = index + direction;
        if (index < 0 || targetIndex < 0 || targetIndex >= _collections.Count) return;
        try
        {
            var target = _collections[targetIndex];
            var now = DateTimeOffset.UtcNow;
            await _repository.UpsertCollectionAsync(item.Definition with { SortOrder = target.Definition.SortOrder, UpdatedAt = now }, CancellationToken.None);
            await _repository.UpsertCollectionAsync(target.Definition with { SortOrder = item.Definition.SortOrder, UpdatedAt = now }, CancellationToken.None);
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) { TaskStatusText.Text = $"Collection could not be reordered: {ex.Message}"; }
    }

    // ============================================================
    //  AI PROPOSALS
    // ============================================================

    private async Task AskAiAsync()
    {
        var prompt = AiPromptBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(prompt)) return;
        try
        {
            TaskStatusText.Text = "Asking a local model to draft a plan…";
            var models = await _ollama.GetModelsAsync(CancellationToken.None);
            var model = models.FirstOrDefault(m => m.Supports(ToolCapability.Tools))
                        ?? throw new InvalidOperationException("No installed local model supports structured tools.");
            var context = JsonSerializer.Serialize(new
            {
                today = DateTimeOffset.Now.ToString("O"),
                collections = _collections.Select(c => new { id = c.Id, c.Name }),
                tasks = _tasks.Take(100).Select(t => t.Definition),
                events = _events.Take(100).Select(e => e.Definition),
                localCalendarId = PlannerDefaults.LocalCalendarId
            });
            var response = await _ollama.ChatWithToolsAsync(new OllamaToolRequest(model.Name,
                [new OllamaToolTurn("user", $"Current planner state:\n{context}\n\nUser request:\n{prompt}\n\nDraft changes with planner_propose_changes. Do not claim they were applied.")],
                [_proposals.ToolDefinition], EffortLevel.Medium,
                "You are Haven Plan's local planning assistant. Preserve existing items unless asked, use ISO-8601 timestamps, and return a reviewable proposal."), CancellationToken.None);
            var call = response.ToolCalls.FirstOrDefault(c => c.Name.Equals(_proposals.ToolDefinition.Name, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException("The model did not return a planner proposal.");
            var proposal = _proposals.ParseToolCall(call.Arguments);
            var validation = _proposals.Validate(proposal);
            if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
            _pendingChanges.Clear();
            foreach (var change in proposal.Changes) _pendingChanges.Add(new(change));
            _pendingProposal = proposal;
            ProposalSummaryText.Text = proposal.Summary;
            ProposalPanel.IsVisible = true;
            RebuildProposalChangesPanel();
            TaskStatusText.Text = "Review the proposal. Nothing has changed yet.";
        }
        catch (Exception ex) { TaskStatusText.Text = $"AI planning failed: {ex.Message}"; }
    }

    private void RebuildProposalChangesPanel()
    {
        using (UiBatcher.BeginBatch())
        {
            ProposalChangesPanel.Items.Clear();
            foreach (var change in _pendingChanges)
            {
                var kindBlock = new TextBlock { Text = change.Kind, Foreground = Brush("HavenAccentBrush"), FontSize = 11, FontWeight = Avalonia.Media.FontWeight.SemiBold };
                var descBlock = new TextBlock { Text = change.Description, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                var stack = new StackPanel { Children = { kindBlock, descBlock }, Margin = new Thickness(0, 0, 0, 8) };
                ProposalChangesPanel.Items.Add(stack);
            }
        }
    }

    private async Task ApplyProposalAsync()
    {
        if (_pendingProposal is null) return;
        try
        {
            await _proposals.ApplyAsync(_pendingProposal, CancellationToken.None);
            DismissProposal();
            AiPromptBox.Text = string.Empty;
            await RefreshAsync(CancellationToken.None);
            TaskStatusText.Text = "AI proposal applied.";
        }
        catch (Exception ex) { TaskStatusText.Text = $"Proposal could not be applied: {ex.Message}"; }
    }

    private void DismissProposal()
    {
        _pendingProposal = null;
        _pendingChanges.Clear();
        ProposalPanel.IsVisible = false;
        TaskStatusText.Text = "Proposal dismissed. No changes were made.";
    }

    // ============================================================
    //  CALENDAR SYNC
    // ============================================================

    private async Task ConnectProviderAsync(CalendarProviderItemViewModel item)
    {
        try
        {
            var result = await item.Provider.ConnectAsync(CancellationToken.None);
            item.Update(result.Status, result.Message);
            TaskStatusText.Text = result.Message;
            if (!result.Succeeded) return;

            var accounts = await _repository.GetCalendarAccountsAsync(CancellationToken.None);
            var account = accounts.Where(a => a.Provider == item.Provider.Kind)
                .OrderByDescending(a => a.UpdatedAt).FirstOrDefault();
            if (account is null) return;
            item.Update(CalendarSyncStatus.Syncing, "Running the initial calendar sync…");
            var sync = await item.Provider.SyncAsync(new CalendarSyncRequest(account.Id, true,
                DateTimeOffset.Now.AddMonths(-1), DateTimeOffset.Now.AddMonths(12)), CancellationToken.None);
            item.Update(sync.Status, sync.Message);
            await RefreshAsync(CancellationToken.None);
            TaskStatusText.Text = sync.Message;
        }
        catch (Exception ex) { TaskStatusText.Text = $"Calendar connection failed: {ex.Message}"; }
    }

    private async Task SyncProviderAsync(CalendarProviderItemViewModel item)
    {
        var accounts = await _repository.GetCalendarAccountsAsync(CancellationToken.None);
        var account = accounts.FirstOrDefault(a => a.Provider == item.Provider.Kind);
        if (account is null) { TaskStatusText.Text = $"Connect {item.Name} before synchronising."; return; }
        var result = await item.Provider.SyncAsync(new CalendarSyncRequest(account.Id, false,
            DateTimeOffset.Now.AddMonths(-1), DateTimeOffset.Now.AddMonths(12)), CancellationToken.None);
        item.Update(result.Status, result.Message);
        await RefreshAsync(CancellationToken.None);
        TaskStatusText.Text = result.Message;
    }

    private async Task DisconnectProviderAsync(CalendarProviderItemViewModel item)
    {
        try
        {
            var accounts = await _repository.GetCalendarAccountsAsync(CancellationToken.None);
            foreach (var account in accounts.Where(a => a.Provider == item.Provider.Kind))
                await item.Provider.DisconnectAsync(account.Id, CancellationToken.None);
            item.Update(CalendarSyncStatus.Disconnected, $"{item.Name} disconnected. Local calendar data is retained.");
            TaskStatusText.Text = item.Status;
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception ex) { TaskStatusText.Text = $"Calendar could not be disconnected: {ex.Message}"; }
    }

    private async Task SyncConnectedCalendarsAsync()
    {
        if (!_isActive || _calendarSyncRunning) return;
        _calendarSyncRunning = true;
        _calendarSyncCancellation?.Cancel();
        _calendarSyncCancellation?.Dispose();
        _calendarSyncCancellation = new CancellationTokenSource();
        var ct = _calendarSyncCancellation.Token;
        try
        {
            var now = DateTimeOffset.UtcNow;
            string? lastMessage = null;
            var accounts = await _repository.GetCalendarAccountsAsync(ct);
            foreach (var account in accounts.Where(a => a.Status != CalendarSyncStatus.Disconnected
                                                        && (a.LastSyncedAt is null || a.LastSyncedAt <= now.AddMinutes(-5))))
            {
                ct.ThrowIfCancellationRequested();
                var provider = _syncProviders.Get(account.Provider);
                if (!provider.IsConfigured) continue;
                var item = _providers.FirstOrDefault(p => p.Provider.Kind == account.Provider);
                item?.Update(CalendarSyncStatus.Syncing, "Synchronising in the background…");
                var result = await provider.SyncAsync(new CalendarSyncRequest(account.Id, false,
                    DateTimeOffset.Now.AddMonths(-1), DateTimeOffset.Now.AddMonths(12)), ct);
                item?.Update(result.Status, result.Message);
                lastMessage = result.Message;
            }
            if (_isActive) await RefreshAsync(ct);
            if (!string.IsNullOrWhiteSpace(lastMessage)) TaskStatusText.Text = lastMessage;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { TaskStatusText.Text = $"Background calendar sync failed: {ex.Message}"; }
        finally { _calendarSyncRunning = false; }
    }

    private async Task ResolveConflictAsync(CalendarConflictItemViewModel item, CalendarConflictResolution resolution)
    {
        try
        {
            await _repository.ResolveConflictAsync(item.Definition.Id, resolution, DateTimeOffset.UtcNow, CancellationToken.None);
            await RefreshAsync(CancellationToken.None);
            TaskStatusText.Text = resolution switch
            {
                CalendarConflictResolution.KeepHaven => "Kept Haven's version; it will be sent on the next calendar sync.",
                CalendarConflictResolution.KeepProvider => "Kept the calendar provider's version.",
                _ => "Kept the provider version and saved the Haven edit as a private local copy."
            };
        }
        catch (Exception ex) { TaskStatusText.Text = $"Calendar conflict could not be resolved: {ex.Message}"; }
    }

    // ============================================================
    //  DRAG-DROP
    // ============================================================

    private async void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryReadDraggedItem(e.DataTransfer, out _, out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnCalendarDayDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: PlannerCalendarDayViewModel day }
            || !TryReadDraggedItem(e.DataTransfer, out var kind, out var id)) return;
        e.Handled = true;
        if (kind == "task") await RescheduleTaskAsync(id, day.Date);
        else if (kind == "event") await RescheduleEventAsync(id, day.Date);
    }

    private async void OnBoardColumnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: PlannerBoardColumnViewModel column }
            || !TryReadDraggedItem(e.DataTransfer, out var kind, out var id) || kind != "task") return;
        e.Handled = true;
        await MoveTaskToStatusAsync(id, column.Status);
    }

    private async void OnDrop(object? sender, DragEventArgs e) { }

    private static async Task StartDragAsync(PointerPressedEventArgs e, string value)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(value));
        await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
    }

    private static bool TryReadDraggedItem(IDataTransfer data, out string kind, out Guid id)
    {
        kind = string.Empty;
        id = Guid.Empty;
        var text = data.TryGetText();
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[0] != "haven-plan" || parts[1] is not ("task" or "event") || !Guid.TryParse(parts[2], out id)) return false;
        kind = parts[1];
        return true;
    }

    // ============================================================
    //  HELPERS
    // ============================================================

    private static string ReadTags(string json)
    {
        return string.Join(", ", PlannerStudyAssignmentTags.GetUserTags(json));

    }

    private static DateTimeOffset LocalDay(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(local.Date));
    }

    private static Avalonia.Media.IBrush? Brush(string key)
        => Avalonia.Application.Current?.TryFindResource(key, out var value) == true ? value as Avalonia.Media.IBrush : null;

    private static DateTimeOffset StartOfWeek(DateTimeOffset value)
    {
        var day = LocalDay(value);
        var delta = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-delta);
    }
}
