using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Haven.Application;
using Haven.Automations;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Browser;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.ContainerSettings;
using Haven.Desktop.Views.Pages.StudioProject;
using Haven.Desktop.Views.Pages.WorkspaceEditor;
using Haven.Infrastructure;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView : UserControl, INotifyPropertyChanged, IDisposable
{
#pragma warning disable CS8618
    private readonly IConversationRepository _conversations;
    private readonly HavenEventBus _bus;
    private readonly IContainerRepository _containers;
    private readonly ICatalogRepository _catalog;
    private readonly IAutomationRepository _automations;
    private readonly IWorkspaceStateRepository _workspaceState;
    private readonly IWorkspaceToolService _workspaceTools;
    private readonly IProjectIntelligenceService _projectIntelligence;
    private readonly IOllamaClient _ollama;
    private readonly ChatSessionService _sessions;
    private readonly CapabilityPreflightService _preflight;
    private readonly BrowserSessionService _browser;
    private readonly BrowserDataService _browserData;
    private readonly WindowsAutomationRegistrationService _registration;
    private readonly AutomationRunner _automationRunner;
    private readonly ScheduleCalculator _scheduleCalculator;
    private readonly UserPreferencesService _preferences;
    private readonly ProjectCreationService _projectCreator;
    private readonly NotificationService _notifications;
    private readonly ITrainingRepository _trainingRepo;
    private readonly IContainerResourceRepository _containerResources;
    private readonly IDashboardRepository _dashboard;
    private readonly IDashboardLayoutRepository _dashboardLayout;
    private readonly IReadOnlyList<IDashboardTileProvider> _dashboardProviders;
    private readonly ICallCoordinator _callCoordinator;
    private readonly ISpeechModelManager _speechModels;
    private readonly IPlannerRepository _planner;
    private readonly IPlannerProposalService _plannerProposals;
    private readonly ICalendarSyncProviderRegistry _calendarProviders;
    private readonly SurfaceOrchestrationService _surfaceOrchestration;
    private readonly IModeRegistry _modeRegistry;
    private readonly IModeUsageRepository _modeUsage;
    private readonly IPinRepository _pins;
    private readonly CompanionDockViewModel _companionDockVm;
    private readonly Dictionary<HavenMode, ChatPage> _chats = [];
    private readonly Dictionary<Guid, ChatPage> _projectChats = [];
    private readonly Dictionary<Guid, StudioProjectPage> _projectPages = [];
    private readonly Dictionary<Guid, ChatPage> _groupChats = [];
    private readonly Dictionary<Guid, ChatGroupPageViewModel> _groupPages = [];
    private HomePageViewModel? _homePage;
    private CallPageViewModel? _callPage;
    private PlanPageViewModel? _planPage;
    private readonly DispatcherTimer _reminderTimer;
    private int _isPollingReminders;
    private object? _currentPage;
    private ChatPage _currentChat;
    private WorkspaceTabViewModel? _selectedTab;
    private string _startupStatus = "Starting Haven\u2026";
    private string _searchQuery = string.Empty;
    private string _commandSearch = string.Empty;
    private bool _isSidebarOpen = true;
    private bool _isCommandPaletteOpen;
    private bool _isRenameOpen;
    private bool _isDeleteConfirmationOpen;
    private string _renameDraft = string.Empty;
    private ContainerDefinition? _activeProject;
    private StudioProjectPage? _activeProjectPage;
    private readonly HavenEventBus _eventBus;
    private object? _previousContent;

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void RaisePropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        RaisePropertyChanged(name);
        return true;
    }

    public MainView(
        HavenEventBus bus,
        IConversationRepository conversations,
        IContainerRepository containers,
        ICatalogRepository catalog,
        IAutomationRepository automations,
        IWorkspaceStateRepository workspaceState,
        IWorkspaceToolService workspaceTools,
        IProjectIntelligenceService projectIntelligence,
        IProviderModelClient ollama,
        ChatSessionService sessions,
        CapabilityPreflightService preflight,
        BrowserSessionService browser,
        BrowserDataService browserData,
        WindowsAutomationRegistrationService registration,
        AutomationRunner automationRunner,
        ScheduleCalculator scheduleCalculator,
        UserPreferencesService preferences,
        ProjectCreationService projectCreator,
        NotificationService notifications,
        ITrainingRepository trainingRepo,
        IContainerResourceRepository containerResources,
        IDashboardRepository dashboard,
        IDashboardLayoutRepository dashboardLayout,
        IDashboardTileProviderRegistry dashboardProviders,
        ICallCoordinator callCoordinator,
        ISpeechModelManager speechModels,
        IPlannerRepository planner,
        IPlannerProposalService plannerProposals,
        ICalendarSyncProviderRegistry calendarProviders,
        SurfaceOrchestrationService surfaceOrchestration,
        IModeRegistry modeRegistry,
        IModeUsageRepository modeUsage,
        IPinRepository pins)
    {
        _eventBus = bus;
        _bus = bus;
        _conversations = conversations;
        _containers = containers;
        _catalog = catalog;
        _automations = automations;
        _workspaceState = workspaceState;
        _workspaceTools = workspaceTools;
        _projectIntelligence = projectIntelligence;
        _ollama = ollama;
        _sessions = sessions;
        _preflight = preflight;
        _browser = browser;
        _browserData = browserData;
        _registration = registration;
        _automationRunner = automationRunner;
        _scheduleCalculator = scheduleCalculator;
        _preferences = preferences;
        _projectCreator = projectCreator;
        _notifications = notifications;
        _trainingRepo = trainingRepo;
        _containerResources = containerResources;
        _dashboard = dashboard;
        _dashboardLayout = dashboardLayout;
        _dashboardProviders = dashboardProviders.Providers;
        _callCoordinator = callCoordinator;
        _speechModels = speechModels;
        _planner = planner;
        _plannerProposals = plannerProposals;
        _calendarProviders = calendarProviders;
        _surfaceOrchestration = surfaceOrchestration;
        _modeRegistry = modeRegistry;
        _modeUsage = modeUsage;
        _pins = pins;
        _companionDockVm = new CompanionDockViewModel(new Haven.Infrastructure.CompanionDockService(), _conversations);
        _reminderTimer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background,
            async (_, _) => await PollPlannerRemindersAsync());

        NavigateChatCommand = new AsyncRelayCommand(() => NavigateModeAsync(HavenMode.Chat, false));
        NavigateTeachCommand = new AsyncRelayCommand(() => NavigateModeAsync(HavenMode.Teach, false));
        NavigateDoCommand = new AsyncRelayCommand(() => NavigateModeAsync(HavenMode.Do, true));
        NavigateStudioCommand = new AsyncRelayCommand(() => NavigateModeAsync(HavenMode.Studio, true));
        NavigateBrowserCommand = new RelayCommand(OpenBrowser);
        NavigateTrainingCommand = new RelayCommand(OpenTraining);
        NavigateHomeCommand = new AsyncRelayCommand(OpenHomeAsync);
        NavigateCallCommand = new AsyncRelayCommand(OpenCallAsync);
        OpenLiveCallCommand = new AsyncRelayCommand(OpenCallAsync);
        EndLiveCallCommand = new AsyncRelayCommand(() => _callCoordinator.EndAsync(CancellationToken.None));
        NavigatePlanCommand = new RelayCommand(OpenPlan);
        NavigateAgentsCommand = new RelayCommand(() => OpenCatalog(CatalogPageKind.Agents));
        NavigatePluginsCommand = new RelayCommand(() => OpenCatalog(CatalogPageKind.Plugins));
        NavigatePromptsCommand = new RelayCommand(() => OpenCatalog(CatalogPageKind.Prompts));
        NavigateAutomationsCommand = new RelayCommand(OpenAutomations);
        NavigateMacrosCommand = new RelayCommand(OpenMacros);
        NavigateArchiveCommand = new RelayCommand(OpenArchive);
        NavigateActivityLogCommand = new RelayCommand(OpenActivityLog);
        DismissNotificationCommand = new RelayCommand<Guid>(id => _notifications.Dismiss(id));
        NavigateSettingsCommand = new RelayCommand(OpenApplicationSettings);
        NavigateContainerSettingsCommand = new RelayCommand(OpenContainerSettings);
        NavigateCurrentChatCommand = new AsyncRelayCommand(NavigateCurrentChatAsync);
        NewChatCommand = new RelayCommand(StartNewConversation);
        NewContainerCommand = new RelayCommand(OpenNewContainer);
        DeleteContainerCommand = new AsyncRelayCommand<ContainerItemViewModel>(item =>
        {
            if (item is not null && CurrentChat.DeleteContainerCommand.CanExecute(item))
                CurrentChat.DeleteContainerCommand.Execute(item);
            return Task.CompletedTask;
        });
        NewProjectChatCommand = new AsyncRelayCommand(() => StartProjectChatAsync(string.Empty));
        NavigateProjectHomeCommand = new RelayCommand(OpenActiveProjectHome);
        ToggleTemporaryCommand = new RelayCommand(() => CurrentChat.ToggleTemporaryCommand.Execute(null));
        RefreshModelsCommand = new RelayCommand(() => CurrentChat.RefreshModelsCommand.Execute(null));
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarOpen = !IsSidebarOpen);
        SelectContainerCommand = new AsyncRelayCommand<ContainerItemViewModel>(SelectContainerAsync);
        OpenCommandPaletteCommand = new RelayCommand(OpenCommandPalette);
        CloseCommandPaletteCommand = new RelayCommand(() => IsCommandPaletteOpen = false);
        SelectTabCommand = new RelayCommand<WorkspaceTabViewModel>(item => SelectedTab = item);
        CloseTabCommand = new RelayCommand<WorkspaceTabViewModel>(CloseTab);
        AddNewTabCommand = new RelayCommand(AddNewTab);
        NavigateBackCommand = new RelayCommand(NavigateBack, () => SelectedTab?.CanGoBack == true);
        NavigateForwardCommand = new RelayCommand(NavigateForward, () => SelectedTab?.CanGoForward == true);
        BranchCurrentCommand = new AsyncRelayCommand(() => CurrentChat.BranchCurrentAsync());
        CompactCurrentCommand = new AsyncRelayCommand(() => CurrentChat.CompactContextAsync());
        ArchiveCurrentCommand = new AsyncRelayCommand(() => CurrentChat.ArchiveCurrentAsync());
        TogglePinCurrentCommand = new AsyncRelayCommand(TogglePinCurrentAsync);
        BeginRenameCurrentCommand = new RelayCommand(BeginRenameCurrent);
        SaveRenameCurrentCommand = new AsyncRelayCommand(SaveRenameCurrentAsync, () => !string.IsNullOrWhiteSpace(RenameDraft));
        CancelRenameCurrentCommand = new RelayCommand(() => IsRenameOpen = false);
        RequestDeleteCurrentCommand = new RelayCommand(() => IsDeleteConfirmationOpen = CurrentChat.HasMessages);
        ConfirmDeleteCurrentCommand = new AsyncRelayCommand(DeleteCurrentAsync);
        CancelDeleteCurrentCommand = new RelayCommand(() => IsDeleteConfirmationOpen = false);
        ConfigureModelCommand = new RelayCommand(() => CurrentChat.IsModelPickerOpen = true);
        CopyLastResponseCommand = new RelayCommand(CopyLastResponse);
        DictateCommand = new RelayCommand(() => DictateRequested?.Invoke(this, EventArgs.Empty));
        UndoCurrentCommand = new RelayCommand(() => (CurrentPage as WorkspaceEditorPage)?.UndoCommand.Execute(null));
        RedoCurrentCommand = new RelayCommand(() => (CurrentPage as WorkspaceEditorPage)?.RedoCommand.Execute(null));
        SaveCurrentCommand = new RelayCommand(() => (CurrentPage as WorkspaceEditorPage)?.SaveCommand.Execute(null));
        BuildBrowserExtensionCommand = new RelayCommand(() =>
        {
            OpenCurrentChatTab();
            CurrentChat.UsePrompt(">Rigid Build a limited-scope Haven Browse extension. Ask for the allowed origins and behavior, then create a declarative manifest and content script without privileged APIs.");
        });
        SwitchSurfaceCommand = new AsyncRelayCommand<string>(SwitchSurfaceAsync);
        NavigateModeLibraryCommand = new RelayCommand(OpenModeLibrary);

        // Initialize chat BEFORE setting DataContext so bindings don't fail
        _currentChat = CreateChat(HavenMode.Chat);
        _chats[HavenMode.Chat] = _currentChat;
        AttachChat(_currentChat);
        _currentPage = _currentChat;
        AddOrSelectTab("chat-general", "General chat", _currentChat, false, HavenSurface.Chat);

        InitializeComponent();
        DataContext = this;

        _callCoordinator.StateChanged += OnCallStateChanged;
        BuildCommandPalette();
    }

    public event EventHandler<string>? CopyRequested;
    public event EventHandler? DictateRequested;
    public ObservableCollection<RecentConversationViewModel> PinnedConversations { get; } = [];
    public ObservableCollection<RecentConversationViewModel> RecentConversations { get; } = [];
    public ObservableCollection<WorkspaceTabViewModel> OpenTabs { get; } = [];
    public ObservableCollection<CommandPaletteItemViewModel> CommandItems { get; } = [];
    public ObservableCollection<ToastNotification> Notifications => _notifications.Notifications;
    private IReadOnlyList<CommandPaletteItemViewModel> AllCommandItems { get; set; } = [];
    public CompanionDockViewModel CompanionDock => _companionDockVm;

    public HavenEventBus EventBus => _eventBus;

    public ChatPage CurrentChat
    {
        get => _currentChat;
        private set
        {
            if (ReferenceEquals(_currentChat, value)) return;
            DetachChat(_currentChat);
            _currentChat = value;
            AttachChat(value);
            RaisePropertyChanged();
            RaiseShellProperties();
        }
    }

    public object? CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            RaisePropertyChanged(nameof(IsChatVisible));
            RaisePropertyChanged(nameof(IsPageVisible));
            RaisePropertyChanged(nameof(IsBrowseMode));
            RaisePropertyChanged(nameof(IsTrainingMode));
            RaisePropertyChanged(nameof(IsSidebarVisible));
            RaisePropertyChanged(nameof(HasFullSidebar));
            RaisePropertyChanged(nameof(HasCompactSidebar));
            RaisePropertyChanged(nameof(IsWorkspaceHeaderVisible));
            RaisePropertyChanged(nameof(ProductName));
        }
    }

    public WorkspaceTabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (ReferenceEquals(_selectedTab, value) || value is null) return;
            if (_selectedTab is not null)
            {
                _selectedTab.IsSelected = false;
                if (_selectedTab.Page is IActivatablePage previous) previous.Deactivate();
            }
            if (!SetProperty(ref _selectedTab, value)) return;
            value.IsSelected = true;
            ApplySelectedTab(value);
        }
    }

    private void ApplySelectedTab(WorkspaceTabViewModel value)
    {
        if (value.Page is ChatPage chat)
        {
            CurrentChat = chat;
            if (chat.Mode == HavenMode.Studio && chat.SelectedContainer is not null)
                ActivateProject(chat.SelectedContainer.Definition);
            else if (chat.Mode == HavenMode.Studio)
                ClearActiveProject();
        }
        else if (value.Page is StudioProjectPage project)
        {
            ActivateProject(project.Definition, project);
            if (_projectChats.TryGetValue(project.ProjectId, out var projectChat)) CurrentChat = projectChat;
        }
        else if (value.Page is WorkspaceEditorPage editor)
        {
            ActivateProject(editor.Container);
            if (_projectChats.TryGetValue(editor.Container.Id, out var projectChat)) CurrentChat = projectChat;
        }

        CurrentPage = value.Page;
        NavigateBackCommand.RaiseCanExecuteChanged();
        NavigateForwardCommand.RaiseCanExecuteChanged();
        RaiseShellProperties();
        if (value.Page is IActivatablePage activatable)
            _ = activatable.ActivateAsync(CancellationToken.None);
    }

    public bool IsChatVisible => ReferenceEquals(CurrentPage, CurrentChat);
    public bool IsPageVisible => !IsChatVisible;
    public HavenSurface CurrentSurface => SelectedTab?.Surface ?? HavenSurface.Chat;
    public bool IsBrowseMode => CurrentSurface == HavenSurface.Browse;
    public bool IsTrainingMode => CurrentSurface == HavenSurface.Training;
    public bool IsProjectOpen => CurrentSurface == HavenSurface.Studio && ActiveProject is not null;
    public bool IsWorkspaceHeaderVisible => IsChatVisible;
    public bool IsHorizontalTabsVisible => OpenTabs.Count > 0;

    public ContainerDefinition? ActiveProject
    {
        get => _activeProject;
        private set
        {
            if (!SetProperty(ref _activeProject, value)) return;
            RaisePropertyChanged(nameof(IsProjectOpen));
            RaisePropertyChanged(nameof(ActiveProjectName));
            RaisePropertyChanged(nameof(ActiveProjectRoot));
            RaisePropertyChanged(nameof(RecentHeading));
            RaisePropertyChanged(nameof(ShowNoProjectChats));
        }
    }

    public StudioProjectPage? ActiveProjectPage
    {
        get => _activeProjectPage;
        private set => SetProperty(ref _activeProjectPage, value);
    }

    public string ActiveProjectName => ActiveProject?.Name ?? "Project";
    public string ActiveProjectRoot => ActiveProject?.RootPath ?? "Folder not connected";

    public string StartupStatus { get => _startupStatus; private set => SetProperty(ref _startupStatus, value); }

    public string SearchQuery
    {
        get => _searchQuery;
        set { if (SetProperty(ref _searchQuery, value)) _ = RefreshRecentsAsync(CancellationToken.None); }
    }

    public string CommandSearch
    {
        get => _commandSearch;
        set
        {
            if (!SetProperty(ref _commandSearch, value)) return;
            FilterCommands();
        }
    }

    public bool IsCommandPaletteOpen { get => _isCommandPaletteOpen; private set => SetProperty(ref _isCommandPaletteOpen, value); }
    public bool IsRenameOpen { get => _isRenameOpen; private set => SetProperty(ref _isRenameOpen, value); }
    public bool IsDeleteConfirmationOpen { get => _isDeleteConfirmationOpen; private set => SetProperty(ref _isDeleteConfirmationOpen, value); }
    public string RenameDraft { get => _renameDraft; set { if (SetProperty(ref _renameDraft, value)) SaveRenameCurrentCommand.RaiseCanExecuteChanged(); } }

    public bool IsSidebarOpen
    {
        get => _isSidebarOpen;
        set
        {
            if (!SetProperty(ref _isSidebarOpen, value)) return;
            RaisePropertyChanged(nameof(IsSidebarClosed));
            RaisePropertyChanged(nameof(IsSidebarVisible));
            RaisePropertyChanged(nameof(HasFullSidebar));
            RaisePropertyChanged(nameof(HasCompactSidebar));
        }
    }

    public bool IsSidebarClosed => !IsSidebarOpen;
    public bool SupportsConversationSidebar => CurrentSurface is HavenSurface.Chat or HavenSurface.Teach or HavenSurface.Do or HavenSurface.Studio;
    public bool SupportsConversationCommands => SupportsConversationSidebar;
    public bool SupportsEditingCommands => SupportsConversationCommands || CurrentPage is WorkspaceEditorPage;
    public bool IsSidebarVisible => SupportsConversationSidebar;
    public bool HasFullSidebar => IsSidebarOpen && SupportsConversationSidebar;
    public bool HasCompactSidebar => !IsSidebarOpen && SupportsConversationSidebar;
    public bool HasPinnedConversations => PinnedConversations.Count > 0;
    public bool HasRecentConversations => RecentConversations.Count > 0;
    public bool ShowNoProjectChats => IsProjectOpen && !HasPinnedConversations && !HasRecentConversations;
    public HavenMode CurrentMode => CurrentChat?.Mode ?? HavenMode.Chat;
    public bool IsTeach => CurrentSurface == HavenSurface.Teach;
    public bool IsChatProduct => CurrentSurface is HavenSurface.Chat or HavenSurface.Teach;
    public bool HasContainers => CurrentChat?.HasContainers ?? false;
    public bool HasAnyContainers => CurrentChat?.HasAnyContainers ?? false;
    public bool SupportsDuo => CurrentChat?.SupportsDuo ?? false;
    public string ChatTypeLabel => CurrentSurface == HavenSurface.Teach ? "Teaching" : "General";

    public string ProductName => CurrentSurface switch
    {
        HavenSurface.Home => "Haven Home",
        HavenSurface.Chat or HavenSurface.Teach => "Haven Chat",
        HavenSurface.Call => "Haven Voice",
        HavenSurface.Do => "Haven Do",
        HavenSurface.Studio => "Haven Studio",
        HavenSurface.Browse => "Haven Browse",
        HavenSurface.Plan => "Haven Plan",
        HavenSurface.Training => "Haven Training",
        _ => "Haven"
    };

    public string NewItemLabel => CurrentMode switch
    {
        HavenMode.Do => "+ New task",
        HavenMode.Teach => "+ Quick chat",
        HavenMode.Studio => "+ New studio chat",
        _ => "New chat"
    };

    public string FileNewLabel => CurrentMode switch { HavenMode.Do => "New task", HavenMode.Teach => "New teaching chat", HavenMode.Studio => "New studio chat", _ => "New chat" };
    public string FileNewContainerLabel => CurrentMode switch { HavenMode.Chat => "New Chat Group", HavenMode.Teach => "New Subject", HavenMode.Do => "New Task Group", _ => "New Project" };
    public string ContainerHeading => CurrentMode switch { HavenMode.Chat => "Chat Groups", HavenMode.Teach => "Subjects", HavenMode.Do => "Task Groups", _ => "Projects" };
    public string ProjectMenuHeader => CurrentMode switch { HavenMode.Chat => "Chat Group", HavenMode.Teach => "Subject", HavenMode.Do => "Task Group", _ => "Project" };

    public string WorkspaceEyebrow => CurrentMode switch
    {
        HavenMode.Chat => CurrentChat?.SelectedContainer?.Name ?? "Chat",
        HavenMode.Teach => "Lesson",
        HavenMode.Do => "Task Group",
        HavenMode.Studio => "Project",
        _ => "Haven"
    };

    public string WorkspaceTitle => CurrentMode == HavenMode.Chat
        ? CurrentChat?.ConversationTitle ?? "Chat"
        : CurrentChat?.SelectedLesson?.Name ?? CurrentChat?.SelectedContainer?.Name ?? "Local workspace";

    public bool ShowTemporaryHeaderAction => CurrentMode == HavenMode.Chat && CurrentChat?.ShowTemporaryHeaderAction == true;
    public bool ShowContextHeaderWidget => CurrentMode == HavenMode.Chat && CurrentChat?.ShowContextHeaderWidget == true;
    public string TemporaryHeaderActionLabel => CurrentChat?.TemporaryHeaderActionLabel ?? string.Empty;
    public int ContextPercent => CurrentChat?.ContextPercent ?? 0;
    public int ContextRemainingPercent => CurrentChat?.ContextRemainingPercent ?? 0;
    public string ContextLabel => CurrentChat?.ContextLabel ?? string.Empty;
    public string ContextRemainingLabel => CurrentChat?.ContextRemainingLabel ?? string.Empty;

    public string RecentHeading => CurrentMode switch { HavenMode.Do => "Tasks", HavenMode.Teach => "Teaching chats", HavenMode.Studio when IsProjectOpen => "Project chats", HavenMode.Studio => "Standalone chats", _ => "Chats" };

    public string ContainerSettingsLabel => CurrentMode switch
    {
        HavenMode.Teach when CurrentChat?.SelectedLesson is not null => "Lesson settings",
        HavenMode.Teach => "Subject settings",
        HavenMode.Do => "Task Group settings",
        HavenMode.Chat => "Chat Group settings",
        _ => "Project settings"
    };

    public string OllamaStatus => CurrentChat?.Status ?? string.Empty;
    public string DuoLabel => CurrentChat?.IsDuoPluginActive == true ? CurrentChat?.SelectedDuo.ToString() ?? "Solo" : "Solo";
    public bool HasLiveCall => _callCoordinator.IsActive;
    public string LiveCallLabel => _callCoordinator.State == CallState.Paused ? "Call paused" : $"Live call \u00b7 {_callCoordinator.State}";

    public AsyncRelayCommand NavigateChatCommand { get; }
    public AsyncRelayCommand NavigateTeachCommand { get; }
    public AsyncRelayCommand NavigateDoCommand { get; }
    public AsyncRelayCommand NavigateStudioCommand { get; }
    public RelayCommand NavigateBrowserCommand { get; }
    public RelayCommand NavigateTrainingCommand { get; }
    public AsyncRelayCommand NavigateHomeCommand { get; }
    public AsyncRelayCommand NavigateCallCommand { get; }
    public AsyncRelayCommand OpenLiveCallCommand { get; }
    public AsyncRelayCommand EndLiveCallCommand { get; }
    public RelayCommand NavigatePlanCommand { get; }
    public RelayCommand NavigateAgentsCommand { get; }
    public RelayCommand NavigatePluginsCommand { get; }
    public RelayCommand NavigatePromptsCommand { get; }
    public RelayCommand NavigateAutomationsCommand { get; }
    public RelayCommand NavigateMacrosCommand { get; }
    public RelayCommand NavigateArchiveCommand { get; }
    public RelayCommand NavigateActivityLogCommand { get; }
    public RelayCommand<Guid> DismissNotificationCommand { get; }
    public RelayCommand NavigateSettingsCommand { get; }
    public RelayCommand NavigateContainerSettingsCommand { get; }
    public AsyncRelayCommand NavigateCurrentChatCommand { get; }
    public RelayCommand NewChatCommand { get; }
    public RelayCommand NewContainerCommand { get; }
    public AsyncRelayCommand<ContainerItemViewModel> DeleteContainerCommand { get; }
    public AsyncRelayCommand NewProjectChatCommand { get; }
    public RelayCommand NavigateProjectHomeCommand { get; }
    public RelayCommand ToggleTemporaryCommand { get; }
    public RelayCommand RefreshModelsCommand { get; }
    public RelayCommand ToggleSidebarCommand { get; }
    public AsyncRelayCommand<ContainerItemViewModel> SelectContainerCommand { get; }
    public RelayCommand OpenCommandPaletteCommand { get; }
    public RelayCommand CloseCommandPaletteCommand { get; }
    public RelayCommand<WorkspaceTabViewModel> SelectTabCommand { get; }
    public RelayCommand<WorkspaceTabViewModel> CloseTabCommand { get; }
    public RelayCommand AddNewTabCommand { get; }
    public RelayCommand NavigateBackCommand { get; }
    public RelayCommand NavigateForwardCommand { get; }
    public AsyncRelayCommand BranchCurrentCommand { get; }
    public AsyncRelayCommand CompactCurrentCommand { get; }
    public AsyncRelayCommand ArchiveCurrentCommand { get; }
    public AsyncRelayCommand TogglePinCurrentCommand { get; }
    public RelayCommand BeginRenameCurrentCommand { get; }
    public AsyncRelayCommand SaveRenameCurrentCommand { get; }
    public RelayCommand CancelRenameCurrentCommand { get; }
    public RelayCommand RequestDeleteCurrentCommand { get; }
    public AsyncRelayCommand ConfirmDeleteCurrentCommand { get; }
    public RelayCommand CancelDeleteCurrentCommand { get; }
    public RelayCommand ConfigureModelCommand { get; }
    public RelayCommand CopyLastResponseCommand { get; }
    public RelayCommand DictateCommand { get; }
    public RelayCommand UndoCurrentCommand { get; }
    public RelayCommand RedoCurrentCommand { get; }
    public RelayCommand SaveCurrentCommand { get; }
    public RelayCommand BuildBrowserExtensionCommand { get; }
    public AsyncRelayCommand<string> SwitchSurfaceCommand { get; }
    public RelayCommand NavigateModeLibraryCommand { get; }
#pragma warning restore CS8618

    public async Task InitializeAsync(LegacyMigrationResult migration, CancellationToken cancellationToken)
    {
        await CurrentChat.InitializeAsync(cancellationToken);
        await RefreshRecentsAsync(cancellationToken);
        await PollPlannerRemindersAsync();
        _reminderTimer.Start();
        _companionDockVm.Start();
        StartupStatus = migration.Imported
            ? $"Imported {migration.ConversationCount} legacy conversations \u00b7 local-only"
            : "Local-only \u00b7 SQLite ready";
    }

    public void SetStartupError(string message) => StartupStatus = $"Startup problem: {message}";

    private async Task PollPlannerRemindersAsync()
    {
        if (Interlocked.Exchange(ref _isPollingReminders, 1) != 0) return;
        try
        {
            foreach (var reminder in await _planner.GetDueRemindersAsync(DateTimeOffset.UtcNow, 20, CancellationToken.None))
            {
                _notifications.Show("Planner reminder", reminder.Title, ToastKind.Info, TimeSpan.FromSeconds(12));
                await _planner.MarkReminderDeliveredAsync(reminder, DateTimeOffset.UtcNow, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Planner reminders] {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isPollingReminders, 0);
        }
    }

    public void OpenCommandPalette()
    {
        CommandSearch = string.Empty;
        BuildCommandPalette();
        IsCommandPaletteOpen = true;
    }

    private Task OpenHomeAsync()
    {
        _homePage ??= new HomePageViewModel(
            _dashboard,
            _dashboardLayout,
            _ollama,
            _catalog,
            _dashboardProviders,
            new Dictionary<string, Func<Task>>(StringComparer.OrdinalIgnoreCase)
            {
                ["new-chat"] = async () => { await NavigateModeAsync(HavenMode.Chat, false); StartNewConversation(); },
                ["chat"] = () => NavigateModeAsync(HavenMode.Chat, false),
                ["teach"] = () => NavigateModeAsync(HavenMode.Teach, false),
                ["call"] = OpenCallAsync,
                ["plan"] = () => { OpenPlan(); return Task.CompletedTask; },
                ["browse"] = () => { OpenBrowser(); return Task.CompletedTask; },
                ["studio"] = () => NavigateModeAsync(HavenMode.Studio, true),
                ["automations"] = () => { OpenAutomations(); return Task.CompletedTask; }
            });
        AddOrSelectTab("home", "Home", _homePage, false, HavenSurface.Home);
        return Task.CompletedTask;
    }

    private async Task OpenCallAsync()
    {
        _callPage ??= new CallPageViewModel(_callCoordinator, _ollama, _speechModels);
        await _callPage.InitializeAsync();
        AddOrSelectTab("call", "Call", _callPage, false, HavenSurface.Call);
    }

    private void OpenPlan()
    {
        _planPage ??= new PlanPageViewModel(_planner, _plannerProposals, _calendarProviders, _ollama);
        AddOrSelectTab("plan", "Plan", _planPage, false, HavenSurface.Plan);
    }

    private async Task NavigateModeAsync(HavenMode mode, bool showHome)
    {
        var page = await GetOrCreateChatAsync(mode);
        CurrentChat = page;
        if (showHome) await OpenModeHomeAsync();
        else AddOrSelectTab(mode == HavenMode.Teach ? "chat-teach" : "chat-general", mode == HavenMode.Teach ? "Teaching" : "General chat", page, false, SurfaceForMode(mode));
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task<ChatPage> GetOrCreateChatAsync(HavenMode mode)
    {
        if (_chats.TryGetValue(mode, out var existing))
        {
            await existing.RefreshCatalogAsync(CancellationToken.None);
            return existing;
        }
        var page = CreateChat(mode);
        _chats[mode] = page;
        await page.InitializeAsync(CancellationToken.None);
        if (mode == HavenMode.Studio) page.SelectedContainer = null;
        return page;
    }

    private async Task OpenModeHomeAsync()
    {
        if (CurrentMode is not (HavenMode.Do or HavenMode.Studio))
        {
            AddOrSelectTab(CurrentMode == HavenMode.Teach ? "chat-teach" : "chat-general", ChatTypeLabel, CurrentChat, false, SurfaceForMode(CurrentMode));
            return;
        }
        if (CurrentMode == HavenMode.Studio)
        {
            if (_chats.TryGetValue(HavenMode.Studio, out var standalone))
            {
                standalone.SelectedContainer = null;
                CurrentChat = standalone;
            }
            ClearActiveProject();
        }
        var key = CurrentMode == HavenMode.Studio ? "studio-home" : "do-home";
        var page = new WorkspaceHomePageViewModel(CurrentMode, _containers, _conversations, _automations, _workspaceState, _projectIntelligence,
            OpenContainerDefinitionAsync, CurrentMode == HavenMode.Studio ? OpenProjectCreatorAsync : null);
        AddOrSelectTab(key, CurrentMode == HavenMode.Studio ? "Studio Home" : "Do Home", page, false, SurfaceForMode(CurrentMode));
    }

    private void OpenNewContainer()
    {
        if (CurrentMode == HavenMode.Studio)
        {
            _ = OpenProjectCreatorAsync();
            return;
        }
        if (CurrentChat.NewContainerCommand.CanExecute(null)) CurrentChat.NewContainerCommand.Execute(null);
    }

    private Task OpenProjectCreatorAsync()
    {
        var page = new ProjectCreatorPageViewModel(_projectCreator, OpenCreatedProjectAsync);
        AddOrSelectTab("new-project", "New project", page, true);
        return Task.CompletedTask;
    }

    private async Task OpenCreatedProjectAsync(ContainerDefinition definition)
    {
        if (!_chats.TryGetValue(HavenMode.Studio, out var standalone)) standalone = await GetOrCreateChatAsync(HavenMode.Studio);
        await standalone.RefreshContainersAsync(CancellationToken.None);
        standalone.SelectedContainer = null;
        await OpenContainerDefinitionAsync(definition);
        await StartProjectChatAsync(string.Empty);
    }

    private async Task<ChatPage> GetOrCreateProjectChatAsync(ContainerDefinition definition)
    {
        if (!_projectChats.TryGetValue(definition.Id, out var chat))
        {
            chat = CreateChat(HavenMode.Studio);
            _projectChats[definition.Id] = chat;
            await chat.InitializeAsync(CancellationToken.None);
        }
        else await chat.RefreshContainersAsync(CancellationToken.None);
        chat.SelectedContainer = chat.Containers.FirstOrDefault(item => item.Id == definition.Id);
        return chat;
    }

    private async Task<ChatPage> GetOrCreateGroupChatAsync(ContainerDefinition definition)
    {
        if (!_groupChats.TryGetValue(definition.Id, out var chat))
        {
            chat = CreateChat(HavenMode.Chat);
            _groupChats[definition.Id] = chat;
            await chat.InitializeAsync(CancellationToken.None);
        }
        else
        {
            await chat.RefreshContainersAsync(CancellationToken.None);
        }
        chat.SelectedContainer = chat.Containers.FirstOrDefault(item => item.Id == definition.Id);
        return chat;
    }

    private ChatGroupPageViewModel GetOrCreateGroupPage(ContainerDefinition definition)
    {
        if (_groupPages.TryGetValue(definition.Id, out var page)) return page;
        page = new ChatGroupPageViewModel(
            definition,
            _conversations,
            _containers,
            _containerResources,
            StartGroupChatAsync,
            OpenGroupedConversationAsync,
            OpenGroupSettingsAsync,
            () => CloseGroupPageAsync(definition.Id));
        _groupPages[definition.Id] = page;
        return page;
    }

    private async Task OpenChatGroupAsync(ContainerDefinition definition)
    {
        var page = GetOrCreateGroupPage(definition);
        await page.InitializeAsync(CancellationToken.None);
        AddOrSelectTab("group-" + definition.Id.ToString("N"), definition.Name, page, true, HavenSurface.Chat);
    }

    private async Task StartGroupChatAsync(ContainerDefinition definition)
    {
        var chat = await GetOrCreateGroupChatAsync(definition);
        CurrentChat = chat;
        chat.NewChat();
        AddOrSelectTab("group-chat-" + definition.Id.ToString("N"), definition.Name + " chat", chat, true, HavenSurface.Chat);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task OpenGroupedConversationAsync(Conversation conversation)
    {
        if (conversation.ContainerId is not Guid groupId) return;
        var definition = (await _containers.GetByModeAsync(HavenMode.Chat, CancellationToken.None))
            .FirstOrDefault(item => item.Id == groupId);
        if (definition is null) return;
        var chat = await GetOrCreateGroupChatAsync(definition);
        CurrentChat = chat;
        AddOrSelectTab("group-chat-" + groupId.ToString("N"), conversation.Title, chat, true, HavenSurface.Chat);
        await chat.LoadConversationAsync(conversation.Id, CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private Task OpenGroupSettingsAsync(ContainerDefinition definition)
    {
        var item = new ContainerItemViewModel(definition);
        AddOrSelectTab(
            "container-settings-" + definition.Id.ToString("N"),
            "Chat Group settings",
            new ContainerSettingsPage(null, item, _containers, async () =>
            {
                await RefreshAfterSettingsAsync();
                _groupPages.Remove(definition.Id);
            }, () => ReturnToChatGroupAsync(definition.Id)),
            true,
            HavenSurface.Chat);
        return Task.CompletedTask;
    }

    private Task CloseGroupPageAsync(Guid groupId)
    {
        _groupPages.Remove(groupId);
        _groupChats.Remove(groupId);
        var tabs = OpenTabs.Where(item => item.Key.Contains(groupId.ToString("N"), StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var tab in tabs) CloseTab(tab);
        return NavigateModeAsync(HavenMode.Chat, false);
    }

    private StudioProjectPage GetOrCreateProjectPage(ContainerDefinition definition)
    {
        if (_projectPages.TryGetValue(definition.Id, out var page)) return page;
        page = new StudioProjectPage(definition, _conversations, _containers, _automations, _workspaceState, _projectIntelligence,
            file => OpenFileAsync(definition, file), StartProjectChatAsync, _modeRegistry, _catalog, _ollama);
        _projectPages[definition.Id] = page;
        return page;
    }

    private void ActivateProject(ContainerDefinition definition, StudioProjectPage? page = null)
    {
        ActiveProject = definition;
        ActiveProjectPage = page ?? GetOrCreateProjectPage(definition);
        RaiseShellProperties();
    }

    private void ClearActiveProject()
    {
        ActiveProject = null;
        ActiveProjectPage = null;
        RaiseShellProperties();
    }

    private void OpenActiveProjectHome()
    {
        if (ActiveProject is null) return;
        var page = ActiveProjectPage ?? GetOrCreateProjectPage(ActiveProject);
        AddOrSelectTab("project-" + ActiveProject.Id.ToString("N"), ActiveProject.Name, page, true);
    }

    private void OpenBrowser()
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "browse");
        if (existing is not null) { SelectedTab = existing; return; }
        var page = new BrowserPage(_bus, _browser, _browserData, _ollama, _preferences);
        AddOrSelectTab("browse", "Browse", page, true);
    }

    private void OpenTraining()
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "training");
        if (existing is not null) { SelectedTab = existing; return; }
        var runner = new TrainingRunner(_sessions, _conversations, _ollama, _preferences);
        var page = new TrainingPageViewModel(runner, _trainingRepo, _conversations, _ollama, _preferences,
            msg => System.Diagnostics.Debug.WriteLine($"[Training] {msg}"),
            () =>
            {
                var tab = OpenTabs.FirstOrDefault(item => item.Key == "training");
                if (tab is not null) CloseTab(tab);
            });
        AddOrSelectTab("training", "Training", page, true);
    }

    private void OpenCatalog(CatalogPageKind kind)
    {
        var page = new CatalogPageViewModel(kind, _catalog, _ollama, true);
        var title = kind switch { CatalogPageKind.Agents => "Agents", CatalogPageKind.Plugins => "Plugins", _ => "Instruction Library" };
        AddOrSelectTab("catalog-" + kind.ToString().ToLowerInvariant(), title, page, true);
    }

    private void OpenAutomations()
    {
        AddOrSelectTab("scheduled-actions", "Scheduled Actions",
            new AutomationsPageViewModel(_automations, _registration, _automationRunner, _scheduleCalculator), true);
    }

    private void OpenMacros()
    {
        var page = new MacrosPageViewModel(_workspaceState, CurrentChat.SelectedContainer?.Id, instruction => InvokeMacroAsync(instruction));
        AddOrSelectTab("macros-" + (CurrentChat.SelectedContainer?.Id.ToString("N") ?? "global"), "Macros", page, true);
    }

    private async Task InvokeMacroAsync(string instruction)
    {
        OpenCurrentChatTab();
        await CurrentChat.InvokeAsync(instruction, "Macro");
    }

    private void OpenArchive() => AddOrSelectTab("archive-" + CurrentMode, "Archive", new ArchivePageViewModel(CurrentMode, _conversations, _containers), true);

    private void OpenActivityLog()
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "activity-log");
        if (existing is not null) { SelectedTab = existing; return; }
        var page = new ActivityLogPageViewModel(_conversations, id => { });
        AddOrSelectTab("activity-log", "Activity Log", page, true);
    }

    private void OpenModeLibrary()
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "mode-library");
        if (existing is not null) { SelectedTab = existing; return; }
        var page = new ModeLibraryPageViewModel(_modeRegistry, _modeUsage, _pins);
        page.OpenInStudio += () =>
        {
            _ = NavigateModeAsync(HavenMode.Studio, true);
        };
        AddOrSelectTab("mode-library", "App Library", page, true);
    }

    private async Task NavigateCurrentChatAsync()
    {
        OpenCurrentChatTab();
        await CurrentChat.RefreshCatalogAsync(CancellationToken.None);
    }

    private void OpenCurrentChatTab()
    {
        var key = IsProjectOpen ? "project-chat-" + ActiveProject!.Id.ToString("N") : "chat-" + CurrentMode.ToString().ToLowerInvariant();
        AddOrSelectTab(key, IsProjectOpen ? ActiveProjectName + " chat" : ProductName, CurrentChat, IsProjectOpen);
    }

    private async Task SwitchSurfaceAsync(string? surfaceName)
    {
        if (string.IsNullOrWhiteSpace(surfaceName)) return;
        if (Enum.TryParse<HavenSurface>(surfaceName, true, out var surface))
        {
            switch (surface)
            {
                case HavenSurface.Chat:
                    await NavigateModeAsync(HavenMode.Chat, false);
                    break;
                case HavenSurface.Teach:
                    await NavigateModeAsync(HavenMode.Teach, false);
                    break;
                case HavenSurface.Do:
                    await NavigateModeAsync(HavenMode.Do, true);
                    break;
                case HavenSurface.Studio:
                    await NavigateModeAsync(HavenMode.Studio, true);
                    break;
                case HavenSurface.Browse:
                    OpenBrowser();
                    break;
                case HavenSurface.Plan:
                    OpenPlan();
                    break;
                case HavenSurface.Training:
                    OpenTraining();
                    break;
                case HavenSurface.Home:
                    await OpenHomeAsync();
                    break;
                case HavenSurface.Call:
                    await OpenCallAsync();
                    break;
            }

            if (_modeRegistry is not null)
            {
                var mode = await _modeRegistry.GetModeByKeyAsync(surfaceName.ToLowerInvariant(), CancellationToken.None);
                if (mode is not null)
                    await _modeUsage.RecordUsageAsync(mode.Id, DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
            }
        }
    }

    private void StartNewConversation()
    {
        if (CurrentMode == HavenMode.Studio && IsProjectOpen)
        {
            _ = StartProjectChatAsync(string.Empty);
            return;
        }
        AddOrSelectTab("chat-" + CurrentMode.ToString().ToLowerInvariant(), ProductName, CurrentChat, false);
        CurrentChat.NewChat();
        _ = RefreshRecentsAsync(CancellationToken.None);
    }

    private void AddNewTab()
    {
        var chat = CreateChat(CurrentMode);
        var key = "chat-" + CurrentMode.ToString().ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N")[..8];
        AddOrSelectTab(key, ProductName, chat, true, forceNewTab: true);
    }

    private async Task SelectContainerAsync(ContainerItemViewModel? item)
    {
        if (item is null) return;
        if (CurrentMode == HavenMode.Chat)
        {
            await OpenChatGroupAsync(item.Definition);
            return;
        }
        CurrentChat.SelectedContainer = item;
        if (CurrentMode == HavenMode.Studio) await OpenContainerDefinitionAsync(item.Definition);
        else AddOrSelectTab("chat-" + CurrentMode.ToString().ToLowerInvariant(), ProductName, CurrentChat, false);
        await RefreshRecentsAsync(CancellationToken.None);
        RaiseShellProperties();
    }

    private async Task OpenContainerDefinitionAsync(ContainerDefinition definition)
    {
        if (definition.Mode == HavenMode.Chat)
        {
            await OpenChatGroupAsync(definition);
        }
        else if (definition.Mode == HavenMode.Studio)
        {
            var chat = await GetOrCreateProjectChatAsync(definition);
            CurrentChat = chat;
            var page = GetOrCreateProjectPage(definition);
            ActivateProject(definition, page);
            AddOrSelectTab("project-" + definition.Id.ToString("N"), definition.Name, page, true);
        }
        else
        {
            var item = CurrentChat.Containers.FirstOrDefault(candidate => candidate.Id == definition.Id);
            if (item is null)
            {
                await CurrentChat.RefreshContainersAsync(CancellationToken.None);
                item = CurrentChat.Containers.FirstOrDefault(candidate => candidate.Id == definition.Id);
            }
            if (item is not null) CurrentChat.SelectedContainer = item;
            AddOrSelectTab("chat-" + CurrentMode.ToString().ToLowerInvariant(), ProductName, CurrentChat, false);
        }
        await RefreshRecentsAsync(CancellationToken.None);
        RaiseShellProperties();
    }

    private async Task StartProjectChatAsync(string prompt)
    {
        if (ActiveProject is null) return;
        var chat = await GetOrCreateProjectChatAsync(ActiveProject);
        CurrentChat = chat;
        chat.NewChat();
        AddOrSelectTab("project-chat-" + ActiveProject.Id.ToString("N"), ActiveProject.Name + " chat", chat, true);
        if (!string.IsNullOrWhiteSpace(prompt)) chat.UsePrompt(prompt);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private Task OpenFileAsync(ContainerDefinition container, WorkspaceFileItemViewModel file)
    {
        ActivateProject(container);
        var page = new WorkspaceEditorPage(container, CurrentChat.ConversationId, file, _workspaceTools, _workspaceState, _conversations,
            () => CurrentChat.BranchCurrentAsync(), () => CurrentChat.StopCommand.Execute(null));
        AddOrSelectTab("file-" + container.Id.ToString("N") + "-" + file.RelativePath.ToLowerInvariant(), file.Name, page, true);
        return Task.CompletedTask;
    }

    private void OpenContainerSettings()
    {
        if (CurrentMode == HavenMode.Teach && CurrentChat.SelectedLesson is not null)
        {
            AddOrSelectTab("lesson-settings-" + CurrentChat.SelectedLesson.Id.ToString("N"), "Lesson settings",
                new LessonSettingsPageViewModel(CurrentChat.SelectedLesson, _containers, RefreshAfterSettingsAsync), true);
            return;
        }
        if (CurrentChat.SelectedContainer is null) return;
        var selected = CurrentChat.SelectedContainer;
        AddOrSelectTab("container-settings-" + CurrentChat.SelectedContainer.Id.ToString("N"), ContainerSettingsLabel,
            new ContainerSettingsPage(null, selected, _containers, RefreshAfterSettingsAsync,
                selected.Definition.Mode == HavenMode.Chat ? () => ReturnToChatGroupAsync(selected.Id) : null), true);
    }

    private async Task ReturnToChatGroupAsync(Guid groupId)
    {
        var group = (await _containers.GetByModeAsync(HavenMode.Chat, CancellationToken.None))
            .FirstOrDefault(item => item.Id == groupId);
        if (group is not null) await OpenChatGroupAsync(group);
        else if (NavigateBackCommand.CanExecute(null)) NavigateBackCommand.Execute(null);
    }

    private async Task RefreshAfterSettingsAsync()
    {
        await CurrentChat.RefreshContainersAsync(CancellationToken.None);
        if (ActiveProject is not null)
        {
            var refreshed = CurrentChat.Containers.FirstOrDefault(item => item.Id == ActiveProject.Id)?.Definition;
            if (refreshed is not null)
            {
                _projectPages.Remove(refreshed.Id);
                ActivateProject(refreshed, GetOrCreateProjectPage(refreshed));
            }
        }
        RaiseShellProperties();
    }

    private void OpenApplicationSettings()
    {
        AddOrSelectTab("settings-" + CurrentMode, "Settings", new SettingsPageViewModel(_preferences, _ollama,
            (model, effort) => CurrentChat.ApplyPreferences(model, effort), CurrentMode == HavenMode.Studio), true);
    }

    private async Task OpenConversationAsync(RecentConversationViewModel? item)
    {
        if (item is null) return;
        if (item.Definition.Mode == HavenMode.Chat && item.Definition.ContainerId is not null)
        {
            await OpenGroupedConversationAsync(item.Definition);
            return;
        }
        if (item.Definition.Mode == HavenMode.Studio && item.Definition.ContainerId is Guid projectId)
        {
            var project = (await _containers.GetByModeAsync(HavenMode.Studio, CancellationToken.None)).FirstOrDefault(candidate => candidate.Id == projectId);
            if (project is null) return;
            CurrentChat = await GetOrCreateProjectChatAsync(project);
            ActivateProject(project);
        }
        else
        {
            if (CurrentChat.Mode != item.Definition.Mode || CurrentChat.Mode == HavenMode.Studio && CurrentChat.SelectedContainer is not null)
                CurrentChat = await GetOrCreateChatAsync(item.Definition.Mode);
            if (item.Definition.Mode == HavenMode.Studio) ClearActiveProject();
        }
        var key = item.Definition.ContainerId is Guid containerId ? "project-chat-" + containerId.ToString("N") : "chat-" + CurrentChat.Mode.ToString().ToLowerInvariant();
        AddOrSelectTab(key, item.Definition.Title, CurrentChat, item.Definition.ContainerId is not null);
        await CurrentChat.LoadConversationAsync(item.Definition.Id, CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task RenameConversationAsync(RecentConversationViewModel? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.DraftTitle)) return;
        var updated = item.Definition with { Title = item.DraftTitle.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
        await _conversations.UpsertConversationAsync(updated, CancellationToken.None);
        item.FinishRename(updated);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task TogglePinAsync(RecentConversationViewModel? item)
    {
        if (item is null) return;
        await _conversations.UpsertConversationAsync(item.Definition with { IsPinned = !item.Definition.IsPinned, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task BranchConversationAsync(RecentConversationViewModel? item)
    {
        if (item is null) return;
        await OpenConversationAsync(item);
        await CurrentChat.BranchCurrentAsync();
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task ArchiveConversationAsync(RecentConversationViewModel? item)
    {
        if (item is null) return;
        await _conversations.UpsertConversationAsync(item.Definition with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        if (CurrentChat.ConversationId == item.Definition.Id) CurrentChat.NewChat();
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task DeleteConversationAsync(RecentConversationViewModel? item)
    {
        if (item is null) return;
        await _conversations.DeleteConversationAsync(item.Definition.Id, CancellationToken.None);
        if (CurrentChat.ConversationId == item.Definition.Id) CurrentChat.NewChat();
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task RefreshRecentsAsync(CancellationToken cancellationToken)
    {
        IEnumerable<Conversation> items;
        if (CurrentMode is HavenMode.Chat or HavenMode.Teach)
        {
            var scope = CurrentChat.CurrentScope ?? (CurrentMode == HavenMode.Teach
                ? ConversationScope.TeachQuickChat
                : ConversationScope.GeneralChat);
            items = await _conversations.GetRecentInScopeAsync(scope, 120, cancellationToken);
        }
        else
        {
            items = await _conversations.GetRecentAsync(CurrentMode, 120, cancellationToken);
        }
        if (CurrentMode == HavenMode.Studio)
        {
            items = ActiveProject is not null ? items.Where(item => item.ContainerId == ActiveProject.Id) : items.Where(item => item.ContainerId is null);
        }
        if (!string.IsNullOrWhiteSpace(SearchQuery)) items = items.Where(item => item.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        PinnedConversations.Clear();
        RecentConversations.Clear();
        foreach (var conversation in items)
        {
            var viewModel = new RecentConversationViewModel(conversation, OpenConversationAsync, RenameConversationAsync, TogglePinAsync,
                BranchConversationAsync, ArchiveConversationAsync, DeleteConversationAsync);
            viewModel.IsActive = conversation.Id == CurrentChat.ConversationId;
            if (conversation.IsPinned) PinnedConversations.Add(viewModel); else RecentConversations.Add(viewModel);
        }
        RaisePropertyChanged(nameof(HasPinnedConversations));
        RaisePropertyChanged(nameof(HasRecentConversations));
        RaisePropertyChanged(nameof(ShowNoProjectChats));
    }

    private async Task TogglePinCurrentAsync()
    {
        var item = await _conversations.GetAsync(CurrentChat.ConversationId, CancellationToken.None);
        if (item is null) return;
        await _conversations.UpsertConversationAsync(item with { IsPinned = !item.IsPinned, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private void BeginRenameCurrent()
    {
        RenameDraft = CurrentChat.ConversationTitle;
        IsRenameOpen = true;
    }

    private async Task SaveRenameCurrentAsync()
    {
        var item = await _conversations.GetAsync(CurrentChat.ConversationId, CancellationToken.None);
        if (item is null) return;
        await _conversations.UpsertConversationAsync(item with { Title = RenameDraft.Trim(), UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        IsRenameOpen = false;
        await CurrentChat.LoadConversationAsync(item.Id, CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private async Task DeleteCurrentAsync()
    {
        IsDeleteConfirmationOpen = false;
        await _conversations.DeleteConversationAsync(CurrentChat.ConversationId, CancellationToken.None);
        CurrentChat.NewChat();
        await RefreshRecentsAsync(CancellationToken.None);
    }

    private void CopyLastResponse()
    {
        var content = CurrentChat.Messages.LastOrDefault(item => item.Role == MessageRole.Assistant)?.Content;
        if (!string.IsNullOrWhiteSpace(content)) CopyRequested?.Invoke(this, content);
    }

    private void AddOrSelectTab(
        string key,
        string title,
        object page,
        bool closeable,
        HavenSurface? surface = null,
        bool forceNewTab = false)
    {
        var resolvedSurface = surface ?? InferSurface(page);
        var existing = OpenTabs.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!ReferenceEquals(existing.Page, page)) existing.ReplacePage(page);
            existing.Title = title;
            existing.SetSurface(resolvedSurface);
            if (ReferenceEquals(SelectedTab, existing)) CurrentPage = existing.Page;
            else SelectedTab = existing;
            return;
        }

        if (!forceNewTab && SelectedTab is not null)
        {
            if (SelectedTab.Page is IActivatablePage previous) previous.Deactivate();
            SelectedTab.NavigateTo(key, title, page, closeable, resolvedSurface);
            ApplySelectedTab(SelectedTab);
            return;
        }

        var tab = new WorkspaceTabViewModel(key, title, page, closeable, resolvedSurface);
        OpenTabs.Add(tab);
        SelectedTab = tab;
        RaisePropertyChanged(nameof(IsHorizontalTabsVisible));
    }

    private HavenSurface InferSurface(object page) => page switch
    {
        HomePageViewModel => HavenSurface.Home,
        CallPageViewModel => HavenSurface.Call,
        PlanPageViewModel => HavenSurface.Plan,
        BrowserPage => HavenSurface.Browse,
        TrainingPageViewModel => HavenSurface.Training,
        ChatGroupPageViewModel => HavenSurface.Chat,
        ModeLibraryPageViewModel => HavenSurface.Home,
        ChatPage chat => SurfaceForMode(chat.Mode),
        StudioProjectPage or WorkspaceEditorPage => HavenSurface.Studio,
        _ => SelectedTab?.Surface ?? SurfaceForMode(CurrentMode)
    };

    private static HavenSurface SurfaceForMode(HavenMode mode) => mode switch
    {
        HavenMode.Chat => HavenSurface.Chat,
        HavenMode.Teach => HavenSurface.Teach,
        HavenMode.Do => HavenSurface.Do,
        HavenMode.Studio => HavenSurface.Studio,
        _ => HavenSurface.Chat
    };

    private void CloseTab(WorkspaceTabViewModel? item)
    {
        if (item is null || !item.IsCloseable || OpenTabs.Count <= 1) return;
        var index = OpenTabs.IndexOf(item);
        OpenTabs.Remove(item);
        item.Dispose();
        if (ReferenceEquals(SelectedTab, item)) SelectedTab = OpenTabs.ElementAtOrDefault(Math.Clamp(index - 1, 0, Math.Max(0, OpenTabs.Count - 1))) ?? OpenTabs.FirstOrDefault();
        RaisePropertyChanged(nameof(IsHorizontalTabsVisible));
    }

    private void NavigateBack()
    {
        if (SelectedTab is null || !SelectedTab.TryGoBack()) return;
        ApplySelectedTab(SelectedTab);
    }

    private void NavigateForward()
    {
        if (SelectedTab is null || !SelectedTab.TryGoForward()) return;
        ApplySelectedTab(SelectedTab);
    }

    private void BuildCommandPalette()
    {
        AllCommandItems =
        [
            Command(FileNewLabel, "Start a clean conversation in the current product.", "Ctrl+N", NewChatCommand),
            Command("Branch chat", "Copy the current conversation and context into an independent branch.", "/Branch", BranchCurrentCommand),
            Command("Temporary chat", "Toggle local history for this conversation.", "/Temporary", ToggleTemporaryCommand),
            Command("Compact context", "Summarise older turns while preserving decisions and requirements.", "/Compact", CompactCurrentCommand),
            Command("Archive current chat", "Remove the chat from recents without destroying it.", string.Empty, ArchiveCurrentCommand),
            Command("Rename chat", "Change the current chat title.", string.Empty, BeginRenameCurrentCommand),
            Command("Pin or unpin chat", "Toggle the chat in the Pinned section.", string.Empty, TogglePinCurrentCommand),
            Command("Configure model", "Search models and open advanced generation and safety options.", string.Empty, ConfigureModelCommand),
            Command("Instruction Library", "Browse built-in and custom reusable instructions invoked with >.", string.Empty, NavigatePromptsCommand),
            Command("Plugins", "Browse functional Haven capabilities invoked with @.", string.Empty, NavigatePluginsCommand),
            Command("Scheduled Actions", "Create and manage scheduled local jobs.", string.Empty, NavigateAutomationsCommand),
            Command("Macros", "Create or run explicit click-to-run actions.", string.Empty, NavigateMacrosCommand),
            Command("Archive", "Restore archived chats, groups, and projects.", string.Empty, NavigateArchiveCommand),
            Command("Activity Log", "View recent conversations and tool activity across sessions.", string.Empty, NavigateActivityLogCommand),
            Command("Haven Browse", "Open the isolated tabbed browser and side assistant.", string.Empty, NavigateBrowserCommand),
            Command("Haven Training", "Run an autonomous agent session and score the result.", string.Empty, NavigateTrainingCommand),
            Command("App Library", "Discover, pin, and create Haven apps.", string.Empty, NavigateModeLibraryCommand),
            Command("Build Browse extension", "Create a scoped Haven extension manifest and content script in Do or Studio.", string.Empty, BuildBrowserExtensionCommand),
            Command("Toggle sidebar", "Show or hide the current product sidebar.", string.Empty, ToggleSidebarCommand),
            Command("Refresh models", "Reload the installed Ollama model list.", string.Empty, RefreshModelsCommand),
            Command("Settings", "Appearance, models, permissions, context, and browser options.", string.Empty, NavigateSettingsCommand)
        ];
        FilterCommands();
    }

    private CommandPaletteItemViewModel Command(string name, string description, string shortcut, System.Windows.Input.ICommand command) =>
        new(name, description, shortcut, new RelayCommand(() => { IsCommandPaletteOpen = false; if (command.CanExecute(null)) command.Execute(null); }));

    private void FilterCommands()
    {
        CommandItems.Clear();
        foreach (var item in AllCommandItems.Where(item => string.IsNullOrWhiteSpace(CommandSearch) ||
                     item.Name.Contains(CommandSearch, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(CommandSearch, StringComparison.OrdinalIgnoreCase)))
            CommandItems.Add(item);
    }

    private void AttachChat(ChatPage chat)
    {
        chat.PropertyChanged += OnChatPropertyChanged;
        chat.ConversationChanged += OnConversationChanged;
    }

    private void DetachChat(ChatPage chat)
    {
        chat.PropertyChanged -= OnChatPropertyChanged;
        chat.ConversationChanged -= OnConversationChanged;
    }

    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatPage.Status)
            or nameof(ChatPage.SelectedContainer)
            or nameof(ChatPage.SelectedLesson)
            or nameof(ChatPage.SelectedDuo)
            or nameof(ChatPage.ConversationTitle)
            or nameof(ChatPage.HasMessages)
            or nameof(ChatPage.IsTemporary)
            or nameof(ChatPage.ContextPercent)
            or nameof(ChatPage.ContextRemainingPercent))
            RaiseShellProperties();
    }

    private void OnConversationChanged(object? sender, EventArgs e)
    {
        RaiseShellProperties();
        _ = RefreshRecentsAsync(CancellationToken.None);
    }

    private void OnCallStateChanged(object? sender, CallStateChangedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        RaisePropertyChanged(nameof(HasLiveCall));
        RaisePropertyChanged(nameof(LiveCallLabel));
    });

    private void RaiseShellProperties()
    {
        RaisePropertyChanged(nameof(CurrentSurface));
        RaisePropertyChanged(nameof(CurrentMode));
        RaisePropertyChanged(nameof(IsTeach));
        RaisePropertyChanged(nameof(IsChatProduct));
        RaisePropertyChanged(nameof(HasContainers));
        RaisePropertyChanged(nameof(HasAnyContainers));
        RaisePropertyChanged(nameof(SupportsDuo));
        RaisePropertyChanged(nameof(ProductName));
        RaisePropertyChanged(nameof(NewItemLabel));
        RaisePropertyChanged(nameof(FileNewLabel));
        RaisePropertyChanged(nameof(FileNewContainerLabel));
        RaisePropertyChanged(nameof(ContainerHeading));
        RaisePropertyChanged(nameof(ProjectMenuHeader));
        RaisePropertyChanged(nameof(WorkspaceEyebrow));
        RaisePropertyChanged(nameof(WorkspaceTitle));
        RaisePropertyChanged(nameof(ShowTemporaryHeaderAction));
        RaisePropertyChanged(nameof(ShowContextHeaderWidget));
        RaisePropertyChanged(nameof(TemporaryHeaderActionLabel));
        RaisePropertyChanged(nameof(ContextPercent));
        RaisePropertyChanged(nameof(ContextRemainingPercent));
        RaisePropertyChanged(nameof(ContextLabel));
        RaisePropertyChanged(nameof(ContextRemainingLabel));
        RaisePropertyChanged(nameof(RecentHeading));
        RaisePropertyChanged(nameof(ContainerSettingsLabel));
        RaisePropertyChanged(nameof(OllamaStatus));
        RaisePropertyChanged(nameof(DuoLabel));
        RaisePropertyChanged(nameof(ChatTypeLabel));
        RaisePropertyChanged(nameof(IsProjectOpen));
        RaisePropertyChanged(nameof(ShowNoProjectChats));
        RaisePropertyChanged(nameof(SupportsConversationSidebar));
        RaisePropertyChanged(nameof(SupportsConversationCommands));
        RaisePropertyChanged(nameof(SupportsEditingCommands));
        RaisePropertyChanged(nameof(IsSidebarVisible));
        RaisePropertyChanged(nameof(HasFullSidebar));
        RaisePropertyChanged(nameof(HasCompactSidebar));
        RaisePropertyChanged(nameof(IsBrowseMode));
        RaisePropertyChanged(nameof(IsTrainingMode));
    }

    private ChatPage CreateChat(HavenMode mode) => new(_bus, mode, _conversations, _containers, _catalog, _ollama, _sessions,
        _preferences, _preflight, _workspaceState, _projectIntelligence, _containerResources);

    public void ShowOverlay(UserControl overlay)
    {
        _previousContent = PageContent.Content;
        OverlayHost.Child = overlay;
        OverlayHost.IsVisible = true;
        PageContent.IsVisible = false;
    }

    public void HideOverlay()
    {
        OverlayHost.IsVisible = false;
        PageContent.Content = _previousContent;
        PageContent.IsVisible = true;
        OverlayHost.Child = null;
        _previousContent = null;
    }

    public async void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (control && e.Key == Key.K)
        {
            OpenCommandPaletteCommand.Execute(null);
            e.Handled = true;
        }
        else if (control && e.Key == Key.N)
        {
            NewChatCommand.Execute(null);
            e.Handled = true;
        }
        else if (control && e.Key == Key.S)
        {
            SaveCurrentCommand.Execute(null);
            e.Handled = true;
        }
        else if (control && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.R
                 && CurrentPage is BrowserPage browser)
        {
            browser.HardReloadCommand.Execute(null);
            e.Handled = true;
        }
    }

    public void Dispose()
    {
        _reminderTimer.Stop();
        _callCoordinator.StateChanged -= OnCallStateChanged;
        _homePage?.Dispose();
        _callPage?.Dispose();
        _planPage?.Dispose();
        _companionDockVm.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
