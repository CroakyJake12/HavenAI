/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/ChatPageViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns ChatPageViewModel, PreparedAttachment, MessageBubbleViewModel, AgentItemViewModel, PluginItemViewModel, PromptItemViewModel, CatalogVisibility, ContainerItemViewModel, LessonItemViewModel, LessonGroupViewModel, ToolActivityViewModel, InlineQuestionViewModel, AttachmentItemViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents chat page view model and keeps its related state and behavior together.
/// </summary>
public sealed class ChatPageViewModel : ObservableObject
{
    /// <summary>
    /// Stores image extensions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".gif" };
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
    /// Stores ollama locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _ollama;
    /// <summary>
    /// Stores sessions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ChatSessionService _sessions;
    /// <summary>
    /// Stores preferences locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly UserPreferencesService _preferences;
    /// <summary>
    /// Stores preflight locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly CapabilityPreflightService _preflight;
    /// <summary>
    /// Stores workspace state locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IWorkspaceStateRepository _workspaceState;
    /// <summary>
    /// Stores project intelligence locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IProjectIntelligenceService _projectIntelligence;
    /// <summary>
    /// Stores container resources locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IContainerResourceRepository? _containerResources;
    /// <summary>
    /// Stores send cancellation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _sendCancellation;
    /// <summary>
    /// Stores lesson load cancellation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _lessonLoadCancellation;
    /// <summary>
    /// Stores conversation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Conversation _conversation;
    /// <summary>
    /// Stores composer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _composer = string.Empty;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Connecting to Ollama…";
    /// <summary>Stores whether the composer should display the local-model offline warning.</summary>
    private bool _isOllamaOfflineWarningVisible;
    /// <summary>
    /// Stores selected model locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ModelDescriptor? _selectedModel;
    /// <summary>
    /// Stores selected agent locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private AgentItemViewModel? _selectedAgent;
    /// <summary>
    /// Stores selected effort locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private EffortLevel _selectedEffort = EffortLevel.Medium;
    /// <summary>
    /// Stores selected duo locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private DuoMode _selectedDuo = DuoMode.Solo;
    /// <summary>
    /// Stores is sending locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isSending;
    /// <summary>
    /// Stores is temporary locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isTemporary;
    /// <summary>
    /// Stores is plugin picker open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isPluginPickerOpen;
    /// <summary>
    /// Stores is prompt picker open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isPromptPickerOpen;
    /// <summary>
    /// Stores is model picker open locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isModelPickerOpen;
    /// <summary>
    /// Stores is computer permission pending locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isComputerPermissionPending;
    /// <summary>
    /// Stores attachment summary locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _attachmentSummary = string.Empty;
    /// <summary>
    /// Stores selected container locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ContainerItemViewModel? _selectedContainer;
    /// <summary>
    /// Stores selected lesson locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private LessonItemViewModel? _selectedLesson;
    /// <summary>
    /// Stores agent notice locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _agentNotice = "The selected agent will handle the next message.";
    /// <summary>
    /// Stores model search locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _modelSearch = string.Empty;
    /// <summary>
    /// Stores missing plugin name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _missingPluginName = string.Empty;
    /// <summary>
    /// Stores missing plugin reason locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _missingPluginReason = string.Empty;
    /// <summary>
    /// Stores is tool permission pending locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isToolPermissionPending;
    /// <summary>
    /// Stores tool permission request locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _toolPermissionRequest = string.Empty;
    /// <summary>
    /// Stores permission retry prompt locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string? _permissionRetryPrompt;
    /// <summary>
    /// Stores approved tool permission once locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _approvedToolPermissionOnce;
    /// <summary>
    /// Stores retry prompt locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string? _retryPrompt;
    /// <summary>
    /// Stores retry after computer permission locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _retryAfterComputerPermission;
    /// <summary>
    /// Stores inline question locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private InlineQuestionViewModel? _inlineQuestion;
    /// <summary>
    /// Stores context tokens locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _contextTokens;
    /// <summary>
    /// Stores context summary locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _contextSummary = string.Empty;
    /// <summary>
    /// Stores edit step locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _editStep;
    /// <summary>
    /// Stores lines added locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _linesAdded;
    /// <summary>
    /// Stores lines removed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _linesRemoved;
    /// <summary>
    /// Stores suppress selection conversation update locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _suppressSelectionConversationUpdate;
    /// <summary>
    /// Stores whether there are unresolved errors after sending, shown as a floating resolve button.
    /// </summary>
    private bool _hasErrorsToResolve;

    public ChatPageViewModel(
        HavenMode mode,
        IConversationRepository conversations,
        IContainerRepository containers,
        ICatalogRepository catalog,
        IOllamaClient ollama,
        ChatSessionService sessions,
        UserPreferencesService preferences,
        CapabilityPreflightService preflight,
        IWorkspaceStateRepository workspaceState,
        IProjectIntelligenceService projectIntelligence,
        IContainerResourceRepository? containerResources = null)
    {
        Mode = mode;
        _conversations = conversations;
        _containers = containers;
        _catalog = catalog;
        _ollama = ollama;
        _sessions = sessions;
        _preferences = preferences;
        _preflight = preflight;
        _workspaceState = workspaceState;
        _projectIntelligence = projectIntelligence;
        _containerResources = containerResources;
        _selectedEffort = preferences.DefaultEffort;
        var now = DateTimeOffset.UtcNow;
        _conversation = NewConversation(now);
        SendCommand = new AsyncRelayCommand(SendAsync, () => !IsSending && !string.IsNullOrWhiteSpace(Composer) && SelectedModel is not null);
        StopCommand = new RelayCommand(Stop, () => IsSending);
        NewChatCommand = new RelayCommand(NewChat);
        ToggleTemporaryCommand = new RelayCommand(ToggleTemporary);
        TogglePluginCommand = new RelayCommand<PluginItemViewModel>(TogglePlugin);
        SelectPluginCommand = new RelayCommand<PluginItemViewModel>(SelectPlugin);
        OpenPluginPickerCommand = new RelayCommand(OpenPluginPicker);
        TogglePromptCommand = new RelayCommand<PromptItemViewModel>(TogglePrompt);
        SelectPromptCommand = new RelayCommand<PromptItemViewModel>(SelectPrompt);
        OpenPromptPickerCommand = new RelayCommand(OpenPromptPicker);
        ClosePickersCommand = new RelayCommand(ClosePickers);
        OpenModelPickerCommand = new RelayCommand(() => IsModelPickerOpen = true);
        CloseModelPickerCommand = new RelayCommand(() => IsModelPickerOpen = false);
        SelectModelCommand = new RelayCommand<ModelDescriptor>(model => { if (model is not null) SelectedModel = model; IsModelPickerOpen = false; });
        RetryWithPluginCommand = new RelayCommand(RetryWithPlugin);
        DismissMissingPluginCommand = new RelayCommand(ClearMissingPlugin);
        ApproveToolPermissionCommand = new RelayCommand(ApproveToolPermission);
        DismissToolPermissionCommand = new RelayCommand(DismissToolPermission);
        CompactContextCommand = new AsyncRelayCommand(() => CompactContextAsync(false));
        AnswerInlineQuestionCommand = new RelayCommand<string>(AnswerInlineQuestion);
        DismissInlineQuestionCommand = new RelayCommand(() => InlineQuestion = null);
        EnableComputerUseCommand = new RelayCommand(EnableComputerUse);
        DismissComputerUseCommand = new RelayCommand(DismissComputerUse);
        RemoveAttachmentCommand = new RelayCommand<AttachmentItemViewModel>(RemoveAttachment);
        SelectContainerCommand = new RelayCommand<ContainerItemViewModel>(item => SelectedContainer = item);
        DeleteContainerCommand = new AsyncRelayCommand<ContainerItemViewModel>(DeleteContainerAsync);
        SelectLessonCommand = new RelayCommand<LessonItemViewModel>(item => SelectedLesson = item);
        SelectQuickChatsCommand = new RelayCommand(SelectQuickChats);
        RefreshModelsCommand = new AsyncRelayCommand(RefreshModelsAsync);
        NewContainerCommand = new AsyncRelayCommand(CreateContainerAsync, () => HasContainers);
        NewLessonCommand = new AsyncRelayCommand(CreateLessonAsync, () => IsTeach && SelectedContainer is not null);
        DeleteLessonCommand = new AsyncRelayCommand<LessonItemViewModel>(DeleteLessonAsync);
        MoveLessonUpCommand = new AsyncRelayCommand<LessonItemViewModel>(item => MoveLessonAsync(item, -1));
        MoveLessonDownCommand = new AsyncRelayCommand<LessonItemViewModel>(item => MoveLessonAsync(item, 1));
        UseStarterCommand = new RelayCommand<string>(text => { if (!string.IsNullOrWhiteSpace(text)) Composer = text; });
        ResolveErrorsCommand = new AsyncRelayCommand(ResolveErrorsAsync);
    }

    /// <summary>
    /// Stores conversation changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler? ConversationChanged;

    /// <summary>
    /// Gets or updates mode, the bindable or domain state represented by this property.
    /// </summary>
    public HavenMode Mode { get; }
    /// <summary>
    /// Gets or updates mode title, the bindable or domain state represented by this property.
    /// </summary>
    public string ModeTitle => Mode switch { HavenMode.Chat => "Chat", HavenMode.Teach => "Study", HavenMode.Do => "Do", HavenMode.Studio => "Studio", _ => "Haven" };
    /// <summary>
    /// Gets or updates mode subtitle, the bindable or domain state represented by this property.
    /// </summary>
    public string ModeSubtitle => Mode switch
    {
        HavenMode.Chat => "Private conversation with your local models",
        HavenMode.Teach => "Structured lessons, explanations and knowledge checks",
        HavenMode.Do => "Task completion with visible approvals and an audit trail",
        HavenMode.Studio => "Inspect, edit, run, test and repair local projects",
        _ => string.Empty
    };
    /// <summary>
    /// Gets or updates empty title, the bindable or domain state represented by this property.
    /// </summary>
    public string EmptyTitle => Mode switch
    {
        HavenMode.Chat => "What’s on your mind?",
        HavenMode.Teach => "What should we learn?",
        HavenMode.Do => "What should Haven do?",
        HavenMode.Studio => "What are we building?",
        _ => "How can Haven help?"
    };
    /// <summary>
    /// Gets or updates starter one, the bindable or domain state represented by this property.
    /// </summary>
    public string StarterOne => Mode switch
    {
        HavenMode.Studio => "Explain this project and identify the riskiest areas.",
        HavenMode.Do => "Research and compare the best options.",
        HavenMode.Teach => "Study a topic step by step.",
        _ => "Explain this clearly."
    };
    /// <summary>
    /// Gets or updates starter two, the bindable or domain state represented by this property.
    /// </summary>
    public string StarterTwo => Mode switch
    {
        HavenMode.Studio => ">Plan Create a precise implementation plan.",
        HavenMode.Do => "Proofread the attached document.",
        HavenMode.Teach => "Create a retrieval quiz for this lesson.",
        _ => "Help me think through a decision."
    };
    /// <summary>
    /// Gets or updates starter three, the bindable or domain state represented by this property.
    /// </summary>
    public string StarterThree => Mode switch
    {
        HavenMode.Studio => ">Debug Diagnose the error in my latest build.",
        HavenMode.Do => "Organise this workspace safely.",
        HavenMode.Teach => "Explain this using examples, then test me.",
        _ => "Summarise the important points."
    };
    /// <summary>
    /// Gets or updates starter four, the bindable or domain state represented by this property.
    /// </summary>
    public string StarterFour => Mode switch
    {
        HavenMode.Studio => "@Agent @WebSearch >Report Research a topic thoroughly.",
        HavenMode.Do => "Create a careful step-by-step action plan.",
        HavenMode.Teach => "Build a structured learning plan.",
        _ => "Compare a few possible approaches."
    };
    /// <summary>
    /// Gets or updates new chat title, the bindable or domain state represented by this property.
    /// </summary>
    public string NewChatTitle => Mode switch { HavenMode.Teach => "Quick chat", HavenMode.Do => "New task", HavenMode.Studio => "New studio chat", _ => "New chat" };
    /// <summary>
    /// Gets or updates container label, the bindable or domain state represented by this property.
    /// </summary>
    public string ContainerLabel => Mode switch { HavenMode.Chat => "Chat group", HavenMode.Teach => "Subject", HavenMode.Do => "Task group", _ => "Project" };
    /// <summary>
    /// Gets or updates new container label, the bindable or domain state represented by this property.
    /// </summary>
    public string NewContainerLabel => "+ " + ContainerLabel;
    /// <summary>
    /// Reports whether teach applies to the current state.
    /// </summary>
    public bool IsTeach => Mode == HavenMode.Teach;
    /// <summary>
    /// Reports whether do applies to the current state.
    /// </summary>
    public bool IsDo => Mode == HavenMode.Do;
    /// <summary>
    /// Reports whether studio applies to the current state.
    /// </summary>
    public bool IsStudio => Mode == HavenMode.Studio;
    /// <summary>
    /// Reports whether containers applies to the current state.
    /// </summary>
    public bool HasContainers => Mode is HavenMode.Chat or HavenMode.Teach or HavenMode.Do or HavenMode.Studio;
    /// <summary>
    /// Reports whether any containers applies to the current state.
    /// </summary>
    public bool HasAnyContainers => Containers.Count > 0;
    /// <summary>
    /// Reports whether subjects applies to the current state.
    /// </summary>
    public bool HasSubjects => IsTeach && Containers.Count > 0;
    /// <summary>
    /// Reports whether no subjects applies to the current state.
    /// </summary>
    public bool HasNoSubjects => IsTeach && Containers.Count == 0;
    /// <summary>
    /// Reports whether selected subject applies to the current state.
    /// </summary>
    public bool HasSelectedSubject => IsTeach && SelectedContainer is not null;
    /// <summary>
    /// Reports whether lessons applies to the current state.
    /// </summary>
    public bool HasLessons => IsTeach && Lessons.Count > 0;
    /// <summary>
    /// Reports whether no lessons applies to the current state.
    /// </summary>
    public bool HasNoLessons => IsTeach && SelectedContainer is not null && Lessons.Count == 0;
    /// <summary>
    /// Gets or updates show teach empty state, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowTeachEmptyState => IsTeach && (SelectedContainer is null || Lessons.Count == 0);
    /// <summary>
    /// Gets or updates teach empty state title, the bindable or domain state represented by this property.
    /// </summary>
    public string TeachEmptyStateTitle => Containers.Count == 0 ? "Create your first subject" : SelectedContainer is null ? "Choose a subject" : "No lessons yet";
    /// <summary>
    /// Gets or updates teach empty state message, the bindable or domain state represented by this property.
    /// </summary>
    public string TeachEmptyStateMessage => Containers.Count == 0
        ? "Subjects organise structured lessons. Quick Chats always remain available outside a subject."
        : SelectedContainer is null
            ? "Open a subject to see its lessons, or continue with a Quick Chat."
            : "Add a lesson to this subject, or use Quick Chats for an unstructured question.";
    /// <summary>
    /// Gets or updates supports duo, the bindable or domain state represented by this property.
    /// </summary>
    public bool SupportsDuo => Mode is HavenMode.Do or HavenMode.Studio;
    /// <summary>
    /// Reports whether workspace root applies to the current state.
    /// </summary>
    public bool HasWorkspaceRoot => HasContainers && !string.IsNullOrWhiteSpace(SelectedContainer?.RootPath);
    /// <summary>
    /// Reports whether workspace tools ready applies to the current state.
    /// </summary>
    public bool IsWorkspaceToolsReady => Mode is HavenMode.Do or HavenMode.Studio && HasWorkspaceRoot;
    /// <summary>
    /// Reports whether agent plugin active applies to the current state.
    /// </summary>
    public bool IsAgentPluginActive => Plugins.Any(x => x.Name == "Agent" && x.IsActive);
    /// <summary>
    /// Reports whether duo plugin active applies to the current state.
    /// </summary>
    public bool IsDuoPluginActive => Plugins.Any(x => x.Name == "DuoMode" && x.IsActive);
    /// <summary>
    /// Reports whether messages applies to the current state.
    /// </summary>
    public bool HasMessages => Messages.Count > 0;
    /// <summary>
    /// Reports whether empty applies to the current state.
    /// </summary>
    public bool IsEmpty => !HasMessages;
    /// <summary>
    /// Reports whether not sending applies to the current state.
    /// </summary>
    public bool IsNotSending => !IsSending;
    /// <summary>
    /// Gets or updates conversation id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid ConversationId => _conversation.Id;
    /// <summary>
    /// Gets or updates conversation title, the bindable or domain state represented by this property.
    /// </summary>
    public string ConversationTitle => _conversation.Title;
    /// <summary>
    /// Gets or updates current scope, the bindable or domain state represented by this property.
    /// </summary>
    public ConversationScope? CurrentScope => Mode is HavenMode.Chat or HavenMode.Teach ? ConversationScope.From(_conversation) : null;

    /// <summary>
    /// Gets or updates messages, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<MessageBubbleViewModel> Messages { get; } = [];
    /// <summary>
    /// Gets or updates models, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ModelDescriptor> Models { get; } = [];
    /// <summary>
    /// Gets or updates agents, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<AgentItemViewModel> Agents { get; } = [];
    /// <summary>
    /// Gets or updates plugins, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<PluginItemViewModel> Plugins { get; } = [];
    /// <summary>
    /// Gets or updates plugin suggestions, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<PluginItemViewModel> PluginSuggestions { get; } = [];
    /// <summary>
    /// Gets or updates prompts, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<PromptItemViewModel> Prompts { get; } = [];
    /// <summary>
    /// Gets or updates prompt suggestions, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<PromptItemViewModel> PromptSuggestions { get; } = [];
    /// <summary>
    /// Gets or updates containers, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ContainerItemViewModel> Containers { get; } = [];
    /// <summary>
    /// Gets or updates lessons, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<LessonItemViewModel> Lessons { get; } = [];
    /// <summary>
    /// Gets or updates lesson groups, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<LessonGroupViewModel> LessonGroups { get; } = [];
    /// <summary>
    /// Gets or updates attachments, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<AttachmentItemViewModel> Attachments { get; } = [];
    /// <summary>
    /// Gets or updates effort levels, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<EffortLevel> EffortLevels { get; } = Enum.GetValues<EffortLevel>();
    /// <summary>
    /// Gets or updates duo modes, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<DuoMode> DuoModes { get; } = [DuoMode.PingPong, DuoMode.Collaborate, DuoMode.Supervise];
    /// <summary>
    /// Gets or updates filtered models, the bindable or domain state represented by this property.
    /// </summary>
    public IEnumerable<ModelDescriptor> FilteredModels => string.IsNullOrWhiteSpace(ModelSearch) ? Models : Models.Where(model =>
        model.Name.Contains(ModelSearch, StringComparison.OrdinalIgnoreCase) || model.Family.Contains(ModelSearch, StringComparison.OrdinalIgnoreCase));

    public string Composer
    {
        get => _composer;
        set
        {
            if (!SetProperty(ref _composer, value)) return;
            UpdateSuggestions(value);
            SendCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates attachment summary, the bindable or domain state represented by this property.
    /// </summary>
    public string AttachmentSummary { get => _attachmentSummary; private set => SetProperty(ref _attachmentSummary, value); }
    /// <summary>
    /// Reports whether attachment applies to the current state.
    /// </summary>
    public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentSummary);
    /// <summary>
    /// Reports whether plugin picker open applies to the current state.
    /// </summary>
    public bool IsPluginPickerOpen { get => _isPluginPickerOpen; private set { if (SetProperty(ref _isPluginPickerOpen, value)) RaisePropertyChanged(nameof(IsPickerOverlayVisible)); } }
    /// <summary>
    /// Reports whether prompt picker open applies to the current state.
    /// </summary>
    public bool IsPromptPickerOpen { get => _isPromptPickerOpen; private set { if (SetProperty(ref _isPromptPickerOpen, value)) RaisePropertyChanged(nameof(IsPickerOverlayVisible)); } }
    /// <summary>
    /// Reports whether picker overlay visible applies to the current state.
    /// </summary>
    public bool IsPickerOverlayVisible => IsPluginPickerOpen || IsPromptPickerOpen;
    /// <summary>
    /// Reports whether model picker open applies to the current state.
    /// </summary>
    public bool IsModelPickerOpen { get => _isModelPickerOpen; set => SetProperty(ref _isModelPickerOpen, value); }
    /// <summary>
    /// Gets or updates model search, the bindable or domain state represented by this property.
    /// </summary>
    public string ModelSearch { get => _modelSearch; set { if (SetProperty(ref _modelSearch, value)) RaisePropertyChanged(nameof(FilteredModels)); } }
    /// <summary>
    /// Reports whether computer permission pending applies to the current state.
    /// </summary>
    public bool IsComputerPermissionPending { get => _isComputerPermissionPending; private set => SetProperty(ref _isComputerPermissionPending, value); }
    /// <summary>
    /// Gets or updates missing plugin name, the bindable or domain state represented by this property.
    /// </summary>
    public string MissingPluginName { get => _missingPluginName; private set => SetProperty(ref _missingPluginName, value); }
    /// <summary>
    /// Gets or updates missing plugin reason, the bindable or domain state represented by this property.
    /// </summary>
    public string MissingPluginReason { get => _missingPluginReason; private set => SetProperty(ref _missingPluginReason, value); }
    /// <summary>
    /// Reports whether missing plugin notice applies to the current state.
    /// </summary>
    public bool HasMissingPluginNotice => !string.IsNullOrWhiteSpace(MissingPluginName);
    /// <summary>Reports whether a local model is selected but Ollama cannot currently be reached.</summary>
    public bool IsOllamaOfflineWarningVisible
    {
        get => _isOllamaOfflineWarningVisible;
        private set => SetProperty(ref _isOllamaOfflineWarningVisible, value);
    }
    /// <summary>
    /// Reports whether tool permission pending applies to the current state.
    /// </summary>
    public bool IsToolPermissionPending { get => _isToolPermissionPending; private set => SetProperty(ref _isToolPermissionPending, value); }
    /// <summary>
    /// Gets or updates tool permission request, the bindable or domain state represented by this property.
    /// </summary>
    public string ToolPermissionRequest { get => _toolPermissionRequest; private set => SetProperty(ref _toolPermissionRequest, value); }
    /// <summary>
    /// Gets or updates inline question, the bindable or domain state represented by this property.
    /// </summary>
    public InlineQuestionViewModel? InlineQuestion { get => _inlineQuestion; private set { if (SetProperty(ref _inlineQuestion, value)) RaisePropertyChanged(nameof(HasInlineQuestion)); } }
    /// <summary>
    /// Reports whether inline question applies to the current state.
    /// </summary>
    public bool HasInlineQuestion => InlineQuestion is not null;
    /// <summary>
    /// Gets or updates context tokens, the bindable or domain state represented by this property.
    /// </summary>
    public int ContextTokens { get => _contextTokens; private set => SetProperty(ref _contextTokens, value); }
    /// <summary>
    /// Gets or updates context limit, the bindable or domain state represented by this property.
    /// </summary>
    public int ContextLimit => _preferences.GenerationOptions.ContextLimit;
    /// <summary>
    /// Gets or updates context percent, the bindable or domain state represented by this property.
    /// </summary>
    public int ContextPercent => Math.Clamp((int)Math.Round(ContextTokens * 100d / Math.Max(1, ContextLimit)), 0, 100);
    /// <summary>Shows the more useful remaining-capacity value in the compact header widget.</summary>
    public int ContextRemainingPercent => 100 - ContextPercent;
    /// <summary>
    /// Gets or updates context label, the bindable or domain state represented by this property.
    /// </summary>
    public string ContextLabel => $"{ContextTokens:N0} / {ContextLimit:N0} tokens · {ContextPercent}%";
    /// <summary>Explains the compact value without making the header itself verbose.</summary>
    public string ContextRemainingLabel => $"{ContextRemainingPercent}% context remaining";
    /// <summary>
    /// Gets or updates context sweep, the bindable or domain state represented by this property.
    /// </summary>
    public double ContextSweep => ContextPercent * 3.6;
    /// <summary>
    /// Gets or updates show confidence, the bindable or domain state represented by this property.
    /// </summary>
    public bool ShowConfidence => _preferences.ConfidenceMeter;
    /// <summary>
    /// Reports whether edit progress visible applies to the current state.
    /// </summary>
    public bool IsEditProgressVisible => IsSending && IsWorkspaceToolsReady;
    /// <summary>
    /// Gets or updates edit progress label, the bindable or domain state represented by this property.
    /// </summary>
    public string EditProgressLabel => $"Step {Math.Max(1, EditStep)} · +{LinesAdded}/-{LinesRemoved} lines";
    /// <summary>
    /// Gets or updates edit step, the bindable or domain state represented by this property.
    /// </summary>
    public int EditStep { get => _editStep; private set { if (SetProperty(ref _editStep, value)) RaisePropertyChanged(nameof(EditProgressLabel)); } }
    /// <summary>
    /// Gets or updates lines added, the bindable or domain state represented by this property.
    /// </summary>
    public int LinesAdded { get => _linesAdded; private set { if (SetProperty(ref _linesAdded, value)) RaisePropertyChanged(nameof(EditProgressLabel)); } }
    /// <summary>
    /// Gets or updates lines removed, the bindable or domain state represented by this property.
    /// </summary>
    public int LinesRemoved { get => _linesRemoved; private set { if (SetProperty(ref _linesRemoved, value)) RaisePropertyChanged(nameof(EditProgressLabel)); } }

    public ModelDescriptor? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!SetProperty(ref _selectedModel, value)) return;
            SendCommand.RaiseCanExecuteChanged();
            if (value is not null) _preferences.SetModelDefaults(value.Name, SelectedEffort);
        }
    }

    public AgentItemViewModel? SelectedAgent
    {
        get => _selectedAgent;
        set
        {
            if (!SetProperty(ref _selectedAgent, value)) return;
            AgentNotice = value?.Name == "Auto"
                ? "Auto will choose a specialist when the request clearly matches one."
                : $"{value?.Name ?? "Default"} will handle the next message.";
        }
    }
    /// <summary>
    /// Gets or updates agent notice, the bindable or domain state represented by this property.
    /// </summary>
    public string AgentNotice { get => _agentNotice; private set => SetProperty(ref _agentNotice, value); }
    public EffortLevel SelectedEffort
    {
        get => _selectedEffort;
        set
        {
            if (!SetProperty(ref _selectedEffort, value)) return;
            _preferences.SetModelDefaults(SelectedModel?.Name ?? _preferences.DefaultModel, value);
        }
    }
    /// <summary>
    /// Gets or updates selected duo, the bindable or domain state represented by this property.
    /// </summary>
    public DuoMode SelectedDuo { get => _selectedDuo; set => SetProperty(ref _selectedDuo, value); }

    public ContainerItemViewModel? SelectedContainer
    {
        get => _selectedContainer;
        set
        {
            if (!SetProperty(ref _selectedContainer, value)) return;
            CancelLessonLoad();
            if (_selectedLesson is not null)
            {
                _selectedLesson = null;
                RaisePropertyChanged(nameof(SelectedLesson));
            }
            Lessons.Clear();
            LessonGroups.Clear();
            ApplySelectionToConversation(startNewWhenScopeChanges: true);
            RaisePropertyChanged(nameof(HasWorkspaceRoot));
            RaisePropertyChanged(nameof(IsWorkspaceToolsReady));
            RaiseTeachStateChanged();
            RefreshPluginAvailability();
            NewLessonCommand.RaiseCanExecuteChanged();
            if (IsTeach && value is not null && !_suppressSelectionConversationUpdate) StartLessonLoad(value.Id);
        }
    }

    public LessonItemViewModel? SelectedLesson
    {
        get => _selectedLesson;
        set
        {
            if (value is not null && value.Definition.SubjectId != SelectedContainer?.Id) return;
            if (!SetProperty(ref _selectedLesson, value)) return;
            ApplySelectionToConversation(startNewWhenScopeChanges: true);
            RaiseTeachStateChanged();
        }
    }

    public bool IsSending
    {
        get => _isSending;
        private set
        {
            if (!SetProperty(ref _isSending, value)) return;
            SendCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(IsNotSending));
            RaisePropertyChanged(nameof(IsEditProgressVisible));
        }
    }

    public bool IsTemporary
    {
        get => _isTemporary;
        private set
        {
            if (!SetProperty(ref _isTemporary, value)) return;
            RaisePropertyChanged(nameof(TemporaryLabel));
            RaisePropertyChanged(nameof(TemporaryHeaderActionLabel));
            _conversation = _conversation with { IsTemporary = value };
        }
    }

    /// <summary>
    /// Gets or updates temporary label, the bindable or domain state represented by this property.
    /// </summary>
    public string TemporaryLabel => IsTemporary ? "Temporary · history off" : "Saved locally";
    /// <summary>Labels the empty-chat header action according to what clicking it will do.</summary>
    public string TemporaryHeaderActionLabel => IsTemporary ? "Make Permanent" : "Make Temporary";
    /// <summary>
    /// Gets or updates send command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SendCommand { get; }
    /// <summary>
    /// Gets or updates stop command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand StopCommand { get; }
    /// <summary>
    /// Gets or updates new chat command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand NewChatCommand { get; }
    /// <summary>
    /// Gets or updates toggle temporary command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ToggleTemporaryCommand { get; }
    /// <summary>
    /// Gets or updates toggle plugin command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<PluginItemViewModel> TogglePluginCommand { get; }
    /// <summary>
    /// Gets or updates select plugin command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<PluginItemViewModel> SelectPluginCommand { get; }
    /// <summary>
    /// Gets or updates open plugin picker command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand OpenPluginPickerCommand { get; }
    /// <summary>
    /// Gets or updates toggle prompt command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<PromptItemViewModel> TogglePromptCommand { get; }
    /// <summary>
    /// Gets or updates select prompt command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<PromptItemViewModel> SelectPromptCommand { get; }
    /// <summary>
    /// Gets or updates open prompt picker command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand OpenPromptPickerCommand { get; }
    /// <summary>
    /// Gets or updates close pickers command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ClosePickersCommand { get; }
    /// <summary>
    /// Gets or updates open model picker command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand OpenModelPickerCommand { get; }
    /// <summary>
    /// Gets or updates close model picker command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand CloseModelPickerCommand { get; }
    /// <summary>
    /// Gets or updates select model command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<ModelDescriptor> SelectModelCommand { get; }
    /// <summary>
    /// Gets or updates retry with plugin command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand RetryWithPluginCommand { get; }
    /// <summary>
    /// Gets or updates dismiss missing plugin command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand DismissMissingPluginCommand { get; }
    /// <summary>
    /// Gets or updates approve tool permission command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ApproveToolPermissionCommand { get; }
    /// <summary>
    /// Gets or updates dismiss tool permission command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand DismissToolPermissionCommand { get; }
    /// <summary>
    /// Gets or updates compact context command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand CompactContextCommand { get; }
    /// <summary>
    /// Gets or updates answer inline question command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<string> AnswerInlineQuestionCommand { get; }
    /// <summary>
    /// Gets or updates dismiss inline question command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand DismissInlineQuestionCommand { get; }
    /// <summary>
    /// Gets or updates enable computer use command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand EnableComputerUseCommand { get; }
    /// <summary>
    /// Gets or updates dismiss computer use command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand DismissComputerUseCommand { get; }
    /// <summary>
    /// Gets or updates remove attachment command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<AttachmentItemViewModel> RemoveAttachmentCommand { get; }
    /// <summary>
    /// Gets or updates select container command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<ContainerItemViewModel> SelectContainerCommand { get; }
    /// <summary>
    /// Gets or updates delete container command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<ContainerItemViewModel> DeleteContainerCommand { get; }
    /// <summary>
    /// Gets or updates select lesson command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<LessonItemViewModel> SelectLessonCommand { get; }
    /// <summary>
    /// Gets or updates select quick chats command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand SelectQuickChatsCommand { get; }
    /// <summary>
    /// Gets or updates refresh models command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshModelsCommand { get; }
    /// <summary>
    /// Gets or updates new container command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NewContainerCommand { get; }
    /// <summary>
    /// Gets or updates new lesson command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NewLessonCommand { get; }
    /// <summary>
    /// Gets or updates delete lesson command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<LessonItemViewModel> DeleteLessonCommand { get; }
    /// <summary>
    /// Gets or updates move lesson up command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<LessonItemViewModel> MoveLessonUpCommand { get; }
    /// <summary>
    /// Gets or updates move lesson down command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<LessonItemViewModel> MoveLessonDownCommand { get; }
    /// <summary>
    /// Gets or updates use starter command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<string> UseStarterCommand { get; }
    /// <summary>
    /// Reports whether there are unresolved errors after a send, exposing a floating resolve button.
    /// </summary>
    public bool HasErrorsToResolve { get => _hasErrorsToResolve; private set { if (SetProperty(ref _hasErrorsToResolve, value)) RaisePropertyChanged(nameof(HasErrorsToResolve)); } }
    /// <summary>
    /// Gets or updates resolve errors command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ResolveErrorsCommand { get; }

    /// <summary>
    /// Performs initialize asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Containers.Clear();
        await RefreshModelsAsync(cancellationToken);
        await RefreshCatalogAsync(cancellationToken);
        await RefreshContainersAsync(cancellationToken);
        await UpdateContextUsageAsync(cancellationToken);
    }

    /// <summary>
    /// Performs refresh catalog asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task RefreshCatalogAsync(CancellationToken cancellationToken)
    {
        var selectedAgentName = SelectedAgent?.Name;
        var activePlugins = Plugins.Where(item => item.IsActive).Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activePrompts = Prompts.Where(item => item.IsActive).Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Agents.Clear();
        Plugins.Clear();
        Prompts.Clear();
        foreach (var agent in await _catalog.GetAgentsAsync(cancellationToken)) Agents.Add(new AgentItemViewModel(agent));
        SelectedAgent = Agents.FirstOrDefault(item => item.Name.Equals(selectedAgentName, StringComparison.OrdinalIgnoreCase)) ?? Agents.FirstOrDefault();
        foreach (var plugin in await _catalog.GetPluginsAsync(cancellationToken))
        {
            var item = new PluginItemViewModel(plugin, Mode, _preferences.ShowAgenticInChat) { IsActive = activePlugins.Contains(plugin.Name) };
            Plugins.Add(item);
        }
        RefreshPluginAvailability();
        foreach (var prompt in await _catalog.GetPromptsAsync(cancellationToken))
        {
            var item = new PromptItemViewModel(prompt, Mode, _preferences.ShowAgenticInChat) { IsActive = activePrompts.Contains(prompt.Name) };
            Prompts.Add(item);
        }
        RaisePropertyChanged(nameof(IsAgentPluginActive));
        RaisePropertyChanged(nameof(IsDuoPluginActive));
    }

    /// <summary>
    /// Performs refresh containers asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task RefreshContainersAsync(CancellationToken cancellationToken)
    {
        if (!HasContainers) return;
        var selectedId = SelectedContainer?.Id;
        var selectedLessonId = SelectedLesson?.Id;
        var loaded = await _containers.GetByModeAsync(Mode, cancellationToken);
        CancelLessonLoad();
        _suppressSelectionConversationUpdate = true;
        try
        {
            Containers.Clear();
            foreach (var container in loaded) Containers.Add(new ContainerItemViewModel(container));
            SelectedContainer = selectedId is null ? null : Containers.FirstOrDefault(item => item.Id == selectedId);
            if (IsTeach && SelectedContainer is not null)
            {
                await LoadLessonsAsync(SelectedContainer.Id, cancellationToken);
                SelectedLesson = selectedLessonId is null ? null : Lessons.FirstOrDefault(item => item.Id == selectedLessonId);
            }
        }
        finally
        {
            _suppressSelectionConversationUpdate = false;
        }
        ApplySelectionToConversation(startNewWhenScopeChanges: true);
        RaiseContainerStateChanged();
    }

    /// <summary>
    /// Performs the apply preferences step owned by this component.
    /// </summary>
    public void ApplyPreferences(string? modelName, EffortLevel effort)
    {
        SelectedEffort = effort;
        if (!string.IsNullOrWhiteSpace(modelName))
            SelectedModel = Models.FirstOrDefault(model => model.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase)) ?? SelectedModel;
    }

    /// <summary>
    /// Performs the add attachment step owned by this component.
    /// </summary>
    public void AddAttachment(string path)
    {
        if (Attachments.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        Attachments.Add(new AttachmentItemViewModel(path));
        UpdateAttachmentSummary();
        Status = Attachments.Any(item => item.IsImage)
            ? "Attachments ready. Vision capability will be checked before sending."
            : "Attachments ready. Text will be added to this request.";
    }

    /// <summary>
    /// Performs the add attachments step owned by this component.
    /// </summary>
    public void AddAttachments(IEnumerable<string> paths)
    {
        foreach (var path in paths) AddAttachment(path);
    }

    /// <summary>
    /// Performs load conversation asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task LoadConversationAsync(Guid id, CancellationToken cancellationToken)
    {
        var conversation = await _conversations.GetAsync(id, cancellationToken);
        if (conversation is null || conversation.Mode != Mode) return;
        if (Mode == HavenMode.Chat && conversation.Kind != ConversationKind.Chat) return;
        if (Mode == HavenMode.Teach && conversation.Kind is not (ConversationKind.QuickChat or ConversationKind.LessonChat)) return;

        Stop();
        _conversation = conversation;
        IsTemporary = conversation.IsTemporary;
        Messages.Clear();
        foreach (var message in await _conversations.GetMessagesAsync(id, cancellationToken))
            Messages.Add(new MessageBubbleViewModel(message, ShowConfidence));

        _suppressSelectionConversationUpdate = true;
        try
        {
            SelectedContainer = Containers.FirstOrDefault(item => item.Id == conversation.ContainerId);
            if (IsTeach && conversation.ContainerId is { } subjectId && conversation.LessonId is not null)
            {
                await LoadLessonsAsync(subjectId, cancellationToken);
                SelectedLesson = Lessons.FirstOrDefault(item => item.Id == conversation.LessonId);
            }
            else if (IsTeach)
            {
                SelectedContainer = null;
                SelectedLesson = null;
            }
        }
        finally
        {
            _suppressSelectionConversationUpdate = false;
        }
        _conversation = conversation;

        RaiseMessageStateChanged();
        RaisePropertyChanged(nameof(ConversationId));
        RaisePropertyChanged(nameof(ConversationTitle));
        RaisePropertyChanged(nameof(CurrentScope));
        Status = "Ready";
        await UpdateContextUsageAsync(cancellationToken);
        ConversationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Performs refresh models asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshModelsAsync() => await RefreshModelsAsync(CancellationToken.None);

    /// <summary>
    /// Performs refresh models asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshModelsAsync(CancellationToken cancellationToken)
    {
        var selectedName = SelectedModel?.Name ?? _preferences.DefaultModel;
        Models.Clear();
        try
        {
            foreach (var model in await _ollama.GetModelsAsync(cancellationToken)) Models.Add(model);
            SelectedModel = Models.FirstOrDefault(model => model.Name == selectedName) ?? Models.FirstOrDefault();
            Status = Models.Count == 0 ? "Ollama is running, but no models are installed." : $"{Models.Count} local model{(Models.Count == 1 ? "" : "s")} available";
        }
        catch (Exception ex)
        {
            SelectedModel = string.IsNullOrWhiteSpace(selectedName) || selectedName.Contains(':', StringComparison.Ordinal)
                ? null
                : new ModelDescriptor(selectedName, 0, "Ollama", string.Empty, string.Empty,
                    new HashSet<ToolCapability>(), DateTimeOffset.MinValue);
            Status = $"Ollama unavailable: {ex.Message}";
        }
    }

    /// <summary>
    /// Performs send asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SendAsync()
    {
        if (SelectedModel is null || string.IsNullOrWhiteSpace(Composer)) return;
        if (!await EnsureSelectedModelAvailableAsync()) return;
        var permissionApproved = _approvedToolPermissionOnce;
        _approvedToolPermissionOnce = false;
        var originalPrompt = Composer.Trim();
        var prompt = await ExecuteSlashCommandsAsync(originalPrompt);
        prompt = ActivateInvocations(prompt, out var needsComputerApproval);
        if (needsComputerApproval)
        {
            Composer = originalPrompt;
            IsComputerPermissionPending = true;
            Status = "Confirm Computer Use before this message is sent.";
            return;
        }
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Composer = string.Empty;
            return;
        }
        DetectMissingPlugin(prompt);
        if (!permissionApproved && TryRequestToolPermission(prompt, originalPrompt)) return;
        if (Prompts.Any(item => item.IsActive && item.Name.Equals("Context", StringComparison.OrdinalIgnoreCase)))
        {
            Composer = string.Empty;
            await RegisterContextAsync(prompt);
            return;
        }
        if (_preferences.AutoCompactContext && ContextPercent >= _preferences.CompactAtPercent)
            await CompactContextAsync(true);
        Composer = string.Empty;
        _sendCancellation = new CancellationTokenSource();
        IsSending = true;
        EditStep = 0;
        LinesAdded = 0;
        LinesRemoved = 0;
        Status = "Preparing…";
        MessageBubbleViewModel? streaming = null;
        ChatMessage? completedMessage = null;

        try
        {
            var prepared = await PrepareAttachmentAsync(prompt, _sendCancellation.Token);
            prepared = await AddGroupResourceImagesAsync(prepared, _sendCancellation.Token);
            if (Messages.Count == 0)
                _conversation = NewConversation(_conversation.CreatedAt) with { Id = _conversation.Id, Title = BuildTitle(prompt), IsTemporary = IsTemporary };
            var active = Plugins.Where(x => x.IsActive).Select(x => new ActivePlugin(x.Name, x.IconKey, x.Persists, x.Instructions)).ToArray();
            var activePrompts = Prompts.Where(x => x.IsActive).Select(x => new ActivePrompt(x.Name, x.IconKey, x.Persists, x.Instructions)).ToArray();
            var model = SelectedModel ?? throw new InvalidOperationException("No compatible local model is selected.");
            var selectedAgent = ResolveAgent(prompt);
            var agent = IsAgentPluginActive ? selectedAgent?.Name ?? "Default" : "Default";
            var agentInstructions = IsAgentPluginActive ? selectedAgent?.Instructions ?? string.Empty : string.Empty;
            var registeredContext = await BuildRegisteredContextAsync(prompt, _sendCancellation.Token);
            var containerContext = await BuildContainerContextAsync(_sendCancellation.Token);
            var containerInstructions = BuildContainerInstructions();
            var deltaBuffer = new StringBuilder();
            var flushTimer = Task.Delay(50, _sendCancellation.Token);

            await foreach (var item in _sessions.SendAsync(
                               _conversation, prepared.Prompt, model, SelectedEffort, active, agent, agentInstructions,
                               SelectedDuo, SelectedContainer?.RootPath, containerContext, containerInstructions,
                               prepared.Images, _sendCancellation.Token, activePrompts, registeredContext, _preferences.GenerationOptions,
                               permissionApproved ? PermissionMode.FullAccess : _preferences.FilePermission,
                               permissionApproved ? PermissionMode.FullAccess : _preferences.CommandPermission,
                               permissionApproved ? PermissionMode.FullAccess : _preferences.BrowserPermission))
            {
                switch (item.Kind)
                {
                    case ChatStreamEventKind.AssistantDelta when streaming is not null:
                        deltaBuffer.Append(item.Delta ?? string.Empty);
                        if (flushTimer.IsCompleted)
                        {
                            var text = deltaBuffer.ToString();
                            deltaBuffer.Clear();
                            await Dispatcher.UIThread.InvokeAsync(() => streaming.Append(text));
                            flushTimer = Task.Delay(50, _sendCancellation.Token);
                        }
                        break;
                    default:
                        if (deltaBuffer.Length > 0)
                        {
                            var text = deltaBuffer.ToString();
                            deltaBuffer.Clear();
                            await Dispatcher.UIThread.InvokeAsync(() => streaming!.Append(text));
                        }
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            switch (item.Kind)
                            {
                                case ChatStreamEventKind.UserMessage when item.Message is not null:
                                    Messages.Add(new MessageBubbleViewModel(item.Message, ShowConfidence));
                                    RaiseMessageStateChanged();
                                    break;
                                case ChatStreamEventKind.AssistantStarted:
                                    streaming = MessageBubbleViewModel.Streaming(item.MessageId ?? Guid.NewGuid(), item.Agent ?? "Haven", item.Model ?? model.Name);
                                    Messages.Add(streaming);
                                    RaiseMessageStateChanged();
                                    break;
                                case ChatStreamEventKind.AssistantCompleted when streaming is not null:
                                    completedMessage = item.Message;
                                    FinaliseAssistant(streaming, item.Message);
                                    break;
                                case ChatStreamEventKind.ToolActivity when item.ToolActivity is not null:
                                    var activity = new ToolActivityViewModel(
                                        item.ToolActivity.Title,
                                        item.ToolActivity.Detail,
                                        item.ToolActivity.Succeeded,
                                        $"{item.ToolActivity.Duration.TotalSeconds:0.0}s",
                                        item.ToolActivity.LinesAdded,
                                        item.ToolActivity.LinesRemoved);
                                    streaming?.Activities.Add(activity);
                                    EditStep++;
                                    LinesAdded += item.ToolActivity.LinesAdded;
                                    LinesRemoved += item.ToolActivity.LinesRemoved;
                                    break;
                                case ChatStreamEventKind.PreflightFailed:
                                    var missing = string.Join(", ", item.PreflightResult?.Missing.Select(x => x.Capability) ?? []);
                                    var suggestion = item.PreflightResult?.SuggestedModel?.Name;
                                    Status = suggestion is null
                                        ? $"Model preflight stopped the request. Missing: {missing}."
                                        : $"Model preflight stopped the request. Missing: {missing}. Suggested: {suggestion}.";
                                    break;
                            }
                        });
                        break;
                }
            }

            if (deltaBuffer.Length > 0)
            {
                var text = deltaBuffer.ToString();
                deltaBuffer.Clear();
                await Dispatcher.UIThread.InvokeAsync(() => streaming!.Append(text));
            }
            if (completedMessage is not null && streaming is not null && !IsTemporary)
            {
                var metadata = streaming.ConfidenceScore is null
                    ? null
                    : JsonSerializer.Serialize(new { confidence = streaming.ConfidenceScore });
                await _conversations.AddMessageAsync(completedMessage with { Content = streaming.Content, MetadataJson = metadata }, _sendCancellation.Token);
            }
            if (!Status.StartsWith("Model preflight", StringComparison.Ordinal)) Status = "Ready";
            HasErrorsToResolve = false;
            foreach (var plugin in Plugins.Where(x => x.IsActive && !x.Persists)) plugin.IsActive = false;
            foreach (var activePrompt in Prompts.Where(x => x.IsActive && !x.Persists)) activePrompt.IsActive = false;
            IsComputerPermissionPending = false;
            RaisePropertyChanged(nameof(IsAgentPluginActive));
            RaisePropertyChanged(nameof(IsDuoPluginActive));
            ClearAttachment();
            RaisePropertyChanged(nameof(ConversationTitle));
            if (IsTeach) await RegisterErrorGenomeSignalAsync(prompt, _sendCancellation.Token);
            await UpdateContextUsageAsync(_sendCancellation.Token);
            ConversationChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { Status = "Stopped"; streaming?.MarkStopped(); }
        catch (Exception ex) { Status = $"Request failed: {ex.Message}"; streaming?.MarkFailed(ex.Message); HasErrorsToResolve = true; }
        finally
        {
            IsSending = false;
            _sendCancellation.Dispose();
            _sendCancellation = null;
        }
    }

    /// <summary>
    /// Makes a local model usable immediately before a send. Provider-qualified
    /// names (for example, <c>openai:gpt-4.1</c>) are cloud models and therefore
    /// bypass this local Ollama check entirely.
    /// </summary>
    private async Task<bool> EnsureSelectedModelAvailableAsync()
    {
        var selectedName = SelectedModel?.Name;
        if (string.IsNullOrWhiteSpace(selectedName) || selectedName.Contains(':', StringComparison.Ordinal))
        {
            IsOllamaOfflineWarningVisible = false;
            return true;
        }

        var wake = App.Services?.GetService<OllamaWakeService>();
        if (wake is null) return true;

        try
        {
            using var probe = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            if (await wake.IsAvailableAsync(probe.Token))
            {
                IsOllamaOfflineWarningVisible = false;
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            // A timed-out probe is the same user-facing state as an offline endpoint.
        }

        if (!_preferences.AutoWakeOllama)
        {
            IsOllamaOfflineWarningVisible = true;
            Status = "Ollama is offline. Start Ollama or enable automatic wake in Settings.";
            return false;
        }

        Messages.Add(MessageBubbleViewModel.SystemNotice("Waking Up Model"));
        RaiseMessageStateChanged();
        Status = "Waking up Ollama...";

        try
        {
            using var wakeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(18));
            if (!await wake.EnsureAvailableAsync(wakeTimeout.Token))
            {
                IsOllamaOfflineWarningVisible = true;
                Status = "Ollama could not be started. Open Ollama, then try again.";
                return false;
            }

            await RefreshModelsAsync();
            SelectedModel = Models.FirstOrDefault(model =>
                model.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase)) ?? SelectedModel;
            IsOllamaOfflineWarningVisible = false;
            Status = "Ollama is ready.";
            return true;
        }
        catch (OperationCanceledException)
        {
            IsOllamaOfflineWarningVisible = true;
            Status = "Ollama did not become ready in time. Open Ollama, then try again.";
            return false;
        }
    }

    /// <summary>
    /// Performs prepare attachment asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<PreparedAttachment> PrepareAttachmentAsync(string prompt, CancellationToken cancellationToken)
    {
        if (Attachments.Count == 0) return new(prompt, null);

        var textContext = new System.Text.StringBuilder(prompt);
        var images = new List<string>();
        foreach (var attachment in Attachments.Where(item => File.Exists(item.Path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attachment.IsImage)
            {
                var info = new FileInfo(attachment.Path);
                if (info.Length > 20 * 1024 * 1024) throw new InvalidOperationException($"{attachment.Name} is larger than 20 MB.");
                var bytes = await File.ReadAllBytesAsync(attachment.Path, cancellationToken);
                images.Add(Convert.ToBase64String(bytes));
                continue;
            }

            var text = await File.ReadAllTextAsync(attachment.Path, cancellationToken);
            if (text.Length > 200_000) text = text[..200_000] + "\n[attachment truncated by Haven]";
            textContext.Append("\n\nAttached file: ").Append(attachment.Name).Append("\n```\n").Append(text).Append("\n```");
        }
        return new(textContext.ToString(), images.Count == 0 ? null : images);
    }

    /// <summary>
    /// Performs add group resource images asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<PreparedAttachment> AddGroupResourceImagesAsync(PreparedAttachment prepared, CancellationToken cancellationToken)
    {
        if (Mode != HavenMode.Chat || SelectedContainer is null || _containerResources is null) return prepared;
        var images = prepared.Images?.ToList() ?? [];
        long totalBytes = 0;
        foreach (var resource in (await _containerResources.GetByContainerAsync(SelectedContainer.Id, cancellationToken))
                     .Where(resource => resource.Kind == ContainerResourceKind.Image).Take(4))
        {
            var path = _containerResources.GetStoredPath(resource);
            if (!File.Exists(path) || resource.SizeBytes > 10 * 1024 * 1024 || totalBytes + resource.SizeBytes > 20 * 1024 * 1024) continue;
            images.Add(Convert.ToBase64String(await File.ReadAllBytesAsync(path, cancellationToken)));
            totalBytes += resource.SizeBytes;
        }
        return prepared with { Images = images.Count == 0 ? null : images };
    }

    /// <summary>
    /// Performs the stop step owned by this component.
    /// </summary>
    private void Stop() => _sendCancellation?.Cancel();

    /// <summary>
    /// Performs the new chat step owned by this component.
    /// </summary>
    public void NewChat()
    {
        Stop();
        Messages.Clear();
        IsComputerPermissionPending = false;
        InlineQuestion = null;
        ClearMissingPlugin();
        ClearAttachment();
        _conversation = NewConversation(DateTimeOffset.UtcNow);
        ContextTokens = 0;
        RaiseContextProperties();
        Status = "Ready";
        RaiseMessageStateChanged();
        RaisePropertyChanged(nameof(ConversationId));
        RaisePropertyChanged(nameof(ConversationTitle));
        RaisePropertyChanged(nameof(CurrentScope));
        ConversationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Performs the new conversation step owned by this component.
    /// </summary>
    private Conversation NewConversation(DateTimeOffset now)
    {
        var lesson = IsTeach ? SelectedLesson : null;
        var containerId = IsTeach ? (lesson is null ? null : SelectedContainer?.Id) : SelectedContainer?.Id;
        return new Conversation(
            Guid.NewGuid(), Mode, IsTeach && lesson is null ? ConversationKind.QuickChat : KindFor(Mode), NewChatTitle,
            containerId, lesson?.Id, false, IsTemporary, now, now);
    }

    /// <summary>
    /// Performs the apply selection to conversation step owned by this component.
    /// </summary>
    private void ApplySelectionToConversation(bool startNewWhenScopeChanges)
    {
        if (_suppressSelectionConversationUpdate) return;
        var lesson = IsTeach ? SelectedLesson : null;
        var containerId = IsTeach ? (lesson is null ? null : SelectedContainer?.Id) : SelectedContainer?.Id;
        var lessonId = lesson?.Id;
        var kind = IsTeach && lesson is null ? ConversationKind.QuickChat : KindFor(Mode);
        var changed = _conversation.ContainerId != containerId || _conversation.LessonId != lessonId || _conversation.Kind != kind;
        if (changed && startNewWhenScopeChanges && Messages.Count > 0)
        {
            NewChat();
            return;
        }
        _conversation = _conversation with { ContainerId = containerId, LessonId = lessonId, Kind = kind };
        RaisePropertyChanged(nameof(CurrentScope));
        if (changed) ConversationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Performs the select quick chats step owned by this component.
    /// </summary>
    private void SelectQuickChats()
    {
        if (!IsTeach) return;
        SelectedContainer = null;
        Status = "Quick Chats selected.";
    }

    /// <summary>
    /// Performs the toggle temporary step owned by this component.
    /// </summary>
    private void ToggleTemporary()
    {
        IsTemporary = !IsTemporary;
        ConversationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Performs the toggle plugin step owned by this component.
    /// </summary>
    private void TogglePlugin(PluginItemViewModel? plugin)
    {
        if (plugin is null) return;
        if (!plugin.IsAvailableInMode)
        {
            Status = $"@{plugin.Name} is available in {plugin.AllowedModesLabel}.";
            return;
        }
        RefreshPluginAvailability();
        if (!plugin.IsRuntimeAvailable)
        {
            Status = plugin.AvailabilityReason;
            return;
        }
        if (plugin.Name.Equals("ComputerUse", StringComparison.OrdinalIgnoreCase) && !plugin.IsActive)
        {
            IsComputerPermissionPending = true;
            IsPluginPickerOpen = false;
            return;
        }
        if (plugin.Name.Equals("ComputerUse", StringComparison.OrdinalIgnoreCase)) IsComputerPermissionPending = false;
        if (!plugin.IsActive)
        {
            foreach (var conflict in plugin.Conflicts)
            {
                var activeConflict = Plugins.FirstOrDefault(item => item.Name.Equals(conflict, StringComparison.OrdinalIgnoreCase));
                if (activeConflict is not null) activeConflict.IsActive = false;
                var activePromptConflict = Prompts.FirstOrDefault(item => item.Name.Equals(conflict, StringComparison.OrdinalIgnoreCase));
                if (activePromptConflict is not null) activePromptConflict.IsActive = false;
            }
        }
        plugin.IsActive = !plugin.IsActive;
        if (plugin.Name.Equals("DuoMode", StringComparison.OrdinalIgnoreCase))
            SelectedDuo = plugin.IsActive ? (SelectedDuo == DuoMode.Solo ? DuoMode.Collaborate : SelectedDuo) : DuoMode.Solo;
        RaisePropertyChanged(nameof(IsAgentPluginActive));
        RaisePropertyChanged(nameof(IsDuoPluginActive));
    }

    /// <summary>
    /// Performs the enable computer use step owned by this component.
    /// </summary>
    private void EnableComputerUse()
    {
        var plugin = Plugins.FirstOrDefault(item => item.Name.Equals("ComputerUse", StringComparison.OrdinalIgnoreCase));
        if (plugin is not null) plugin.IsActive = true;
        IsComputerPermissionPending = false;
        IsPluginPickerOpen = false;
        Status = "Computer Use enabled for this pass.";
        if (_retryAfterComputerPermission && !string.IsNullOrWhiteSpace(_retryPrompt))
        {
            _retryAfterComputerPermission = false;
            Composer = _retryPrompt;
            ClearMissingPlugin();
            if (SendCommand.CanExecute(null)) SendCommand.Execute(null);
        }
    }

    /// <summary>
    /// Performs the dismiss computer use step owned by this component.
    /// </summary>
    private void DismissComputerUse()
    {
        var plugin = Plugins.FirstOrDefault(item => item.Name.Equals("ComputerUse", StringComparison.OrdinalIgnoreCase));
        if (plugin is not null) plugin.IsActive = false;
        IsComputerPermissionPending = false;
        IsPluginPickerOpen = false;
        _retryAfterComputerPermission = false;
    }

    /// <summary>
    /// Performs the select plugin step owned by this component.
    /// </summary>
    private void SelectPlugin(PluginItemViewModel? plugin)
    {
        if (plugin is null) return;
        if (!plugin.IsActive) TogglePlugin(plugin);
        var lastAt = Composer.LastIndexOf('@');
        if (lastAt >= 0) Composer = Composer[..lastAt];
        IsPluginPickerOpen = false;
        RaisePropertyChanged(nameof(IsAgentPluginActive));
        RaisePropertyChanged(nameof(IsDuoPluginActive));
    }

    /// <summary>
    /// Performs the open plugin picker step owned by this component.
    /// </summary>
    private void OpenPluginPicker()
    {
        IsPromptPickerOpen = false;
        RefreshPluginAvailability();
        PluginSuggestions.Clear();
        foreach (var plugin in Plugins.Where(item => item.IsVisibleInPicker && item.IsAvailableInMode).Take(12)) PluginSuggestions.Add(plugin);
        IsPluginPickerOpen = true;
    }

    /// <summary>
    /// Performs the open prompt picker step owned by this component.
    /// </summary>
    private void OpenPromptPicker()
    {
        IsPluginPickerOpen = false;
        PromptSuggestions.Clear();
        foreach (var prompt in Prompts.Where(item => item.IsVisibleInPicker && item.IsAvailableInMode).Take(16)) PromptSuggestions.Add(prompt);
        IsPromptPickerOpen = true;
    }

    /// <summary>
    /// Performs the close pickers step owned by this component.
    /// </summary>
    private void ClosePickers()
    {
        IsPluginPickerOpen = false;
        IsPromptPickerOpen = false;
    }

    /// <summary>
    /// Performs the toggle prompt step owned by this component.
    /// </summary>
    private void TogglePrompt(PromptItemViewModel? prompt)
    {
        if (prompt is null) return;
        if (!prompt.IsAvailableInMode)
        {
            Status = $">{prompt.Name} is available in {prompt.AllowedModesLabel}.";
            return;
        }
        prompt.IsActive = !prompt.IsActive;
    }

    /// <summary>
    /// Performs the select prompt step owned by this component.
    /// </summary>
    private void SelectPrompt(PromptItemViewModel? prompt)
    {
        if (prompt is null) return;
        if (!prompt.IsActive) TogglePrompt(prompt);
        var last = Composer.LastIndexOf('>');
        if (last >= 0) Composer = Composer[..last];
        IsPromptPickerOpen = false;
    }

    /// <summary>
    /// Performs the update suggestions step owned by this component.
    /// </summary>
    private void UpdateSuggestions(string text)
    {
        RefreshPluginAvailability();
        var lastToken = text.Split([' ', '\r', '\n', '\t'], StringSplitOptions.None).LastOrDefault() ?? string.Empty;
        PluginSuggestions.Clear();
        PromptSuggestions.Clear();
        if (lastToken.StartsWith('@'))
        {
            var query = lastToken[1..];
            foreach (var plugin in Plugins.Where(item => item.IsVisibleInPicker && item.IsAvailableInMode && item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8))
                PluginSuggestions.Add(plugin);
            IsPluginPickerOpen = PluginSuggestions.Count > 0;
            IsPromptPickerOpen = false;
            return;
        }
        if (lastToken.StartsWith('>'))
        {
            var query = lastToken[1..];
            foreach (var prompt in Prompts.Where(item => item.IsVisibleInPicker && item.IsAvailableInMode && item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8))
                PromptSuggestions.Add(prompt);
            IsPromptPickerOpen = PromptSuggestions.Count > 0;
            IsPluginPickerOpen = false;
            return;
        }
        ClosePickers();
    }

    /// <summary>
    /// Performs the refresh plugin availability step owned by this component.
    /// </summary>
    private void RefreshPluginAvailability()
    {
        foreach (var plugin in Plugins)
        {
            if (!IsRuntimePlugin(plugin.Name))
            {
                plugin.SetRuntimeAvailability(true, string.Empty);
                continue;
            }
            var available = _sessions.CanActivatePlugin(plugin.Name, Mode, SelectedContainer?.RootPath,
                _preferences.FilePermission, _preferences.CommandPermission, _preferences.BrowserPermission);
            plugin.SetRuntimeAvailability(available, available ? string.Empty : RuntimePluginReason(plugin.Name));
            if (!available) plugin.IsActive = false;
        }
    }

    /// <summary>
    /// Reports whether runtime plugin applies to the current state.
    /// </summary>
    private static bool IsRuntimePlugin(string name) => name.Equals("BrowserUse", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("WebSearch", StringComparison.OrdinalIgnoreCase) || name.Equals("ComputerUse", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Automate", StringComparison.OrdinalIgnoreCase) || name.Equals("Macro", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Test", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs runtime plugin reason while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private static string RuntimePluginReason(string name) => name switch
    {
        "BrowserUse" => "@BrowserUse appears only while the native Browse host is attached and interactive browser permission is available.",
        "WebSearch" => "@WebSearch needs the local browser runtime and browser permission.",
        "ComputerUse" => "@ComputerUse is available only on Windows with explicit approval.",
        "Automate" or "Macro" => $"@{name} is available only in Haven Do or Studio.",
        "Test" => "@Test requires a connected local project and command permission.",
        _ => $"@{name} is not available in this context."
    };

    /// <summary>
    /// Performs the resolve agent step owned by this component.
    /// </summary>
    private AgentItemViewModel? ResolveAgent(string prompt)
    {
        if (!IsAgentPluginActive) return null;
        if (!string.Equals(SelectedAgent?.Name, "Auto", StringComparison.OrdinalIgnoreCase)) return SelectedAgent;
        var matched = Agents
            .Where(agent => agent.Name is not "Auto" and not "Default")
            .Select(agent => new
            {
                Agent = agent,
                Score = agent.DetectionRules
                    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Count(rule => prompt.Contains(rule, StringComparison.OrdinalIgnoreCase))
            })
            .OrderByDescending(item => item.Score)
            .FirstOrDefault(item => item.Score > 0)?.Agent;
        matched ??= Agents.FirstOrDefault(agent => agent.Name == "Default");
        AgentNotice = matched?.Name == "Default"
            ? "Auto found no strong specialist match, so Default is handling this message."
            : $"Auto selected {matched?.Name} from the request's matching rules.";
        return matched;
    }

    /// <summary>
    /// Creates container async with the invariants required by its callers.
    /// </summary>
    private async Task CreateContainerAsync()
    {
        if (!HasContainers) return;
        var now = DateTimeOffset.UtcNow;
        var name = Mode switch
        {
            HavenMode.Chat => "Untitled Chat Group",
            HavenMode.Teach => "Untitled Subject",
            HavenMode.Do => "Untitled Task Group",
            _ => "Untitled Project"
        };
        var item = new ContainerDefinition(Guid.NewGuid(), Mode, name, null, string.Empty, string.Empty, now, now);
        var vm = new ContainerItemViewModel(item);
        if (IsTeach)
        {
            var general = await _containers.CreateSubjectAsync(item, CancellationToken.None);
            CancelLessonLoad();
            _suppressSelectionConversationUpdate = true;
            try
            {
                Containers.Add(vm);
                SelectedContainer = vm;
                Lessons.Add(new LessonItemViewModel(general));
                RebuildLessonGroups();
                SelectedLesson = Lessons[0];
            }
            finally
            {
                _suppressSelectionConversationUpdate = false;
            }
            ApplySelectionToConversation(startNewWhenScopeChanges: true);
            RaiseContainerStateChanged();
            Status = "Created subject with a General lesson.";
            return;
        }

        await _containers.UpsertAsync(item, CancellationToken.None);
        Containers.Add(vm);
        SelectedContainer = vm;
        RaiseContainerStateChanged();
        Status = $"Created {name.ToLowerInvariant()}.";
    }

    /// <summary>
    /// Performs delete container asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DeleteContainerAsync(ContainerItemViewModel? item)
    {
        if (item is null) return;
        await _containers.UpsertAsync(item.Definition with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        Containers.Remove(item);
        if (ReferenceEquals(SelectedContainer, item))
            SelectedContainer = null;
        RaiseContainerStateChanged();
        Status = $"Archived \"{item.Name}\". Restore it from Archive when needed.";
    }

    /// <summary>
    /// Creates lesson async with the invariants required by its callers.
    /// </summary>
    private async Task CreateLessonAsync()
    {
        if (!IsTeach || SelectedContainer is null) return;
        var now = DateTimeOffset.UtcNow;
        var nextSort = Lessons.Count == 0 ? 0 : Lessons.Max(lesson => lesson.Definition.SortOrder) + 1;
        var item = new Lesson(Guid.NewGuid(), SelectedContainer.Id, "General", "Untitled Lesson", "{}", nextSort, now, now);
        await _containers.UpsertLessonAsync(item, CancellationToken.None);
        var vm = new LessonItemViewModel(item);
        Lessons.Add(vm);
        RebuildLessonGroups();
        SelectedLesson = vm;
        RaiseTeachStateChanged();
        Status = "Created lesson.";
    }

    /// <summary>
    /// Performs delete lesson asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task DeleteLessonAsync(LessonItemViewModel? item)
    {
        if (!IsTeach || item is null) return;
        await _containers.DeleteLessonAsync(item.Id, CancellationToken.None);
        if (ReferenceEquals(SelectedLesson, item)) SelectedLesson = null;
        Lessons.Remove(item);
        RebuildLessonGroups();
        RaiseTeachStateChanged();
        Status = $"Deleted lesson \"{item.Name}\". Its chats are preserved in Quick Chats.";
    }

    /// <summary>
    /// Performs move lesson asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task MoveLessonAsync(LessonItemViewModel? item, int direction)
    {
        if (!IsTeach || item is null || direction == 0) return;
        var ordered = Lessons.OrderBy(lesson => lesson.Definition.SortOrder).ThenBy(lesson => lesson.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var index = ordered.FindIndex(lesson => lesson.Id == item.Id);
        var otherIndex = index + Math.Sign(direction);
        if (index < 0 || otherIndex < 0 || otherIndex >= ordered.Count) return;
        var other = ordered[otherIndex];
        var itemSort = item.Definition.SortOrder;
        var otherSort = other.Definition.SortOrder;
        if (itemSort == otherSort)
        {
            itemSort = index;
            otherSort = otherIndex;
        }
        var now = DateTimeOffset.UtcNow;
        await _containers.UpsertLessonAsync(item.Definition with { SortOrder = otherSort, UpdatedAt = now }, CancellationToken.None);
        await _containers.UpsertLessonAsync(other.Definition with { SortOrder = itemSort, UpdatedAt = now }, CancellationToken.None);
        var selectedId = SelectedLesson?.Id;
        await LoadLessonsAsync(item.Definition.SubjectId, CancellationToken.None);
        SelectedLesson = Lessons.FirstOrDefault(lesson => lesson.Id == selectedId);
        Status = "Lesson order updated.";
    }

    /// <summary>
    /// Performs the start lesson load step owned by this component.
    /// </summary>
    private void StartLessonLoad(Guid subjectId)
    {
        CancelLessonLoad();
        _lessonLoadCancellation = new CancellationTokenSource();
        _ = LoadLessonsSafelyAsync(subjectId, _lessonLoadCancellation.Token);
    }

    /// <summary>
    /// Performs load lessons safely asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task LoadLessonsSafelyAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        try { await LoadLessonsAsync(subjectId, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { Status = $"Could not load lessons: {ex.Message}"; }
        finally
        {
            if (_lessonLoadCancellation?.Token == cancellationToken)
            {
                _lessonLoadCancellation.Dispose();
                _lessonLoadCancellation = null;
            }
        }
    }

    /// <summary>
    /// Performs load lessons asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task LoadLessonsAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        var loaded = await _containers.GetLessonsAsync(subjectId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (SelectedContainer?.Id != subjectId) return;
        Lessons.Clear();
        foreach (var lesson in loaded) Lessons.Add(new LessonItemViewModel(lesson));
        RebuildLessonGroups();
        RaiseTeachStateChanged();
    }

    /// <summary>
    /// Reports whether cancel lesson load is true for the current state.
    /// </summary>
    private void CancelLessonLoad()
    {
        if (_lessonLoadCancellation is null) return;
        _lessonLoadCancellation.Cancel();
        _lessonLoadCancellation.Dispose();
        _lessonLoadCancellation = null;
    }

    /// <summary>
    /// Performs the rebuild lesson groups step owned by this component.
    /// </summary>
    private void RebuildLessonGroups()
    {
        LessonGroups.Clear();
        foreach (var group in Lessons.GroupBy(item => string.IsNullOrWhiteSpace(item.TopicGroup) ? "General" : item.TopicGroup)
                     .OrderBy(group => group.Min(item => item.Definition.SortOrder)).ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            LessonGroups.Add(new LessonGroupViewModel(group.Key, group.OrderBy(item => item.Definition.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray()));
    }

    /// <summary>
    /// Performs the activate invocations step owned by this component.
    /// </summary>
    private string ActivateInvocations(string prompt, out bool needsComputerApproval)
    {
        needsComputerApproval = false;
        foreach (Match match in Regex.Matches(prompt, @"(?<!\S)@(?<name>[A-Za-z][A-Za-z0-9_-]*)", RegexOptions.CultureInvariant))
        {
            var item = Plugins.FirstOrDefault(plugin => plugin.Name.Equals(match.Groups["name"].Value, StringComparison.OrdinalIgnoreCase));
            if (item is null || !item.IsAvailableInMode) continue;
            if (item.Name.Equals("ComputerUse", StringComparison.OrdinalIgnoreCase) && !item.IsActive)
            {
                needsComputerApproval = true;
                continue;
            }
            if (!item.IsActive) TogglePlugin(item);
        }
        foreach (Match match in Regex.Matches(prompt, @"(?<!\S)>(?<name>[A-Za-z][A-Za-z0-9_-]*)", RegexOptions.CultureInvariant))
        {
            var item = Prompts.FirstOrDefault(candidate => candidate.Name.Equals(match.Groups["name"].Value, StringComparison.OrdinalIgnoreCase));
            if (item is not null && item.IsAvailableInMode) item.IsActive = true;
        }
        prompt = Regex.Replace(prompt, @"(?<!\S)[@>][A-Za-z][A-Za-z0-9_-]*\s*", string.Empty, RegexOptions.CultureInvariant).Trim();
        RaisePropertyChanged(nameof(IsAgentPluginActive));
        RaisePropertyChanged(nameof(IsDuoPluginActive));
        return prompt;
    }

    /// <summary>
    /// Runs execute slash commands async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task<string> ExecuteSlashCommandsAsync(string prompt)
    {
        var matches = Regex.Matches(prompt, @"(?<!\S)/(?<name>[A-Za-z]+)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        foreach (Match match in matches)
        {
            switch (match.Groups["name"].Value.ToLowerInvariant())
            {
                case "branch": await BranchCurrentAsync(); break;
                case "temporary": ToggleTemporary(); break;
                case "compact": await CompactContextAsync(false); break;
                case "archive": await ArchiveCurrentAsync(); break;
                case "new" or "clear": NewChat(); break;
                case "handoff":
                    var handoff = Prompts.FirstOrDefault(item => item.Name.Equals("Handoff", StringComparison.OrdinalIgnoreCase));
                    if (handoff is not null) handoff.IsActive = true;
                    break;
                case "context":
                    var context = Prompts.FirstOrDefault(item => item.Name.Equals("Context", StringComparison.OrdinalIgnoreCase));
                    if (context is not null) context.IsActive = true;
                    break;
                default: continue;
            }
            prompt = prompt.Remove(match.Index, match.Length).Insert(match.Index, new string(' ', match.Length));
        }
        return Regex.Replace(prompt, @"\s{2,}", " ").Trim();
    }

    /// <summary>
    /// Performs branch current asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task BranchCurrentAsync()
    {
        if (Messages.Count == 0)
        {
            NewChat();
            Status = "Started a new branch.";
            return;
        }
        var now = DateTimeOffset.UtcNow;
        if (!_conversation.IsTemporary) await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        var branch = _conversation with
        {
            Id = Guid.NewGuid(),
            Title = $"Branch of {_conversation.Title}",
            IsPinned = false,
            IsArchived = false,
            IsTemporary = false,
            ParentConversationId = _conversation.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _conversations.UpsertConversationAsync(branch, CancellationToken.None);
        if (!_conversation.IsTemporary)
        {
            foreach (var message in await _conversations.GetMessagesAsync(_conversation.Id, CancellationToken.None))
                await _conversations.AddMessageAsync(message with { Id = Guid.NewGuid(), ConversationId = branch.Id }, CancellationToken.None);
            foreach (var entry in await _conversations.GetContextEntriesAsync(_conversation.Id, CancellationToken.None))
                await _conversations.AddContextEntryAsync(entry with { Id = Guid.NewGuid(), ConversationId = branch.Id }, CancellationToken.None);
        }
        _conversation = branch;
        Messages.Clear();
        foreach (var message in await _conversations.GetMessagesAsync(branch.Id, CancellationToken.None))
            Messages.Add(new MessageBubbleViewModel(message, ShowConfidence));
        RaiseMessageStateChanged();
        RaisePropertyChanged(nameof(ConversationId));
        RaisePropertyChanged(nameof(ConversationTitle));
        await UpdateContextUsageAsync(CancellationToken.None);
        Status = "Branched this conversation. New edits are isolated from the original.";
        ConversationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Performs archive current asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task ArchiveCurrentAsync()
    {
        if (Messages.Count == 0) return;
        if (!_conversation.IsTemporary)
            await _conversations.UpsertConversationAsync(_conversation with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        NewChat();
        Status = "Conversation archived. It remains available from the archive.";
    }

    /// <summary>
    /// Performs the use prompt step owned by this component.
    /// </summary>
    public void UsePrompt(string text)
    {
        Composer = text;
    }

    /// <summary>
    /// Performs invoke asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task InvokeAsync(string instruction, string? pluginName = null)
    {
        if (!string.IsNullOrWhiteSpace(pluginName))
        {
            var plugin = Plugins.FirstOrDefault(item => item.Name.Equals(pluginName, StringComparison.OrdinalIgnoreCase));
            if (plugin is not null && !plugin.Name.Equals("ComputerUse", StringComparison.OrdinalIgnoreCase)) plugin.IsActive = true;
        }
        Composer = instruction;
        await SendAsync();
    }

    /// <summary>
    /// Performs register context asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RegisterContextAsync(string content)
    {
        var now = DateTimeOffset.UtcNow;
        if (Messages.Count == 0) _conversation = _conversation with { Title = BuildTitle(content), UpdatedAt = now };
        if (_conversation.IsTemporary)
        {
            _contextSummary = string.IsNullOrWhiteSpace(_contextSummary) ? content : _contextSummary + "\n" + content;
        }
        else
        {
            await _conversations.UpsertConversationAsync(_conversation with { UpdatedAt = now }, CancellationToken.None);
            await _conversations.AddContextEntryAsync(new ConversationContextEntry(Guid.NewGuid(), _conversation.Id, ContextEntryKind.Registered,
                "Registered context", content, string.Empty, now), CancellationToken.None);
        }
        var user = new ChatMessage(Guid.NewGuid(), _conversation.Id, MessageRole.User, content, null, null, null, now, true);
        var acknowledgement = new ChatMessage(Guid.NewGuid(), _conversation.Id, MessageRole.System,
            "Context registered locally for future messages in this conversation.", null, null, null, now.AddMilliseconds(1), true);
        if (!_conversation.IsTemporary)
        {
            await _conversations.AddMessageAsync(user, CancellationToken.None);
            await _conversations.AddMessageAsync(acknowledgement, CancellationToken.None);
        }
        Messages.Add(new MessageBubbleViewModel(user, false));
        Messages.Add(new MessageBubbleViewModel(acknowledgement, false));
        foreach (var item in Prompts.Where(item => item.Name.Equals("Context", StringComparison.OrdinalIgnoreCase))) item.IsActive = false;
        RaiseMessageStateChanged();
        await UpdateContextUsageAsync(CancellationToken.None);
        Status = "Context registered. >Handoff creates a portable handoff instead.";
        ConversationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Performs compact context asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task CompactContextAsync() => CompactContextAsync(false);

    /// <summary>
    /// Performs compact context asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task CompactContextAsync(bool automatic)
    {
        if (SelectedModel is null) return;
        IReadOnlyList<ChatMessage> contextMessages;
        if (_conversation.IsTemporary)
            contextMessages = Messages.Where(item => !item.IsCompacted && item.Role is MessageRole.User or MessageRole.Assistant)
                .Select(item => item.ToMessage(_conversation.Id)).ToArray();
        else
        {
            await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
            contextMessages = await _conversations.GetContextMessagesAsync(_conversation.Id, CancellationToken.None);
        }
        var compactable = contextMessages.Where(message => message.Role is MessageRole.User or MessageRole.Assistant).SkipLast(6).ToArray();
        if (compactable.Length < 4)
        {
            Status = "There is not enough older context to compact yet.";
            return;
        }
        Status = automatic ? "Context is nearly full; compacting older turns…" : "Compacting older context…";
        var transcript = string.Join("\n\n", compactable.Select(message => $"{message.Role}: {message.Content}"));
        if (transcript.Length > 180_000) transcript = transcript[^180_000..];
        string summary;
        try
        {
            summary = await _ollama.CompleteAsync(new OllamaChatRequest(SelectedModel.Name,
                [new OllamaMessage("user", "Summarise this conversation context for a future assistant. Preserve requirements, decisions, named files, unresolved questions, errors, and verified evidence. Do not invent facts.\n\n" + transcript)],
                EffortLevel.Medium, Options: _preferences.GenerationOptions with { Temperature = 0.2 }), CancellationToken.None);
        }
        catch (Exception)
        {
            summary = string.Join("\n", compactable.Select(message => $"- {message.Role}: {Truncate(message.Content, 320)}"));
        }
        var now = DateTimeOffset.UtcNow;
        _contextSummary = string.IsNullOrWhiteSpace(_contextSummary) ? summary : _contextSummary + "\n\n" + summary;
        if (!_conversation.IsTemporary)
        {
            await _conversations.AddContextEntryAsync(new ConversationContextEntry(Guid.NewGuid(), _conversation.Id, ContextEntryKind.CompactSummary,
                automatic ? "Automatic compact summary" : "Manual compact summary", summary, $"Compacted {compactable.Length} messages at {now:O}", now), CancellationToken.None);
            await _conversations.MarkMessagesCompactedAsync(_conversation.Id, compactable.Select(message => message.Id).ToArray(), CancellationToken.None);
            _conversation = _conversation with { CompactedAt = now, UpdatedAt = now };
            await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        }
        var ids = compactable.Select(message => message.Id).ToHashSet();
        foreach (var bubble in Messages.Where(item => ids.Contains(item.Id))) bubble.MarkCompacted();
        await UpdateContextUsageAsync(CancellationToken.None);
        Status = $"Compacted {compactable.Length} older messages into a durable summary.";
    }

    /// <summary>
    /// Re-sends the last user message with an explicit instruction to fix all errors that occurred.
    /// </summary>
    private async Task ResolveErrorsAsync()
    {
        HasErrorsToResolve = false;
        if (SelectedModel is null) return;
        var lastUserMessage = Messages.LastOrDefault(item => item.Role == MessageRole.User);
        if (lastUserMessage is null) return;
        Composer = "Please review and resolve all errors from the previous response. Fix any issues and try again.";
        if (SendCommand.CanExecute(null)) SendCommand.Execute(null);
    }

    /// <summary>
    /// Clears the floating resolve-errors button so the UI hides it.
    /// </summary>
    public void ClearErrorsToResolve() => HasErrorsToResolve = false;

    /// <summary>
    /// Performs update context usage asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task UpdateContextUsageAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ChatMessage> messages;
        IReadOnlyList<ConversationContextEntry> entries = [];
        if (_conversation.IsTemporary)
            messages = Messages.Where(item => !item.IsCompacted).Select(item => item.ToMessage(_conversation.Id)).ToArray();
        else
        {
            messages = await _conversations.GetContextMessagesAsync(_conversation.Id, cancellationToken);
            entries = await _conversations.GetContextEntriesAsync(_conversation.Id, cancellationToken);
        }
        var characters = messages.Sum(message => message.Content.Length) + entries.Sum(entry => entry.Content.Length) +
                         Plugins.Where(item => item.IsActive).Sum(item => item.Instructions.Length) +
                         Prompts.Where(item => item.IsActive).Sum(item => item.Instructions.Length) + _contextSummary.Length +
                         BuildContainerInstructions().Length + (IsTeach && SelectedLesson is null ? 0 : SelectedContainer?.Definition.Context.Length ?? 0);
        ContextTokens = Math.Max(0, (int)Math.Ceiling(characters / 3.7));
        RaiseContextProperties();
    }

    /// <summary>
    /// Performs the raise context properties step owned by this component.
    /// </summary>
    private void RaiseContextProperties()
    {
        RaisePropertyChanged(nameof(ContextLimit));
        RaisePropertyChanged(nameof(ContextPercent));
        RaisePropertyChanged(nameof(ContextRemainingPercent));
        RaisePropertyChanged(nameof(ContextLabel));
        RaisePropertyChanged(nameof(ContextRemainingLabel));
        RaisePropertyChanged(nameof(ContextSweep));
    }

    /// <summary>
    /// Builds registered context async from the currently available inputs.
    /// </summary>
    private async Task<string> BuildRegisteredContextAsync(string prompt, CancellationToken cancellationToken)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_contextSummary)) parts.Add(_contextSummary);
        if (!_conversation.IsTemporary)
        {
            var entries = await _conversations.GetContextEntriesAsync(_conversation.Id, cancellationToken);
            parts.AddRange(entries.TakeLast(30).Select(entry => $"[{entry.Kind}] {entry.Title}: {entry.Content}{(string.IsNullOrWhiteSpace(entry.Evidence) ? string.Empty : "\nEvidence: " + entry.Evidence)}"));
        }
        if (SelectedContainer is not null && Mode is HavenMode.Do or HavenMode.Studio)
        {
            var decisions = await _workspaceState.GetDecisionsAsync(SelectedContainer.Id, cancellationToken);
            if (decisions.Count > 0)
                parts.Add("Decision Memory:\n" + string.Join("\n", decisions.Take(25).Select(item => $"- {item.Title}: {item.Decision}. Reason: {item.Reasoning}. Consequences: {item.Consequences}")));
        }
        if (Mode is HavenMode.Do or HavenMode.Studio && HasWorkspaceRoot && LooksLikeError(prompt))
        {
            var state = await _projectIntelligence.GetStateAsync(SelectedContainer!.RootPath!, cancellationToken);
            parts.Add($"Automatic relevant error context: branch {state.Branch}; uncommitted work: {state.HasUncommittedWork}; last build: {state.LastBuildResult}; recent error: {state.MostRecentError}. Collected at {state.CapturedAt:O}.");
        }
        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// Builds container context async from the currently available inputs.
    /// </summary>
    private async Task<string> BuildContainerContextAsync(CancellationToken cancellationToken)
    {
        if (SelectedContainer is null || IsTeach && SelectedLesson is null) return string.Empty;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(SelectedContainer.Definition.Context)) parts.Add(SelectedContainer.Definition.Context);
        if (Mode == HavenMode.Chat && _containerResources is not null)
        {
            var references = await _containerResources.BuildPromptContextAsync(SelectedContainer.Id, cancellationToken);
            if (!string.IsNullOrWhiteSpace(references)) parts.Add(references);
        }
        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// Builds container instructions from the currently available inputs.
    /// </summary>
    private string BuildContainerInstructions()
    {
        if (SelectedContainer is null || IsTeach && SelectedLesson is null) return string.Empty;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(SelectedContainer.Definition.Instructions)) parts.Add(SelectedContainer.Definition.Instructions);
        if (IsTeach && SelectedLesson is { } lesson)
        {
            parts.Add($"Teaching scope:\nSubject: {SelectedContainer.Name}\nLesson: {lesson.Name}\nTopic group: {lesson.TopicGroup}\nLesson structure: {lesson.Definition.StructureJson}");
        }
        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// Performs the raise container state changed step owned by this component.
    /// </summary>
    private void RaiseContainerStateChanged()
    {
        RaisePropertyChanged(nameof(HasAnyContainers));
        RaisePropertyChanged(nameof(HasSubjects));
        RaisePropertyChanged(nameof(HasNoSubjects));
        RaiseTeachStateChanged();
    }

    /// <summary>
    /// Performs the raise teach state changed step owned by this component.
    /// </summary>
    private void RaiseTeachStateChanged()
    {
        RaisePropertyChanged(nameof(HasSelectedSubject));
        RaisePropertyChanged(nameof(HasLessons));
        RaisePropertyChanged(nameof(HasNoLessons));
        RaisePropertyChanged(nameof(ShowTeachEmptyState));
        RaisePropertyChanged(nameof(TeachEmptyStateTitle));
        RaisePropertyChanged(nameof(TeachEmptyStateMessage));
    }

    /// <summary>
    /// Performs register error genome signal asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RegisterErrorGenomeSignalAsync(string prompt, CancellationToken cancellationToken)
    {
        if (_conversation.IsTemporary || !Regex.IsMatch(prompt, @"\b(wrong|mistake|marked|feedback|error|incorrect|lost marks)\b", RegexOptions.IgnoreCase)) return;
        await _conversations.AddContextEntryAsync(new ConversationContextEntry(Guid.NewGuid(), _conversation.Id, ContextEntryKind.ErrorPattern,
            "Revision Error Genome signal", Truncate(prompt, 1200), "Captured from learner-provided marked work or correction signal.", DateTimeOffset.UtcNow), cancellationToken);
    }

    /// <summary>
    /// Performs the detect missing plugin step owned by this component.
    /// </summary>
    private void DetectMissingPlugin(string prompt)
    {
        var required = RequiredPlugin(prompt);
        if (required is null || Plugins.Any(item => item.Name.Equals(required.Value.Name, StringComparison.OrdinalIgnoreCase) && item.IsActive)) return;
        MissingPluginName = required.Value.Name;
        MissingPluginReason = required.Value.Reason;
        _retryPrompt = prompt;
        RaisePropertyChanged(nameof(HasMissingPluginNotice));
    }

    private static (string Name, string Reason)? RequiredPlugin(string prompt)
    {
        if (Regex.IsMatch(prompt, @"\b(open|launch|close|focus|click|type|press)\b.*\b(notepad|edge|chrome|window|application|app|calculator|explorer|settings)\b", RegexOptions.IgnoreCase))
            return ("ComputerUse", "This request appears to control a Windows application.");
        if (Regex.IsMatch(prompt, @"\b(navigate|browse|website|webpage|click (?:the )?(?:link|button)|fill (?:the )?(?:form|field))\b|https?://", RegexOptions.IgnoreCase))
            return ("BrowserUse", "This request appears to interact with a web page.");
        if (Regex.IsMatch(prompt, @"\b(latest|current news|look up|search the web|find sources|research online)\b", RegexOptions.IgnoreCase))
            return ("WebSearch", "This request needs current web information.");
        if (Regex.IsMatch(prompt, @"\b(schedule|scheduled|automate|automation|every (?:day|hour|week))\b", RegexOptions.IgnoreCase))
            return ("Automate", "This request appears to create a Scheduled Action.");
        if (Regex.IsMatch(prompt, @"\b(run|execute|generate)\b.{0,30}\btests?\b|\btest (?:this|the) (?:app|program|project|code)\b", RegexOptions.IgnoreCase))
            return ("Test", "This request asks Haven to run or generate targeted tests.");
        if (Regex.IsMatch(prompt, @"\bmacro\b", RegexOptions.IgnoreCase))
            return ("Macro", "This request appears to create or invoke a macro.");
        return null;
    }

    /// <summary>
    /// Performs the retry with plugin step owned by this component.
    /// </summary>
    private void RetryWithPlugin()
    {
        var item = Plugins.FirstOrDefault(plugin => plugin.Name.Equals(MissingPluginName, StringComparison.OrdinalIgnoreCase));
        if (item is null || string.IsNullOrWhiteSpace(_retryPrompt)) return;
        if (item.Name.Equals("ComputerUse", StringComparison.OrdinalIgnoreCase) && !item.IsActive)
        {
            _retryAfterComputerPermission = true;
            IsComputerPermissionPending = true;
            return;
        }
        item.IsActive = true;
        Composer = _retryPrompt;
        ClearMissingPlugin();
        if (SendCommand.CanExecute(null)) SendCommand.Execute(null);
    }

    /// <summary>
    /// Performs the clear missing plugin step owned by this component.
    /// </summary>
    private void ClearMissingPlugin()
    {
        MissingPluginName = string.Empty;
        MissingPluginReason = string.Empty;
        _retryPrompt = null;
        RaisePropertyChanged(nameof(HasMissingPluginNotice));
    }

    /// <summary>
    /// Attempts to request tool permission and reports the result without using failure for normal control flow.
    /// </summary>
    private bool TryRequestToolPermission(string prompt, string originalPrompt)
    {
        var requested = new List<string>();
        if (IsWorkspaceToolsReady && _preferences.FilePermission == PermissionMode.Ask &&
            Regex.IsMatch(prompt, @"\b(edit|change|fix|implement|create|write|replace|delete|rename|move|refactor|format)\b", RegexOptions.IgnoreCase))
            requested.Add("edit files inside the selected workspace");
        if (IsWorkspaceToolsReady && _preferences.CommandPermission == PermissionMode.Ask &&
            Regex.IsMatch(prompt, @"\b(run|build|test|execute|install|restore|publish|compile|launch)\b", RegexOptions.IgnoreCase))
            requested.Add("run local commands or tests in the selected workspace");
        var browserActive = Plugins.Any(item => item.IsActive && item.Name is "BrowserUse" or "WebSearch");
        if (browserActive && _preferences.BrowserPermission == PermissionMode.Ask &&
            Regex.IsMatch(prompt, @"\b(search|browse|navigate|open|click|fill|submit|website|webpage|sources?)\b|https?://", RegexOptions.IgnoreCase))
            requested.Add("use Haven's isolated browser for this message");
        if (requested.Count == 0) return false;
        _permissionRetryPrompt = originalPrompt;
        ToolPermissionRequest = "Allow Haven to " + string.Join(", and ", requested) + "? This approval applies only to the retried message.";
        IsToolPermissionPending = true;
        Composer = originalPrompt;
        Status = "Waiting for tool permission.";
        return true;
    }

    /// <summary>
    /// Performs the approve tool permission step owned by this component.
    /// </summary>
    private void ApproveToolPermission()
    {
        if (string.IsNullOrWhiteSpace(_permissionRetryPrompt)) return;
        var prompt = _permissionRetryPrompt;
        _permissionRetryPrompt = null;
        IsToolPermissionPending = false;
        ToolPermissionRequest = string.Empty;
        _approvedToolPermissionOnce = true;
        Composer = prompt;
        if (SendCommand.CanExecute(null)) SendCommand.Execute(null);
    }

    /// <summary>
    /// Performs the dismiss tool permission step owned by this component.
    /// </summary>
    private void DismissToolPermission()
    {
        _permissionRetryPrompt = null;
        IsToolPermissionPending = false;
        ToolPermissionRequest = string.Empty;
        Status = "Tool action was not approved.";
    }

    /// <summary>
    /// Performs the finalise assistant step owned by this component.
    /// </summary>
    private void FinaliseAssistant(MessageBubbleViewModel streaming, ChatMessage? message)
    {
        var content = streaming.Content;
        var match = Regex.Match(content, @"<haven-question>(?<json>[\s\S]*?)</haven-question>", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            try
            {
                using var document = JsonDocument.Parse(match.Groups["json"].Value);
                var question = document.RootElement.GetProperty("question").GetString();
                var options = document.RootElement.GetProperty("options").EnumerateArray().Select(item => item.GetString()).OfType<string>().Take(3).ToArray();
                if (!string.IsNullOrWhiteSpace(question) && options.Length is >= 2 and <= 3)
                    InlineQuestion = new InlineQuestionViewModel(question, options);
                content = content.Remove(match.Index, match.Length).TrimEnd();
                streaming.ReplaceContent(content);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { }
        }
        var prompt = Messages.LastOrDefault(item => item.Role == MessageRole.User)?.Content ?? string.Empty;
        var score = ComputeConfidence(prompt, content, streaming.Activities, _preferences.GenerationOptions.Temperature);
        streaming.MarkComplete(_preferences.ConfidenceMeter ? score : null);
        streaming.SetConfidenceAdvice(_preferences.ConfidenceMeter && score < 75 && _preferences.GenerationOptions.Temperature > 1.0
            ? "Lower the model temperature in Advanced configurations for less creative, more consistent answers."
            : string.Empty);
    }

    /// <summary>
    /// Performs the compute confidence step owned by this component.
    /// </summary>
    private static int? ComputeConfidence(
        string prompt,
        string content,
        IEnumerable<ToolActivityViewModel> activities,
        double temperature)
    {
        // Confidence is useful for claims and work products, but it is visual
        // noise for greetings, thanks and other purely conversational turns.
        if (!NeedsConfidenceIndicator(prompt, content)) return null;

        var score = 82;
        foreach (var activity in activities) score += activity.Succeeded ? 5 : -12;
        if (Regex.IsMatch(content, @"https?://|\[[^\]]+\]\([^)]+\)")) score += 10;
        if (Regex.IsMatch(content, @"\b(i'?m not sure|uncertain|might|possibly|cannot verify|unverified)\b", RegexOptions.IgnoreCase)) score -= 14;
        if (Regex.IsMatch(content, @"\b(verified|test(?:s)? passed|exit code 0|primary source)\b", RegexOptions.IgnoreCase)) score += 8;
        if (content.Length < 40) score -= 6;
        // Ordinary temperatures should not suppress confidence. Only unusually
        // creative sampling contributes a bounded penalty.
        if (temperature > 1.0) score -= (int)Math.Round(Math.Min(18, (temperature - 1.0) * 18));
        return Math.Clamp(score, 5, 98);
    }

    private static bool NeedsConfidenceIndicator(string prompt, string content)
    {
        var combined = prompt + "\n" + content;
        if (Regex.IsMatch(prompt.Trim(), @"^(hi|hello|hey|thanks|thank you|ok|okay|good (morning|afternoon|evening)|how are you)[!.? ]*$", RegexOptions.IgnoreCase))
            return false;
        return Regex.IsMatch(combined,
            @"\b(why|how|what|when|where|who|which|fact|source|research|code|bug|error|build|test|file|project|work|calculate|medical|medicine|health|legal|law|finance|money|safety|risk|verify|latest|current)\b|https?://|```",
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Performs the answer inline question step owned by this component.
    /// </summary>
    private void AnswerInlineQuestion(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return;
        Composer = answer;
        InlineQuestion = null;
    }

    /// <summary>
    /// Performs the looks like error step owned by this component.
    /// </summary>
    private static bool LooksLikeError(string prompt) => Regex.IsMatch(prompt, @"\b(error|exception|failed|failure|crash|stack trace|cannot|could not|CS\d{4})\b", RegexOptions.IgnoreCase);
    /// <summary>
    /// Performs the truncate step owned by this component.
    /// </summary>
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";

    /// <summary>
    /// Performs the clear attachment step owned by this component.
    /// </summary>
    private void ClearAttachment()
    {
        Attachments.Clear();
        UpdateAttachmentSummary();
    }

    /// <summary>
    /// Performs the remove attachment step owned by this component.
    /// </summary>
    private void RemoveAttachment(AttachmentItemViewModel? item)
    {
        if (item is null) return;
        Attachments.Remove(item);
        UpdateAttachmentSummary();
    }

    /// <summary>
    /// Performs the update attachment summary step owned by this component.
    /// </summary>
    private void UpdateAttachmentSummary()
    {
        AttachmentSummary = Attachments.Count switch
        {
            0 => string.Empty,
            1 => Attachments[0].Name,
            _ => $"{Attachments.Count} attachments"
        };
        RaisePropertyChanged(nameof(HasAttachment));
    }

    /// <summary>
    /// Performs the raise message state changed step owned by this component.
    /// </summary>
    private void RaiseMessageStateChanged()
    {
        RaisePropertyChanged(nameof(HasMessages));
        RaisePropertyChanged(nameof(IsEmpty));
        RaisePropertyChanged(nameof(ShowTemporaryHeaderAction));
        RaisePropertyChanged(nameof(ShowContextHeaderWidget));
    }

    /// <summary>Temporary-chat is a setup action and therefore appears only before the first turn.</summary>
    public bool ShowTemporaryHeaderAction => !HasMessages;

    /// <summary>Context usage becomes meaningful after the conversation contains at least one turn.</summary>
    public bool ShowContextHeaderWidget => HasMessages;

    /// <summary>
    /// Builds title from the currently available inputs.
    /// </summary>
    private static string BuildTitle(string prompt)
    {
        var firstLine = prompt.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "New chat";
        return firstLine.Length <= 58 ? firstLine : firstLine[..55] + "…";
    }

    /// <summary>
    /// Performs the kind for step owned by this component.
    /// </summary>
    private static ConversationKind KindFor(HavenMode mode) => mode switch
    {
        HavenMode.Teach => ConversationKind.LessonChat,
        HavenMode.Do => ConversationKind.Task,
        HavenMode.Studio => ConversationKind.StudioChat,
        _ => ConversationKind.Chat
    };

    /// <summary>
    /// Represents prepared attachment and keeps its related state and behavior together.
    /// </summary>
    private sealed record PreparedAttachment(string Prompt, IReadOnlyList<string>? Images);
}

/// <summary>
/// Represents message bubble view model and keeps its related state and behavior together.
/// </summary>
public sealed class MessageBubbleViewModel : ObservableObject
{
    /// <summary>
    /// Stores content locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _content;
    /// <summary>
    /// Stores state locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _state;
    /// <summary>
    /// Stores confidence score locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int? _confidenceScore;
    /// <summary>Explains when an unusually creative temperature materially reduced the estimate.</summary>
    private string _confidenceAdvice = string.Empty;
    /// <summary>
    /// Stores is compacted locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isCompacted;

    public MessageBubbleViewModel(ChatMessage message, bool showConfidence = true)
    {
        Id = message.Id;
        Role = message.Role;
        _content = message.Content;
        AgentName = message.AgentName;
        ModelName = message.ModelName;
        CreatedAt = message.CreatedAt;
        _state = string.Empty;
        _isCompacted = message.IsCompacted;
        Activities.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(HasActivities));
        if (showConfidence && message.Metadata.TryGetValue("confidence", out var confidence) && confidence.TryGetInt32(out var score))
            _confidenceScore = Math.Clamp(score, 0, 100);
    }

    private MessageBubbleViewModel(Guid id, MessageRole role, string content, string state, string? agentName, string? modelName)
    {
        Id = id; Role = role; _content = content; _state = state; AgentName = agentName; ModelName = modelName; CreatedAt = DateTimeOffset.Now;
        Activities.CollectionChanged += (_, _) => RaisePropertyChanged(nameof(HasActivities));
    }

    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id { get; }
    /// <summary>
    /// Gets or updates role, the bindable or domain state represented by this property.
    /// </summary>
    public MessageRole Role { get; }
    /// <summary>
    /// Gets or updates agent name, the bindable or domain state represented by this property.
    /// </summary>
    public string? AgentName { get; }
    /// <summary>
    /// Gets or updates model name, the bindable or domain state represented by this property.
    /// </summary>
    public string? ModelName { get; }
    /// <summary>
    /// Creates d at with the invariants required by its callers.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }
    /// <summary>
    /// Gets or updates role label, the bindable or domain state represented by this property.
    /// </summary>
    public string RoleLabel => Role switch { MessageRole.User => "YOU", MessageRole.Assistant => "HAVEN", MessageRole.Tool => "TOOL", _ => "SYSTEM" };
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public string DisplayName => Role switch { MessageRole.User => "You", MessageRole.Assistant => AgentName ?? "Haven", MessageRole.Tool => "Activity", _ => "Haven" };
    /// <summary>
    /// Gets or updates avatar label, the bindable or domain state represented by this property.
    /// </summary>
    public string AvatarLabel => Role switch { MessageRole.User => "Y", MessageRole.Assistant => "H", MessageRole.Tool => "A", _ => "H" };
    /// <summary>
    /// Gets or updates time label, the bindable or domain state represented by this property.
    /// </summary>
    public string TimeLabel => CreatedAt.LocalDateTime.ToString("t");
    /// <summary>
    /// Gets or updates model label, the bindable or domain state represented by this property.
    /// </summary>
    public string ModelLabel => Role == MessageRole.Assistant && !string.IsNullOrWhiteSpace(ModelName) ? ModelName : string.Empty;
    /// <summary>
    /// Reports whether model label applies to the current state.
    /// </summary>
    public bool HasModelLabel => !string.IsNullOrWhiteSpace(ModelLabel);
    /// <summary>
    /// Gets or updates rendered content, the bindable or domain state represented by this property.
    /// </summary>
    public string RenderedContent => Content;
    /// <summary>
    /// Reports whether state applies to the current state.
    /// </summary>
    public bool HasState => !string.IsNullOrWhiteSpace(State);
    /// <summary>
    /// Gets or updates activities, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ToolActivityViewModel> Activities { get; } = [];
    /// <summary>
    /// Reports whether activities applies to the current state.
    /// </summary>
    public bool HasActivities => Activities.Count > 0;
    /// <summary>
    /// Gets or updates confidence score, the bindable or domain state represented by this property.
    /// </summary>
    public int? ConfidenceScore => _confidenceScore;
    /// <summary>
    /// Reports whether confidence applies to the current state.
    /// </summary>
    public bool HasConfidence => _confidenceScore is not null;
    /// <summary>
    /// Gets or updates confidence label, the bindable or domain state represented by this property.
    /// </summary>
    public string ConfidenceLabel => _confidenceScore is null ? string.Empty : $"Confidence {_confidenceScore}%";
    /// <summary>
    /// Gets or updates confidence width, the bindable or domain state represented by this property.
    /// </summary>
    public double ConfidenceWidth => (_confidenceScore ?? 0) * 1.6;
    public string ConfidenceAdvice => _confidenceAdvice;
    public bool HasConfidenceAdvice => !string.IsNullOrWhiteSpace(_confidenceAdvice);
    /// <summary>
    /// Reports whether compacted applies to the current state.
    /// </summary>
    public bool IsCompacted => _isCompacted;
    /// <summary>
    /// Gets or updates content opacity, the bindable or domain state represented by this property.
    /// </summary>
    public double ContentOpacity => IsCompacted ? 0.68 : 1;
    /// <summary>
    /// Gets or updates alignment, the bindable or domain state represented by this property.
    /// </summary>
    public HorizontalAlignment Alignment => Role == MessageRole.User ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
    /// <summary>
    /// Gets or updates max bubble width, the bindable or domain state represented by this property.
    /// </summary>
    public double MaxBubbleWidth => Role == MessageRole.User ? 720 : 10000;
    /// <summary>
    /// Gives user turns an accent-tinted speech bubble while assistant and system
    /// turns continue to use the neutral conversational surface.
    /// </summary>
    public IBrush BubbleBackground => Role == MessageRole.User
        ? new SolidColorBrush(Color.FromArgb(42, 124, 92, 255))
        : new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
    /// <summary>
    /// Uses a tighter lower-right corner for sent messages so their silhouette reads
    /// as a speech bubble without introducing decorative tails that steal space.
    /// </summary>
    public CornerRadius BubbleCornerRadius => Role == MessageRole.User
        ? new CornerRadius(16, 5, 16, 16)
        : new CornerRadius(5, 16, 16, 16);
    public string Content
    {
        get => _content;
        private set
        {
            if (SetProperty(ref _content, value)) RaisePropertyChanged(nameof(RenderedContent));
        }
    }
    public string State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value)) RaisePropertyChanged(nameof(HasState));
        }
    }

    /// <summary>
    /// Performs the streaming step owned by this component.
    /// </summary>
    public static MessageBubbleViewModel Streaming(Guid id, string agent, string model) => new(id, MessageRole.Assistant, string.Empty, "Streaming…", agent, model);
    /// <summary>
    /// Performs the system notice step owned by this component.
    /// </summary>
    public static MessageBubbleViewModel SystemNotice(string content) => new(Guid.NewGuid(), MessageRole.System, content, string.Empty, "Haven", null);
    /// <summary>
    /// Performs the append step owned by this component.
    /// </summary>
    public void Append(string text) => Content += text;
    /// <summary>
    /// Performs the replace content step owned by this component.
    /// </summary>
    public void ReplaceContent(string text) => Content = text;
    /// <summary>
    /// Performs the mark complete step owned by this component.
    /// </summary>
    public void MarkComplete(int? confidence)
    {
        State = string.Empty;
        _confidenceScore = confidence;
        RaisePropertyChanged(nameof(ConfidenceScore));
        RaisePropertyChanged(nameof(HasConfidence));
        RaisePropertyChanged(nameof(ConfidenceLabel));
        RaisePropertyChanged(nameof(ConfidenceWidth));
    }
    public void SetConfidenceAdvice(string advice)
    {
        _confidenceAdvice = advice;
        RaisePropertyChanged(nameof(ConfidenceAdvice));
        RaisePropertyChanged(nameof(HasConfidenceAdvice));
    }
    /// <summary>
    /// Performs the mark compacted step owned by this component.
    /// </summary>
    public void MarkCompacted()
    {
        _isCompacted = true;
        RaisePropertyChanged(nameof(IsCompacted));
        RaisePropertyChanged(nameof(ContentOpacity));
    }
    /// <summary>
    /// Performs the to message step owned by this component.
    /// </summary>
    public ChatMessage ToMessage(Guid conversationId) => new(Id, conversationId, Role, Content, AgentName, ModelName,
        _confidenceScore is null ? null : JsonSerializer.Serialize(new { confidence = _confidenceScore }), CreatedAt, IsCompacted);
    /// <summary>
    /// Performs the mark complete step owned by this component.
    /// </summary>
    public void MarkComplete() => State = string.Empty;
    /// <summary>
    /// Performs the mark stopped step owned by this component.
    /// </summary>
    public void MarkStopped() => State = "Stopped";
    /// <summary>
    /// Performs the mark failed step owned by this component.
    /// </summary>
    public void MarkFailed(string error) => State = $"Failed · {error}";

}

/// <summary>
/// Represents agent item view model and keeps its related state and behavior together.
/// </summary>
public sealed class AgentItemViewModel(AgentDefinition definition)
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id => definition.Id;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => definition.Name;
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description => definition.Description;
    /// <summary>
    /// Gets or updates icon key, the bindable or domain state represented by this property.
    /// </summary>
    public string IconKey => definition.IconKey;
    /// <summary>
    /// Gets or updates instructions, the bindable or domain state represented by this property.
    /// </summary>
    public string Instructions => definition.Instructions;
    /// <summary>
    /// Gets or updates detection rules, the bindable or domain state represented by this property.
    /// </summary>
    public string DetectionRules => definition.DetectionRules;
    /// <summary>
    /// Performs the to string step owned by this component.
    /// </summary>
    public override string ToString() => Name;
}

/// <summary>
/// Represents plugin item view model and keeps its related state and behavior together.
/// </summary>
public sealed class PluginItemViewModel(PluginDefinition definition, HavenMode mode, bool showAgenticInChat) : ObservableObject
{
    /// <summary>
    /// Stores is active locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isActive;
    /// <summary>
    /// Stores is runtime available locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isRuntimeAvailable = true;
    /// <summary>
    /// Stores availability reason locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _availabilityReason = string.Empty;
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id => definition.Id;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => definition.Name;
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description => definition.Description;
    /// <summary>
    /// Gets or updates icon key, the bindable or domain state represented by this property.
    /// </summary>
    public string IconKey => definition.IconKey;
    /// <summary>
    /// Gets or updates instructions, the bindable or domain state represented by this property.
    /// </summary>
    public string Instructions => definition.Instructions;
    /// <summary>
    /// Gets or updates persists, the bindable or domain state represented by this property.
    /// </summary>
    public bool Persists => definition.Persists;
    /// <summary>
    /// Reports whether agentic applies to the current state.
    /// </summary>
    public bool IsAgentic => definition.IsAgentic;
    /// <summary>
    /// Reports whether available in mode applies to the current state.
    /// </summary>
    public bool IsAvailableInMode => CatalogVisibility.IsAllowed(definition.AllowedModesJson, mode);
    /// <summary>
    /// Gets or updates allowed modes label, the bindable or domain state represented by this property.
    /// </summary>
    public string AllowedModesLabel => CatalogVisibility.Label(definition.AllowedModesJson);
    /// <summary>
    /// Reports whether runtime available applies to the current state.
    /// </summary>
    public bool IsRuntimeAvailable => _isRuntimeAvailable;
    /// <summary>
    /// Gets or updates availability reason, the bindable or domain state represented by this property.
    /// </summary>
    public string AvailabilityReason => _availabilityReason;
    /// <summary>
    /// Reports whether visible in picker applies to the current state.
    /// </summary>
    public bool IsVisibleInPicker => IsAvailableInMode && IsRuntimeAvailable && (!definition.IsAgentic || mode is not (HavenMode.Chat or HavenMode.Teach) || showAgenticInChat);
    public IReadOnlyList<string> Conflicts
    {
        get
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(definition.ConflictsJson) ?? []; }
            catch (System.Text.Json.JsonException) { return []; }
        }
    }
    /// <summary>
    /// Reports whether active applies to the current state.
    /// </summary>
    public bool IsActive { get => _isActive; set { if (SetProperty(ref _isActive, value)) RaisePropertyChanged(nameof(DisplayName)); } }
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public string DisplayName => $"@{Name}";
    /// <summary>
    /// Performs the set runtime availability step owned by this component.
    /// </summary>
    public void SetRuntimeAvailability(bool available, string reason)
    {
        if (_isRuntimeAvailable == available && string.Equals(_availabilityReason, reason, StringComparison.Ordinal)) return;
        _isRuntimeAvailable = available;
        _availabilityReason = reason;
        RaisePropertyChanged(nameof(IsRuntimeAvailable));
        RaisePropertyChanged(nameof(AvailabilityReason));
        RaisePropertyChanged(nameof(IsVisibleInPicker));
    }
}

/// <summary>
/// Represents prompt item view model and keeps its related state and behavior together.
/// </summary>
public sealed class PromptItemViewModel(PromptDefinition definition, HavenMode mode, bool showAgenticInChat) : ObservableObject
{
    /// <summary>
    /// Stores is active locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isActive;
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id => definition.Id;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => definition.Name;
    /// <summary>
    /// Gets or updates description, the bindable or domain state represented by this property.
    /// </summary>
    public string Description => definition.Description;
    /// <summary>
    /// Gets or updates icon key, the bindable or domain state represented by this property.
    /// </summary>
    public string IconKey => definition.IconKey;
    /// <summary>
    /// Gets or updates instructions, the bindable or domain state represented by this property.
    /// </summary>
    public string Instructions => definition.Instructions;
    /// <summary>
    /// Gets or updates persists, the bindable or domain state represented by this property.
    /// </summary>
    public bool Persists => definition.Persists;
    /// <summary>
    /// Reports whether agentic applies to the current state.
    /// </summary>
    public bool IsAgentic => definition.IsAgentic;
    /// <summary>
    /// Reports whether available in mode applies to the current state.
    /// </summary>
    public bool IsAvailableInMode => CatalogVisibility.IsAllowed(definition.AllowedModesJson, mode);
    /// <summary>
    /// Gets or updates allowed modes label, the bindable or domain state represented by this property.
    /// </summary>
    public string AllowedModesLabel => CatalogVisibility.Label(definition.AllowedModesJson);
    /// <summary>
    /// Reports whether visible in picker applies to the current state.
    /// </summary>
    public bool IsVisibleInPicker => IsAvailableInMode && (!definition.IsAgentic || mode is not (HavenMode.Chat or HavenMode.Teach) || showAgenticInChat);
    /// <summary>
    /// Reports whether active applies to the current state.
    /// </summary>
    public bool IsActive { get => _isActive; set { if (SetProperty(ref _isActive, value)) RaisePropertyChanged(nameof(DisplayName)); } }
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public string DisplayName => $">{Name}";
}

/// <summary>
/// Represents catalog visibility and keeps its related state and behavior together.
/// </summary>
internal static class CatalogVisibility
{
    /// <summary>
    /// Reports whether allowed applies to the current state.
    /// </summary>
    public static bool IsAllowed(string json, HavenMode mode)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json) ?? [];
            return values.Length == 0 || values.Any(value => value.Equals(mode.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException) { return true; }
    }

    /// <summary>
    /// Performs the label step owned by this component.
    /// </summary>
    public static string Label(string json)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json) ?? [];
            return values.Length == 0 ? "all modes" : string.Join(" or ", values);
        }
        catch (JsonException) { return "all modes"; }
    }
}

/// <summary>
/// Represents container item view model and keeps its related state and behavior together.
/// </summary>
public sealed class ContainerItemViewModel(ContainerDefinition definition)
{
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public ContainerDefinition Definition => definition;
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id => definition.Id;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => definition.Name;
    /// <summary>
    /// Gets or updates root path, the bindable or domain state represented by this property.
    /// </summary>
    public string? RootPath => definition.RootPath;
    /// <summary>
    /// Performs the to string step owned by this component.
    /// </summary>
    public override string ToString() => Name;
}

/// <summary>
/// Represents lesson item view model and keeps its related state and behavior together.
/// </summary>
public sealed class LessonItemViewModel(Lesson lesson)
{
    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public Lesson Definition => lesson;
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id => lesson.Id;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => lesson.Name;
    /// <summary>
    /// Gets or updates topic group, the bindable or domain state represented by this property.
    /// </summary>
    public string TopicGroup => lesson.TopicGroup;
    /// <summary>
    /// Performs the to string step owned by this component.
    /// </summary>
    public override string ToString() => Name;
}

/// <summary>
/// Represents lesson group view model and keeps its related state and behavior together.
/// </summary>
public sealed class LessonGroupViewModel(string name, IReadOnlyList<LessonItemViewModel> lessons)
{
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => name;
    /// <summary>
    /// Gets or updates lessons, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<LessonItemViewModel> Lessons => lessons;
    /// <summary>
    /// Gets or updates count, the bindable or domain state represented by this property.
    /// </summary>
    public int Count => lessons.Count;
}

/// <summary>
/// Represents tool activity view model and keeps its related state and behavior together.
/// </summary>
public sealed record ToolActivityViewModel(string Title, string Detail, bool Succeeded, string Duration, int LinesAdded = 0, int LinesRemoved = 0)
{
    /// <summary>
    /// Gets or updates change label, the bindable or domain state represented by this property.
    /// </summary>
    public string ChangeLabel => LinesAdded == 0 && LinesRemoved == 0 ? string.Empty : $"+{LinesAdded}/-{LinesRemoved}";
    /// <summary>
    /// Reports whether changes applies to the current state.
    /// </summary>
    public bool HasChanges => LinesAdded != 0 || LinesRemoved != 0;
}

/// <summary>
/// Represents inline question view model and keeps its related state and behavior together.
/// </summary>
public sealed record InlineQuestionViewModel(string Question, IReadOnlyList<string> Options);

/// <summary>
/// Represents attachment item view model and keeps its related state and behavior together.
/// </summary>
public sealed class AttachmentItemViewModel(string path)
{
    /// <summary>
    /// Gets or updates path, the bindable or domain state represented by this property.
    /// </summary>
    public string Path => path;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => System.IO.Path.GetFileName(path);
    /// <summary>
    /// Reports whether image applies to the current state.
    /// </summary>
    public bool IsImage => new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif" }.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Gets or updates kind label, the bindable or domain state represented by this property.
    /// </summary>
    public string KindLabel => IsImage ? "Image" : "File";
}
