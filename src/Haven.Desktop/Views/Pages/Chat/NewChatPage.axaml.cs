using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.HavenUI.Components.Buttons;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// New Haven's mockup-defined conversation surface. It owns its domain state
/// directly and talks to application services through constructor injection;
/// no Classic view, ViewModel, binding, or service locator participates.
/// </summary>
public sealed partial class NewChatPage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly IConversationRepository _conversations;
    private readonly IOllamaClient _ollama;
    private readonly ChatSessionService _sessions;
    private readonly IConversationVersioningService _versioning;
    private readonly UserPreferencesService _preferences;
    private readonly GenerativeUiEventRouter _genUiRouter;
    private readonly GenUiInstanceStore _genUiInstances;
    private readonly CalculatorTemplateRuntime _calculatorTemplate;
    private readonly StructuredFormTemplateRuntime _structuredFormTemplate;
    private readonly ChoicePromptTemplateRuntime _choicePromptTemplate;
    private readonly ChecklistTemplateRuntime _checklistTemplate;
    private readonly DataGridTemplateRuntime _dataGridTemplate;
    private readonly CardDeckTemplateRuntime _cardDeckTemplate;
    private readonly GraphTemplateRuntime _graphTemplate;
    private readonly TaskListTemplateRuntime _taskListTemplate;
    private readonly DashboardTemplateRuntime _dashboardTemplate;
    private readonly AssessmentTemplateRuntime _assessmentTemplate;
    private readonly WorkflowTemplateRuntime _workflowTemplate;
    private readonly CustomTemplateRuntime _customTemplate;
    private readonly List<ChatMessage> _messages = [];
    private readonly HashSet<Guid> _streamingMessages = [];
    private IReadOnlyList<CapabilityDefinition> _availableCapabilities = [];
    private IReadOnlyList<ModeDefinition> _availableApps = [];
    private readonly List<PromptDefinition> _activeInstructions = [];
    private readonly List<string> _attachedImages = [];
    private readonly List<string> _attachedContext = [];
    private readonly TaskAttachmentContext _taskAttachments = new();
    private readonly Dictionary<Guid, ProductionMarkdownView> _messageBodies = [];
    private readonly Dictionary<Guid, string> _thinkingContent = new();
    private readonly Dictionary<Guid, long> _thinkingStartTick = new();
    private readonly Dictionary<Guid, long> _thinkingEndTick = new();
    private readonly Dictionary<(Guid MessageId, int SurfaceIndex), GenerativeUiSurface> _generatedSurfaces = [];
    private readonly Dictionary<(Guid MessageId, int SurfaceIndex), Guid> _generatedInstanceIds = [];
    private readonly Dictionary<(Guid MessageId, int SurfaceIndex), string> _generatedSignatures = [];
    private readonly StackPanel _taskHistory = new() { Spacing = 6 };
    private ModeDefinition? _modeDefinition;
    private Conversation _conversation;
    private AgentDefinition? _activeAgent;
    private ChatActionMode? _chatActionModeOverride;
    private GenerativeUiResponseMode? _chatGenerativeUiResponseModeOverride;
    private ModelDescriptor? _selectedModel;
    private string? _pendingInstruction;
    private CancellationTokenSource? _sendCancellation;
    private Flyout? _resolveProblemsFlyout;
    private Flyout? _messageActionsFlyout;
    private Flyout? _messageSecondaryFlyout;
    private bool _isSending;
    private bool _isTaskMode;
    private bool _lastReportedHasStarted;
    private bool _disposed;
    private long _sendStartTick;
    private readonly DispatcherTimer _sendProgressTimer;

    public NewChatPage(
        HavenEventBus bus,
        IConversationRepository conversations,
        IOllamaClient ollama,
        ChatSessionService sessions,
        IConversationVersioningService versioning,
        UserPreferencesService preferences,
        GenerativeUiEventRouter genUiRouter,
        GenUiInstanceStore genUiInstances,
        CalculatorTemplateRuntime calculatorTemplate,
        StructuredFormTemplateRuntime structuredFormTemplate,
        ChoicePromptTemplateRuntime choicePromptTemplate,
        ChecklistTemplateRuntime checklistTemplate,
        DataGridTemplateRuntime dataGridTemplate,
        CardDeckTemplateRuntime cardDeckTemplate,
        GraphTemplateRuntime graphTemplate,
        TaskListTemplateRuntime taskListTemplate,
        DashboardTemplateRuntime dashboardTemplate,
        AssessmentTemplateRuntime assessmentTemplate,
        WorkflowTemplateRuntime workflowTemplate,
        CustomTemplateRuntime customTemplate)
    {
        _bus = bus;
        _conversations = conversations;
        _ollama = ollama;
        _sessions = sessions;
        _versioning = versioning;
        _preferences = preferences;
        _genUiRouter = genUiRouter;
        _genUiInstances = genUiInstances;
        _calculatorTemplate = calculatorTemplate;
        _structuredFormTemplate = structuredFormTemplate;
        _choicePromptTemplate = choicePromptTemplate;
        _checklistTemplate = checklistTemplate;
        _dataGridTemplate = dataGridTemplate;
        _cardDeckTemplate = cardDeckTemplate;
        _graphTemplate = graphTemplate;
        _taskListTemplate = taskListTemplate;
        _dashboardTemplate = dashboardTemplate;
        _assessmentTemplate = assessmentTemplate;
        _workflowTemplate = workflowTemplate;
        _customTemplate = customTemplate;
        _conversation = CreateConversation(HavenMode.Chat);

        _sendProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _sendProgressTimer.Tick += (_, _) =>
        {
            if (!_isSending) return;
            var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - _sendStartTick);
            StatusText.Text = elapsed.TotalSeconds switch
            {
                < 15 => "Thinking\u2026",
                < 30 => "Taking a moment\u2026",
                < 60 => "Still working\u2026",
                < 120 => "This is a complex request\u2026",
                _ => $"Working for {(int)elapsed.TotalMinutes}m {(int)elapsed.Seconds}s\u2026"
            };
        };

        InitializeComponent();
        WireEvents();
        RefreshVisualState();
        SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        _ = InitialiseAsync();
    }

    public event EventHandler? ModelChanged;
    public event EventHandler? ConversationStateChanged;
    public event EventHandler<AddMenu.AddMenuAction>? AddActionSelected;
    public event EventHandler<AddMenuSelection>? AddCatalogItemSelected;

    public string? SelectedModelName => _selectedModel?.Name;
    public ModelDescriptor? SelectedModel => _selectedModel;
    public Guid ConversationId => _conversation.Id;
    public Conversation CurrentConversation => _conversation;
    public bool IsTemporary => _conversation.IsTemporary;
    public bool HasStarted => _messages.Count > 0;
    public string ActiveAgentName => _activeAgent?.Name ?? "No Agent (Default)";
    public ChatActionMode EffectiveChatActionMode => _chatActionModeOverride ?? ChatActionMode.AllowBasicActions;

    /// <summary>
    /// Gives the shared conversation canvas a registered app identity. The
    /// conversation keeps its compatible base mode while the app-specific
    /// instructions are included in every turn.
    /// </summary>
    public void ConfigureMode(ModeDefinition mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        if (HasStarted)
            throw new InvalidOperationException("A started conversation cannot be reassigned to another app.");

        _modeDefinition = mode;
        _conversation = CreateConversation(mode.BaseMode);
        _chatActionModeOverride = null;
        _chatGenerativeUiResponseModeOverride = null;
        InstructionBox.PlaceholderText = mode.Key switch
        {
            "imagine" => "Describe an image, style or visual concept",
            "present" => "Describe the presentation you want to create",
            "data" => "Attach data or ask Haven to analyse it",
            "vision" => "Attach an image and ask what you want to inspect",
            "play" => "Describe what you want to play, build or explore",
            "translate" => "Paste text and name the target language",
            "launcher" => "Find an app, project, command or recent item",
            _ => $"Ask Haven {mode.Name}"
        };
        StatusText.Text = mode.Description;
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Configures the shared modern conversation canvas for a one-time Task.
    /// No synthetic setup message is inserted into the transcript: the user
    /// starts by describing the outcome they actually want.
    /// </summary>
    public void ConfigureTaskMode()
    {
        if (HasStarted)
            throw new InvalidOperationException("A started conversation cannot be reassigned to Tasks.");

        _isTaskMode = true;
        _modeDefinition = null;
        _conversation = CreateConversation(HavenMode.Tasks);
        _chatActionModeOverride = null;
        _chatGenerativeUiResponseModeOverride = null;
        SurfaceGrid.ColumnDefinitions = new ColumnDefinitions("340,*");
        SurfaceTitle.IsVisible = true;
        TasksSidebarHost.IsVisible = true;
        TasksSidebarHost.Child = BuildTasksSidebar();
        InstructionBox.PlaceholderText = "Describe your task";
        StatusText.Text = "Describe what you want Haven to complete. Haven will ask only for details that materially affect the result.";
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
        ApplyResponsiveLayout(Bounds.Width);
        _ = RefreshTaskHistoryAsync();
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;
        var compact = width < 760;
        var showCompactTaskRail = compact && _isTaskMode;

        SurfaceGrid.ColumnDefinitions = compact
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions(_isTaskMode ? "340,*" : "0,*");
        SurfaceGrid.RowDefinitions = showCompactTaskRail
            ? new RowDefinitions("Auto,*")
            : new RowDefinitions("*");
        SurfaceGrid.ColumnSpacing = compact ? 0 : 18;
        SurfaceGrid.RowSpacing = showCompactTaskRail ? 12 : 0;

        Grid.SetColumn(TasksSidebarHost, 0);
        Grid.SetRow(TasksSidebarHost, 0);
        Grid.SetColumn(ChatWorkspace, compact ? 0 : 1);
        Grid.SetRow(ChatWorkspace, showCompactTaskRail ? 1 : 0);

        TasksSidebarHost.MaxHeight = showCompactTaskRail ? 205 : double.PositiveInfinity;
        TasksSidebarHost.Margin = showCompactTaskRail
            ? new Thickness(14, 10, 14, 0)
            : new Thickness(0);
        ChatWorkspace.Margin = compact
            ? new Thickness(14, 4, 14, 8)
            : new Thickness(32, 10, 32, 4);
    }

    private Control BuildTasksSidebar()
    {
        var newTask = new HavenPrimaryButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new HavenIcon { IconKey = "tasks", Width = 20, Height = 20 },
                    new TextBlock { Text = "New Task", FontSize = 15, FontWeight = FontWeight.Bold }
                }
            },
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 54,
            Padding = new Thickness(16, 10),
            CornerRadius = new CornerRadius(16)
        };
        newTask.Click += async (_, _) =>
        {
            await StartFreshConversationAsync(HavenMode.Tasks, null);
            StatusText.Text = "Describe what you want Haven to complete.";
            await RefreshTaskHistoryAsync();
        };
        AutomationProperties.SetName(newTask, "Start a new one-time task");

        return new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            RowSpacing = 14,
            Children =
            {
                newTask,
                Row(new TextBlock
                {
                    Text = "Task History",
                    FontSize = 13,
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(6, 0, 0, 0)
                }, 1),
                Row(new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = _taskHistory
                }, 2)
            }
        };
    }

    private async Task RefreshTaskHistoryAsync()
    {
        if (!_isTaskMode) return;
        try
        {
            var conversations = await _conversations.GetRecentAsync(HavenMode.Tasks, 40, CancellationToken.None);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _taskHistory.Children.Clear();
                foreach (var conversation in conversations.Where(item => !item.IsArchived))
                {
                    var captured = conversation;
                    var button = new HavenNavigationButton
                    {
                        Content = new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 9,
                            Children =
                            {
                                new HavenIcon { IconKey = "clock", Width = 17, Height = 17 },
                                new TextBlock
                                {
                                    Text = string.IsNullOrWhiteSpace(captured.Title) ? "Untitled task" : captured.Title,
                                    FontSize = 14,
                                    FontWeight = FontWeight.SemiBold,
                                    TextTrimming = TextTrimming.CharacterEllipsis,
                                    MaxWidth = 250
                                }
                            }
                        },
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MinHeight = 44,
                        Padding = new Thickness(10, 7),
                        CornerRadius = new CornerRadius(13)
                    };
                    button.Classes.Set("selected", captured.Id == ConversationId);
                    button.Click += async (_, _) => await LoadConversationAsync(captured);
                    AutomationProperties.SetName(button, "Open task " + captured.Title);
                    _taskHistory.Children.Add(button);
                }

                if (_taskHistory.Children.Count == 0)
                    _taskHistory.Children.Add(new TextBlock
                    {
                        Text = "Completed and active one-time tasks will appear here.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = ResourceBrush("HavenMutedBrush", Color.Parse("#FF666666")),
                        FontSize = 12,
                        Margin = new Thickness(8, 4)
                    });
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await SetStatusAsync("Task history could not be loaded: " + exception.Message);
        }
    }

    private static T Row<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    public void SelectModel(ModelDescriptor model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _selectedModel = model;
        _preferences.SetModelDefaults(model.Name, _preferences.DefaultEffort);
        StatusText.Text = string.Empty;
        ModelChanged?.Invoke(this, EventArgs.Empty);
        RefreshVisualState();
        TrySubmitPendingInstruction();
    }

    public async Task RefreshModelsAsync()
    {
        try
        {
            var models = await _ollama.GetModelsAsync(CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var selectedName = _selectedModel?.Name ?? _preferences.DefaultModel;
                _selectedModel = models.FirstOrDefault(model =>
                                     model.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase))
                                 ?? models.FirstOrDefault();
                StatusText.Text = _selectedModel is null ? "No local model is available." : string.Empty;
                ModelChanged?.Invoke(this, EventArgs.Empty);
                RefreshVisualState();
                TrySubmitPendingInstruction();
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            await SetStatusAsync("Local models are unavailable. Start Ollama, then try again.");
        }
    }

    public void Submit(string instruction)
    {
        var pending = instruction.Trim();
        if (string.IsNullOrWhiteSpace(pending)) return;
        InstructionBox.Text = pending;
        _pendingInstruction = pending;
        TrySubmitPendingInstruction();
    }

    public void FocusComposer() => InstructionBox.Focus();

    public void SetDraft(string instruction)
    {
        InstructionBox.Text = instruction;
        InstructionBox.CaretIndex = instruction.Length;
        FocusComposer();
    }

    public void ShowAddMenu() => AddButton.ShowMenu();

    public Task RegenerateLatestAsync()
    {
        var response = _messages.LastOrDefault(message => message.Role == MessageRole.Assistant);
        return response is null
            ? Task.CompletedTask
            : RegenerateResponseAsync(response, ResponseRegenerationMode.Here);
    }

    public Task BranchLatestAsync()
    {
        var through = _messages.LastOrDefault();
        return through is null ? Task.CompletedTask : BranchIntoNewChatAsync(through);
    }

    public void StartFreshConversation(Guid? chatGroupId = null)
    {
        ResetToFreshConversation(_modeDefinition?.BaseMode ?? HavenMode.Chat, chatGroupId, null);
        _ = PersistFreshConversationAsync(_conversation);
        NotifyFreshConversationReady();
    }

    public async Task StartFreshConversationAsync(Guid? chatGroupId = null)
    {
        ResetToFreshConversation(_modeDefinition?.BaseMode ?? HavenMode.Chat, chatGroupId, null);
        await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        NotifyFreshConversationReady();
    }

    public async Task StartFreshConversationAsync(
        HavenMode mode,
        Guid? containerId,
        Guid? lessonId = null)
    {
        ResetToFreshConversation(mode, containerId, lessonId);
        await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        NotifyFreshConversationReady();
    }

    private void ResetToFreshConversation(HavenMode mode, Guid? containerId, Guid? lessonId)
    {
        _sendCancellation?.Cancel();
        _conversation = CreateConversation(mode, containerId, lessonId);
        _chatActionModeOverride = null;
        _chatGenerativeUiResponseModeOverride = null;
        _messages.Clear();
        _streamingMessages.Clear();
        _pendingInstruction = null;
        StatusText.Text = string.Empty;
        RefreshMessages();
    }

    private async Task PersistFreshConversationAsync(Conversation conversation)
    {
        try
        {
            await _conversations.UpsertConversationAsync(conversation, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            await SetStatusAsync("The new chat could not be saved yet. It will be retried when you send a message.");
        }
    }

    private void NotifyFreshConversationReady()
    {
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
        if (_isTaskMode) _ = RefreshTaskHistoryAsync();
        FocusComposer();
    }

    public void SetAddCatalogue(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<CapabilityDefinition> capabilities,
        IReadOnlyList<PromptDefinition> instructions,
        IReadOnlyList<ModeDefinition> apps)
    {
        _availableCapabilities = capabilities;
        _availableApps = apps;
        AddButton.SetCatalogue(agents, capabilities, instructions, apps);
    }

    public async Task LoadConversationAsync(Conversation conversation)
    {
        _conversation = conversation;
        _chatActionModeOverride = null;
        _chatGenerativeUiResponseModeOverride = null;
        _messages.Clear();
        _messages.AddRange(await _conversations.GetMessagesAsync(conversation.Id, CancellationToken.None));
        RefreshMessages();
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
        if (_isTaskMode) _ = RefreshTaskHistoryAsync();
        FocusComposer();
    }

    public void ApplyAddSelection(AddMenuSelection selection)
    {
        switch (selection.Item)
        {
            case AgentDefinition agent:
                _activeAgent = agent;
                StatusText.Text = $"{agent.Name} selected.";
                break;
            case CapabilityDefinition capability:
                AttachCapability(capability);
                break;
            case PromptDefinition instruction:
                if (_activeInstructions.All(item => item.Id != instruction.Id)) _activeInstructions.Add(instruction);
                StatusText.Text = $"{instruction.Name} instruction added.";
                break;
            case ChatActionMode actionMode:
                _chatActionModeOverride = actionMode;
                StatusText.Text = $"{ActionModeLabel(actionMode)} for this chat.";
                break;
            case GenerativeUiResponseMode responseMode:
                _chatGenerativeUiResponseModeOverride = responseMode;
                StatusText.Text = $"{VisualResponseModeLabel(responseMode)} for this chat.";
                break;
            case ModeDefinition app:
                _taskAttachments.AttachApp(app);
                StatusText.Text = $"{app.Name} attached to this task.";
                break;
        }
    }

    public bool IsCapabilityAttached(Guid capabilityId) =>
        _taskAttachments.IsCapabilityAttached(capabilityId);

    public void ToggleCapability(CapabilityDefinition capability)
    {
        if (_taskAttachments.IsCapabilityAttached(capability.Id))
        {
            _taskAttachments.RemoveCapability(capability.Id);
            StatusText.Text = $"{capability.Name} removed from this task context.";
            return;
        }
        AttachCapability(capability);
    }

    public void AttachSnapshot(TaskAttachmentSnapshot snapshot) =>
        _taskAttachments.AttachSnapshot(snapshot);

    private void AttachCapability(CapabilityDefinition capability)
    {
        var owner = _availableApps.FirstOrDefault(app =>
            app.Key.Equals(capability.OwnerAppKey, StringComparison.OrdinalIgnoreCase));
        _taskAttachments.AttachCapability(capability, owner);
        StatusText.Text = $"{capability.Name} attached as task relevance; permissions are unchanged.";
    }

    public async Task AddFileAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add files to this chat",
            AllowMultiple = true
        });
        await AddFilesAsync(files.Select(file => file.TryGetLocalPath()).OfType<string>());
    }

    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var added = 0;
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif")
            {
                if (!_attachedImages.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    _attachedImages.Add(path);
                    added++;
                }
                continue;
            }
            try
            {
                var info = new FileInfo(path);
                if (info.Length > 2_000_000)
                {
                    _attachedContext.Add($"Attached file: {info.Name} ({info.Length:N0} bytes; content omitted because it is too large)." );
                    added++;
                    continue;
                }
                var text = await File.ReadAllTextAsync(path, CancellationToken.None);
                _attachedContext.Add($"Attached file {info.Name}:\n{text}");
                added++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusText.Text = $"Could not attach {Path.GetFileName(path)}.";
            }
        }
        if (added > 0)
            StatusText.Text = $"{added} file{(added == 1 ? "" : "s")} attached to this task.";
    }

    public string? GetLastAssistantResponse() =>
        _messages.LastOrDefault(message => message.Role == MessageRole.Assistant)?.Content;

    public async Task TogglePinAsync()
    {
        _conversation = _conversation with
        {
            IsPinned = !_conversation.IsPinned,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        StatusText.Text = _conversation.IsPinned ? "Chat pinned." : "Chat unpinned.";
    }

    public async Task ArchiveAsync()
    {
        if (_messages.Count > 0)
        {
            _conversation = _conversation with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow };
            await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        }
        StartFreshConversation();
    }

    public async Task DeleteConversationAsync()
    {
        if (_messages.Count > 0)
            await _conversations.DeleteConversationAsync(_conversation.Id, CancellationToken.None);
        StartFreshConversation();
    }

    public void ShowRenameFlyout()
    {
        var editor = new HavenTextInput
        {
            Text = _conversation.Title,
            MinWidth = 300,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold
        };
        var save = new HavenPrimaryButton
        {
            Content = "Rename chat",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var flyout = new HavenDropdown
        {
            Placement = PlacementMode.Top,
            Content = new HavenDropdownCard
            {
                Width = 360,
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Rename chat", FontSize = 20, FontWeight = FontWeight.ExtraBold },
                        editor,
                        save
                    }
                }
            }
        };
        save.Click += async (_, _) =>
        {
            var title = editor.Text?.Trim();
            if (string.IsNullOrWhiteSpace(title)) return;
            _conversation = _conversation with { Title = title, UpdatedAt = DateTimeOffset.UtcNow };
            if (_messages.Count > 0)
                await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
            flyout.Hide();
        };
        flyout.ShowAt(InstructionBox);
        Dispatcher.UIThread.Post(() => editor.Focus(), DispatcherPriority.Background);
    }

    public void ShowDeleteConfirmation()
    {
        if (_messages.Count == 0) return;
        var cancel = new HavenTertiaryButton { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Stretch };
        var delete = new HoldToConfirmButton
        {
            Content = "Delete chat",
            ActionLabel = "delete chat",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var flyout = new HavenPopup
        {
            Placement = PlacementMode.Top,
            Content = new HavenPopupCard
            {
                Width = 520,
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Delete this chat?", FontSize = 20, FontWeight = FontWeight.ExtraBold },
                        new TextBlock { Text = "This permanently removes the conversation and its messages.", TextWrapping = TextWrapping.Wrap },
                        delete,
                        cancel
                    }
                }
            }
        };
        cancel.Click += (_, _) => flyout.Hide();
        delete.Click += async (_, _) =>
        {
            flyout.Hide();
            await DeleteConversationAsync();
        };
        flyout.ShowAt(InstructionBox);
    }

    public void SetTemporary(bool isTemporary)
    {
        _conversation = _conversation with { IsTemporary = isTemporary, UpdatedAt = DateTimeOffset.UtcNow };
    }

    private void WireEvents()
    {
        Register("Chat.Composer.Add", AddButton);
        Register("Chat.Composer.Instruction", InstructionBox);
        Register("Chat.Composer.Send", SendButton);
        Register("Chat.Problems.Resolve", ResolveProblemsButton);

        SendButton.Click += OnSendClicked;
        InstructionBox.KeyDown += OnInstructionKeyDown;
        AddButton.CurrentAgentNameProvider = () => ActiveAgentName;
        AddButton.ActionSelected += OnAddActionSelected;
        AddButton.CatalogItemSelected += OnCatalogItemSelected;
        ResolveProblemsButton.Click += OnResolveProblemsClicked;
    }

    private async Task InitialiseAsync()
    {
        await SetStatusAsync("Connecting to local models…");
        await RefreshModelsAsync();
    }

    private async void OnSendClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await SubmitCurrentInstructionAsync();

    private async void OnInstructionKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        e.Handled = true;
        await SubmitCurrentInstructionAsync();
    }

    private void OnAddActionSelected(object? sender, AddMenu.AddMenuAction action) =>
        AddActionSelected?.Invoke(this, action);

    private void OnCatalogItemSelected(object? sender, AddMenuSelection selection)
    {
        ApplyAddSelection(selection);
        AddCatalogItemSelected?.Invoke(this, selection);
    }

    private void OnResolveProblemsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _bus.Fire("Chat.Problems.Resolve.Click");
        ShowResolveProblemsFlyout();
    }

    private void ShowResolveProblemsFlyout()
    {
        _resolveProblemsFlyout?.Hide();

        var hallucinating = BuildProblemResolutionAction(
            "info",
            "Hallucinating",
            "Re-check claims and separate facts from assumptions.",
            "Stop and audit your previous responses in this chat for hallucinations. Re-check every factual claim against the information actually available, clearly separate verified facts from assumptions, correct anything unsupported, and ask for missing information instead of guessing.");
        var looping = BuildProblemResolutionAction(
            "refresh",
            "Looping",
            "Break the cycle and take a genuinely different approach.",
            "You are repeating the same approach. Stop the loop, briefly identify what has already been attempted and why it did not work, then choose a materially different approach and continue from there without repeating earlier steps.");
        var recurringBug = BuildProblemResolutionAction(
            "code",
            "Recurring Bug in Produced Code",
            "Find the root cause before proposing another patch.",
            "The produced code has a recurring bug. Reproduce or trace the failure, compare it with the previous attempted fixes, identify the underlying root cause, then propose the smallest complete correction and explain how to verify that the same bug no longer recurs. Do not repeat an earlier patch unchanged.");
        var other = BuildProblemResolutionAction(
            "warning",
            "Something Else",
            "Run a structured review of the unresolved problem.",
            "Review this conversation for the unresolved problem. Summarise the observed symptoms and attempted fixes, identify the most likely root cause, state what evidence is still missing, and propose the safest next step without claiming success until it is verified.");

        var panel = new StackPanel
        {
            Width = 380,
            Spacing = 3,
            Margin = new Thickness(12),
            Children =
            {
                new TextBlock
                {
                    Text = "Resolve Problems",
                    FontSize = 20,
                    FontWeight = FontWeight.ExtraBold,
                    Margin = new Thickness(10, 5, 10, 8)
                },
                new TextBlock
                {
                    Text = "Choose what is going wrong. Haven will prepare a focused recovery instruction for you to review and send.",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = ResourceBrush("HavenMutedTextBrush", Color.Parse("#FF666666")),
                    Margin = new Thickness(10, 0, 10, 8)
                },
                hallucinating,
                looping,
                recurringBug,
                other
            }
        };

        _resolveProblemsFlyout = new HavenDropdown
        {
            Placement = PlacementMode.Top,
            Content = new HavenDropdownCard { Width = 410, Child = panel }
        };
        _resolveProblemsFlyout.ShowAt(ResolveProblemsButton);
    }

    private HavenDropdownItemButton BuildProblemResolutionAction(
        string icon,
        string title,
        string description,
        string recoveryInstruction)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12
        };
        grid.Children.Add(new HavenIcon
        {
            IconKey = icon,
            Width = 20,
            Height = 20,
            Foreground = ResourceBrush("HavenTextBrush", Colors.Black),
            VerticalAlignment = VerticalAlignment.Center
        });

        var copy = new StackPanel { Spacing = 1 };
        copy.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeight.ExtraBold
        });
        copy.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("HavenMutedTextBrush", Color.Parse("#FF666666"))
        });
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);

        var button = new HavenDropdownItemButton
        {
            Content = grid,
            MinHeight = 48,
            Padding = new Thickness(12, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Role = HavenDropdownItemRole.Main
        };
        button.Click += (_, _) =>
        {
            _resolveProblemsFlyout?.Hide();
            InstructionBox.Text = recoveryInstruction;
            InstructionBox.CaretIndex = recoveryInstruction.Length;
            StatusText.Text = "Review the recovery instruction, then send it when ready.";
            FocusComposer();
        };
        return button;
    }

    private async Task SubmitCurrentInstructionAsync()
    {
        var instruction = InstructionBox.Text?.Trim();
        if (_isSending || string.IsNullOrWhiteSpace(instruction)) return;
        if (_selectedModel is null)
        {
            _pendingInstruction = instruction;
            StatusText.Text = "Connecting to the selected local model…";
            return;
        }

        _pendingInstruction = null;
        InstructionBox.Text = string.Empty;
        _bus.Fire("Chat.Composer.Send.Click");
        _isSending = true;
        _sendStartTick = Environment.TickCount64;
        _sendProgressTimer.Start();
        _sendCancellation = new CancellationTokenSource();
        RefreshVisualState();

        var now = DateTimeOffset.UtcNow;
        if (_messages.Count == 0)
        {
            var title = instruction.Length > 56 ? instruction[..53] + "…" : instruction;
            _conversation = _conversation with { Title = title, UpdatedAt = now };
        }

        try
        {
            var deltaBuffer = new StringBuilder();
            Guid? bufferedMessageId = null;
            var nextDeltaFlushAt = Environment.TickCount64 + 50;

            async Task FlushDeltasAsync()
            {
                if (bufferedMessageId is not { } messageId || deltaBuffer.Length == 0) return;
                var delta = deltaBuffer.ToString();
                deltaBuffer.Clear();
                await Dispatcher.UIThread.InvokeAsync(() =>
                    ApplyStreamEvent(ChatStreamEvent.AssistantDelta(messageId, delta)));
            }

            await foreach (var streamEvent in _sessions.SendAsync(
                               _conversation,
                               instruction,
                               _selectedModel,
                               _preferences.DefaultEffort,
                               [],
                               _activeAgent?.Name ?? "Haven",
                               _activeAgent?.Instructions ?? string.Empty,
                               DuoMode.Solo,
                               null,
                               null,
                               null,
                               _attachedImages.Count == 0 ? null : _attachedImages.ToArray(),
                               _sendCancellation.Token,
                               prompts: _activeInstructions.Select(item => new ActivePrompt(item.Name, item.IconKey, item.Persists, item.Instructions)).ToArray(),
                               registeredContext: BuildRegisteredContext(),
                               generationOptions: _preferences.GenerationOptions,
                               filePermission: _preferences.FilePermission,
                               commandPermission: _preferences.CommandPermission,
                               browserPermission: _preferences.BrowserPermission,
                               availableCapabilities: ActiveCapabilitiesForCurrentChat()).ConfigureAwait(false))
            {
                if (streamEvent.Kind == ChatStreamEventKind.AssistantDelta &&
                    streamEvent.MessageId is { } deltaMessageId)
                {
                    if (bufferedMessageId is not null && bufferedMessageId != deltaMessageId)
                        await FlushDeltasAsync();
                    bufferedMessageId = deltaMessageId;
                    deltaBuffer.Append(streamEvent.Delta);
                    if (Environment.TickCount64 < nextDeltaFlushAt) continue;
                    await FlushDeltasAsync();
                    nextDeltaFlushAt = Environment.TickCount64 + 50;
                    continue;
                }

                await FlushDeltasAsync();
                await Dispatcher.UIThread.InvokeAsync(() => ApplyStreamEvent(streamEvent));
            }

            await FlushDeltasAsync();

            await SetStatusAsync(string.Empty);
        }
        catch (OperationCanceledException)
        {
            await SetStatusAsync("Response stopped.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText.Text = "Haven could not complete that response.";
                ResolveProblemsButton.IsVisible = true;
            });
        }
        finally
        {
            _sendProgressTimer.Stop();
            _sendCancellation.Dispose();
            _sendCancellation = null;
            _isSending = false;
            await Dispatcher.UIThread.InvokeAsync(RefreshVisualState);
        }
    }

    private IReadOnlyList<ActiveCapability> ActiveCapabilitiesForCurrentChat()
    {
        IEnumerable<CapabilityDefinition> allowed = EffectiveChatActionMode switch
        {
            ChatActionMode.JustChat => [],
            ChatActionMode.AllowBasicActions => _availableCapabilities.Where(item => item.RiskClass is CapabilityRiskClass.ReadOnly or CapabilityRiskClass.Low),
            _ => _availableCapabilities
        };
        return allowed.Select(ActiveCapability.FromDefinition).ToArray();
    }

    private static string ActionModeLabel(ChatActionMode mode) => mode switch
    {
        ChatActionMode.AllowAllActions => "Allow All Actions",
        ChatActionMode.JustChat => "Just Chat",
        _ => "Allow Basic Actions"
    };

    private static string VisualResponseModeLabel(GenerativeUiResponseMode mode) => mode switch
    {
        GenerativeUiResponseMode.AlwaysVisual => "Always Visual",
        GenerativeUiResponseMode.PreferVisual => "Prefer Visual",
        GenerativeUiResponseMode.PreferText => "Prefer Text",
        GenerativeUiResponseMode.AlwaysText => "Always Text",
        _ => "Auto"
    };

    private string? BuildRegisteredContext()
    {
        var sections = new List<string>();
        if (_modeDefinition is { } mode)
        {
            sections.Add($"Active Haven app: {mode.Name}.\nPurpose: {mode.Description}");
            if (!string.IsNullOrWhiteSpace(mode.SystemPromptSuffix))
                sections.Add(mode.SystemPromptSuffix.Trim());
        }
        if (_taskAttachments.BuildAppContext() is { } appContext)
            sections.Add(appContext);
        if (_taskAttachments.BuildCapabilityContext() is { } capabilityContext)
            sections.Add(capabilityContext);
        sections.AddRange(_attachedContext.Where(item => !string.IsNullOrWhiteSpace(item)));
        sections.Add(GenUiChatDirectiveParser.ModelInstructionFor(_chatGenerativeUiResponseModeOverride ?? GenerativeUiResponseMode.Auto));
        return sections.Count == 0 ? null : string.Join("\n\n", sections);
    }

    private void ApplyStreamEvent(ChatStreamEvent streamEvent)
    {
        var rebuildMessages = true;
        switch (streamEvent.Kind)
        {
            case ChatStreamEventKind.UserMessage when streamEvent.Message is not null:
                UpsertMessage(streamEvent.Message);
                break;
            case ChatStreamEventKind.AssistantStarted when streamEvent.MessageId is { } assistantId:
                _streamingMessages.Add(assistantId);
                UpsertMessage(new ChatMessage(
                    assistantId,
                    _conversation.Id,
                    MessageRole.Assistant,
                    string.Empty,
                    streamEvent.Agent,
                    streamEvent.Model,
                    null,
                    DateTimeOffset.UtcNow));
                break;
            case ChatStreamEventKind.AssistantDelta when streamEvent.MessageId is { } deltaId:
                var index = _messages.FindIndex(message => message.Id == deltaId);
                if (index >= 0)
                {
                    _messages[index] = _messages[index] with
                    {
                        Content = _messages[index].Content + (streamEvent.Delta ?? string.Empty)
                    };
                    if (_messageBodies.TryGetValue(deltaId, out var body))
                    {
                        var parsed = GenUiChatDirectiveParser.Parse(_messages[index].Content);
                        // During streaming, show the text content. If the model is only
                        // generating a haven-ui block with no surrounding text, show a
                        // preview of what's being generated so the user isn't staring at
                        // a blank "Thinking" state.
                        if (!string.IsNullOrWhiteSpace(parsed.DisplayContent))
                            body.Text = parsed.DisplayContent;
                        else if (parsed.HasDirective && parsed.Requests.Count == 0)
                            body.Text = "Generating interactive content\u2026";
                        else
                            body.Text = parsed.DisplayContent;

                        if (parsed.HasDirective && parsed.Requests.Count == 0 && string.IsNullOrEmpty(parsed.Error))
                            StatusText.Text = "Preparing interactive content\u2026";
                        rebuildMessages = false;
                        Dispatcher.UIThread.Post(() => MessagesScroll.ScrollToEnd(), DispatcherPriority.Background);
                    }
                }
                break;
            case ChatStreamEventKind.ThinkingDelta when streamEvent.MessageId is { } thinkingId:
                if (!_thinkingContent.ContainsKey(thinkingId))
                {
                    _thinkingContent[thinkingId] = string.Empty;
                    _thinkingStartTick[thinkingId] = Environment.TickCount64;
                }
                _thinkingContent[thinkingId] += streamEvent.Thinking ?? string.Empty;
                rebuildMessages = false;
                break;
            case ChatStreamEventKind.AssistantCompleted when streamEvent.Message is not null:
                _streamingMessages.Remove(streamEvent.Message.Id);
                if (_thinkingStartTick.ContainsKey(streamEvent.Message.Id) && !_thinkingEndTick.ContainsKey(streamEvent.Message.Id))
                    _thinkingEndTick[streamEvent.Message.Id] = Environment.TickCount64;
                UpsertMessage(streamEvent.Message);
                break;
            case ChatStreamEventKind.ToolActivity when streamEvent.ToolActivity is not null:
                StatusText.Text = streamEvent.ToolActivity.Detail;
                break;
            case ChatStreamEventKind.PreflightFailed:
                StatusText.Text = streamEvent.PreflightResult is { Missing.Count: > 0 } preflight
                    ? string.Join(" ", preflight.Missing.Select(item => item.Reason))
                    : "The selected model cannot complete this request.";
                ResolveProblemsButton.IsVisible = true;
                break;
        }

        if (rebuildMessages) RefreshMessages();
    }

    private void UpsertMessage(ChatMessage message)
    {
        var index = _messages.FindIndex(existing => existing.Id == message.Id);
        if (index < 0) _messages.Add(message);
        else _messages[index] = message;
    }

    private void TrySubmitPendingInstruction()
    {
        if (_selectedModel is null || _isSending || string.IsNullOrWhiteSpace(_pendingInstruction)) return;
        InstructionBox.Text = _pendingInstruction;
        _pendingInstruction = null;
        _ = SubmitCurrentInstructionAsync();
    }

    private void RefreshVisualState()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshVisualState);
            return;
        }

        SendButton.IsEnabled = !_isSending && _selectedModel is not null;
        InstructionBox.IsEnabled = !_isSending;
        RefreshMessages();
    }

    private void RefreshMessages()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshMessages);
            return;
        }

        MessagesPanel.Children.Clear();
        _messageBodies.Clear();
        PruneGeneratedSurfaces();
        foreach (var message in _messages)
            MessagesPanel.Children.Add(BuildMessage(message, _streamingMessages.Contains(message.Id)));

        ResolveProblemsButton.IsVisible = HasStarted;
        EmptyState.IsVisible = !HasStarted;
        MessagesScroll.IsVisible = HasStarted;
        if (_lastReportedHasStarted != HasStarted)
        {
            _lastReportedHasStarted = HasStarted;
            ConversationStateChanged?.Invoke(this, EventArgs.Empty);
            if (_isTaskMode) _ = RefreshTaskHistoryAsync();
        }

        Dispatcher.UIThread.Post(() => MessagesScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    private Control BuildMessage(ChatMessage message, bool isStreaming)
    {
        var directive = message.Role == MessageRole.Assistant
            ? GenUiChatDirectiveParser.Parse(message.Content)
            : new GenUiChatDirectiveParseResult(message.Content, [], null, false);
        var displayContent = string.IsNullOrWhiteSpace(directive.DisplayContent) && directive.Requests.Count > 0
            ? ""
            : directive.DisplayContent;

        // ChatGPT-style message: clean text, no bubble chrome
        var messageView = new MessageView
        {
            Role = message.Role,
            MessageContent = displayContent,
            AgentName = message.AgentName,
            IsStreaming = isStreaming
        };
        _messageBodies[message.Id] = messageView.FindControl<ProductionMarkdownView>("Body")!;

        messageView.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(messageView).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed) return;
            args.Handled = true;
            ShowMessageActions(messageView, message);
        };

        // The chat stream is the canvas. Messages and GenUI surfaces flow
        // together in one scrollable vertical stream.
        var stream = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // Show thinking as a collapsible if present
        if (_thinkingContent.TryGetValue(message.Id, out var thinking) && !string.IsNullOrWhiteSpace(thinking))
        {
            var elapsed = 0L;
            if (_thinkingStartTick.TryGetValue(message.Id, out var startTick))
            {
                var endTick = _thinkingEndTick.TryGetValue(message.Id, out var et) ? et : Environment.TickCount64;
                elapsed = Math.Max(0, (endTick - startTick) / 1000);
            }
            var header = elapsed > 0 ? $"Thought for {elapsed} seconds" : "Thinking\u2026";
            var thinkingExpander = new HavenExpander
            {
                Header = header,
                IsExpanded = false
            };
            var thinkingText = new TextBlock
            {
                Text = thinking,
                FontSize = 12,
                Foreground = ResourceBrush("HavenTextMutedBrush", Colors.Gray),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            thinkingExpander.Content = thinkingText;
            stream.Children.Add(thinkingExpander);
        }

        stream.Children.Add(messageView);

        // GenUI surfaces render as natural inline content in the chat stream.
        // The chat area is the canvas — surfaces flow alongside messages.
        for (var surfaceIndex = 0; surfaceIndex < directive.Requests.Count; surfaceIndex++)
        {
            var request = directive.Requests[surfaceIndex];
            try
            {
                messageView.ShowGenUIProgress(request.TemplateKey);
                var surface = GetOrCreateGeneratedSurface(message, surfaceIndex, request);

                // Validate the generated UI after rendering
                var warnings = ValidateGeneratedSurface(surface);
                if (surface.Document is { } generatedDocument)
                {
                    warnings.AddRange(GenUiDocumentQualityValidator.Validate(generatedDocument)
                        .Select(issue => $"GenUI self-check: {issue.Message}"));
                }
                if (warnings.Count > 0)
                {
                    // Append validation warnings below the surface
                    surface.Margin = new Thickness(0, 4, 0, 0);
                    stream.Children.Add(surface);
                    foreach (var warning in warnings)
                    {
                        stream.Children.Add(new TextBlock
                        {
                            Text = warning,
                            FontSize = 11,
                            Foreground = ResourceBrush("HavenWarningBrush", Colors.Orange),
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(4, 0, 0, 0)
                        });
                    }
                }
                else
                {
                    surface.Margin = new Thickness(0, 4, 0, 4);
                    stream.Children.Add(surface);
                }
                messageView.HideGenUIProgress();
            }
            catch (Exception ex)
            {
                messageView.HideGenUIProgress();
                stream.Children.Add(new TextBlock
                {
                    Text = $"Could not render {request.TemplateKey}: {ex.Message}",
                    FontSize = 12,
                    Foreground = ResourceBrush("HavenDangerBrush", Colors.Red),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(38, 0, 0, 0)
                });
            }
        }
        RemoveGeneratedSurfacesAfter(message.Id, directive.Requests.Count);

        if (!string.IsNullOrWhiteSpace(directive.Error))
        {
            stream.Children.Add(new HavenCard
            {
                Padding = new Thickness(12),
                Child = new TextBlock
                {
                    Text = "Interactive content unavailable: " + directive.Error,
                    Classes = { "muted" },
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }

        return stream;
    }

    /// <summary>
    /// Validates a generated GenUI surface after rendering.
    /// Returns warnings if the output is empty, broken, or inappropriate.
    /// </summary>
    private static List<string> ValidateGeneratedSurface(GenerativeUiSurface surface)
    {
        var warnings = new List<string>();

        // Check if the surface has any visible content
        if (surface.Content is null)
        {
            warnings.Add("The generated UI rendered nothing.");
            return warnings;
        }

        // Walk the visual tree to check for common issues
        var controlCount = 0;
        var emptyContainers = 0;

        void WalkVisual(Avalonia.Visual visual, int depth)
        {
            if (depth > 20) return;

            if (visual is Control)
            {
                controlCount++;
                if (visual is Panel panel && panel.Children.Count == 0)
                    emptyContainers++;
            }

            if (visual is Panel layoutPanel)
            {
                foreach (var child in layoutPanel.Children)
                    WalkVisual(child, depth + 1);
            }
            else if (visual is ContentControl contentControl && contentControl.Content is Avalonia.Visual content)
            {
                WalkVisual(content, depth + 1);
            }
            else if (visual is Border border && border.Child is Avalonia.Visual borderChild)
            {
                WalkVisual(borderChild, depth + 1);
            }
        }

        WalkVisual(surface, 0);

        if (controlCount <= 1)
            warnings.Add("The generated UI appears mostly empty — the model may not have provided enough components.");

        if (emptyContainers > 0)
            warnings.Add($"{emptyContainers} container(s) have no children — the model may have forgotten to nest items inside them.");

        return warnings;
    }

    private GenerativeUiSurface GetOrCreateGeneratedSurface(ChatMessage message, int surfaceIndex, GenUiTemplateRequest request)
    {
        var slot = (message.Id, surfaceIndex);
        var signature = request.Signature;
        GenUiDocument? document = null;
        var reuseRegisteredInstance = _generatedSignatures.TryGetValue(slot, out var existingSignature)
            && existingSignature.Equals(signature, StringComparison.Ordinal)
            && _generatedInstanceIds.TryGetValue(slot, out var existingInstanceId)
            && (document = _genUiInstances.TryGet(existingInstanceId)) is not null;

        if (reuseRegisteredInstance)
        {
            if (_generatedSurfaces.Remove(slot, out var previousSurface)) previousSurface.Dispose();
            var surface = new GenerativeUiSurface(_genUiRouter, _genUiInstances)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            surface.PresentExisting(document!);
            _generatedSurfaces[slot] = surface;
            _generatedInstanceIds[slot] = document!.Origin.InstanceId;
            _generatedSignatures[slot] = signature;
            return surface;
        }

        if (_generatedSurfaces.TryGetValue(slot, out var existing)
            && _generatedSignatures.TryGetValue(slot, out var sig)
            && sig.Equals(signature, StringComparison.Ordinal)) return existing;

        RemoveGeneratedSurfacesForMessage(message.Id);
        var appKey = _modeDefinition?.Key ?? (_isTaskMode ? "tasks" : "chat");
        document = CreateGeneratedDocument(request, appKey);
        // Apply per-surface accent if specified by the model
        if (!string.IsNullOrWhiteSpace(request.AccentKey))
            document = document with { AccentKey = request.AccentKey };
        var newSurface = new GenerativeUiSurface(_genUiRouter, _genUiInstances)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        newSurface.Present(document);
        _generatedSurfaces[slot] = newSurface;
        _generatedInstanceIds[slot] = document.Origin.InstanceId;
        _generatedSignatures[slot] = signature;
        return newSurface;
    }

    private GenUiDocument CreateGeneratedDocument(GenUiTemplateRequest request, string appKey) =>
        request.TemplateKey switch
        {
            "calculator" => _calculatorTemplate.Create(_conversation.Id, appKey, request.Expression),
            "structured-form" => _structuredFormTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "choice-prompt" => _choicePromptTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "checklist" => _checklistTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "data-grid" => _dataGridTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "card-deck" => _cardDeckTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "graph" => _graphTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "task-list" => _taskListTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "dashboard" => _dashboardTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "assessment" => _assessmentTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "workflow" => _workflowTemplate.Create(_conversation.Id, appKey, request.Inputs),
            "custom" => _customTemplate.Create(_conversation.Id, appKey, request.Inputs),
            _ => throw new InvalidOperationException($"Live GenUI template '{request.TemplateKey}' has no trusted runtime.")
        };

    private void PruneGeneratedSurfaces()
    {
        var messageIds = _messages.Select(message => message.Id).ToHashSet();
        foreach (var slot in _generatedSurfaces.Keys.Where(key => !messageIds.Contains(key.MessageId)).ToArray())
            RemoveGeneratedSurface(slot);
    }

    private void RemoveGeneratedSurface((Guid MessageId, int SurfaceIndex) slot)
    {
        if (_generatedSurfaces.Remove(slot, out var surface)) surface.Dispose();
        if (_generatedInstanceIds.Remove(slot, out var instanceId)) _genUiInstances.Remove(instanceId);
        _generatedSignatures.Remove(slot);
    }

    private void RemoveGeneratedSurfacesForMessage(Guid messageId)
    {
        foreach (var slot in _generatedSurfaces.Keys.Where(key => key.MessageId == messageId).ToArray())
            RemoveGeneratedSurface(slot);
    }

    private void RemoveGeneratedSurfacesAfter(Guid messageId, int keepCount)
    {
        foreach (var slot in _generatedSurfaces.Keys
                     .Where(key => key.MessageId == messageId && key.SurfaceIndex >= keepCount)
                     .ToArray())
            RemoveGeneratedSurface(slot);
    }

    private void ShowMessageActions(Control anchor, ChatMessage message)
    {
        _messageSecondaryFlyout?.Hide();
        _messageActionsFlyout?.Hide();

        if (message.Role == MessageRole.Assistant)
        {
            ShowAssistantMessageActions(anchor, message);
            return;
        }

        var edit = BuildMessageAction("edit", "Edit Message");
        var copy = BuildMessageAction("copy", "Copy Message");
        var branch = BuildMessageAction("branch", "Branch Message");
        var delete = BuildMessageAction("delete", "Delete Message", true);

        edit.Click += (_, _) => ShowEditOptions(edit, message);
        copy.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null) await clipboard.SetTextAsync(message.Content);
            _messageActionsFlyout?.Hide();
        };
        branch.Click += (_, _) => ShowBranchOptions(branch, message);
        delete.Click += async (_, _) =>
        {
            _messageActionsFlyout?.Hide();
            await DeleteMessageAsync(message);
        };

        _messageActionsFlyout = new HavenDropdown
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = new HavenDropdownCard
            {
                Width = 260,
                Child = new StackPanel { Spacing = 3, Children = { edit, copy, branch, delete } }
            }
        };
        _messageActionsFlyout.ShowAt(anchor);
    }

    private void ShowAssistantMessageActions(Control anchor, ChatMessage message)
    {
        var regenerate = BuildMessageAction("refresh", "Re-Generate Response");
        var copy = BuildMessageAction("copy", "Copy Response");
        var branch = BuildMessageAction("branch", "Branch Response");
        var forget = BuildMessageAction("delete", "Delete Response from Memory", true);

        regenerate.Click += (_, _) => ShowRegenerationOptions(regenerate, message);
        copy.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null) await clipboard.SetTextAsync(message.Content);
            _messageActionsFlyout?.Hide();
        };
        branch.Click += (_, _) => ShowBranchOptions(branch, message);
        forget.Click += async (_, _) =>
        {
            await _conversations.MarkMessagesCompactedAsync(
                _conversation.Id,
                [message.Id],
                CancellationToken.None);
            _messages.RemoveAll(item => item.Id == message.Id);
            _messageActionsFlyout?.Hide();
            RefreshMessages();
        };

        _messageActionsFlyout = new HavenDropdown
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = new HavenDropdownCard
            {
                Width = 260,
                Child = new StackPanel { Spacing = 3, Children = { regenerate, copy, branch, forget } }
            }
        };
        _messageActionsFlyout.ShowAt(anchor);
    }

    private void ShowRegenerationOptions(Control anchor, ChatMessage message)
    {
        var current = BuildMessageAction("refresh", "Re-Generate in Current Chat");
        var branch = BuildMessageAction("refresh", "Re-Generate in Branch");
        current.Click += async (_, _) => await RegenerateResponseAsync(message, ResponseRegenerationMode.Here);
        branch.Click += async (_, _) => await RegenerateResponseAsync(message, ResponseRegenerationMode.NewBranch);

        var modelChip = new HavenPill
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 7),
            Margin = new Thickness(0, 4, 0, 0),
            Child = new TextBlock
            {
                Text = $"{_selectedModel?.Name ?? "No model"} · 100%",
                FontSize = 13,
                FontWeight = FontWeight.ExtraBold
            }
        };

        _messageSecondaryFlyout?.Hide();
        _messageSecondaryFlyout = new HavenDropdown
        {
            Placement = PlacementMode.Right,
            Content = new HavenDropdownCard
            {
                Width = 300,
                Child = new StackPanel { Spacing = 2, Children = { current, branch, modelChip } }
            }
        };
        _messageSecondaryFlyout.ShowAt(anchor);
    }

    private async Task RegenerateResponseAsync(ChatMessage message, ResponseRegenerationMode requestedMode)
    {
        try
        {
            var index = _messages.FindIndex(item => item.Id == message.Id);
            if (index < 0) return;
            var precedingUser = _messages.Take(index).LastOrDefault(item => item.Role == MessageRole.User)
                                ?? throw new InvalidOperationException("This response has no preceding user message.");
            var latestAssistant = _messages.LastOrDefault(item => item.Role == MessageRole.Assistant);
            var isLatest = latestAssistant?.Id == message.Id;
            var mode = isLatest ? requestedMode : ResponseRegenerationMode.NewBranch;

            await _versioning.PrepareRegenerationAsync(
                _conversation.Id,
                message.Id,
                isLatest,
                mode,
                CancellationToken.None);

            var persisted = await _conversations.GetMessagesAsync(_conversation.Id, CancellationToken.None);
            _messages.Clear();
            _messages.AddRange(persisted);
            RefreshMessages();
            _messageSecondaryFlyout?.Hide();
            _messageActionsFlyout?.Hide();
            Submit(precedingUser.Content);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            StatusText.Text = ex.Message;
        }
    }

    private static ActionButton BuildMessageAction(string icon, string label, bool dangerous = false)
    {
        return new ActionButton
        {
            IconKey = icon,
            LabelText = label,
            IsDangerous = dangerous
        };
    }

    private void ShowEditOptions(Control anchor, ChatMessage message)
    {
        var restart = BuildMessageAction("edit", "Restart from Here");
        var branch = BuildMessageAction("edit", "Edit in new Branch");
        var memory = BuildMessageAction("edit", "Edit in Memory Only");
        restart.Click += (_, _) => ShowMessageEditor(restart, message, MessageEditChoice.RestartHere);
        branch.Click += (_, _) => ShowMessageEditor(branch, message, MessageEditChoice.NewBranch);
        memory.Click += (_, _) => ShowMessageEditor(memory, message, MessageEditChoice.MemoryOnly);
        ShowSecondaryFlyout(anchor, [restart, branch, memory]);
    }

    private void ShowBranchOptions(Control anchor, ChatMessage message)
    {
        var create = BuildMessageAction("branch", "Branch in New Chat");
        var existing = BuildMessageAction("branch", "Branch in Existing Chat");
        create.Click += async (_, _) => await BranchIntoNewChatAsync(message);
        existing.Click += (_, _) => ShowExistingChatPicker(existing, message);
        ShowSecondaryFlyout(anchor, [create, existing]);
    }

    private void ShowSecondaryFlyout(Control anchor, IReadOnlyList<ActionButton> actions)
    {
        _messageSecondaryFlyout?.Hide();
        var panel = new FlyoutPanel();
        foreach (var action in actions) panel.Content.Children.Add(action);
        _messageSecondaryFlyout = panel.CreateFlyout(PlacementMode.Left);
        _messageSecondaryFlyout.ShowAt(anchor);
    }

    private void ShowMessageEditor(Control anchor, ChatMessage message, MessageEditChoice choice)
    {
        var editor = new HavenMultilineInput
        {
            Text = message.Content,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 360,
            MinHeight = 120,
            MaxHeight = 300
        };
        var apply = new HavenPrimaryButton
        {
            Content = choice == MessageEditChoice.MemoryOnly ? "Apply for this session" : "Apply edit",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var editorFlyout = new HavenPopup
        {
            Placement = PlacementMode.Left,
            Content = new HavenPopupCard
            {
                Width = 390,
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Edit message", FontSize = 20, FontWeight = FontWeight.ExtraBold },
                        editor,
                        apply
                    }
                }
            }
        };
        apply.Click += async (_, _) =>
        {
            var content = editor.Text?.Trim();
            if (string.IsNullOrWhiteSpace(content)) return;
            await ApplyMessageEditAsync(message, content, choice);
            editorFlyout.Hide();
            _messageSecondaryFlyout?.Hide();
            _messageActionsFlyout?.Hide();
        };
        editorFlyout.ShowAt(anchor);
        Dispatcher.UIThread.Post(() => editor.Focus(), DispatcherPriority.Background);
    }

    private async Task ApplyMessageEditAsync(ChatMessage message, string content, MessageEditChoice choice)
    {
        var index = _messages.FindIndex(item => item.Id == message.Id);
        if (index < 0) return;

        if (choice == MessageEditChoice.MemoryOnly)
        {
            _messages[index] = message with { Content = content };
            RefreshMessages();
            return;
        }

        try
        {
            if (message.Role == MessageRole.User)
            {
                await _versioning.EditUserMessageAsync(
                    _conversation.Id,
                    message.Id,
                    content,
                    choice == MessageEditChoice.NewBranch
                        ? MessageEditMode.NewBranch
                        : MessageEditMode.OverwriteCurrentBranch,
                    CancellationToken.None);
                var persisted = await _conversations.GetMessagesAsync(_conversation.Id, CancellationToken.None);
                _messages.Clear();
                _messages.AddRange(persisted);
            }
            else
            {
                var updated = message with { Content = content };
                await _conversations.AddMessageAsync(updated, CancellationToken.None);
                _messages[index] = updated;
                if (choice == MessageEditChoice.RestartHere && index + 1 < _messages.Count)
                    _messages.RemoveRange(index + 1, _messages.Count - index - 1);
            }
            RefreshMessages();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async Task BranchIntoNewChatAsync(ChatMessage throughMessage)
    {
        var index = _messages.FindIndex(message => message.Id == throughMessage.Id);
        if (index < 0) return;
        var source = _conversation;
        var now = DateTimeOffset.UtcNow;
        var branch = new Conversation(
            Guid.NewGuid(),
            source.Mode,
            source.Kind,
            $"Branch of {source.Title}",
            source.ContainerId,
            source.LessonId,
            false,
            source.IsTemporary,
            now,
            now,
            ParentConversationId: source.Id);
        var copies = _messages.Take(index + 1)
            .Select(message => message with
            {
                Id = Guid.NewGuid(),
                ConversationId = branch.Id,
                CreatedAt = now.AddTicks(_messages.IndexOf(message))
            })
            .ToArray();

        await _conversations.UpsertConversationAsync(branch, CancellationToken.None);
        foreach (var copy in copies)
            await _conversations.AddMessageAsync(copy, CancellationToken.None);

        _conversation = branch;
        _messages.Clear();
        _messages.AddRange(copies);
        _messageSecondaryFlyout?.Hide();
        _messageActionsFlyout?.Hide();
        RefreshMessages();
        FocusComposer();
    }

    private async void ShowExistingChatPicker(Control anchor, ChatMessage message)
    {
        var recent = (await _conversations.GetRecentAsync(_conversation.Mode, 12, CancellationToken.None))
            .Where(item => item.Id != _conversation.Id && !item.IsArchived)
            .ToArray();
        var panel = new StackPanel { Width = 330, Spacing = 3, Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock
        {
            Text = "Choose an existing chat",
            FontSize = 16,
            FontWeight = FontWeight.ExtraBold,
            Margin = new Thickness(10, 6, 10, 8)
        });
        foreach (var conversation in recent)
        {
            var button = BuildMessageAction("chat", conversation.Title);
            button.Click += async (_, _) => await ContinueInExistingChatAsync(conversation, message);
            panel.Children.Add(button);
        }
        if (recent.Length == 0)
            panel.Children.Add(new TextBlock { Text = "No other saved chats yet.", Margin = new Thickness(10) });
        _messageSecondaryFlyout?.Hide();
        _messageSecondaryFlyout = new HavenDropdown
        {
            Placement = PlacementMode.Left,
            Content = new HavenDropdownCard
            {
                Width = 360,
                Child = new ScrollViewer { MaxHeight = 430, Content = panel }
            }
        };
        _messageSecondaryFlyout.ShowAt(anchor);
    }

    private async Task ContinueInExistingChatAsync(Conversation target, ChatMessage sourceMessage)
    {
        var messages = await _conversations.GetMessagesAsync(target.Id, CancellationToken.None);
        _conversation = target;
        _messages.Clear();
        _messages.AddRange(messages);
        InstructionBox.Text = sourceMessage.Content;
        _messageSecondaryFlyout?.Hide();
        _messageActionsFlyout?.Hide();
        RefreshMessages();
        FocusComposer();
    }

    private async Task DeleteMessageAsync(ChatMessage message)
    {
        try
        {
            await _conversations.DeleteMessageAsync(_conversation.Id, message.Id, CancellationToken.None);
            _messages.RemoveAll(item => item.Id == message.Id);
            RefreshMessages();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async Task SetStatusAsync(string status) =>
        await Dispatcher.UIThread.InvokeAsync(() => StatusText.Text = status);

    private static Conversation CreateConversation(
        HavenMode mode,
        Guid? containerId = null,
        Guid? lessonId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var kind = mode switch
        {
            HavenMode.Study when lessonId is not null => ConversationKind.LessonChat,
            HavenMode.Study => ConversationKind.QuickChat,
            HavenMode.Tasks => ConversationKind.Task,
            HavenMode.Studio => ConversationKind.StudioChat,
            _ => ConversationKind.Chat
        };
        if (mode == HavenMode.Study && lessonId is null)
        {
            containerId = null;
        }
        return new Conversation(
            Guid.NewGuid(),
            mode,
            kind,
            mode == HavenMode.Study ? "New study chat" : mode == HavenMode.Tasks ? "New task" : "New chat",
            containerId,
            lessonId,
            false,
            false,
            now,
            now);
    }

    private void Register(string name, Control control)
    {
        _bus.RegisterElement(name, control);
        _bus.WirePointerEvents(name, control);
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sendCancellation?.Cancel();
        _sendCancellation?.Dispose();
        foreach (var slot in _generatedSurfaces.Keys.ToArray()) RemoveGeneratedSurface(slot);
        AddButton.Dispose();
    }

    private enum MessageEditChoice
    {
        RestartHere,
        NewBranch,
        MemoryOnly
    }
}
