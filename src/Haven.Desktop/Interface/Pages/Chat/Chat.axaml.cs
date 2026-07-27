using System.Text;
using Avalonia;
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
    private readonly List<ChatMessage> _messages = [];
    private readonly HashSet<Guid> _streamingMessages = [];
    private readonly List<PluginDefinition> _activePlugins = [];
    private readonly List<PromptDefinition> _activeInstructions = [];
    private readonly List<string> _attachedImages = [];
    private readonly List<string> _attachedContext = [];
    private readonly Dictionary<Guid, MarkdownView> _messageBodies = [];
    private Conversation _conversation;
    private AgentDefinition? _activeAgent;
    private ModelDescriptor? _selectedModel;
    private string? _pendingInstruction;
    private CancellationTokenSource? _sendCancellation;
    private Flyout? _resolveProblemsFlyout;
    private Flyout? _messageActionsFlyout;
    private Flyout? _messageSecondaryFlyout;
    private bool _isSending;
    private bool _lastReportedHasStarted;
    private bool _disposed;

    public NewChatPage(
        HavenEventBus bus,
        IConversationRepository conversations,
        IOllamaClient ollama,
        ChatSessionService sessions,
        IConversationVersioningService versioning,
        UserPreferencesService preferences)
    {
        _bus = bus;
        _conversations = conversations;
        _ollama = ollama;
        _sessions = sessions;
        _versioning = versioning;
        _preferences = preferences;
        _conversation = CreateConversation(HavenMode.Chat);

        InitializeComponent();
        WireEvents();
        RefreshVisualState();
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

    public void StartFreshConversation(Guid? chatGroupId = null)
    {
        ResetToFreshConversation(HavenMode.Chat, chatGroupId, null);
        _ = PersistFreshConversationAsync(_conversation);
        NotifyFreshConversationReady();
    }

    public async Task StartFreshConversationAsync(Guid? chatGroupId = null)
    {
        ResetToFreshConversation(HavenMode.Chat, chatGroupId, null);
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
        FocusComposer();
    }

    public void SetAddCatalogue(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<PluginDefinition> plugins,
        IReadOnlyList<PromptDefinition> instructions,
        IReadOnlyList<ModeDefinition> apps) =>
        AddButton.SetCatalogue(agents, plugins, instructions, apps);

    public async Task LoadConversationAsync(Conversation conversation)
    {
        _conversation = conversation;
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
                StatusText.Text = $"{agent.Name} selected.";
                break;
            case PluginDefinition plugin:
                if (_activePlugins.All(item => item.Id != plugin.Id)) _activePlugins.Add(plugin);
                StatusText.Text = $"{plugin.Name} added.";
                break;
            case PromptDefinition instruction:
                if (_activeInstructions.All(item => item.Id != instruction.Id)) _activeInstructions.Add(instruction);
                StatusText.Text = $"{instruction.Name} instruction added.";
                break;
        }
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
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) continue;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif")
            {
                _attachedImages.Add(path);
                continue;
            }
            try
            {
                var info = new FileInfo(path);
                if (info.Length > 2_000_000)
                {
                    _attachedContext.Add($"Attached file: {info.Name} ({info.Length:N0} bytes; content omitted because it is too large)." );
                    continue;
                }
                var text = await File.ReadAllTextAsync(path, CancellationToken.None);
                _attachedContext.Add($"Attached file {info.Name}:\n{text}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusText.Text = $"Could not attach {Path.GetFileName(path)}.";
            }
        }
        if (files.Count > 0)
            StatusText.Text = $"{files.Count} file{(files.Count == 1 ? "" : "s")} added.";
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
        var editor = new TextBox
        {
            Text = _conversation.Title,
            MinWidth = 300,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold
        };
        var save = new Button
        {
            Content = "Rename chat",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var flyout = new Flyout
        {
            Placement = PlacementMode.Top,
            Content = new StackPanel
            {
                Width = 260,
                Spacing = 3,
                Margin = new Thickness(12),
                Children =
                {
                    new TextBlock { Text = "Rename chat", FontSize = 20, FontWeight = FontWeight.ExtraBold, Margin = new Thickness(10, 5, 10, 8) },
                    editor,
                    save
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
        var cancel = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Stretch };
        var delete = new Button { Content = "Delete chat", HorizontalAlignment = HorizontalAlignment.Stretch };
        delete.Classes.Add("negative");
        var flyout = new Flyout
        {
            Placement = PlacementMode.Top,
            Content = new StackPanel
            {
                Width = 260,
                Spacing = 3,
                Margin = new Thickness(12),
                Children =
                {
                    new TextBlock { Text = "Delete this chat?", FontSize = 20, FontWeight = FontWeight.ExtraBold, Margin = new Thickness(10, 5, 10, 8) },
                    new TextBlock { Text = "This permanently removes the conversation and its messages.", TextWrapping = TextWrapping.Wrap },
                    delete,
                    cancel
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

        _resolveProblemsFlyout = new Flyout
        {
            Placement = PlacementMode.Top,
            Content = panel
        };
        _resolveProblemsFlyout.ShowAt(ResolveProblemsButton);
    }

    private Button BuildProblemResolutionAction(
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

        var button = new Button
        {
            Content = grid,
            MinHeight = 48,
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        button.Classes.Add("sidebar");
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
            StatusText.Text = "Connecting to the selected local modelâ€¦";
            return;
        }

        _pendingInstruction = null;
        InstructionBox.Text = string.Empty;
        _bus.Fire("Chat.Composer.Send.Click");
        _isSending = true;
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
                               _activePlugins.Select(item => new ActivePlugin(item.Name, item.IconKey, item.Persists, item.Instructions)).ToArray(),
                               _activeAgent?.Name ?? "Haven",
                               _activeAgent?.Instructions ?? string.Empty,
                               DuoMode.Solo,
                               null,
                               null,
                               null,
                               _attachedImages.Count == 0 ? null : _attachedImages.ToArray(),
                               _sendCancellation.Token,
                               prompts: _activeInstructions.Select(item => new ActivePrompt(item.Name, item.IconKey, item.Persists, item.Instructions)).ToArray(),
                               registeredContext: _attachedContext.Count == 0 ? null : string.Join("\n\n", _attachedContext),
                               generationOptions: _preferences.GenerationOptions,
                               filePermission: _preferences.FilePermission,
                               commandPermission: _preferences.CommandPermission,
                               browserPermission: _preferences.BrowserPermission).ConfigureAwait(false))
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
            _sendCancellation.Dispose();
            _sendCancellation = null;
            _isSending = false;
            await Dispatcher.UIThread.InvokeAsync(RefreshVisualState);
        }
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
                        body.Markdown = _messages[index].Content;
                        rebuildMessages = false;
                        Dispatcher.UIThread.Post(() => MessagesScroll.ScrollToEnd(), DispatcherPriority.Background);
                    }
                }
                break;
            case ChatStreamEventKind.AssistantCompleted when streamEvent.Message is not null:
                _streamingMessages.Remove(streamEvent.Message.Id);
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
        foreach (var message in _messages)
            MessagesPanel.Children.Add(BuildMessage(message, _streamingMessages.Contains(message.Id)));

        ResolveProblemsButton.IsVisible = HasStarted;
        if (_lastReportedHasStarted != HasStarted)
        {
            _lastReportedHasStarted = HasStarted;
            ConversationStateChanged?.Invoke(this, EventArgs.Empty);
        }

        Dispatcher.UIThread.Post(() => MessagesScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    private Control BuildMessage(ChatMessage message, bool isStreaming)
    {
        var content = new StackPanel { Spacing = 9 };
        content.Children.Add(new TextBlock
        {
            Text = message.Role == MessageRole.User ? "You" : "Haven",
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            Foreground = ResourceBrush("HavenTextBrush", Colors.Black)
        });
        var messageBody = new MarkdownView
        {
            Markdown = string.IsNullOrEmpty(message.Content) && isStreaming ? "Thinking…" : message.Content,
            Foreground = ResourceBrush("HavenTextBrush", Colors.Black)
        };
        _messageBodies[message.Id] = messageBody;
        content.Children.Add(messageBody);

        var bubble = new Border
        {
            Child = content,
            MaxWidth = message.Role == MessageRole.User ? 680 : 900,
            HorizontalAlignment = message.Role == MessageRole.User
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Stretch,
            Background = message.Role == MessageRole.User
                ? ResourceBrush("HavenAccentSoftBrush", Color.Parse("#FFE0F7FA"))
                : new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(45, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(22, 18)
        };

        var more = new Button
        {
            Width = 48,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(12, 7),
            Opacity = 0,
            IsHitTestVisible = false,
            HorizontalAlignment = message.Role == MessageRole.User
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left,
            Content = new HavenIcon
            {
                IconKey = "more",
                Width = 22,
                Height = 12,
                Foreground = ResourceBrush("HavenTextBrush", Colors.Black)
            }
        };
        more.Classes.Add("chip");
        more.Click += (_, _) => ShowMessageActions(more, message);
        bubble.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(bubble).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed) return;
            args.Handled = true;
            ShowMessageActions(bubble, message);
        };

        var messageHost = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = message.Role == MessageRole.User
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Stretch,
            Children = { bubble, more }
        };
        var hoverHost = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(26, 10),
            Margin = new Thickness(-26, -10),
            HorizontalAlignment = message.Role == MessageRole.User
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Stretch,
            Child = messageHost
        };
        hoverHost.PointerEntered += (_, _) =>
        {
            more.Opacity = 1;
            more.IsHitTestVisible = true;
        };
        hoverHost.PointerExited += (_, _) =>
        {
            more.Opacity = 0;
            more.IsHitTestVisible = false;
        };
        return hoverHost;
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

        _messageActionsFlyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = new StackPanel
            {
                Width = 260,
                Spacing = 3,
                Margin = new Thickness(12),
                Children = { edit, copy, branch, delete }
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

        _messageActionsFlyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = new StackPanel
            {
                Width = 260,
                Spacing = 3,
                Margin = new Thickness(12),
                Children = { regenerate, copy, branch, forget }
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

        var modelChip = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = ResourceBrush("HavenPanel2Brush", Color.Parse("#FFF4F4F4")),
            CornerRadius = new CornerRadius(999),
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
        _messageSecondaryFlyout = new Flyout
        {
            Placement = PlacementMode.Right,
            Content = new StackPanel
            {
                Width = 300,
                Spacing = 2,
                Margin = new Thickness(8),
                Children = { current, branch, modelChip }
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

    private static Button BuildMessageAction(string icon, string label, bool dangerous = false)
    {
        var foreground = dangerous
            ? ResourceBrush("HavenDangerBrush", Color.Parse("#FFD32F2F"))
            : ResourceBrush("HavenTextBrush", Colors.Black);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        var glyph = new HavenIcon
        {
            IconKey = icon,
            Width = 20,
            Height = 20,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center
        };
        var text = new TextBlock
        {
            Text = label,
            FontSize = 14,
            FontWeight = FontWeight.ExtraBold,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(glyph);
        grid.Children.Add(text);

        var button = new Button
        {
            Content = grid,
            MinHeight = 48,
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        button.Classes.Add("sidebar");
        if (dangerous) button.Classes.Add("danger");
        return button;
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

    private void ShowSecondaryFlyout(Control anchor, IReadOnlyList<Button> actions)
    {
        _messageSecondaryFlyout?.Hide();
        var panel = new StackPanel { Width = 260, Spacing = 3, Margin = new Thickness(12) };
        foreach (var action in actions) panel.Children.Add(action);
        _messageSecondaryFlyout = new Flyout { Placement = PlacementMode.Left, Content = panel };
        _messageSecondaryFlyout.ShowAt(anchor);
    }

    private void ShowMessageEditor(Control anchor, ChatMessage message, MessageEditChoice choice)
    {
        var editor = new TextBox
        {
            Text = message.Content,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 360,
            MinHeight = 120,
            MaxHeight = 300
        };
        var apply = new Button
        {
            Content = choice == MessageEditChoice.MemoryOnly ? "Apply for this session" : "Apply edit",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        var editorFlyout = new Flyout
        {
            Placement = PlacementMode.Left,
            Content = new StackPanel
            {
                Width = 390,
                Spacing = 10,
                Margin = new Thickness(12),
                Children =
                {
                    new TextBlock { Text = "Edit message", FontSize = 20, FontWeight = FontWeight.ExtraBold, Margin = new Thickness(10, 5, 10, 8) },
                    editor,
                    apply
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
        _messageSecondaryFlyout = new Flyout
        {
            Placement = PlacementMode.Left,
            Content = new ScrollViewer { MaxHeight = 430, Content = panel }
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
            HavenMode.Teach when lessonId is not null => ConversationKind.LessonChat,
            HavenMode.Teach => ConversationKind.QuickChat,
            HavenMode.Do => ConversationKind.Task,
            HavenMode.Studio => ConversationKind.StudioChat,
            _ => ConversationKind.Chat
        };
        if (mode == HavenMode.Teach && lessonId is null)
        {
            containerId = null;
        }
        return new Conversation(
            Guid.NewGuid(),
            mode,
            kind,
            mode == HavenMode.Teach ? "New study chat" : mode == HavenMode.Do ? "New research" : "New chat",
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
        AddButton.Dispose();
    }

    private enum MessageEditChoice
    {
        RestartHere,
        NewBranch,
        MemoryOnly
    }
}
