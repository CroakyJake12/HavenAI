using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.GenerativeUi;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// Production Haven-native conversation surface. Avalonia owns only the platform host/window/file-picker boundary;
/// visible Chat composition, editing, transcript rendering, menus and generated UI live in Haven scene elements.
/// </summary>
public sealed class NewChatPage : UserControl, IDisposable
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
    private readonly ChatHavenScene _scene;
    private readonly List<ChatMessage> _messages = [];
    private readonly HashSet<Guid> _streamingMessages = [];
    private readonly List<PromptDefinition> _activeInstructions = [];
    private readonly List<string> _attachedImages = [];
    private readonly Dictionary<string, string> _attachedContext = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskAttachmentContext _taskAttachments = new();
    private readonly Dictionary<Guid, string> _thinkingContent = [];
    private readonly Dictionary<Guid, long> _thinkingStartTick = [];
    private readonly Dictionary<Guid, long> _thinkingEndTick = [];
    private readonly Dictionary<(Guid MessageId, int SurfaceIndex), HavenGenUiSceneSurface> _generatedSurfaces = [];
    private readonly Dictionary<(Guid MessageId, int SurfaceIndex), Guid> _generatedInstanceIds = [];
    private readonly Dictionary<(Guid MessageId, int SurfaceIndex), string> _generatedSignatures = [];
    private IReadOnlyList<CapabilityDefinition> _availableCapabilities = [];
    private IReadOnlyList<ModeDefinition> _availableApps = [];
    private ModeDefinition? _modeDefinition;
    private Conversation _conversation;
    private AgentDefinition? _activeAgent;
    private ChatActionMode? _chatActionModeOverride;
    private GenerativeUiResponseMode? _chatGenerativeUiResponseModeOverride;
    private ModelDescriptor? _selectedModel;
    private string? _pendingInstruction;
    private CancellationTokenSource? _sendCancellation;
    private readonly DispatcherTimer _sendProgressTimer;
    private bool _isSending;
    private bool _isTaskMode;
    private bool _lastReportedHasStarted;
    private bool _disposed;
    private long _sendStartTick;

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

        _scene = new ChatHavenScene();
        AutomationProperties.SetAutomationId(this, "HavenNativeChatPage");
        AutomationProperties.SetName(this, "Haven-native Chat page");
        Scene = new HavenSceneControl { Root = _scene.Root };
        AutomationProperties.SetAutomationId(Scene, "HavenNativeChatScene");
        AutomationProperties.SetName(Scene, "Haven-native Chat");
        Content = Scene;
        WireScene();

        _sendProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _sendProgressTimer.Tick += (_, _) =>
        {
            if (!_isSending) return;
            var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - _sendStartTick);
            _scene.SetStatus(elapsed.TotalSeconds switch
            {
                < 15 => "Thinking…",
                < 30 => "Taking a moment…",
                < 60 => "Still working…",
                < 120 => "This is a complex request…",
                _ => $"Working for {(int)elapsed.TotalMinutes}m {(int)elapsed.Seconds}s…"
            });
        };

        RefreshVisualState();
        _ = InitialiseAsync();
    }

    public HavenSceneControl Scene { get; }

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

    public void ConfigureMode(ModeDefinition mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        if (HasStarted) throw new InvalidOperationException("A started conversation cannot be reassigned to another app.");
        _modeDefinition = mode;
        _isTaskMode = false;
        _conversation = CreateConversation(mode.BaseMode);
        _chatActionModeOverride = null;
        _chatGenerativeUiResponseModeOverride = null;
        _scene.SetComposerPlaceholder(mode.Key switch
        {
            "imagine" => "Describe an image, style or visual concept",
            "present" => "Describe the presentation you want to create",
            "data" => "Attach data or ask Haven to analyse it",
            "vision" => "Attach an image and ask what you want to inspect",
            "play" => "Describe what you want to play, build or explore",
            "translate" => "Paste text and name the target language",
            "launcher" => "Find an app, project, command or recent item",
            _ => $"Ask Haven {mode.Name}"
        });
        _scene.SetStatus(mode.Description);
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ConfigureTaskMode()
    {
        if (HasStarted) throw new InvalidOperationException("A started conversation cannot be reassigned to Tasks.");
        _isTaskMode = true;
        _modeDefinition = null;
        _conversation = CreateConversation(HavenMode.Tasks);
        _chatActionModeOverride = null;
        _chatGenerativeUiResponseModeOverride = null;
        _scene.SetComposerPlaceholder("Describe your task");
        _scene.SetStatus("Describe what you want Haven to complete. Haven will ask only for details that materially affect the result.");
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectModel(ModelDescriptor model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _selectedModel = model;
        _preferences.SetModelDefaults(model.Name, _preferences.DefaultEffort);
        _scene.SetStatus(null);
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
                _selectedModel = models.FirstOrDefault(model => model.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase)) ?? models.FirstOrDefault();
                _scene.SetStatus(_selectedModel is null ? "No local model is available." : null);
                ModelChanged?.Invoke(this, EventArgs.Empty);
                RefreshVisualState();
                TrySubmitPendingInstruction();
            });
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            await SetStatusAsync("Local models are unavailable. Start Ollama, then try again.");
        }
    }

    public void Submit(string instruction)
    {
        var pending = instruction?.Trim();
        if (string.IsNullOrWhiteSpace(pending)) return;
        _scene.Instruction.Text = pending;
        _pendingInstruction = pending;
        TrySubmitPendingInstruction();
    }

    public void FocusComposer() => Scene.FocusElement(_scene.Instruction);

    public void SetDraft(string instruction)
    {
        _scene.Instruction.Text = instruction ?? string.Empty;
        _scene.Instruction.PlaceCaretAtEnd();
        FocusComposer();
    }

    public void ShowAddMenu() => _scene.ShowAddMenu();

    public Task RegenerateLatestAsync()
    {
        var response = _messages.LastOrDefault(message => message.Role == MessageRole.Assistant);
        return response is null ? Task.CompletedTask : RegenerateResponseAsync(response, ResponseRegenerationMode.Here);
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

    public async Task StartFreshConversationAsync(HavenMode mode, Guid? containerId, Guid? lessonId = null)
    {
        ResetToFreshConversation(mode, containerId, lessonId);
        await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        NotifyFreshConversationReady();
    }

    public void SetAddCatalogue(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<CapabilityDefinition> capabilities,
        IReadOnlyList<PromptDefinition> instructions,
        IReadOnlyList<ModeDefinition> apps)
    {
        _availableCapabilities = capabilities ?? [];
        _availableApps = apps ?? [];
        _scene.SetCatalogue(agents ?? [], capabilities ?? [], instructions ?? [], apps ?? []);
    }

    public async Task LoadConversationAsync(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        _conversation = conversation;
        _chatActionModeOverride = null;
        _chatGenerativeUiResponseModeOverride = null;
        _messages.Clear();
        _messages.AddRange(await _conversations.GetMessagesAsync(conversation.Id, CancellationToken.None));
        RefreshMessages();
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
        FocusComposer();
    }

    public void ApplyAddSelection(AddMenuSelection selection)
    {
        switch (selection.Item)
        {
            case AgentDefinition agent:
                _activeAgent = agent;
                _scene.SetStatus($"{agent.Name} selected.");
                break;
            case CapabilityDefinition capability:
                AttachCapability(capability);
                break;
            case PromptDefinition instruction:
                if (_activeInstructions.All(item => item.Id != instruction.Id)) _activeInstructions.Add(instruction);
                _scene.SetStatus($"{instruction.Name} instruction added.");
                break;
            case ChatActionMode actionMode:
                _chatActionModeOverride = actionMode;
                _scene.SetStatus($"{ActionModeLabel(actionMode)} for this chat.");
                break;
            case GenerativeUiResponseMode responseMode:
                _chatGenerativeUiResponseModeOverride = responseMode;
                _scene.SetStatus($"{VisualResponseModeLabel(responseMode)} for this chat.");
                break;
            case ModeDefinition app:
                _taskAttachments.AttachApp(app);
                _scene.SetStatus($"{app.Name} attached to this chat.");
                RefreshAttachmentStatus();
                break;
        }
    }

    public bool IsCapabilityAttached(Guid capabilityId) => _taskAttachments.IsCapabilityAttached(capabilityId);

    public void ToggleCapability(CapabilityDefinition capability)
    {
        if (_taskAttachments.IsCapabilityAttached(capability.Id))
        {
            _taskAttachments.RemoveCapability(capability.Id);
            _scene.SetStatus($"{capability.Name} removed from this chat context.");
            RefreshAttachmentStatus();
            return;
        }
        AttachCapability(capability);
    }

    public void AttachSnapshot(TaskAttachmentSnapshot snapshot)
    {
        _taskAttachments.AttachSnapshot(snapshot);
        RefreshAttachmentStatus();
        _ = AddFilesAsync(snapshot.Files);
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
                    _taskAttachments.AttachFiles([path]);
                    added++;
                }
                continue;
            }
            try
            {
                var info = new FileInfo(path);
                if (info.Length > 2_000_000)
                {
                    _attachedContext[path] = $"Attached file: {info.Name} ({info.Length:N0} bytes; content omitted because it is too large).";
                    _taskAttachments.AttachFiles([path]);
                    added++;
                    continue;
                }
                var text = await File.ReadAllTextAsync(path, CancellationToken.None);
                _attachedContext[path] = $"Attached file {info.Name}:\n{text}";
                _taskAttachments.AttachFiles([path]);
                added++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _scene.SetStatus($"Could not attach {Path.GetFileName(path)}: {exception.Message}");
            }
        }
        if (added > 0)
        {
            _scene.SetStatus($"{added} file{(added == 1 ? "" : "s")} attached to this chat.");
            RefreshAttachmentStatus();
        }
    }

    public string? GetLastAssistantResponse() => _messages.LastOrDefault(message => message.Role == MessageRole.Assistant)?.Content;

    public async Task TogglePinAsync()
    {
        _conversation = _conversation with { IsPinned = !_conversation.IsPinned, UpdatedAt = DateTimeOffset.UtcNow };
        await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
        _scene.SetStatus(_conversation.IsPinned ? "Chat pinned." : "Chat unpinned.");
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
        if (_messages.Count > 0) await _conversations.DeleteConversationAsync(_conversation.Id, CancellationToken.None);
        StartFreshConversation();
    }

    public void ShowRenameFlyout()
    {
        _scene.ShowTextPrompt(
            "Rename chat",
            "Give this conversation a clear title.",
            _conversation.Title,
            "Rename chat",
            async title =>
            {
                _conversation = _conversation with { Title = title.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
                await _conversations.UpsertConversationAsync(_conversation, CancellationToken.None);
                ConversationStateChanged?.Invoke(this, EventArgs.Empty);
            });
    }

    public void ShowDeleteConfirmation()
    {
        _scene.ShowChoicePrompt(
            "Delete this chat?",
            "This permanently deletes the saved conversation and its messages.",
            [("Delete chat", () => _ = DeleteConversationAsync())]);
    }

    public void SetTemporary(bool isTemporary) =>
        _conversation = _conversation with { IsTemporary = isTemporary, UpdatedAt = DateTimeOffset.UtcNow };

    private void WireScene()
    {
        _bus.RegisterElement("Chat.Composer.Add", Scene);
        _bus.RegisterElement("Chat.Composer.Instruction", Scene);
        _bus.RegisterElement("Chat.Composer.Send", Scene);
        _bus.RegisterElement("Chat.Problems.Resolve", Scene);

        _scene.SendRequested += async (_, _) => await SubmitCurrentInstructionAsync();
        _scene.ResolveProblemsRequested += (_, _) =>
        {
            _bus.Fire("Chat.Problems.Resolve.Click");
            ShowResolveProblems();
        };
        _scene.AddActionSelected += (_, action) => AddActionSelected?.Invoke(this, action);
        _scene.CatalogItemSelected += (_, selection) =>
        {
            ApplyAddSelection(selection);
            AddCatalogItemSelected?.Invoke(this, selection);
        };
        _scene.MessageActionRequested += OnMessageActionRequested;
        _scene.MarkdownCodeActionRequested += OnMarkdownCodeActionRequested;
        Scene.InputSubmitted += OnInputSubmitted;
        Scene.PointerPressedOutside += _scene.HideAddMenu;
    }

    private async Task InitialiseAsync()
    {
        await SetStatusAsync("Connecting to local models…");
        await RefreshModelsAsync();
    }

    private void OnInputSubmitted(Input input)
    {
        if (ReferenceEquals(input, _scene.Instruction))
        {
            _ = SubmitCurrentInstructionAsync();
            return;
        }
        foreach (var surface in _generatedSurfaces.Values)
        {
            if (!surface.OwnsInput(input)) continue;
            _ = surface.SubmitInputAsync(input);
            return;
        }
    }

    private void ShowResolveProblems()
    {
        _scene.ShowChoicePrompt(
            "Resolve Problems",
            "Choose what is going wrong. Haven will prepare a focused recovery instruction for you to review and send.",
            [
                ("Hallucinating", () => SetRecoveryDraft("Stop and audit your previous responses in this chat for hallucinations. Re-check every factual claim against the information actually available, clearly separate verified facts from assumptions, correct anything unsupported, and ask for missing information instead of guessing.")),
                ("Looping", () => SetRecoveryDraft("You are repeating the same approach. Stop the loop, briefly identify what has already been attempted and why it did not work, then choose a materially different approach and continue from there without repeating earlier steps.")),
                ("Recurring Bug in Produced Code", () => SetRecoveryDraft("The produced code has a recurring bug. Reproduce or trace the failure, compare it with the previous attempted fixes, identify the underlying root cause, then propose the smallest complete correction and explain how to verify that the same bug no longer recurs. Do not repeat an earlier patch unchanged.")),
                ("Something Else", () => SetRecoveryDraft("Review this conversation for the unresolved problem. Summarise the observed symptoms and attempted fixes, identify the most likely root cause, state what evidence is still missing, and propose the safest next step without claiming success until it is verified."))
            ]);
    }

    private void SetRecoveryDraft(string text)
    {
        SetDraft(text);
        _scene.SetStatus("Review the recovery instruction, then send it when ready.");
    }

    private async Task SubmitCurrentInstructionAsync()
    {
        var instruction = _scene.Instruction.Text.Trim();
        if (_isSending || string.IsNullOrWhiteSpace(instruction)) return;
        if (_selectedModel is null)
        {
            _pendingInstruction = instruction;
            _scene.SetStatus("Connecting to the selected local model…");
            return;
        }

        _pendingInstruction = null;
        _scene.Instruction.Text = string.Empty;
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
                await Dispatcher.UIThread.InvokeAsync(() => ApplyStreamEvent(ChatStreamEvent.AssistantDelta(messageId, delta)));
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
                if (streamEvent.Kind == ChatStreamEventKind.AssistantDelta && streamEvent.MessageId is { } deltaMessageId)
                {
                    if (bufferedMessageId is not null && bufferedMessageId != deltaMessageId) await FlushDeltasAsync();
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
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            await SetStatusAsync("Haven could not complete that response: " + exception.Message);
        }
        finally
        {
            _sendProgressTimer.Stop();
            _sendCancellation?.Dispose();
            _sendCancellation = null;
            _isSending = false;
            await Dispatcher.UIThread.InvokeAsync(RefreshVisualState);
        }
    }

    private void ApplyStreamEvent(ChatStreamEvent streamEvent)
    {
        switch (streamEvent.Kind)
        {
            case ChatStreamEventKind.UserMessage when streamEvent.Message is not null:
                UpsertMessage(streamEvent.Message);
                RefreshMessages();
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
                RefreshMessages();
                break;
            case ChatStreamEventKind.AssistantDelta when streamEvent.MessageId is { } deltaId:
            {
                var index = _messages.FindIndex(message => message.Id == deltaId);
                if (index < 0) break;
                _messages[index] = _messages[index] with { Content = _messages[index].Content + (streamEvent.Delta ?? string.Empty) };
                RefreshMessage(_messages[index]);
                Dispatcher.UIThread.Post(_scene.ScrollToEnd, DispatcherPriority.Background);
                break;
            }
            case ChatStreamEventKind.ThinkingDelta when streamEvent.MessageId is { } thinkingId:
            {
                if (!_thinkingContent.ContainsKey(thinkingId))
                {
                    _thinkingContent[thinkingId] = string.Empty;
                    _thinkingStartTick[thinkingId] = Environment.TickCount64;
                }
                _thinkingContent[thinkingId] += streamEvent.Thinking ?? string.Empty;
                var message = _messages.FirstOrDefault(item => item.Id == thinkingId);
                if (message is not null) RefreshMessage(message);
                break;
            }
            case ChatStreamEventKind.AssistantCompleted when streamEvent.Message is not null:
                _streamingMessages.Remove(streamEvent.Message.Id);
                if (_thinkingStartTick.ContainsKey(streamEvent.Message.Id) && !_thinkingEndTick.ContainsKey(streamEvent.Message.Id))
                    _thinkingEndTick[streamEvent.Message.Id] = Environment.TickCount64;
                UpsertMessage(streamEvent.Message);
                RefreshMessage(streamEvent.Message);
                break;
            case ChatStreamEventKind.ToolActivity when streamEvent.ToolActivity is not null:
                _scene.SetStatus(streamEvent.ToolActivity.Detail);
                break;
            case ChatStreamEventKind.PreflightFailed:
                _scene.SetStatus(streamEvent.PreflightResult is { Missing.Count: > 0 } preflight
                    ? string.Join(" ", preflight.Missing.Select(item => item.Reason))
                    : "The selected model cannot complete this request.");
                break;
        }
    }

    private void RefreshMessages()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshMessages);
            return;
        }
        PruneGeneratedSurfaces();
        var sceneMessages = _messages.Select(BuildSceneMessage).ToArray();
        _scene.SyncMessages(sceneMessages);
        foreach (var message in _messages.Where(message => message.Role == MessageRole.Assistant)) RefreshGeneratedContent(message);
        if (_lastReportedHasStarted != HasStarted)
        {
            _lastReportedHasStarted = HasStarted;
            ConversationStateChanged?.Invoke(this, EventArgs.Empty);
        }
        Dispatcher.UIThread.Post(_scene.ScrollToEnd, DispatcherPriority.Background);
    }

    private void RefreshMessage(ChatMessage message)
    {
        _scene.UpdateMessage(BuildSceneMessage(message));
        if (message.Role == MessageRole.Assistant) RefreshGeneratedContent(message);
    }

    private ChatSceneMessage BuildSceneMessage(ChatMessage message)
    {
        var directive = message.Role == MessageRole.Assistant
            ? GenUiChatDirectiveParser.Parse(message.Content)
            : new GenUiChatDirectiveParseResult(message.Content, [], null, false);
        var display = directive.DisplayContent;
        if (message.Role == MessageRole.Assistant && string.IsNullOrWhiteSpace(display) && directive.HasDirective && directive.Requests.Count == 0)
            display = "Generating interactive content…";
        var thinking = string.Empty;
        if (_thinkingContent.TryGetValue(message.Id, out var content) && !string.IsNullOrWhiteSpace(content))
        {
            var elapsed = 0L;
            if (_thinkingStartTick.TryGetValue(message.Id, out var start))
            {
                var end = _thinkingEndTick.TryGetValue(message.Id, out var completed) ? completed : Environment.TickCount64;
                elapsed = Math.Max(0, (end - start) / 1000);
            }
            thinking = (elapsed > 0 ? $"Thought for {elapsed} seconds\n" : "Thinking…\n") + content;
        }
        return new ChatSceneMessage(
            message.Id,
            message.Role,
            display ?? string.Empty,
            message.AgentName ?? "Haven",
            _streamingMessages.Contains(message.Id),
            thinking);
    }

    private void RefreshGeneratedContent(ChatMessage message)
    {
        var directive = GenUiChatDirectiveParser.Parse(message.Content);
        var generated = new List<HavenElement>();
        if (_streamingMessages.Contains(message.Id) && directive.Requests.Count == 0 && (directive.HasDirective || ShouldExpectGeneratedSurface(message)))
        {
            var preview = new Text { Content = "Preparing interactive content…" };
            preview.SetValue(HavenProperties.Foreground, "TextSoft");
            generated.Add(preview);
        }
        for (var surfaceIndex = 0; surfaceIndex < directive.Requests.Count; surfaceIndex++)
        {
            try
            {
                var surface = GetOrCreateGeneratedSurface(message, surfaceIndex, directive.Requests[surfaceIndex]);
                generated.Add(surface.Root);
                if (surface.Document is { } document)
                {
                    foreach (var issue in GenUiDocumentQualityValidator.Validate(document))
                    {
                        var warning = new Text { Content = "GenUI self-check: " + issue.Message };
                        warning.SetValue(HavenProperties.Foreground, "Danger");
                        generated.Add(warning);
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                var warning = new Text { Content = $"Could not render {directive.Requests[surfaceIndex].TemplateKey}: {exception.Message}" };
                warning.SetValue(HavenProperties.Foreground, "Danger");
                generated.Add(warning);
            }
        }
        RemoveGeneratedSurfacesAfter(message.Id, directive.Requests.Count);
        if (!string.IsNullOrWhiteSpace(directive.Error))
        {
            var warning = new Text { Content = "Interactive content unavailable: " + directive.Error };
            warning.SetValue(HavenProperties.Foreground, "Danger");
            generated.Add(warning);
        }
        if (generated.Count == 0) _scene.ClearGeneratedContent(message.Id);
        else _scene.SetGeneratedContent(message.Id, generated);
    }

    private HavenGenUiSceneSurface GetOrCreateGeneratedSurface(ChatMessage message, int surfaceIndex, GenUiTemplateRequest request)
    {
        var slot = (message.Id, surfaceIndex);
        var signature = request.Signature;
        if (_generatedSurfaces.TryGetValue(slot, out var mounted)
            && _generatedSignatures.TryGetValue(slot, out var mountedSignature)
            && mountedSignature.Equals(signature, StringComparison.Ordinal))
            return mounted;

        if (_generatedSurfaces.Remove(slot, out var previousSurface)) previousSurface.Dispose();
        GenUiDocument? document = null;
        var reuse = _generatedSignatures.TryGetValue(slot, out var previousSignature)
                    && previousSignature.Equals(signature, StringComparison.Ordinal)
                    && _generatedInstanceIds.TryGetValue(slot, out var existingInstanceId)
                    && (document = _genUiInstances.TryGet(existingInstanceId)) is not null;

        var surface = new HavenGenUiSceneSurface(_genUiRouter, _genUiInstances);
        if (reuse)
        {
            surface.PresentExisting(document!);
        }
        else
        {
            if (_generatedInstanceIds.Remove(slot, out var obsoleteInstance)) _genUiInstances.Remove(obsoleteInstance);
            var appKey = _modeDefinition?.Key ?? (_isTaskMode ? "tasks" : "chat");
            document = CreateGeneratedDocument(request, appKey);
            if (!string.IsNullOrWhiteSpace(request.AccentKey)) document = document with { AccentKey = request.AccentKey };
            surface.Present(document);
        }
        _generatedSurfaces[slot] = surface;
        _generatedInstanceIds[slot] = document!.Origin.InstanceId;
        _generatedSignatures[slot] = signature;
        return surface;
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
        foreach (var slot in _generatedSurfaces.Keys.Where(slot => !messageIds.Contains(slot.MessageId)).ToArray()) RemoveGeneratedSurface(slot);
    }

    private void RemoveGeneratedSurfacesAfter(Guid messageId, int keepCount)
    {
        foreach (var slot in _generatedSurfaces.Keys.Where(slot => slot.MessageId == messageId && slot.SurfaceIndex >= keepCount).ToArray())
            RemoveGeneratedSurface(slot);
    }

    private void RemoveGeneratedSurface((Guid MessageId, int SurfaceIndex) slot)
    {
        if (_generatedSurfaces.Remove(slot, out var surface)) surface.Dispose();
        if (_generatedInstanceIds.Remove(slot, out var instanceId)) _genUiInstances.Remove(instanceId);
        _generatedSignatures.Remove(slot);
    }

    private void ClearGeneratedSurfaces()
    {
        foreach (var slot in _generatedSurfaces.Keys.ToArray()) RemoveGeneratedSurface(slot);
        _generatedInstanceIds.Clear();
        _generatedSignatures.Clear();
    }

    private bool ShouldExpectGeneratedSurface(ChatMessage assistantMessage)
    {
        var responseMode = _chatGenerativeUiResponseModeOverride ?? GenerativeUiResponseMode.Auto;
        if (responseMode == GenerativeUiResponseMode.AlwaysVisual) return true;
        var index = _messages.FindIndex(message => message.Id == assistantMessage.Id);
        var prompt = index > 0 ? _messages.Take(index).LastOrDefault(message => message.Role == MessageRole.User)?.Content : null;
        if (string.IsNullOrWhiteSpace(prompt)) return responseMode == GenerativeUiResponseMode.PreferVisual;
        string[] terms = ["generative ui", "generate ui", "generated ui", "interactive ui", "flashcard", "whiteboard", "dashboard", "calculator", "data grid", "interactive form", "quiz", "assessment", "workflow", "task list", "graph", "chart", "visual response"];
        return terms.Any(term => prompt.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private async void OnMessageActionRequested(object? sender, ChatMessageActionRequest request)
    {
        var message = _messages.FirstOrDefault(item => item.Id == request.MessageId);
        if (message is null) return;
        switch (request.Action)
        {
            case ChatMessageAction.Copy:
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(message.Content);
                break;
            case ChatMessageAction.Delete:
                await DeleteMessageAsync(message);
                break;
            case ChatMessageAction.Forget:
                await _conversations.MarkMessagesCompactedAsync(_conversation.Id, [message.Id], CancellationToken.None);
                _messages.RemoveAll(item => item.Id == message.Id);
                RefreshMessages();
                break;
            case ChatMessageAction.Regenerate:
                ShowRegenerateChoices(message);
                break;
            case ChatMessageAction.Branch:
                ShowBranchChoices(message);
                break;
            case ChatMessageAction.Edit:
                ShowEditChoices(message);
                break;
        }
    }

    private async void OnMarkdownCodeActionRequested(object? sender, ChatMarkdownCodeActionRequest request)
    {
        if (request.Request.Action == Haven.UI.Components.MarkdownCodeAction.Copy)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(request.Request.Code);
            return;
        }
        var verb = request.Request.Action == Haven.UI.Components.MarkdownCodeAction.AskToRun ? "Run" : "Apply";
        var language = string.IsNullOrWhiteSpace(request.Request.Language) ? string.Empty : request.Request.Language;
        SetDraft($"{verb} this code safely and explain the result:\n```{language}\n{request.Request.Code}\n```");
    }

    private void ShowRegenerateChoices(ChatMessage message)
    {
        _scene.ShowChoicePrompt(
            "Re-generate response",
            $"Current model: {_selectedModel?.Name ?? "No model"}",
            [
                ("Re-generate in current chat", () => _ = RegenerateResponseAsync(message, ResponseRegenerationMode.Here)),
                ("Re-generate in branch", () => _ = RegenerateResponseAsync(message, ResponseRegenerationMode.NewBranch))
            ]);
    }

    private void ShowBranchChoices(ChatMessage message)
    {
        _scene.ShowChoicePrompt(
            "Branch message",
            "Continue from this point without changing the current conversation.",
            [
                ("Branch in new chat", () => _ = BranchIntoNewChatAsync(message)),
                ("Branch in existing chat", () => _ = ShowExistingChatPickerAsync(message))
            ]);
    }

    private void ShowEditChoices(ChatMessage message)
    {
        _scene.ShowChoicePrompt(
            "Edit message",
            "Choose how this edit should affect conversation history.",
            [
                ("Restart from here", () => ShowMessageEditor(message, MessageEditChoice.RestartHere)),
                ("Edit in new branch", () => ShowMessageEditor(message, MessageEditChoice.NewBranch)),
                ("Edit in memory only", () => ShowMessageEditor(message, MessageEditChoice.MemoryOnly))
            ]);
    }

    private void ShowMessageEditor(ChatMessage message, MessageEditChoice choice)
    {
        _scene.ShowTextPrompt(
            "Edit message",
            choice == MessageEditChoice.MemoryOnly ? "This edit changes only the current in-memory session." : "The conversation versioning service will preserve history semantics.",
            message.Content,
            choice == MessageEditChoice.MemoryOnly ? "Apply for this session" : "Apply edit",
            content => ApplyMessageEditAsync(message, content, choice));
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
            await _versioning.PrepareRegenerationAsync(_conversation.Id, message.Id, isLatest, mode, CancellationToken.None);
            var persisted = await _conversations.GetMessagesAsync(_conversation.Id, CancellationToken.None);
            _messages.Clear();
            _messages.AddRange(persisted);
            RefreshMessages();
            Submit(precedingUser.Content);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            _scene.SetStatus(exception.Message);
        }
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
                    choice == MessageEditChoice.NewBranch ? MessageEditMode.NewBranch : MessageEditMode.OverwriteCurrentBranch,
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
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            _scene.SetStatus(exception.Message);
        }
    }

    private async Task BranchIntoNewChatAsync(ChatMessage throughMessage)
    {
        var index = _messages.FindIndex(message => message.Id == throughMessage.Id);
        if (index < 0) return;
        var source = _conversation;
        var now = DateTimeOffset.UtcNow;
        var branch = new Conversation(
            Guid.NewGuid(), source.Mode, source.Kind, $"Branch of {source.Title}", source.ContainerId, source.LessonId,
            false, source.IsTemporary, now, now, ParentConversationId: source.Id);
        var copies = _messages.Take(index + 1)
            .Select((message, order) => message with { Id = Guid.NewGuid(), ConversationId = branch.Id, CreatedAt = now.AddTicks(order) })
            .ToArray();
        await _conversations.UpsertConversationAsync(branch, CancellationToken.None);
        foreach (var copy in copies) await _conversations.AddMessageAsync(copy, CancellationToken.None);
        _conversation = branch;
        _messages.Clear();
        _messages.AddRange(copies);
        ClearGeneratedSurfaces();
        RefreshMessages();
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
        FocusComposer();
    }

    private async Task ShowExistingChatPickerAsync(ChatMessage sourceMessage)
    {
        var recent = (await _conversations.GetRecentAsync(_conversation.Mode, 12, CancellationToken.None))
            .Where(item => item.Id != _conversation.Id && !item.IsArchived)
            .ToArray();
        var choices = recent
            .Select<Conversation, (string Label, Action Action)>(conversation =>
                (string.IsNullOrWhiteSpace(conversation.Title) ? "Untitled chat" : conversation.Title,
                    () => _ = ContinueInExistingChatAsync(conversation, sourceMessage)))
            .ToArray();
        if (choices.Length == 0)
        {
            _scene.SetStatus("No other saved chats yet.");
            return;
        }
        _scene.ShowChoicePrompt("Choose an existing chat", "The selected message will be placed in the composer of that chat.", choices);
    }

    private async Task ContinueInExistingChatAsync(Conversation target, ChatMessage sourceMessage)
    {
        var messages = await _conversations.GetMessagesAsync(target.Id, CancellationToken.None);
        _conversation = target;
        _messages.Clear();
        _messages.AddRange(messages);
        ClearGeneratedSurfaces();
        RefreshMessages();
        SetDraft(sourceMessage.Content);
        ConversationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task DeleteMessageAsync(ChatMessage message)
    {
        try
        {
            await _conversations.DeleteMessageAsync(_conversation.Id, message.Id, CancellationToken.None);
            _messages.RemoveAll(item => item.Id == message.Id);
            RefreshMessages();
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or IOException)
        {
            _scene.SetStatus(exception.Message);
        }
    }

    private void ResetToFreshConversation(HavenMode mode, Guid? containerId, Guid? lessonId)
    {
        _sendCancellation?.Cancel();
        _conversation = CreateConversation(mode, containerId, lessonId);
        _activeAgent = null;
        _activeInstructions.Clear();
        _chatActionModeOverride = null;
        _chatGenerativeUiResponseModeOverride = null;
        _attachedImages.Clear();
        _attachedContext.Clear();
        _taskAttachments.Clear();
        _messages.Clear();
        _streamingMessages.Clear();
        _thinkingContent.Clear();
        _thinkingStartTick.Clear();
        _thinkingEndTick.Clear();
        _pendingInstruction = null;
        ClearGeneratedSurfaces();
        _scene.SetStatus(null);
        RefreshAttachmentStatus();
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
        FocusComposer();
    }

    private void AttachCapability(CapabilityDefinition capability)
    {
        var owner = _availableApps.FirstOrDefault(app => app.Key.Equals(capability.OwnerAppKey, StringComparison.OrdinalIgnoreCase));
        _taskAttachments.AttachCapability(capability, owner);
        _scene.SetStatus($"{capability.Name} attached as chat relevance; permissions are unchanged.");
        RefreshAttachmentStatus();
    }

    private void RefreshAttachmentStatus()
    {
        if (_taskAttachments.IsEmpty)
        {
            _scene.SetAttachmentStatus(null);
            return;
        }
        var parts = new List<string>();
        if (_taskAttachments.Apps.Count > 0) parts.Add("Apps: " + string.Join(", ", _taskAttachments.Apps.Select(item => item.Name)));
        if (_taskAttachments.Capabilities.Count > 0) parts.Add("Capabilities: " + string.Join(", ", _taskAttachments.Capabilities.Select(item => item.Name)));
        if (_taskAttachments.Files.Count > 0) parts.Add("Files: " + string.Join(", ", _taskAttachments.Files.Select(Path.GetFileName)));
        _scene.SetAttachmentStatus(string.Join("  •  ", parts));
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

    private string? BuildRegisteredContext()
    {
        var sections = new List<string>();
        if (_modeDefinition is { } mode)
        {
            sections.Add($"Active Haven app: {mode.Name}.\nPurpose: {mode.Description}");
            if (!string.IsNullOrWhiteSpace(mode.SystemPromptSuffix)) sections.Add(mode.SystemPromptSuffix.Trim());
        }
        if (_taskAttachments.BuildAppContext() is { } appContext) sections.Add(appContext);
        if (_taskAttachments.BuildCapabilityContext() is { } capabilityContext) sections.Add(capabilityContext);
        sections.AddRange(_attachedContext.Values.Where(item => !string.IsNullOrWhiteSpace(item)));
        sections.Add(GenUiChatDirectiveParser.ModelInstructionFor(_chatGenerativeUiResponseModeOverride ?? GenerativeUiResponseMode.Auto));
        return sections.Count == 0 ? null : string.Join("\n\n", sections);
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
        _scene.Instruction.Text = _pendingInstruction;
        _pendingInstruction = null;
        _ = SubmitCurrentInstructionAsync();
    }

    private void RefreshVisualState()
    {
        _scene.SetSending(_isSending, _selectedModel is not null);
        RefreshMessages();
    }

    private async Task SetStatusAsync(string status) =>
        await Dispatcher.UIThread.InvokeAsync(() => _scene.SetStatus(string.IsNullOrWhiteSpace(status) ? null : status));

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

    private static Conversation CreateConversation(HavenMode mode, Guid? containerId = null, Guid? lessonId = null)
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
        if (mode == HavenMode.Study && lessonId is null) containerId = null;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sendCancellation?.Cancel();
        _sendCancellation?.Dispose();
        _sendProgressTimer.Stop();
        Scene.InputSubmitted -= OnInputSubmitted;
        Scene.PointerPressedOutside -= _scene.HideAddMenu;
        ClearGeneratedSurfaces();
        _scene.Dispose();
    }

    private enum MessageEditChoice
    {
        RestartHere,
        NewBranch,
        MemoryOnly
    }
}
