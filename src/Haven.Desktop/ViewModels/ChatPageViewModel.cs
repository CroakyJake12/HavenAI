using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Layout;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class ChatPageViewModel : ObservableObject
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".gif" };
    private readonly IConversationRepository _conversations;
    private readonly IContainerRepository _containers;
    private readonly ICatalogRepository _catalog;
    private readonly IOllamaClient _ollama;
    private readonly ChatSessionService _sessions;
    private readonly UserPreferencesService _preferences;
    private readonly CapabilityPreflightService _preflight;
    private readonly IWorkspaceStateRepository _workspaceState;
    private readonly IProjectIntelligenceService _projectIntelligence;
    private readonly IContainerResourceRepository? _containerResources;
    private CancellationTokenSource? _sendCancellation;
    private CancellationTokenSource? _lessonLoadCancellation;
    private Conversation _conversation;
    private string _composer = string.Empty;
    private string _status = "Connecting to Ollama…";
    private ModelDescriptor? _selectedModel;
    private AgentItemViewModel? _selectedAgent;
    private EffortLevel _selectedEffort = EffortLevel.Medium;
    private DuoMode _selectedDuo = DuoMode.Solo;
    private bool _isSending;
    private bool _isTemporary;
    private bool _isPluginPickerOpen;
    private bool _isPromptPickerOpen;
    private bool _isModelPickerOpen;
    private bool _isComputerPermissionPending;
    private string _attachmentSummary = string.Empty;
    private ContainerItemViewModel? _selectedContainer;
    private LessonItemViewModel? _selectedLesson;
    private string _agentNotice = "The selected agent will handle the next message.";
    private string _modelSearch = string.Empty;
    private string _missingPluginName = string.Empty;
    private string _missingPluginReason = string.Empty;
    private bool _isToolPermissionPending;
    private string _toolPermissionRequest = string.Empty;
    private string? _permissionRetryPrompt;
    private bool _approvedToolPermissionOnce;
    private string? _retryPrompt;
    private bool _retryAfterComputerPermission;
    private InlineQuestionViewModel? _inlineQuestion;
    private int _contextTokens;
    private string _contextSummary = string.Empty;
    private int _editStep;
    private int _linesAdded;
    private int _linesRemoved;
    private bool _suppressSelectionConversationUpdate;

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
    }

    public event EventHandler? ConversationChanged;

    public HavenMode Mode { get; }
    public string ModeTitle => Mode switch { HavenMode.Chat => "Chat", HavenMode.Teach => "Teach", HavenMode.Do => "Do", HavenMode.Studio => "Studio", _ => "Haven" };
    public string ModeSubtitle => Mode switch
    {
        HavenMode.Chat => "Private conversation with your local models",
        HavenMode.Teach => "Structured lessons, explanations and knowledge checks",
        HavenMode.Do => "Task completion with visible approvals and an audit trail",
        HavenMode.Studio => "Inspect, edit, run, test and repair local projects",
        _ => string.Empty
    };
    public string EmptyTitle => Mode switch
    {
        HavenMode.Chat => "What’s on your mind?",
        HavenMode.Teach => "What should we learn?",
        HavenMode.Do => "What should Haven do?",
        HavenMode.Studio => "What are we building?",
        _ => "How can Haven help?"
    };
    public string StarterOne => Mode switch
    {
        HavenMode.Studio => "Explain this project and identify the riskiest areas.",
        HavenMode.Do => "Research and compare the best options.",
        HavenMode.Teach => "Teach me a topic step by step.",
        _ => "Explain this clearly."
    };
    public string StarterTwo => Mode switch
    {
        HavenMode.Studio => ">Plan Create a precise implementation plan.",
        HavenMode.Do => "Proofread the attached document.",
        HavenMode.Teach => "Create a retrieval quiz for this lesson.",
        _ => "Help me think through a decision."
    };
    public string StarterThree => Mode switch
    {
        HavenMode.Studio => ">Debug Diagnose the error in my latest build.",
        HavenMode.Do => "Organise this workspace safely.",
        HavenMode.Teach => "Explain this using examples, then test me.",
        _ => "Summarise the important points."
    };
    public string StarterFour => Mode switch
    {
        HavenMode.Studio => "@Agent @WebSearch >Report Research a topic thoroughly.",
        HavenMode.Do => "Create a careful step-by-step action plan.",
        HavenMode.Teach => "Build a structured learning plan.",
        _ => "Compare a few possible approaches."
    };
    public string NewChatTitle => Mode switch { HavenMode.Teach => "Quick chat", HavenMode.Do => "New task", HavenMode.Studio => "New studio chat", _ => "New chat" };
    public string ContainerLabel => Mode switch { HavenMode.Chat => "Chat group", HavenMode.Teach => "Subject", HavenMode.Do => "Task group", _ => "Project" };
    public string NewContainerLabel => "+ " + ContainerLabel;
    public bool IsTeach => Mode == HavenMode.Teach;
    public bool IsDo => Mode == HavenMode.Do;
    public bool IsStudio => Mode == HavenMode.Studio;
    public bool HasContainers => Mode is HavenMode.Chat or HavenMode.Teach or HavenMode.Do or HavenMode.Studio;
    public bool HasAnyContainers => Containers.Count > 0;
    public bool HasSubjects => IsTeach && Containers.Count > 0;
    public bool HasNoSubjects => IsTeach && Containers.Count == 0;
    public bool HasSelectedSubject => IsTeach && SelectedContainer is not null;
    public bool HasLessons => IsTeach && Lessons.Count > 0;
    public bool HasNoLessons => IsTeach && SelectedContainer is not null && Lessons.Count == 0;
    public bool ShowTeachEmptyState => IsTeach && (SelectedContainer is null || Lessons.Count == 0);
    public string TeachEmptyStateTitle => Containers.Count == 0 ? "Create your first subject" : SelectedContainer is null ? "Choose a subject" : "No lessons yet";
    public string TeachEmptyStateMessage => Containers.Count == 0
        ? "Subjects organise structured lessons. Quick Chats always remain available outside a subject."
        : SelectedContainer is null
            ? "Open a subject to see its lessons, or continue with a Quick Chat."
            : "Add a lesson to this subject, or use Quick Chats for an unstructured question.";
    public bool SupportsDuo => Mode is HavenMode.Do or HavenMode.Studio;
    public bool HasWorkspaceRoot => HasContainers && !string.IsNullOrWhiteSpace(SelectedContainer?.RootPath);
    public bool IsWorkspaceToolsReady => Mode is HavenMode.Do or HavenMode.Studio && HasWorkspaceRoot;
    public bool IsAgentPluginActive => Plugins.Any(x => x.Name == "Agent" && x.IsActive);
    public bool IsDuoPluginActive => Plugins.Any(x => x.Name == "DuoMode" && x.IsActive);
    public bool HasMessages => Messages.Count > 0;
    public bool IsEmpty => !HasMessages;
    public bool IsNotSending => !IsSending;
    public Guid ConversationId => _conversation.Id;
    public string ConversationTitle => _conversation.Title;
    public ConversationScope? CurrentScope => Mode is HavenMode.Chat or HavenMode.Teach ? ConversationScope.From(_conversation) : null;

    public ObservableCollection<MessageBubbleViewModel> Messages { get; } = [];
    public ObservableCollection<ModelDescriptor> Models { get; } = [];
    public ObservableCollection<AgentItemViewModel> Agents { get; } = [];
    public ObservableCollection<PluginItemViewModel> Plugins { get; } = [];
    public ObservableCollection<PluginItemViewModel> PluginSuggestions { get; } = [];
    public ObservableCollection<PromptItemViewModel> Prompts { get; } = [];
    public ObservableCollection<PromptItemViewModel> PromptSuggestions { get; } = [];
    public ObservableCollection<ContainerItemViewModel> Containers { get; } = [];
    public ObservableCollection<LessonItemViewModel> Lessons { get; } = [];
    public ObservableCollection<LessonGroupViewModel> LessonGroups { get; } = [];
    public ObservableCollection<AttachmentItemViewModel> Attachments { get; } = [];
    public IReadOnlyList<EffortLevel> EffortLevels { get; } = Enum.GetValues<EffortLevel>();
    public IReadOnlyList<DuoMode> DuoModes { get; } = [DuoMode.PingPong, DuoMode.Collaborate, DuoMode.Supervise];
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

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string AttachmentSummary { get => _attachmentSummary; private set => SetProperty(ref _attachmentSummary, value); }
    public bool HasAttachment => !string.IsNullOrWhiteSpace(AttachmentSummary);
    public bool IsPluginPickerOpen { get => _isPluginPickerOpen; private set { if (SetProperty(ref _isPluginPickerOpen, value)) RaisePropertyChanged(nameof(IsPickerOverlayVisible)); } }
    public bool IsPromptPickerOpen { get => _isPromptPickerOpen; private set { if (SetProperty(ref _isPromptPickerOpen, value)) RaisePropertyChanged(nameof(IsPickerOverlayVisible)); } }
    public bool IsPickerOverlayVisible => IsPluginPickerOpen || IsPromptPickerOpen;
    public bool IsModelPickerOpen { get => _isModelPickerOpen; set => SetProperty(ref _isModelPickerOpen, value); }
    public string ModelSearch { get => _modelSearch; set { if (SetProperty(ref _modelSearch, value)) RaisePropertyChanged(nameof(FilteredModels)); } }
    public bool IsComputerPermissionPending { get => _isComputerPermissionPending; private set => SetProperty(ref _isComputerPermissionPending, value); }
    public string MissingPluginName { get => _missingPluginName; private set => SetProperty(ref _missingPluginName, value); }
    public string MissingPluginReason { get => _missingPluginReason; private set => SetProperty(ref _missingPluginReason, value); }
    public bool HasMissingPluginNotice => !string.IsNullOrWhiteSpace(MissingPluginName);
    public bool IsToolPermissionPending { get => _isToolPermissionPending; private set => SetProperty(ref _isToolPermissionPending, value); }
    public string ToolPermissionRequest { get => _toolPermissionRequest; private set => SetProperty(ref _toolPermissionRequest, value); }
    public InlineQuestionViewModel? InlineQuestion { get => _inlineQuestion; private set { if (SetProperty(ref _inlineQuestion, value)) RaisePropertyChanged(nameof(HasInlineQuestion)); } }
    public bool HasInlineQuestion => InlineQuestion is not null;
    public int ContextTokens { get => _contextTokens; private set => SetProperty(ref _contextTokens, value); }
    public int ContextLimit => _preferences.GenerationOptions.ContextLimit;
    public int ContextPercent => Math.Clamp((int)Math.Round(ContextTokens * 100d / Math.Max(1, ContextLimit)), 0, 100);
    public string ContextLabel => $"{ContextTokens:N0} / {ContextLimit:N0} tokens · {ContextPercent}%";
    public double ContextSweep => ContextPercent * 3.6;
    public bool ShowConfidence => _preferences.ConfidenceMeter;
    public bool IsEditProgressVisible => IsSending && IsWorkspaceToolsReady;
    public string EditProgressLabel => $"Step {Math.Max(1, EditStep)} · +{LinesAdded}/-{LinesRemoved} lines";
    public int EditStep { get => _editStep; private set { if (SetProperty(ref _editStep, value)) RaisePropertyChanged(nameof(EditProgressLabel)); } }
    public int LinesAdded { get => _linesAdded; private set { if (SetProperty(ref _linesAdded, value)) RaisePropertyChanged(nameof(EditProgressLabel)); } }
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
            _conversation = _conversation with { IsTemporary = value };
        }
    }

    public string TemporaryLabel => IsTemporary ? "Temporary · history off" : "Saved locally";
    public AsyncRelayCommand SendCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand NewChatCommand { get; }
    public RelayCommand ToggleTemporaryCommand { get; }
    public RelayCommand<PluginItemViewModel> TogglePluginCommand { get; }
    public RelayCommand<PluginItemViewModel> SelectPluginCommand { get; }
    public RelayCommand OpenPluginPickerCommand { get; }
    public RelayCommand<PromptItemViewModel> TogglePromptCommand { get; }
    public RelayCommand<PromptItemViewModel> SelectPromptCommand { get; }
    public RelayCommand OpenPromptPickerCommand { get; }
    public RelayCommand ClosePickersCommand { get; }
    public RelayCommand OpenModelPickerCommand { get; }
    public RelayCommand CloseModelPickerCommand { get; }
    public RelayCommand<ModelDescriptor> SelectModelCommand { get; }
    public RelayCommand RetryWithPluginCommand { get; }
    public RelayCommand DismissMissingPluginCommand { get; }
    public RelayCommand ApproveToolPermissionCommand { get; }
    public RelayCommand DismissToolPermissionCommand { get; }
    public AsyncRelayCommand CompactContextCommand { get; }
    public RelayCommand<string> AnswerInlineQuestionCommand { get; }
    public RelayCommand DismissInlineQuestionCommand { get; }
    public RelayCommand EnableComputerUseCommand { get; }
    public RelayCommand DismissComputerUseCommand { get; }
    public RelayCommand<AttachmentItemViewModel> RemoveAttachmentCommand { get; }
    public RelayCommand<ContainerItemViewModel> SelectContainerCommand { get; }
    public AsyncRelayCommand<ContainerItemViewModel> DeleteContainerCommand { get; }
    public RelayCommand<LessonItemViewModel> SelectLessonCommand { get; }
    public RelayCommand SelectQuickChatsCommand { get; }
    public AsyncRelayCommand RefreshModelsCommand { get; }
    public AsyncRelayCommand NewContainerCommand { get; }
    public AsyncRelayCommand NewLessonCommand { get; }
    public AsyncRelayCommand<LessonItemViewModel> DeleteLessonCommand { get; }
    public AsyncRelayCommand<LessonItemViewModel> MoveLessonUpCommand { get; }
    public AsyncRelayCommand<LessonItemViewModel> MoveLessonDownCommand { get; }
    public RelayCommand<string> UseStarterCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Containers.Clear();
        await RefreshModelsAsync(cancellationToken);
        await RefreshCatalogAsync(cancellationToken);
        await RefreshContainersAsync(cancellationToken);
        await UpdateContextUsageAsync(cancellationToken);
    }

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

    public void ApplyPreferences(string? modelName, EffortLevel effort)
    {
        SelectedEffort = effort;
        if (!string.IsNullOrWhiteSpace(modelName))
            SelectedModel = Models.FirstOrDefault(model => model.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase)) ?? SelectedModel;
    }

    public void AddAttachment(string path)
    {
        if (Attachments.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        Attachments.Add(new AttachmentItemViewModel(path));
        UpdateAttachmentSummary();
        Status = Attachments.Any(item => item.IsImage)
            ? "Attachments ready. Vision capability will be checked before sending."
            : "Attachments ready. Text will be added to this request.";
    }

    public void AddAttachments(IEnumerable<string> paths)
    {
        foreach (var path in paths) AddAttachment(path);
    }

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

    private async Task RefreshModelsAsync() => await RefreshModelsAsync(CancellationToken.None);

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
            SelectedModel = null;
            Status = $"Ollama unavailable: {ex.Message}";
        }
    }

    private async Task SendAsync()
    {
        if (SelectedModel is null || string.IsNullOrWhiteSpace(Composer)) return;
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
        Status = $"{(IsAgentPluginActive ? SelectedAgent?.Name : "Default") ?? "Default"} is working…";
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
            var check = _preflight.Evaluate(SelectedModel, active, prepared.Images is { Count: > 0 }, Models);
            if (!check.IsCompatible && _preferences.AutoSwitchCompatibleModels && check.SuggestedModel is not null)
            {
                var previous = SelectedModel.Name;
                SelectedModel = Models.FirstOrDefault(model => model.Name.Equals(check.SuggestedModel.Name, StringComparison.OrdinalIgnoreCase)) ?? check.SuggestedModel;
                Messages.Add(MessageBubbleViewModel.SystemNotice($"Switched from {previous} to {SelectedModel.Name} because the active tools require {string.Join(", ", check.Missing.Select(item => item.Capability))}."));
                RaiseMessageStateChanged();
            }
            var model = SelectedModel ?? throw new InvalidOperationException("No compatible local model is selected.");
            var selectedAgent = ResolveAgent(prompt);
            var agent = IsAgentPluginActive ? selectedAgent?.Name ?? "Default" : "Default";
            var agentInstructions = IsAgentPluginActive ? selectedAgent?.Instructions ?? string.Empty : string.Empty;
            var registeredContext = await BuildRegisteredContextAsync(prompt, _sendCancellation.Token);
            var containerContext = await BuildContainerContextAsync(_sendCancellation.Token);
            var containerInstructions = BuildContainerInstructions();
            await foreach (var item in _sessions.SendAsync(
                               _conversation, prepared.Prompt, model, SelectedEffort, active, agent, agentInstructions,
                               SelectedDuo, SelectedContainer?.RootPath, containerContext, containerInstructions,
                               prepared.Images, _sendCancellation.Token, activePrompts, registeredContext, _preferences.GenerationOptions,
                               permissionApproved ? PermissionMode.FullAccess : _preferences.FilePermission,
                               permissionApproved ? PermissionMode.FullAccess : _preferences.CommandPermission,
                               permissionApproved ? PermissionMode.FullAccess : _preferences.BrowserPermission))
            {
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
                        case ChatStreamEventKind.AssistantDelta when streaming is not null:
                            streaming.Append(item.Delta ?? string.Empty);
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
            }
            if (completedMessage is not null && streaming is not null && !IsTemporary)
            {
                var metadata = JsonSerializer.Serialize(new { confidence = streaming.ConfidenceScore });
                await _conversations.AddMessageAsync(completedMessage with { Content = streaming.Content, MetadataJson = metadata }, _sendCancellation.Token);
            }
            if (!Status.StartsWith("Model preflight", StringComparison.Ordinal)) Status = "Ready";
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
        catch (Exception ex) { Status = $"Request failed: {ex.Message}"; streaming?.MarkFailed(ex.Message); }
        finally
        {
            IsSending = false;
            _sendCancellation.Dispose();
            _sendCancellation = null;
        }
    }

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

    private void Stop() => _sendCancellation?.Cancel();

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

    private Conversation NewConversation(DateTimeOffset now)
    {
        var lesson = IsTeach ? SelectedLesson : null;
        var containerId = IsTeach ? (lesson is null ? null : SelectedContainer?.Id) : SelectedContainer?.Id;
        return new Conversation(
            Guid.NewGuid(), Mode, IsTeach && lesson is null ? ConversationKind.QuickChat : KindFor(Mode), NewChatTitle,
            containerId, lesson?.Id, false, IsTemporary, now, now);
    }

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

    private void SelectQuickChats()
    {
        if (!IsTeach) return;
        SelectedContainer = null;
        Status = "Quick Chats selected.";
    }

    private void ToggleTemporary()
    {
        IsTemporary = !IsTemporary;
        ConversationChanged?.Invoke(this, EventArgs.Empty);
    }

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

    private void DismissComputerUse()
    {
        var plugin = Plugins.FirstOrDefault(item => item.Name.Equals("ComputerUse", StringComparison.OrdinalIgnoreCase));
        if (plugin is not null) plugin.IsActive = false;
        IsComputerPermissionPending = false;
        IsPluginPickerOpen = false;
        _retryAfterComputerPermission = false;
    }

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

    private void OpenPluginPicker()
    {
        IsPromptPickerOpen = false;
        RefreshPluginAvailability();
        PluginSuggestions.Clear();
        foreach (var plugin in Plugins.Where(item => item.IsVisibleInPicker && item.IsAvailableInMode).Take(12)) PluginSuggestions.Add(plugin);
        IsPluginPickerOpen = true;
    }

    private void OpenPromptPicker()
    {
        IsPluginPickerOpen = false;
        PromptSuggestions.Clear();
        foreach (var prompt in Prompts.Where(item => item.IsVisibleInPicker && item.IsAvailableInMode).Take(16)) PromptSuggestions.Add(prompt);
        IsPromptPickerOpen = true;
    }

    private void ClosePickers()
    {
        IsPluginPickerOpen = false;
        IsPromptPickerOpen = false;
    }

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

    private void SelectPrompt(PromptItemViewModel? prompt)
    {
        if (prompt is null) return;
        if (!prompt.IsActive) TogglePrompt(prompt);
        var last = Composer.LastIndexOf('>');
        if (last >= 0) Composer = Composer[..last];
        IsPromptPickerOpen = false;
    }

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

    private static bool IsRuntimePlugin(string name) => name.Equals("BrowserUse", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("WebSearch", StringComparison.OrdinalIgnoreCase) || name.Equals("ComputerUse", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Automate", StringComparison.OrdinalIgnoreCase) || name.Equals("Macro", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Test", StringComparison.OrdinalIgnoreCase);

    private static string RuntimePluginReason(string name) => name switch
    {
        "BrowserUse" => "@BrowserUse appears only while the native Browse host is attached and interactive browser permission is available.",
        "WebSearch" => "@WebSearch needs the local browser runtime and browser permission.",
        "ComputerUse" => "@ComputerUse is available only on Windows with explicit approval.",
        "Automate" or "Macro" => $"@{name} is available only in Haven Do or Studio.",
        "Test" => "@Test requires a connected local project and command permission.",
        _ => $"@{name} is not available in this context."
    };

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

    private void StartLessonLoad(Guid subjectId)
    {
        CancelLessonLoad();
        _lessonLoadCancellation = new CancellationTokenSource();
        _ = LoadLessonsSafelyAsync(subjectId, _lessonLoadCancellation.Token);
    }

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

    private void CancelLessonLoad()
    {
        if (_lessonLoadCancellation is null) return;
        _lessonLoadCancellation.Cancel();
        _lessonLoadCancellation.Dispose();
        _lessonLoadCancellation = null;
    }

    private void RebuildLessonGroups()
    {
        LessonGroups.Clear();
        foreach (var group in Lessons.GroupBy(item => string.IsNullOrWhiteSpace(item.TopicGroup) ? "General" : item.TopicGroup)
                     .OrderBy(group => group.Min(item => item.Definition.SortOrder)).ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            LessonGroups.Add(new LessonGroupViewModel(group.Key, group.OrderBy(item => item.Definition.SortOrder).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray()));
    }

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

    public async Task ArchiveCurrentAsync()
    {
        if (Messages.Count == 0) return;
        if (!_conversation.IsTemporary)
            await _conversations.UpsertConversationAsync(_conversation with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        NewChat();
        Status = "Conversation archived. It remains available from the archive.";
    }

    public void UsePrompt(string text)
    {
        Composer = text;
    }

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

    public Task CompactContextAsync() => CompactContextAsync(false);

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

    private void RaiseContextProperties()
    {
        RaisePropertyChanged(nameof(ContextLimit));
        RaisePropertyChanged(nameof(ContextPercent));
        RaisePropertyChanged(nameof(ContextLabel));
        RaisePropertyChanged(nameof(ContextSweep));
    }

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

    private void RaiseContainerStateChanged()
    {
        RaisePropertyChanged(nameof(HasAnyContainers));
        RaisePropertyChanged(nameof(HasSubjects));
        RaisePropertyChanged(nameof(HasNoSubjects));
        RaiseTeachStateChanged();
    }

    private void RaiseTeachStateChanged()
    {
        RaisePropertyChanged(nameof(HasSelectedSubject));
        RaisePropertyChanged(nameof(HasLessons));
        RaisePropertyChanged(nameof(HasNoLessons));
        RaisePropertyChanged(nameof(ShowTeachEmptyState));
        RaisePropertyChanged(nameof(TeachEmptyStateTitle));
        RaisePropertyChanged(nameof(TeachEmptyStateMessage));
    }

    private async Task RegisterErrorGenomeSignalAsync(string prompt, CancellationToken cancellationToken)
    {
        if (_conversation.IsTemporary || !Regex.IsMatch(prompt, @"\b(wrong|mistake|marked|feedback|error|incorrect|lost marks)\b", RegexOptions.IgnoreCase)) return;
        await _conversations.AddContextEntryAsync(new ConversationContextEntry(Guid.NewGuid(), _conversation.Id, ContextEntryKind.ErrorPattern,
            "Revision Error Genome signal", Truncate(prompt, 1200), "Captured from learner-provided marked work or correction signal.", DateTimeOffset.UtcNow), cancellationToken);
    }

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

    private void ClearMissingPlugin()
    {
        MissingPluginName = string.Empty;
        MissingPluginReason = string.Empty;
        _retryPrompt = null;
        RaisePropertyChanged(nameof(HasMissingPluginNotice));
    }

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

    private void DismissToolPermission()
    {
        _permissionRetryPrompt = null;
        IsToolPermissionPending = false;
        ToolPermissionRequest = string.Empty;
        Status = "Tool action was not approved.";
    }

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
        var score = ComputeConfidence(content, streaming.Activities);
        streaming.MarkComplete(_preferences.ConfidenceMeter ? score : null);
    }

    private static int ComputeConfidence(string content, IEnumerable<ToolActivityViewModel> activities)
    {
        var score = 62;
        foreach (var activity in activities) score += activity.Succeeded ? 5 : -12;
        if (Regex.IsMatch(content, @"https?://|\[[^\]]+\]\([^)]+\)")) score += 10;
        if (Regex.IsMatch(content, @"\b(i'?m not sure|uncertain|might|possibly|cannot verify|unverified)\b", RegexOptions.IgnoreCase)) score -= 14;
        if (Regex.IsMatch(content, @"\b(verified|test(?:s)? passed|exit code 0|primary source)\b", RegexOptions.IgnoreCase)) score += 8;
        if (content.Length < 20) score -= 5;
        return Math.Clamp(score, 5, 98);
    }

    private void AnswerInlineQuestion(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return;
        Composer = answer;
        InlineQuestion = null;
    }

    private static bool LooksLikeError(string prompt) => Regex.IsMatch(prompt, @"\b(error|exception|failed|failure|crash|stack trace|cannot|could not|CS\d{4})\b", RegexOptions.IgnoreCase);
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";

    private void ClearAttachment()
    {
        Attachments.Clear();
        UpdateAttachmentSummary();
    }

    private void RemoveAttachment(AttachmentItemViewModel? item)
    {
        if (item is null) return;
        Attachments.Remove(item);
        UpdateAttachmentSummary();
    }

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

    private void RaiseMessageStateChanged()
    {
        RaisePropertyChanged(nameof(HasMessages));
        RaisePropertyChanged(nameof(IsEmpty));
    }

    private static string BuildTitle(string prompt)
    {
        var firstLine = prompt.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "New chat";
        return firstLine.Length <= 58 ? firstLine : firstLine[..55] + "…";
    }

    private static ConversationKind KindFor(HavenMode mode) => mode switch
    {
        HavenMode.Teach => ConversationKind.LessonChat,
        HavenMode.Do => ConversationKind.Task,
        HavenMode.Studio => ConversationKind.StudioChat,
        _ => ConversationKind.Chat
    };

    private sealed record PreparedAttachment(string Prompt, IReadOnlyList<string>? Images);
}

public sealed class MessageBubbleViewModel : ObservableObject
{
    private string _content;
    private string _state;
    private int? _confidenceScore;
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

    public Guid Id { get; }
    public MessageRole Role { get; }
    public string? AgentName { get; }
    public string? ModelName { get; }
    public DateTimeOffset CreatedAt { get; }
    public string RoleLabel => Role switch { MessageRole.User => "YOU", MessageRole.Assistant => "HAVEN", MessageRole.Tool => "TOOL", _ => "SYSTEM" };
    public string DisplayName => Role switch { MessageRole.User => "You", MessageRole.Assistant => AgentName ?? "Haven", MessageRole.Tool => "Activity", _ => "Haven" };
    public string AvatarLabel => Role switch { MessageRole.User => "Y", MessageRole.Assistant => "H", MessageRole.Tool => "A", _ => "H" };
    public string TimeLabel => CreatedAt.LocalDateTime.ToString("t");
    public string ModelLabel => Role == MessageRole.Assistant && !string.IsNullOrWhiteSpace(ModelName) ? ModelName : string.Empty;
    public bool HasModelLabel => !string.IsNullOrWhiteSpace(ModelLabel);
    public string RenderedContent => Content;
    public bool HasState => !string.IsNullOrWhiteSpace(State);
    public ObservableCollection<ToolActivityViewModel> Activities { get; } = [];
    public bool HasActivities => Activities.Count > 0;
    public int? ConfidenceScore => _confidenceScore;
    public bool HasConfidence => _confidenceScore is not null;
    public string ConfidenceLabel => _confidenceScore is null ? string.Empty : $"Confidence {_confidenceScore}%";
    public double ConfidenceWidth => (_confidenceScore ?? 0) * 1.6;
    public bool IsCompacted => _isCompacted;
    public double ContentOpacity => IsCompacted ? 0.68 : 1;
    public HorizontalAlignment Alignment => Role == MessageRole.User ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
    public double MaxBubbleWidth => Role == MessageRole.User ? 720 : 10000;
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

    public static MessageBubbleViewModel Streaming(Guid id, string agent, string model) => new(id, MessageRole.Assistant, string.Empty, "Streaming…", agent, model);
    public static MessageBubbleViewModel SystemNotice(string content) => new(Guid.NewGuid(), MessageRole.System, content, string.Empty, "Haven", null);
    public void Append(string text) => Content += text;
    public void ReplaceContent(string text) => Content = text;
    public void MarkComplete(int? confidence)
    {
        State = string.Empty;
        _confidenceScore = confidence;
        RaisePropertyChanged(nameof(ConfidenceScore));
        RaisePropertyChanged(nameof(HasConfidence));
        RaisePropertyChanged(nameof(ConfidenceLabel));
        RaisePropertyChanged(nameof(ConfidenceWidth));
    }
    public void MarkCompacted()
    {
        _isCompacted = true;
        RaisePropertyChanged(nameof(IsCompacted));
        RaisePropertyChanged(nameof(ContentOpacity));
    }
    public ChatMessage ToMessage(Guid conversationId) => new(Id, conversationId, Role, Content, AgentName, ModelName,
        _confidenceScore is null ? null : JsonSerializer.Serialize(new { confidence = _confidenceScore }), CreatedAt, IsCompacted);
    public void MarkComplete() => State = string.Empty;
    public void MarkStopped() => State = "Stopped";
    public void MarkFailed(string error) => State = $"Failed · {error}";

}

public sealed class AgentItemViewModel(AgentDefinition definition)
{
    public Guid Id => definition.Id;
    public string Name => definition.Name;
    public string Description => definition.Description;
    public string IconKey => definition.IconKey;
    public string Instructions => definition.Instructions;
    public string DetectionRules => definition.DetectionRules;
    public override string ToString() => Name;
}

public sealed class PluginItemViewModel(PluginDefinition definition, HavenMode mode, bool showAgenticInChat) : ObservableObject
{
    private bool _isActive;
    private bool _isRuntimeAvailable = true;
    private string _availabilityReason = string.Empty;
    public Guid Id => definition.Id;
    public string Name => definition.Name;
    public string Description => definition.Description;
    public string IconKey => definition.IconKey;
    public string Instructions => definition.Instructions;
    public bool Persists => definition.Persists;
    public bool IsAgentic => definition.IsAgentic;
    public bool IsAvailableInMode => CatalogVisibility.IsAllowed(definition.AllowedModesJson, mode);
    public string AllowedModesLabel => CatalogVisibility.Label(definition.AllowedModesJson);
    public bool IsRuntimeAvailable => _isRuntimeAvailable;
    public string AvailabilityReason => _availabilityReason;
    public bool IsVisibleInPicker => IsAvailableInMode && IsRuntimeAvailable && (!definition.IsAgentic || mode is not (HavenMode.Chat or HavenMode.Teach) || showAgenticInChat);
    public IReadOnlyList<string> Conflicts
    {
        get
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(definition.ConflictsJson) ?? []; }
            catch (System.Text.Json.JsonException) { return []; }
        }
    }
    public bool IsActive { get => _isActive; set { if (SetProperty(ref _isActive, value)) RaisePropertyChanged(nameof(DisplayName)); } }
    public string DisplayName => $"@{Name}";
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

public sealed class PromptItemViewModel(PromptDefinition definition, HavenMode mode, bool showAgenticInChat) : ObservableObject
{
    private bool _isActive;
    public Guid Id => definition.Id;
    public string Name => definition.Name;
    public string Description => definition.Description;
    public string IconKey => definition.IconKey;
    public string Instructions => definition.Instructions;
    public bool Persists => definition.Persists;
    public bool IsAgentic => definition.IsAgentic;
    public bool IsAvailableInMode => CatalogVisibility.IsAllowed(definition.AllowedModesJson, mode);
    public string AllowedModesLabel => CatalogVisibility.Label(definition.AllowedModesJson);
    public bool IsVisibleInPicker => IsAvailableInMode && (!definition.IsAgentic || mode is not (HavenMode.Chat or HavenMode.Teach) || showAgenticInChat);
    public bool IsActive { get => _isActive; set { if (SetProperty(ref _isActive, value)) RaisePropertyChanged(nameof(DisplayName)); } }
    public string DisplayName => $">{Name}";
}

internal static class CatalogVisibility
{
    public static bool IsAllowed(string json, HavenMode mode)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json) ?? [];
            return values.Length == 0 || values.Any(value => value.Equals(mode.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException) { return true; }
    }

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

public sealed class ContainerItemViewModel(ContainerDefinition definition)
{
    public ContainerDefinition Definition => definition;
    public Guid Id => definition.Id;
    public string Name => definition.Name;
    public string? RootPath => definition.RootPath;
    public override string ToString() => Name;
}

public sealed class LessonItemViewModel(Lesson lesson)
{
    public Lesson Definition => lesson;
    public Guid Id => lesson.Id;
    public string Name => lesson.Name;
    public string TopicGroup => lesson.TopicGroup;
    public override string ToString() => Name;
}

public sealed class LessonGroupViewModel(string name, IReadOnlyList<LessonItemViewModel> lessons)
{
    public string Name => name;
    public IReadOnlyList<LessonItemViewModel> Lessons => lessons;
    public int Count => lessons.Count;
}

public sealed record ToolActivityViewModel(string Title, string Detail, bool Succeeded, string Duration, int LinesAdded = 0, int LinesRemoved = 0)
{
    public string ChangeLabel => LinesAdded == 0 && LinesRemoved == 0 ? string.Empty : $"+{LinesAdded}/-{LinesRemoved}";
    public bool HasChanges => LinesAdded != 0 || LinesRemoved != 0;
}

public sealed record InlineQuestionViewModel(string Question, IReadOnlyList<string> Options);

public sealed class AttachmentItemViewModel(string path)
{
    public string Path => path;
    public string Name => System.IO.Path.GetFileName(path);
    public bool IsImage => new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif" }.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    public string KindLabel => IsImage ? "Image" : "File";
}
