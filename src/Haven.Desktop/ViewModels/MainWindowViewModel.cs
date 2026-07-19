/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/MainWindowViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns MainWindowViewModel, WorkspaceTabViewModel, CommandPaletteItemViewModel, RecentConversationViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using Haven.Application;
using Haven.Automations;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents main window view model and keeps its related state and behavior together.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Stores conversations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationRepository _conversations;
    /// <summary>
    /// Stores containers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IContainerRepository _containers;
    /// <summary>
    /// Stores catalog locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ICatalogRepository _catalog;
    /// <summary>
    /// Stores automations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAutomationRepository _automations;
    /// <summary>
    /// Stores workspace state locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IWorkspaceStateRepository _workspaceState;
    /// <summary>
    /// Stores workspace tools locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IWorkspaceToolService _workspaceTools;
    /// <summary>
    /// Stores project intelligence locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IProjectIntelligenceService _projectIntelligence;
    /// <summary>
    /// Stores ollama locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _ollama;
    /// <summary>
    /// Stores sessions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ChatSessionService _sessions;
    /// <summary>
    /// Stores preflight locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly CapabilityPreflightService _preflight;
    /// <summary>
    /// Stores browser locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly BrowserSessionService _browser;
    /// <summary>
    /// Stores browser data locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly BrowserDataService _browserData;
    /// <summary>
    /// Stores registration locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly WindowsAutomationRegistrationService _registration;
    /// <summary>
    /// Stores automation runner locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly AutomationRunner _automationRunner;
    /// <summary>
    /// Stores schedule calculator locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ScheduleCalculator _scheduleCalculator;
    /// <summary>
    /// Stores preferences locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly UserPreferencesService _preferences;
    /// <summary>
    /// Stores project creator locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ProjectCreationService _projectCreator;
    /// <summary>
    /// Stores notifications locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly NotificationService _notifications;
    /// <summary>
    /// Stores training repo locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ITrainingRepository _trainingRepo;
    /// <summary>
    /// Stores container resources locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IContainerResourceRepository _containerResources;
    /// <summary>
    /// Stores dashboard locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IDashboardRepository _dashboard;
    /// <summary>
    /// Stores dashboard layout locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IDashboardLayoutRepository _dashboardLayout;
    /// <summary>
    /// Stores dashboard providers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IReadOnlyList<IDashboardTileProvider> _dashboardProviders;
    /// <summary>
    /// Stores call coordinator locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ICallCoordinator _callCoordinator;
    /// <summary>
    /// Stores speech models locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ISpeechModelManager _speechModels;
    /// <summary>
    /// Stores planner locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IPlannerRepository _planner;
    /// <summary>
    /// Stores planner proposals locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IPlannerProposalService _plannerProposals;
    /// <summary>
    /// Stores calendar providers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ICalendarSyncProviderRegistry _calendarProviders;
    /// <summary>
    /// Stores surface orchestration locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SurfaceOrchestrationService _surfaceOrchestration;
    /// <summary>
    /// Stores mode registry locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModeRegistry _modeRegistry;
    /// <summary>
    /// Stores mode usage locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModeUsageRepository _modeUsage;
    /// <summary>
    /// Stores pins locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IPinRepository _pins;
    /// <summary>
    /// Stores companion dock vm locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly CompanionDockViewModel _companionDockVm;
    /// <summary>
    /// Stores chats locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Dictionary<HavenMode, ChatPageViewModel> _chats = [];
    /// <summary>
    /// Stores project chats locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Dictionary<Guid, ChatPageViewModel> _projectChats = [];
    /// <summary>
    /// Stores project pages locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Dictionary<Guid, StudioProjectPageViewModel> _projectPages = [];
    /// <summary>
    /// Stores group chats locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Dictionary<Guid, ChatPageViewModel> _groupChats = [];
    /// <summary>
    /// Stores group pages locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Dictionary<Guid, ChatGroupPageViewModel> _groupPages = [];
    /// <summary>
    /// Stores home page locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private HomePageViewModel? _homePage;
    /// <summary>
    /// Stores call page locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CallPageViewModel? _callPage;
    /// <summary>
    /// Stores plan page locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PlanPageViewModel? _planPage;
    /// <summary>
    /// Stores reminder timer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly DispatcherTimer _reminderTimer;
    /// <summary>
    /// Stores is polling reminders locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _isPollingReminders;
    /// <summary>
    /// Stores current page locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private object? _currentPage;
    /// <summary>
    /// Stores current chat locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ChatPageViewModel _currentChat;
    /// <summary>
    /// Stores selected tab locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private WorkspaceTabViewModel? _selectedTab;
    /// <summary>
    /// Stores startup status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _startupStatus = "Starting Haven…";
    /// <summary>
    /// Stores search query locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _searchQuery = string.Empty;
    /// <summary>
    /// Stores command search locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _commandSearch = string.Empty;
    /// <summary>
    /// Stores is sidebar open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isSidebarOpen = true;
    /// <summary>
    /// Stores is command palette open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isCommandPaletteOpen;
    /// <summary>
    /// Stores is rename open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isRenameOpen;
    /// <summary>
    /// Stores is delete confirmation open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isDeleteConfirmationOpen;
    /// <summary>
    /// Stores rename draft locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _renameDraft = string.Empty;
    /// <summary>
    /// Stores active project locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ContainerDefinition? _activeProject;
    /// <summary>
    /// Stores active project page locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private StudioProjectPageViewModel? _activeProjectPage;

    public MainWindowViewModel(
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
        UndoCurrentCommand = new RelayCommand(() => (CurrentPage as WorkspaceEditorPageViewModel)?.UndoCommand.Execute(null));
        RedoCurrentCommand = new RelayCommand(() => (CurrentPage as WorkspaceEditorPageViewModel)?.RedoCommand.Execute(null));
        SaveCurrentCommand = new RelayCommand(() => (CurrentPage as WorkspaceEditorPageViewModel)?.SaveCommand.Execute(null));
        BuildBrowserExtensionCommand = new RelayCommand(() =>
        {
            OpenCurrentChatTab();
            CurrentChat.UsePrompt(">Rigid Build a limited-scope Haven Browse extension. Ask for the allowed origins and behavior, then create a declarative manifest and content script without privileged APIs.");
        });
        SwitchSurfaceCommand = new AsyncRelayCommand<string>(SwitchSurfaceAsync);
        NavigateModeLibraryCommand = new RelayCommand(OpenModeLibrary);

        _currentChat = CreateChat(HavenMode.Chat);
        _chats[HavenMode.Chat] = _currentChat;
        AttachChat(_currentChat);
        _currentPage = _currentChat;
        AddOrSelectTab("chat-general", "General chat", _currentChat, false, HavenSurface.Chat);
        _callCoordinator.StateChanged += OnCallStateChanged;
        BuildCommandPalette();
    }

    /// <summary>
    /// Stores copy requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<string>? CopyRequested;
    /// <summary>
    /// Stores dictate requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler? DictateRequested;
    /// <summary>
    /// Gets or updates pinned conversations, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<RecentConversationViewModel> PinnedConversations { get; } = [];
    /// <summary>
    /// Gets or updates recent conversations, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<RecentConversationViewModel> RecentConversations { get; } = [];
    /// <summary>
    /// Gets or updates open tabs, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<WorkspaceTabViewModel> OpenTabs { get; } = [];
    /// <summary>
    /// Gets or updates command items, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<CommandPaletteItemViewModel> CommandItems { get; } = [];
    /// <summary>
    /// Gets or updates notifications, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ToastNotification> Notifications => _notifications.Notifications;
    /// <summary>
    /// Gets or updates all command items, the bindable or domain state represented by this property.
    /// </summary>
    private IReadOnlyList<CommandPaletteItemViewModel> AllCommandItems { get; set; } = [];
    /// <summary>
    /// Gets or updates companion dock, the bindable or domain state represented by this property.
    /// </summary>
    public CompanionDockViewModel CompanionDock => _companionDockVm;

    public ChatPageViewModel CurrentChat
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

    /// <summary>
    /// Applies the selected tab's current history entry to shell-wide state.
    /// This is shared by direct tab selection and Back/Forward navigation so
    /// every screen participates in one predictable navigation contract.
    /// </summary>
    private void ApplySelectedTab(WorkspaceTabViewModel value)
    {
        if (value.Page is ChatPageViewModel chat)
        {
            CurrentChat = chat;
            if (chat.Mode == HavenMode.Studio && chat.SelectedContainer is not null)
                ActivateProject(chat.SelectedContainer.Definition);
            else if (chat.Mode == HavenMode.Studio)
                ClearActiveProject();
        }
        else if (value.Page is StudioProjectPageViewModel project)
        {
            ActivateProject(project.Definition, project);
            if (_projectChats.TryGetValue(project.ProjectId, out var projectChat)) CurrentChat = projectChat;
        }
        else if (value.Page is WorkspaceEditorPageViewModel editor)
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

    /// <summary>
    /// Reports whether chat visible applies to the current state.
    /// </summary>
    public bool IsChatVisible => ReferenceEquals(CurrentPage, CurrentChat);
    /// <summary>
    /// Reports whether page visible applies to the current state.
    /// </summary>
    public bool IsPageVisible => !IsChatVisible;
    /// <summary>
    /// Gets or updates current surface, the bindable or domain state represented by this property.
    /// </summary>
    public HavenSurface CurrentSurface => SelectedTab?.Surface ?? HavenSurface.Chat;
    /// <summary>
    /// Reports whether browse mode applies to the current state.
    /// </summary>
    public bool IsBrowseMode => CurrentSurface == HavenSurface.Browse;
    /// <summary>
    /// Reports whether training mode applies to the current state.
    /// </summary>
    public bool IsTrainingMode => CurrentSurface == HavenSurface.Training;
    /// <summary>
    /// Reports whether project open applies to the current state.
    /// </summary>
    public bool IsProjectOpen => CurrentSurface == HavenSurface.Studio && ActiveProject is not null;
    /// <summary>
    /// Reports whether workspace header visible applies to the current state.
    /// </summary>
    public bool IsWorkspaceHeaderVisible => IsChatVisible;
    /// <summary>
    /// Reports whether horizontal tabs visible applies to the current state.
    /// </summary>
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
    public StudioProjectPageViewModel? ActiveProjectPage
    {
        get => _activeProjectPage;
        private set => SetProperty(ref _activeProjectPage, value);
    }
    /// <summary>
    /// Gets or updates active project name, the bindable or domain state represented by this property.
    /// </summary>
    public string ActiveProjectName => ActiveProject?.Name ?? "Project";
    /// <summary>
    /// Gets or updates active project root, the bindable or domain state represented by this property.
    /// </summary>
    public string ActiveProjectRoot => ActiveProject?.RootPath ?? "Folder not connected";
    /// <summary>
    /// Gets or updates startup status, the bindable or domain state represented by this property.
    /// </summary>
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

    /// <summary>
    /// Reports whether command palette open applies to the current state.
    /// </summary>
    public bool IsCommandPaletteOpen { get => _isCommandPaletteOpen; private set => SetProperty(ref _isCommandPaletteOpen, value); }
    /// <summary>
    /// Reports whether rename open applies to the current state.
    /// </summary>
    public bool IsRenameOpen { get => _isRenameOpen; private set => SetProperty(ref _isRenameOpen, value); }
    /// <summary>
    /// Reports whether delete confirmation open applies to the current state.
    /// </summary>
    public bool IsDeleteConfirmationOpen { get => _isDeleteConfirmationOpen; private set => SetProperty(ref _isDeleteConfirmationOpen, value); }
    /// <summary>
    /// Gets or updates rename draft, the bindable or domain state represented by this property.
    /// </summary>
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
    /// <summary>
    /// Reports whether sidebar closed applies to the current state.
    /// </summary>
    public bool IsSidebarClosed => !IsSidebarOpen;
    /// <summary>
    /// Gets or updates supports conversation sidebar, the bindable or domain state represented by this property.
    /// </summary>
    public bool SupportsConversationSidebar => CurrentSurface is HavenSurface.Chat or HavenSurface.Teach or HavenSurface.Do or HavenSurface.Studio;
    /// <summary>
    /// Gets or updates supports conversation commands, the bindable or domain state represented by this property.
    /// </summary>
    public bool SupportsConversationCommands => SupportsConversationSidebar;
    /// <summary>
    /// Gets or updates supports editing commands, the bindable or domain state represented by this property.
    /// </summary>
    public bool SupportsEditingCommands => SupportsConversationCommands || CurrentPage is WorkspaceEditorPageViewModel;
    /// <summary>
    /// Reports whether sidebar visible applies to the current state.
    /// </summary>
    public bool IsSidebarVisible => SupportsConversationSidebar;
    /// <summary>
    /// Reports whether full sidebar applies to the current state.
    /// </summary>
    public bool HasFullSidebar => IsSidebarOpen && SupportsConversationSidebar;
    /// <summary>
    /// Reports whether compact sidebar applies to the current state.
    /// </summary>
    public bool HasCompactSidebar => !IsSidebarOpen && SupportsConversationSidebar;
    /// <summary>
    /// Reports whether pinned conversations applies to the current state.
    /// </summary>
    public bool HasPinnedConversations => PinnedConversations.Count > 0;
    /// <summary>
    /// Reports whether recent conversations applies to the current state.
    /// </summary>
    public bool HasRecentConversations => RecentConversations.Count > 0;
    /// <summary>
    /// Gets or updates show no project chats, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowNoProjectChats => IsProjectOpen && !HasPinnedConversations && !HasRecentConversations;
    /// <summary>
    /// Gets or updates current mode, the bindable or domain state represented by this property.
    /// </summary>
    public HavenMode CurrentMode => CurrentChat.Mode;
    /// <summary>
    /// Reports whether teach applies to the current state.
    /// </summary>
    public bool IsTeach => CurrentSurface == HavenSurface.Teach;
    /// <summary>
    /// Reports whether chat product applies to the current state.
    /// </summary>
    public bool IsChatProduct => CurrentSurface is HavenSurface.Chat or HavenSurface.Teach;
    /// <summary>
    /// Reports whether containers applies to the current state.
    /// </summary>
    public bool HasContainers => CurrentChat.HasContainers;
    /// <summary>
    /// Reports whether any containers applies to the current state.
    /// </summary>
    public bool HasAnyContainers => CurrentChat.HasAnyContainers;
    /// <summary>
    /// Gets or updates supports duo, the bindable or domain state represented by this property.
    /// </summary>
    public bool SupportsDuo => CurrentChat.SupportsDuo;
    /// <summary>
    /// Gets or updates chat type label, the bindable or domain state represented by this property.
    /// </summary>
    public string ChatTypeLabel => CurrentSurface == HavenSurface.Teach ? "Teaching" : "General";
    /// <summary>
    /// Gets or updates product name, the bindable or domain state represented by this property.
    /// </summary>
    public string ProductName => CurrentSurface switch
    {
        HavenSurface.Home => "Haven Home",
        HavenSurface.Chat or HavenSurface.Teach => "Haven Chat",
        HavenSurface.Call => "Haven Call",
        HavenSurface.Do => "Haven Do",
        HavenSurface.Studio => "Haven Studio",
        HavenSurface.Browse => "Haven Browse",
        HavenSurface.Plan => "Haven Plan",
        HavenSurface.Training => "Haven Training",
        _ => "Haven"
    };
    /// <summary>
    /// Gets or updates new item label, the bindable or domain state represented by this property.
    /// </summary>
    public string NewItemLabel => CurrentMode switch
    {
        HavenMode.Do => "+ New task",
        HavenMode.Teach => "+ Quick chat",
        HavenMode.Studio => "+ New studio chat",
        _ => "New chat"
    };
    /// <summary>
    /// Gets or updates file new label, the bindable or domain state represented by this property.
    /// </summary>
    public string FileNewLabel => CurrentMode switch { HavenMode.Do => "New task", HavenMode.Teach => "New teaching chat", HavenMode.Studio => "New studio chat", _ => "New chat" };
    /// <summary>
    /// Gets or updates file new container label, the bindable or domain state represented by this property.
    /// </summary>
    public string FileNewContainerLabel => CurrentMode switch { HavenMode.Chat => "New Chat Group", HavenMode.Teach => "New Subject", HavenMode.Do => "New Task Group", _ => "New Project" };
    /// <summary>
    /// Gets or updates container heading, the bindable or domain state represented by this property.
    /// </summary>
    public string ContainerHeading => CurrentMode switch { HavenMode.Chat => "Chat Groups", HavenMode.Teach => "Subjects", HavenMode.Do => "Task Groups", _ => "Projects" };
    /// <summary>
    /// Gets or updates project menu header, the bindable or domain state represented by this property.
    /// </summary>
    public string ProjectMenuHeader => CurrentMode switch { HavenMode.Chat => "Chat Group", HavenMode.Teach => "Subject", HavenMode.Do => "Task Group", _ => "Project" };
    /// <summary>
    /// Gets or updates workspace eyebrow, the bindable or domain state represented by this property.
    /// </summary>
    public string WorkspaceEyebrow => CurrentMode switch
    {
        HavenMode.Chat => CurrentChat.SelectedContainer?.Name ?? "Chat",
        HavenMode.Teach => "Lesson",
        HavenMode.Do => "Task Group",
        HavenMode.Studio => "Project",
        _ => "Haven"
    };
    /// <summary>
    /// Gets or updates workspace title, the bindable or domain state represented by this property.
    /// </summary>
    public string WorkspaceTitle => CurrentMode == HavenMode.Chat
        ? CurrentChat.ConversationTitle
        : CurrentChat.SelectedLesson?.Name ?? CurrentChat.SelectedContainer?.Name ?? "Local workspace";
    /// <summary>Forwards the empty-chat setup state to the shell header.</summary>
    public bool ShowTemporaryHeaderAction => CurrentMode == HavenMode.Chat && CurrentChat.ShowTemporaryHeaderAction;
    /// <summary>Forwards context state to the shell after a conversation starts.</summary>
    public bool ShowContextHeaderWidget => CurrentMode == HavenMode.Chat && CurrentChat.ShowContextHeaderWidget;
    public string TemporaryHeaderActionLabel => CurrentChat.TemporaryHeaderActionLabel;
    public int ContextPercent => CurrentChat.ContextPercent;
    public int ContextRemainingPercent => CurrentChat.ContextRemainingPercent;
    public string ContextLabel => CurrentChat.ContextLabel;
    public string ContextRemainingLabel => CurrentChat.ContextRemainingLabel;
    /// <summary>
    /// Gets or updates recent heading, the bindable or domain state represented by this property.
    /// </summary>
    public string RecentHeading => CurrentMode switch { HavenMode.Do => "Tasks", HavenMode.Teach => "Teaching chats", HavenMode.Studio when IsProjectOpen => "Project chats", HavenMode.Studio => "Standalone chats", _ => "Chats" };
    /// <summary>
    /// Gets or updates container settings label, the bindable or domain state represented by this property.
    /// </summary>
    public string ContainerSettingsLabel => CurrentMode switch
    {
        HavenMode.Teach when CurrentChat.SelectedLesson is not null => "Lesson settings",
        HavenMode.Teach => "Subject settings",
        HavenMode.Do => "Task Group settings",
        HavenMode.Chat => "Chat Group settings",
        _ => "Project settings"
    };
    /// <summary>
    /// Gets or updates ollama status, the bindable or domain state represented by this property.
    /// </summary>
    public string OllamaStatus => CurrentChat.Status;
    /// <summary>
    /// Gets or updates duo label, the bindable or domain state represented by this property.
    /// </summary>
    public string DuoLabel => CurrentChat.IsDuoPluginActive ? CurrentChat.SelectedDuo.ToString() : "Solo";
    /// <summary>
    /// Reports whether live call applies to the current state.
    /// </summary>
    public bool HasLiveCall => _callCoordinator.IsActive;
    /// <summary>
    /// Gets or updates live call label, the bindable or domain state represented by this property.
    /// </summary>
    public string LiveCallLabel => _callCoordinator.State == CallState.Paused ? "Call paused" : $"Live call · {_callCoordinator.State}";

    /// <summary>
    /// Gets or updates navigate chat command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NavigateChatCommand { get; }
    /// <summary>
    /// Gets or updates navigate teach command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NavigateTeachCommand { get; }
    /// <summary>
    /// Gets or updates navigate do command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NavigateDoCommand { get; }
    /// <summary>
    /// Gets or updates navigate studio command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NavigateStudioCommand { get; }
    /// <summary>
    /// Gets or updates navigate browser command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NavigateBrowserCommand { get; }
    /// <summary>
    /// Gets or updates navigate training command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NavigateTrainingCommand { get; }
    /// <summary>
    /// Gets or updates navigate home command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NavigateHomeCommand { get; }
    /// <summary>
    /// Gets or updates navigate call command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NavigateCallCommand { get; }
    /// <summary>
    /// Gets or updates open live call command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand OpenLiveCallCommand { get; }
    /// <summary>
    /// Gets or updates end live call command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand EndLiveCallCommand { get; }
    /// <summary>
    /// Gets or updates navigate plan command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NavigatePlanCommand { get; }
    /// <summary>
    /// Gets or updates navigate agents command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NavigateAgentsCommand { get; }
    /// <summary>
    /// Gets or updates navigate plugins command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NavigatePluginsCommand { get; }
    /// <summary>
    /// Gets or updates navigate prompts command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NavigatePromptsCommand { get; }
    /// <summary>
    /// Gets or updates navigate automations command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NavigateAutomationsCommand { get; }
    /// <summary>
    /// Gets or updates navigate macros command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NavigateMacrosCommand { get; }
    /// <summary>
    /// Gets or updates navigate archive command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NavigateArchiveCommand { get; }
    /// <summary>
    /// Gets or updates navigate activity log command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NavigateActivityLogCommand { get; }
    /// <summary>
    /// Gets or updates dismiss notification command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<Guid> DismissNotificationCommand { get; }
    /// <summary>
    /// Gets or updates navigate settings command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NavigateSettingsCommand { get; }
    /// <summary>
    /// Gets or updates navigate container settings command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NavigateContainerSettingsCommand { get; }
    /// <summary>
    /// Gets or updates navigate current chat command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NavigateCurrentChatCommand { get; }
    /// <summary>
    /// Gets or updates new chat command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NewChatCommand { get; }
    /// <summary>
    /// Gets or updates new container command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NewContainerCommand { get; }
    /// <summary>
    /// Gets or updates delete container command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<ContainerItemViewModel> DeleteContainerCommand { get; }
    /// <summary>
    /// Gets or updates new project chat command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NewProjectChatCommand { get; }
    /// <summary>
    /// Gets or updates navigate project home command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NavigateProjectHomeCommand { get; }
    /// <summary>
    /// Gets or updates toggle temporary command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleTemporaryCommand { get; }
    /// <summary>
    /// Gets or updates refresh models command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand RefreshModelsCommand { get; }
    /// <summary>
    /// Gets or updates toggle sidebar command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleSidebarCommand { get; }
    /// <summary>
    /// Gets or updates select container command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<ContainerItemViewModel> SelectContainerCommand { get; }
    /// <summary>
    /// Gets or updates open command palette command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand OpenCommandPaletteCommand { get; }
    /// <summary>
    /// Gets or updates close command palette command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand CloseCommandPaletteCommand { get; }
    /// <summary>
    /// Gets or updates select tab command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<WorkspaceTabViewModel> SelectTabCommand { get; }
    /// <summary>
    /// Gets or updates close tab command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<WorkspaceTabViewModel> CloseTabCommand { get; }
    /// <summary>
    /// Gets or updates add new tab command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddNewTabCommand { get; }
    /// <summary>
    /// Navigates to the previous screen within the selected workspace tab.
    /// </summary>
    public RelayCommand NavigateBackCommand { get; }
    /// <summary>
    /// Reapplies the screen most recently undone by the universal Back command.
    /// </summary>
    public RelayCommand NavigateForwardCommand { get; }
    /// <summary>
    /// Gets or updates branch current command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand BranchCurrentCommand { get; }
    /// <summary>
    /// Gets or updates compact current command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand CompactCurrentCommand { get; }
    /// <summary>
    /// Gets or updates archive current command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ArchiveCurrentCommand { get; }
    /// <summary>
    /// Gets or updates toggle pin current command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand TogglePinCurrentCommand { get; }
    /// <summary>
    /// Gets or updates begin rename current command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand BeginRenameCurrentCommand { get; }
    /// <summary>
    /// Gets or updates save rename current command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SaveRenameCurrentCommand { get; }
    /// <summary>
    /// Reports whether cancel rename current command is true for the current state.
    /// </summary>
    public RelayCommand CancelRenameCurrentCommand { get; }
    /// <summary>
    /// Gets or updates request delete current command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand RequestDeleteCurrentCommand { get; }
    /// <summary>
    /// Gets or updates confirm delete current command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ConfirmDeleteCurrentCommand { get; }
    /// <summary>
    /// Reports whether cancel delete current command is true for the current state.
    /// </summary>
    public RelayCommand CancelDeleteCurrentCommand { get; }
    /// <summary>
    /// Gets or updates configure model command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ConfigureModelCommand { get; }
    /// <summary>
    /// Gets or updates copy last response command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand CopyLastResponseCommand { get; }
    /// <summary>
    /// Gets or updates dictate command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand DictateCommand { get; }
    /// <summary>
    /// Gets or updates undo current command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand UndoCurrentCommand { get; }
    /// <summary>
    /// Gets or updates redo current command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand RedoCurrentCommand { get; }
    /// <summary>
    /// Gets or updates save current command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand SaveCurrentCommand { get; }
    /// <summary>
    /// Builds browser extension command from the currently available inputs.
    /// </summary>
    public RelayCommand BuildBrowserExtensionCommand { get; }
    /// <summary>
    /// Gets or updates switch surface command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<string> SwitchSurfaceCommand { get; }
    /// <summary>
    /// Gets or updates navigate mode library command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NavigateModeLibraryCommand { get; }

    /// <summary>
    /// Performs initialize asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task InitializeAsync(LegacyMigrationResult migration, CancellationToken cancellationToken)
    {
        await CurrentChat.InitializeAsync(cancellationToken);
        await RefreshRecentsAsync(cancellationToken);
        await PollPlannerRemindersAsync();
        _reminderTimer.Start();
        _companionDockVm.Start();
        StartupStatus = migration.Imported
            ? $"Imported {migration.ConversationCount} legacy conversations · local-only"
            : "Local-only · SQLite ready";
    }

    /// <summary>
    /// Performs the set startup error step owned by this component.
    /// </summary>
    public void SetStartupError(string message) => StartupStatus = $"Startup problem: {message}";

    /// <summary>
    /// Performs poll planner reminders asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
    /// <summary>
    /// Performs the open command palette step owned by this component.
    /// </summary>
    public void OpenCommandPalette()
    {
        CommandSearch = string.Empty;
        BuildCommandPalette();
        IsCommandPaletteOpen = true;
    }

    /// <summary>
    /// Performs open home asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs open call asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task OpenCallAsync()
    {
        _callPage ??= new CallPageViewModel(_callCoordinator, _ollama, _speechModels);
        await _callPage.InitializeAsync();
        AddOrSelectTab("call", "Call", _callPage, false, HavenSurface.Call);
    }

    /// <summary>
    /// Performs the open plan step owned by this component.
    /// </summary>
    private void OpenPlan()
    {
        _planPage ??= new PlanPageViewModel(_planner, _plannerProposals, _calendarProviders, _ollama);
        AddOrSelectTab("plan", "Plan", _planPage, false, HavenSurface.Plan);
    }

    /// <summary>
    /// Performs navigate mode asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task NavigateModeAsync(HavenMode mode, bool showHome)
    {
        var page = await GetOrCreateChatAsync(mode);
        CurrentChat = page;
        if (showHome) await OpenModeHomeAsync();
        else AddOrSelectTab(mode == HavenMode.Teach ? "chat-teach" : "chat-general", mode == HavenMode.Teach ? "Teaching" : "General chat", page, false, SurfaceForMode(mode));
        await RefreshRecentsAsync(CancellationToken.None);
    }

    /// <summary>
    /// Retrieves or create chat async for the current operation.
    /// </summary>
    private async Task<ChatPageViewModel> GetOrCreateChatAsync(HavenMode mode)
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

    /// <summary>
    /// Performs open mode home asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the open new container step owned by this component.
    /// </summary>
    private void OpenNewContainer()
    {
        if (CurrentMode == HavenMode.Studio)
        {
            _ = OpenProjectCreatorAsync();
            return;
        }
        if (CurrentChat.NewContainerCommand.CanExecute(null)) CurrentChat.NewContainerCommand.Execute(null);
    }

    /// <summary>
    /// Performs open project creator asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task OpenProjectCreatorAsync()
    {
        var page = new ProjectCreatorPageViewModel(_projectCreator, OpenCreatedProjectAsync);
        AddOrSelectTab("new-project", "New project", page, true);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs open created project asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task OpenCreatedProjectAsync(ContainerDefinition definition)
    {
        if (!_chats.TryGetValue(HavenMode.Studio, out var standalone)) standalone = await GetOrCreateChatAsync(HavenMode.Studio);
        await standalone.RefreshContainersAsync(CancellationToken.None);
        standalone.SelectedContainer = null;
        await OpenContainerDefinitionAsync(definition);
        await StartProjectChatAsync(string.Empty);
    }

    /// <summary>
    /// Retrieves or create project chat async for the current operation.
    /// </summary>
    private async Task<ChatPageViewModel> GetOrCreateProjectChatAsync(ContainerDefinition definition)
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

    /// <summary>
    /// Retrieves or create group chat async for the current operation.
    /// </summary>
    private async Task<ChatPageViewModel> GetOrCreateGroupChatAsync(ContainerDefinition definition)
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

    /// <summary>
    /// Retrieves or create group page for the current operation.
    /// </summary>
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

    /// <summary>
    /// Performs open chat group asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task OpenChatGroupAsync(ContainerDefinition definition)
    {
        var page = GetOrCreateGroupPage(definition);
        await page.InitializeAsync(CancellationToken.None);
        AddOrSelectTab("group-" + definition.Id.ToString("N"), definition.Name, page, true, HavenSurface.Chat);
    }

    /// <summary>
    /// Performs start group chat asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task StartGroupChatAsync(ContainerDefinition definition)
    {
        var chat = await GetOrCreateGroupChatAsync(definition);
        CurrentChat = chat;
        chat.NewChat();
        AddOrSelectTab("group-chat-" + definition.Id.ToString("N"), definition.Name + " chat", chat, true, HavenSurface.Chat);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    /// <summary>
    /// Performs open grouped conversation asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs open group settings asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task OpenGroupSettingsAsync(ContainerDefinition definition)
    {
        var item = new ContainerItemViewModel(definition);
        AddOrSelectTab(
            "container-settings-" + definition.Id.ToString("N"),
            "Chat Group settings",
            new ContainerSettingsPageViewModel(item, _containers, async () =>
            {
                await RefreshAfterSettingsAsync();
                _groupPages.Remove(definition.Id);
            }, () => ReturnToChatGroupAsync(definition.Id)),
            true,
            HavenSurface.Chat);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs close group page asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task CloseGroupPageAsync(Guid groupId)
    {
        _groupPages.Remove(groupId);
        _groupChats.Remove(groupId);
        var tabs = OpenTabs.Where(item => item.Key.Contains(groupId.ToString("N"), StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var tab in tabs) CloseTab(tab);
        return NavigateModeAsync(HavenMode.Chat, false);
    }

    /// <summary>
    /// Retrieves or create project page for the current operation.
    /// </summary>
    private StudioProjectPageViewModel GetOrCreateProjectPage(ContainerDefinition definition)
    {
        if (_projectPages.TryGetValue(definition.Id, out var page)) return page;
        page = new StudioProjectPageViewModel(definition, _conversations, _containers, _automations, _workspaceState, _projectIntelligence,
            file => OpenFileAsync(definition, file), StartProjectChatAsync, _modeRegistry, _catalog, _ollama);
        _projectPages[definition.Id] = page;
        return page;
    }

    /// <summary>
    /// Performs the activate project step owned by this component.
    /// </summary>
    private void ActivateProject(ContainerDefinition definition, StudioProjectPageViewModel? page = null)
    {
        ActiveProject = definition;
        ActiveProjectPage = page ?? GetOrCreateProjectPage(definition);
        RaiseShellProperties();
    }

    /// <summary>
    /// Performs the clear active project step owned by this component.
    /// </summary>
    private void ClearActiveProject()
    {
        ActiveProject = null;
        ActiveProjectPage = null;
        RaiseShellProperties();
    }

    /// <summary>
    /// Performs the open active project home step owned by this component.
    /// </summary>
    private void OpenActiveProjectHome()
    {
        if (ActiveProject is null) return;
        var page = ActiveProjectPage ?? GetOrCreateProjectPage(ActiveProject);
        AddOrSelectTab("project-" + ActiveProject.Id.ToString("N"), ActiveProject.Name, page, true);
    }

    /// <summary>
    /// Performs the open browser step owned by this component.
    /// </summary>
    private void OpenBrowser()
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "browse");
        if (existing is not null) { SelectedTab = existing; return; }
        var page = new BrowserPageViewModel(_browser, _browserData, _ollama, _preferences);
        AddOrSelectTab("browse", "Browse", page, true);
    }

    /// <summary>
    /// Performs the open training step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the open catalog step owned by this component.
    /// </summary>
    private void OpenCatalog(CatalogPageKind kind)
    {
        var page = new CatalogPageViewModel(kind, _catalog, _ollama, true);
        var title = kind switch { CatalogPageKind.Agents => "Agents", CatalogPageKind.Plugins => "Plugins", _ => "Prompt Library" };
        AddOrSelectTab("catalog-" + kind.ToString().ToLowerInvariant(), title, page, true);
    }

    /// <summary>
    /// Performs the open automations step owned by this component.
    /// </summary>
    private void OpenAutomations()
    {
        AddOrSelectTab("scheduled-actions", "Scheduled Actions",
            new AutomationsPageViewModel(_automations, _registration, _automationRunner, _scheduleCalculator), true);
    }

    /// <summary>
    /// Performs the open macros step owned by this component.
    /// </summary>
    private void OpenMacros()
    {
        var page = new MacrosPageViewModel(_workspaceState, CurrentChat.SelectedContainer?.Id, instruction => InvokeMacroAsync(instruction));
        AddOrSelectTab("macros-" + (CurrentChat.SelectedContainer?.Id.ToString("N") ?? "global"), "Macros", page, true);
    }

    /// <summary>
    /// Performs invoke macro asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task InvokeMacroAsync(string instruction)
    {
        OpenCurrentChatTab();
        await CurrentChat.InvokeAsync(instruction, "Macro");
    }

    /// <summary>
    /// Performs the open archive step owned by this component.
    /// </summary>
    private void OpenArchive() => AddOrSelectTab("archive-" + CurrentMode, "Archive", new ArchivePageViewModel(CurrentMode, _conversations, _containers), true);

    /// <summary>
    /// Performs the open activity log step owned by this component.
    /// </summary>
    private void OpenActivityLog()
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "activity-log");
        if (existing is not null) { SelectedTab = existing; return; }
        var page = new ActivityLogPageViewModel(_conversations, id => { /* navigate to chat by id */ });
        AddOrSelectTab("activity-log", "Activity Log", page, true);
    }

    /// <summary>
    /// Performs the open mode library step owned by this component.
    /// </summary>
    private void OpenModeLibrary()
    {
        var existing = OpenTabs.FirstOrDefault(item => item.Key == "mode-library");
        if (existing is not null) { SelectedTab = existing; return; }
        var page = new ModeLibraryPageViewModel(_modeRegistry, _modeUsage, _pins);
        page.OpenInStudio += () =>
        {
            _ = NavigateModeAsync(HavenMode.Studio, true);
        };
        AddOrSelectTab("mode-library", "Mode Library", page, true);
    }

    /// <summary>
    /// Performs navigate current chat asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task NavigateCurrentChatAsync()
    {
        OpenCurrentChatTab();
        await CurrentChat.RefreshCatalogAsync(CancellationToken.None);
    }

    /// <summary>
    /// Performs the open current chat tab step owned by this component.
    /// </summary>
    private void OpenCurrentChatTab()
    {
        var key = IsProjectOpen ? "project-chat-" + ActiveProject!.Id.ToString("N") : "chat-" + CurrentMode.ToString().ToLowerInvariant();
        AddOrSelectTab(key, IsProjectOpen ? ActiveProjectName + " chat" : ProductName, CurrentChat, IsProjectOpen);
    }

    /// <summary>
    /// Performs switch surface asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the start new conversation step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the add new tab step owned by this component.
    /// </summary>
    private void AddNewTab()
    {
        var chat = CreateChat(CurrentMode);
        var key = "chat-" + CurrentMode.ToString().ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N")[..8];
        AddOrSelectTab(key, ProductName, chat, true, forceNewTab: true);
    }

    /// <summary>
    /// Performs select container asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs open container definition asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs start project chat asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs open file asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private Task OpenFileAsync(ContainerDefinition container, WorkspaceFileItemViewModel file)
    {
        ActivateProject(container);
        var page = new WorkspaceEditorPageViewModel(container, CurrentChat.ConversationId, file, _workspaceTools, _workspaceState, _conversations,
            () => CurrentChat.BranchCurrentAsync(), () => CurrentChat.StopCommand.Execute(null));
        AddOrSelectTab("file-" + container.Id.ToString("N") + "-" + file.RelativePath.ToLowerInvariant(), file.Name, page, true);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs the open container settings step owned by this component.
    /// </summary>
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
            new ContainerSettingsPageViewModel(selected, _containers, RefreshAfterSettingsAsync,
                selected.Definition.Mode == HavenMode.Chat ? () => ReturnToChatGroupAsync(selected.Id) : null), true);
    }

    /// <summary>Returns from group settings to the group's chat list using freshly persisted data.</summary>
    private async Task ReturnToChatGroupAsync(Guid groupId)
    {
        var group = (await _containers.GetByModeAsync(HavenMode.Chat, CancellationToken.None))
            .FirstOrDefault(item => item.Id == groupId);
        if (group is not null) await OpenChatGroupAsync(group);
        else if (NavigateBackCommand.CanExecute(null)) NavigateBackCommand.Execute(null);
    }

    /// <summary>
    /// Performs refresh after settings asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the open application settings step owned by this component.
    /// </summary>
    private void OpenApplicationSettings()
    {
        AddOrSelectTab("settings-" + CurrentMode, "Settings", new SettingsPageViewModel(_preferences, _ollama,
            (model, effort) => CurrentChat.ApplyPreferences(model, effort), CurrentMode == HavenMode.Studio), true);
    }

    /// <summary>
    /// Performs open conversation asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs rename conversation asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RenameConversationAsync(RecentConversationViewModel? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.DraftTitle)) return;
        var updated = item.Definition with { Title = item.DraftTitle.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
        await _conversations.UpsertConversationAsync(updated, CancellationToken.None);
        item.FinishRename(updated);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    /// <summary>
    /// Performs toggle pin asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task TogglePinAsync(RecentConversationViewModel? item)
    {
        if (item is null) return;
        await _conversations.UpsertConversationAsync(item.Definition with { IsPinned = !item.Definition.IsPinned, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    /// <summary>
    /// Performs branch conversation asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task BranchConversationAsync(RecentConversationViewModel? item)
    {
        if (item is null) return;
        await OpenConversationAsync(item);
        await CurrentChat.BranchCurrentAsync();
        await RefreshRecentsAsync(CancellationToken.None);
    }

    /// <summary>
    /// Performs archive conversation asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ArchiveConversationAsync(RecentConversationViewModel? item)
    {
        if (item is null) return;
        await _conversations.UpsertConversationAsync(item.Definition with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        if (CurrentChat.ConversationId == item.Definition.Id) CurrentChat.NewChat();
        await RefreshRecentsAsync(CancellationToken.None);
    }

    /// <summary>
    /// Performs delete conversation asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DeleteConversationAsync(RecentConversationViewModel? item)
    {
        if (item is null) return;
        await _conversations.DeleteConversationAsync(item.Definition.Id, CancellationToken.None);
        if (CurrentChat.ConversationId == item.Definition.Id) CurrentChat.NewChat();
        await RefreshRecentsAsync(CancellationToken.None);
    }

    /// <summary>
    /// Performs refresh recents asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs toggle pin current asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task TogglePinCurrentAsync()
    {
        var item = await _conversations.GetAsync(CurrentChat.ConversationId, CancellationToken.None);
        if (item is null) return;
        await _conversations.UpsertConversationAsync(item with { IsPinned = !item.IsPinned, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    /// <summary>
    /// Performs the begin rename current step owned by this component.
    /// </summary>
    private void BeginRenameCurrent()
    {
        RenameDraft = CurrentChat.ConversationTitle;
        IsRenameOpen = true;
    }

    /// <summary>
    /// Performs save rename current asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SaveRenameCurrentAsync()
    {
        var item = await _conversations.GetAsync(CurrentChat.ConversationId, CancellationToken.None);
        if (item is null) return;
        await _conversations.UpsertConversationAsync(item with { Title = RenameDraft.Trim(), UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        IsRenameOpen = false;
        await CurrentChat.LoadConversationAsync(item.Id, CancellationToken.None);
        await RefreshRecentsAsync(CancellationToken.None);
    }

    /// <summary>
    /// Performs delete current asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DeleteCurrentAsync()
    {
        IsDeleteConfirmationOpen = false;
        await _conversations.DeleteConversationAsync(CurrentChat.ConversationId, CancellationToken.None);
        CurrentChat.NewChat();
        await RefreshRecentsAsync(CancellationToken.None);
    }

    /// <summary>
    /// Performs the copy last response step owned by this component.
    /// </summary>
    private void CopyLastResponse()
    {
        var content = CurrentChat.Messages.LastOrDefault(item => item.Role == MessageRole.Assistant)?.Content;
        if (!string.IsNullOrWhiteSpace(content)) CopyRequested?.Invoke(this, content);
    }

    /// <summary>
    /// Performs the add or select tab step owned by this component.
    /// </summary>
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

        // Sidebar and in-app navigation reuse the current tab. A second tab is
        // created only by the explicit + command, matching familiar browser UI.
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

    /// <summary>
    /// Performs the infer surface step owned by this component.
    /// </summary>
    private HavenSurface InferSurface(object page) => page switch
    {
        HomePageViewModel => HavenSurface.Home,
        CallPageViewModel => HavenSurface.Call,
        PlanPageViewModel => HavenSurface.Plan,
        BrowserPageViewModel => HavenSurface.Browse,
        TrainingPageViewModel => HavenSurface.Training,
        ChatGroupPageViewModel => HavenSurface.Chat,
        ModeLibraryPageViewModel => HavenSurface.Home,
        ChatPageViewModel chat => SurfaceForMode(chat.Mode),
        StudioProjectPageViewModel or WorkspaceEditorPageViewModel => HavenSurface.Studio,
        _ => SelectedTab?.Surface ?? SurfaceForMode(CurrentMode)
    };

    /// <summary>
    /// Performs the surface for mode step owned by this component.
    /// </summary>
    private static HavenSurface SurfaceForMode(HavenMode mode) => mode switch
    {
        HavenMode.Chat => HavenSurface.Chat,
        HavenMode.Teach => HavenSurface.Teach,
        HavenMode.Do => HavenSurface.Do,
        HavenMode.Studio => HavenSurface.Studio,
        _ => HavenSurface.Chat
    };

    /// <summary>
    /// Performs the close tab step owned by this component.
    /// </summary>
    private void CloseTab(WorkspaceTabViewModel? item)
    {
        if (item is null || !item.IsCloseable || OpenTabs.Count <= 1) return;
        var index = OpenTabs.IndexOf(item);
        OpenTabs.Remove(item);
        item.Dispose();
        if (ReferenceEquals(SelectedTab, item)) SelectedTab = OpenTabs.ElementAtOrDefault(Math.Clamp(index - 1, 0, Math.Max(0, OpenTabs.Count - 1))) ?? OpenTabs.FirstOrDefault();
        RaisePropertyChanged(nameof(IsHorizontalTabsVisible));
    }

    /// <summary>
    /// Navigates backward inside the selected tab without creating another tab.
    /// </summary>
    private void NavigateBack()
    {
        if (SelectedTab is null || !SelectedTab.TryGoBack()) return;
        ApplySelectedTab(SelectedTab);
    }

    /// <summary>
    /// Reverses the most recent backward navigation inside the selected tab.
    /// </summary>
    private void NavigateForward()
    {
        if (SelectedTab is null || !SelectedTab.TryGoForward()) return;
        ApplySelectedTab(SelectedTab);
    }

    /// <summary>
    /// Builds command palette from the currently available inputs.
    /// </summary>
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
            Command("Prompt Library", "Browse built-in and custom reusable prompts invoked with >.", string.Empty, NavigatePromptsCommand),
            Command("Plugins", "Browse functional Haven capabilities invoked with @.", string.Empty, NavigatePluginsCommand),
            Command("Scheduled Actions", "Create and manage scheduled local jobs.", string.Empty, NavigateAutomationsCommand),
            Command("Macros", "Create or run explicit click-to-run actions.", string.Empty, NavigateMacrosCommand),
            Command("Archive", "Restore archived chats, groups, and projects.", string.Empty, NavigateArchiveCommand),
            Command("Activity Log", "View recent conversations and tool activity across sessions.", string.Empty, NavigateActivityLogCommand),
            Command("Haven Browse", "Open the isolated tabbed browser and side assistant.", string.Empty, NavigateBrowserCommand),
            Command("Haven Training", "Run an autonomous agent session and score the result.", string.Empty, NavigateTrainingCommand),
            Command("Mode Library", "Discover, pin, and create Haven modes.", string.Empty, NavigateModeLibraryCommand),
            Command("Build Browse extension", "Create a scoped Haven extension manifest and content script in Do or Studio.", string.Empty, BuildBrowserExtensionCommand),
            Command("Toggle sidebar", "Show or hide the current product sidebar.", string.Empty, ToggleSidebarCommand),
            Command("Refresh models", "Reload the installed Ollama model list.", string.Empty, RefreshModelsCommand),
            Command("Settings", "Appearance, models, permissions, context, and browser options.", string.Empty, NavigateSettingsCommand)
        ];
        FilterCommands();
    }

    /// <summary>
    /// Performs the command step owned by this component.
    /// </summary>
    private CommandPaletteItemViewModel Command(string name, string description, string shortcut, System.Windows.Input.ICommand command) =>
        new(name, description, shortcut, new RelayCommand(() => { IsCommandPaletteOpen = false; if (command.CanExecute(null)) command.Execute(null); }));

    /// <summary>
    /// Performs the filter commands step owned by this component.
    /// </summary>
    private void FilterCommands()
    {
        CommandItems.Clear();
        foreach (var item in AllCommandItems.Where(item => string.IsNullOrWhiteSpace(CommandSearch) ||
                     item.Name.Contains(CommandSearch, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(CommandSearch, StringComparison.OrdinalIgnoreCase)))
            CommandItems.Add(item);
    }

    /// <summary>
    /// Performs the attach chat step owned by this component.
    /// </summary>
    private void AttachChat(ChatPageViewModel chat)
    {
        chat.PropertyChanged += OnChatPropertyChanged;
        chat.ConversationChanged += OnConversationChanged;
    }
    /// <summary>
    /// Performs the detach chat step owned by this component.
    /// </summary>
    private void DetachChat(ChatPageViewModel chat)
    {
        chat.PropertyChanged -= OnChatPropertyChanged;
        chat.ConversationChanged -= OnConversationChanged;
    }
    /// <summary>
    /// Handles the chat property changed event raised by the UI or runtime.
    /// </summary>
    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatPageViewModel.Status)
            or nameof(ChatPageViewModel.SelectedContainer)
            or nameof(ChatPageViewModel.SelectedLesson)
            or nameof(ChatPageViewModel.SelectedDuo)
            or nameof(ChatPageViewModel.ConversationTitle)
            or nameof(ChatPageViewModel.HasMessages)
            or nameof(ChatPageViewModel.IsTemporary)
            or nameof(ChatPageViewModel.ContextPercent)
            or nameof(ChatPageViewModel.ContextRemainingPercent))
            RaiseShellProperties();
    }
    /// <summary>
    /// Handles the conversation changed event raised by the UI or runtime.
    /// </summary>
    private void OnConversationChanged(object? sender, EventArgs e)
    {
        RaiseShellProperties();
        _ = RefreshRecentsAsync(CancellationToken.None);
    }
    /// <summary>
    /// Handles the call state changed event raised by the UI or runtime.
    /// </summary>
    private void OnCallStateChanged(object? sender, CallStateChangedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        RaisePropertyChanged(nameof(HasLiveCall));
        RaisePropertyChanged(nameof(LiveCallLabel));
    });
    /// <summary>
    /// Performs the raise shell properties step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Creates chat with the invariants required by its callers.
    /// </summary>
    private ChatPageViewModel CreateChat(HavenMode mode) => new(mode, _conversations, _containers, _catalog, _ollama, _sessions,
        _preferences, _preflight, _workspaceState, _projectIntelligence, _containerResources);

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        _reminderTimer.Stop();
        _callCoordinator.StateChanged -= OnCallStateChanged;
        _homePage?.Dispose();
        _callPage?.Dispose();
        _planPage?.Dispose();
        _companionDockVm.Dispose();
    }
}

/// <summary>
/// Represents workspace tab view model and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceTabViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Stores title locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _title;
    /// <summary>
    /// Stores is selected locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isSelected;
    /// <summary>
    /// Stores is hovered locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isHovered;
    private string _key;
    private bool _isCloseable;
    private Guid? _groupId;
    private string _groupName = string.Empty;
    private bool _isGroupCollapsed;
    private bool _isMarkedForGrouping;
    private readonly Stack<WorkspaceTabState> _backHistory = new();
    private readonly Stack<WorkspaceTabState> _forwardHistory = new();

    public WorkspaceTabViewModel(string key, string title, object page, bool isCloseable, HavenSurface surface)
    {
        _key = key;
        _title = title;
        Page = page;
        _isCloseable = isCloseable;
        Surface = surface;
    }
    /// <summary>
    /// Gets or updates key, the bindable or domain state represented by this property.
    /// </summary>
    public string Key { get => _key; private set => SetProperty(ref _key, value); }
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title { get => _title; set => SetProperty(ref _title, value); }
    /// <summary>
    /// Gets or updates page, the bindable or domain state represented by this property.
    /// </summary>
    public object Page { get; private set; }
    /// <summary>
    /// Reports whether closeable applies to the current state.
    /// </summary>
    public bool IsCloseable { get => _isCloseable; private set => SetProperty(ref _isCloseable, value); }
    /// <summary>
    /// Gets or updates surface, the bindable or domain state represented by this property.
    /// </summary>
    public HavenSurface Surface { get; private set; }
    /// <summary>
    /// Reports whether selected applies to the current state.
    /// </summary>
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    /// <summary>
    /// Reports whether hovered applies to the current state.
    /// </summary>
    public bool IsHovered { get => _isHovered; set => SetProperty(ref _isHovered, value); }
    /// <summary>Identifies the optional Chrome-style visual group containing this tab.</summary>
    public Guid? GroupId { get => _groupId; set => SetProperty(ref _groupId, value); }
    /// <summary>Stores the shared, user-editable label rendered before a group of tabs.</summary>
    public string GroupName { get => _groupName; set => SetProperty(ref _groupName, value); }
    /// <summary>Collapses every member behind the group's label without closing its pages.</summary>
    public bool IsGroupCollapsed { get => _isGroupCollapsed; set => SetProperty(ref _isGroupCollapsed, value); }
    /// <summary>Marks a tab during Ctrl-click multi-selection before creating a group.</summary>
    public bool IsMarkedForGrouping { get => _isMarkedForGrouping; set => SetProperty(ref _isMarkedForGrouping, value); }
    /// <summary>
    /// Indicates whether the universal Back command has an earlier screen to restore.
    /// </summary>
    public bool CanGoBack => _backHistory.Count > 0;
    /// <summary>
    /// Indicates whether the universal Forward command can reverse a Back action.
    /// </summary>
    public bool CanGoForward => _forwardHistory.Count > 0;

    /// <summary>
    /// Navigates this tab to a new screen while retaining the old screen for Back.
    /// </summary>
    public void NavigateTo(string key, string title, object page, bool isCloseable, HavenSurface surface)
    {
        if (ReferenceEquals(Page, page) && Key.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            Title = title;
            IsCloseable = isCloseable;
            SetSurface(surface);
            return;
        }

        _backHistory.Push(CaptureState());
        _forwardHistory.Clear();
        ApplyState(new WorkspaceTabState(key, title, page, isCloseable, surface));
        RaiseHistoryChanged();
    }

    /// <summary>
    /// Restores the preceding screen in this tab and saves the current screen for Forward.
    /// </summary>
    public bool TryGoBack()
    {
        if (_backHistory.Count == 0) return false;
        _forwardHistory.Push(CaptureState());
        ApplyState(_backHistory.Pop());
        RaiseHistoryChanged();
        return true;
    }

    /// <summary>
    /// Restores the screen most recently removed by Back.
    /// </summary>
    public bool TryGoForward()
    {
        if (_forwardHistory.Count == 0) return false;
        _backHistory.Push(CaptureState());
        ApplyState(_forwardHistory.Pop());
        RaiseHistoryChanged();
        return true;
    }

    private WorkspaceTabState CaptureState() => new(Key, Title, Page, IsCloseable, Surface);

    private void ApplyState(WorkspaceTabState state)
    {
        Key = state.Key;
        Title = state.Title;
        Page = state.Page;
        IsCloseable = state.IsCloseable;
        Surface = state.Surface;
        RaisePropertyChanged(nameof(Page));
        RaisePropertyChanged(nameof(Surface));
    }

    private void RaiseHistoryChanged()
    {
        RaisePropertyChanged(nameof(CanGoBack));
        RaisePropertyChanged(nameof(CanGoForward));
    }

    /// <summary>
    /// Releases the current screen and every retained history screen exactly once
    /// when the owning tab closes.
    /// </summary>
    public void Dispose()
    {
        var pages = _backHistory.Select(state => state.Page)
            .Concat(_forwardHistory.Select(state => state.Page))
            .Append(Page)
            .Distinct(ReferenceEqualityComparer.Instance);
        foreach (var page in pages)
            if (page is IDisposable disposable) disposable.Dispose();
        _backHistory.Clear();
        _forwardHistory.Clear();
        RaiseHistoryChanged();
    }
    /// <summary>
    /// Performs the replace page step owned by this component.
    /// </summary>
    public void ReplacePage(object page)
    {
        if (ReferenceEquals(Page, page)) return;
        if (Page is IDisposable disposable) disposable.Dispose();
        Page = page;
        RaisePropertyChanged(nameof(Page));
    }
    /// <summary>
    /// Performs the set surface step owned by this component.
    /// </summary>
    public void SetSurface(HavenSurface surface)
    {
        if (Surface == surface) return;
        Surface = surface;
        RaisePropertyChanged(nameof(Surface));
    }
}

/// <summary>
/// Immutable snapshot of one screen in a workspace tab's navigation history.
/// </summary>
public sealed record WorkspaceTabState(
    string Key,
    string Title,
    object Page,
    bool IsCloseable,
    HavenSurface Surface);

/// <summary>
/// Represents command palette item view model and keeps its related state and behavior together.
/// </summary>
public sealed record CommandPaletteItemViewModel(string Name, string Description, string Shortcut, RelayCommand RunCommand);

/// <summary>
/// Represents recent conversation view model and keeps its related state and behavior together.
/// </summary>
public sealed class RecentConversationViewModel : ObservableObject
{
    /// <summary>
    /// Stores definition locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Conversation _definition;
    /// <summary>
    /// Stores is renaming locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isRenaming;
    /// <summary>
    /// Stores is delete confirming locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isDeleteConfirming;
    /// <summary>
    /// Stores draft title locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _draftTitle;
    /// <summary>
    /// Stores is active locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isActive;

    public RecentConversationViewModel(Conversation definition, Func<RecentConversationViewModel?, Task> open,
        Func<RecentConversationViewModel?, Task> rename, Func<RecentConversationViewModel?, Task> togglePin,
        Func<RecentConversationViewModel?, Task> branch, Func<RecentConversationViewModel?, Task> archive,
        Func<RecentConversationViewModel?, Task> delete)
    {
        _definition = definition;
        _draftTitle = definition.Title;
        OpenCommand = new AsyncRelayCommand(() => open(this));
        BeginRenameCommand = new RelayCommand(() => IsRenaming = true);
        SaveRenameCommand = new AsyncRelayCommand(() => rename(this), () => !string.IsNullOrWhiteSpace(DraftTitle));
        CancelRenameCommand = new RelayCommand(() => { DraftTitle = Definition.Title; IsRenaming = false; });
        TogglePinCommand = new AsyncRelayCommand(() => togglePin(this));
        BranchCommand = new AsyncRelayCommand(() => branch(this));
        ArchiveCommand = new AsyncRelayCommand(() => archive(this));
        DeleteCommand = new RelayCommand(() => IsDeleteConfirming = true);
        ConfirmDeleteCommand = new AsyncRelayCommand(async () => { await delete(this); IsDeleteConfirming = false; });
        CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirming = false);
    }

    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public Conversation Definition => _definition;
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => Definition.Title;
    /// <summary>
    /// Gets or updates meta, the bindable or domain state represented by this property.
    /// </summary>
    public string Meta => Definition.UpdatedAt.LocalDateTime.ToString("g");
    /// <summary>
    /// Reports whether pinned applies to the current state.
    /// </summary>
    public bool IsPinned => Definition.IsPinned;
    /// <summary>
    /// Gets or updates pin label, the bindable or domain state represented by this property.
    /// </summary>
    public string PinLabel => IsPinned ? "Unpin" : "Pin";
    /// <summary>
    /// Reports whether active applies to the current state.
    /// </summary>
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
    /// <summary>
    /// Reports whether renaming applies to the current state.
    /// </summary>
    public bool IsRenaming { get => _isRenaming; set { if (SetProperty(ref _isRenaming, value)) RaisePropertyChanged(nameof(IsNormal)); } }
    /// <summary>
    /// Reports whether delete confirming applies to the current state.
    /// </summary>
    public bool IsDeleteConfirming { get => _isDeleteConfirming; set { if (SetProperty(ref _isDeleteConfirming, value)) RaisePropertyChanged(nameof(IsNormal)); } }
    /// <summary>
    /// Reports whether normal applies to the current state.
    /// </summary>
    public bool IsNormal => !IsRenaming && !IsDeleteConfirming;
    /// <summary>
    /// Gets or updates draft title, the bindable or domain state represented by this property.
    /// </summary>
    public string DraftTitle { get => _draftTitle; set { if (SetProperty(ref _draftTitle, value)) SaveRenameCommand.RaiseCanExecuteChanged(); } }
    /// <summary>
    /// Gets or updates open command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand OpenCommand { get; }
    /// <summary>
    /// Gets or updates begin rename command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand BeginRenameCommand { get; }
    /// <summary>
    /// Gets or updates save rename command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SaveRenameCommand { get; }
    /// <summary>
    /// Reports whether cancel rename command is true for the current state.
    /// </summary>
    public RelayCommand CancelRenameCommand { get; }
    /// <summary>
    /// Gets or updates toggle pin command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand TogglePinCommand { get; }
    /// <summary>
    /// Gets or updates branch command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand BranchCommand { get; }
    /// <summary>
    /// Gets or updates archive command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ArchiveCommand { get; }
    /// <summary>
    /// Gets or updates delete command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand DeleteCommand { get; }
    /// <summary>
    /// Gets or updates confirm delete command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ConfirmDeleteCommand { get; }
    /// <summary>
    /// Reports whether cancel delete command is true for the current state.
    /// </summary>
    public RelayCommand CancelDeleteCommand { get; }

    /// <summary>
    /// Performs the finish rename step owned by this component.
    /// </summary>
    public void FinishRename(Conversation updated)
    {
        _definition = updated;
        DraftTitle = updated.Title;
        IsRenaming = false;
        RaisePropertyChanged(nameof(Definition));
        RaisePropertyChanged(nameof(Title));
        RaisePropertyChanged(nameof(Meta));
    }
}
