using Haven.Core;
using Haven.Desktop.HavenUI.Tokens;
using Haven.Desktop.Prefabs;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Chat;

internal enum ChatMessageAction
{
    Edit,
    Copy,
    Branch,
    Delete,
    Regenerate,
    Forget
}

internal sealed record ChatMessageActionRequest(Guid MessageId, ChatMessageAction Action);
internal sealed record ChatMarkdownCodeActionRequest(Guid MessageId, Haven.UI.Components.MarkdownCodeActionRequest Request);

internal sealed record ChatSceneMessage(
    Guid Id,
    MessageRole Role,
    string Content,
    string AgentName,
    bool IsStreaming,
    string Thinking,
    IReadOnlyList<ToolActivity>? ToolActivities = null);

/// <summary>
/// One persisted conversation context row projected into the collapsible Context card.
/// </summary>
internal sealed record ChatSceneContextEntry(Guid EntryId, string CategoryLabel, string Title, string Preview, bool IsRemovable);

/// <summary>
/// Canonical Haven-native Chat presentation. App/domain state remains owned by NewChatPage and application services;
/// this class only projects that state into Prefab/DynamicUI scene elements and emits semantic UI intent.
/// </summary>
internal sealed partial class ChatHavenScene : IDisposable
{
    private readonly HavenPrefabCatalog _prefabs;
    private readonly HavenDynamicUITemplateCatalog _templates;
    private readonly DynamicUI _dynamicUi;
    private readonly Dictionary<Guid, DynamicUIItem> _messageItems = [];
    private readonly Dictionary<Guid, DynamicUIItem> _toolItems = [];
    private readonly Dictionary<Guid, Guid> _toolOwners = [];
    private readonly Dictionary<Guid, IReadOnlyList<HavenElement>> _generatedContent = [];
    private readonly ChatAddMenuSurface _addMenu;
    private PopupMenu? _activeMessagePopup;
    private bool _isSending;
    private bool _safetyLocked;
    private bool _isContextExpanded;
    private bool _contextDismissed;
    private bool _disposed;

    public ChatHavenScene()
    {
        _prefabs = HavenPrefabCatalog.FromAssembly(typeof(ChatHavenScene).Assembly);
        _templates = HavenDynamicUITemplateCatalog.FromAssembly(typeof(ChatHavenScene).Assembly);
        Root = BuildRoot();
        _dynamicUi = new DynamicUI(Root, _templates, _prefabs);
        Chatbox = Root.DescendantsAndSelf().OfType<Prefab>().Single(prefab => prefab.PrefabID == "Chatbox");
        ChatboxRoot = Chatbox.GetComponent<Container>("ChatboxRoot");
        AttachmentChips = Chatbox.GetComponent<Container>("AttachmentChips");
        ComposerRow = Chatbox.GetComponent<Container>("ComposerRow");
        InstructionViewport = Chatbox.GetComponent<Container>("InstructionViewport");
        Instruction = Chatbox.GetComponent<Input>("Instruction");
        AddButton = Chatbox.GetComponent<HavenButton>("AddMenu");
        SendButton = Chatbox.GetComponent<HavenButton>("Send");
        SendIcon = Chatbox.GetComponent<Icon>("SendIcon");
        Messages = (DynamicUIRuntime)Root.DescendantsAndSelf().Single(element => element.Name == "Messages");
        EmptyState = Root.DescendantsAndSelf().Single(element => element.Name == "EmptyState");
        Status = (HavenText)Root.DescendantsAndSelf().Single(element => element.Name == "Status");
        AttachmentStatus = (HavenText)Root.DescendantsAndSelf().Single(element => element.Name == "AttachmentStatus");
        ManageAttachments = (HavenButton)Root.DescendantsAndSelf().Single(element => element.Name == "ManageAttachments");
        TaskActions = (Container)Root.DescendantsAndSelf().Single(element => element.Name == "TaskActions");
        NewTask = (HavenButton)Root.DescendantsAndSelf().Single(element => element.Name == "NewTask");
        TaskHistory = (HavenButton)Root.DescendantsAndSelf().Single(element => element.Name == "TaskHistory");
        ResolveProblems = (HavenButton)Root.DescendantsAndSelf().Single(element => element.Name == "ResolveProblems");
        ContextCard = Root.DescendantsAndSelf().OfType<Container>().Single(element => element.Name == "ContextCard");
        ContextToggle = (HavenButton)Root.DescendantsAndSelf().Single(element => element.Name == "ContextToggle");
        ContextSummaryText = (HavenText)Root.DescendantsAndSelf().Single(element => element.Name == "ContextSummary");
        ContextClose = (HavenButton)Root.DescendantsAndSelf().Single(element => element.Name == "ContextClose");
        ContextBody = (Container)Root.DescendantsAndSelf().Single(element => element.Name == "ContextBody");

        ContextCard.SetValue(HavenProperties.Background, "SurfaceRaised");
        ContextCard.SetValue(HavenProperties.BorderColor, "Border");
        ContextCard.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        ContextCard.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));

        AddMenuPrefab = _prefabs.Create("ChatAddMenu", "Chat-AddMenu");
        AddMenuPrefab.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        AddMenuPrefab.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        AddMenuPrefab.SetValue(HavenProperties.ZIndex, 100);
        AddMenuPrefab.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.ChildrenOnly);
        Root.Add(AddMenuPrefab);
        _addMenu = new ChatAddMenuSurface(AddMenuPrefab, composerOwnsSearch: true, showThreadSettings: false);

        AddButton.Invoked += OnAddInvoked;
        SendButton.Invoked += OnSendInvoked;
        ResolveProblems.Invoked += OnResolveInvoked;
        ContextToggle.Invoked += OnContextToggleInvoked;
        ContextClose.Invoked += OnContextCloseInvoked;
        ManageAttachments.Invoked += (_, _) => ManageAttachmentsRequested?.Invoke(this, EventArgs.Empty);
        NewTask.Invoked += (_, _) => NewTaskRequested?.Invoke(this, EventArgs.Empty);
        TaskHistory.Invoked += (_, _) => TaskHistoryRequested?.Invoke(this, EventArgs.Empty);
        _addMenu.AddActionSelected += OnSharedAddActionSelected;
        _addMenu.CatalogItemSelected += OnSharedCatalogItemSelected;
        SetHasMessages(false);
        HideAddMenu();
    }

    public Page Root { get; }
    public Prefab Chatbox { get; }
    public Container ChatboxRoot { get; }
    public Container AttachmentChips { get; }
    public Container ComposerRow { get; }
    public Container InstructionViewport { get; }
    public Input Instruction { get; }
    public HavenButton AddButton { get; }
    public HavenButton SendButton { get; }
    public Icon SendIcon { get; }
    public DynamicUIRuntime Messages { get; }
    public HavenElement EmptyState { get; }
    public HavenText Status { get; }
    public HavenText AttachmentStatus { get; }
    public HavenButton ManageAttachments { get; }
    public Container TaskActions { get; }
    public HavenButton NewTask { get; }
    public HavenButton TaskHistory { get; }
    public HavenButton ResolveProblems { get; }
    public Container ContextCard { get; }
    public HavenButton ContextToggle { get; }
    public HavenText ContextSummaryText { get; }
    public HavenButton ContextClose { get; }
    public Container ContextBody { get; }
    public Prefab AddMenuPrefab { get; }
    public Container AddOverlay => _addMenu.Overlay;
    public HavenButton DismissAddButton => _addMenu.DismissButton;
    public Container CatalogPanel => _addMenu.CatalogPanel;
    public Container CatalogRows => _addMenu.CatalogResults;
    public Input CatalogSearch => _addMenu.CatalogSearch;

    public event EventHandler? SendRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? ResolveProblemsRequested;
    public event EventHandler? ManageAttachmentsRequested;
    public event EventHandler? NewTaskRequested;
    public event EventHandler? TaskHistoryRequested;
    public event EventHandler? ContextDismissRequested;
    public event EventHandler<Guid>? ContextRemoveRequested;
    public event EventHandler<AddMenu.AddMenuAction>? AddActionSelected;
    public event EventHandler<AddMenuSelection>? CatalogItemSelected;
    public event EventHandler<ChatMessageActionRequest>? MessageActionRequested;
    public event EventHandler<ChatMarkdownCodeActionRequest>? MarkdownCodeActionRequested;

    /// <summary>Gets a value indicating whether the user dismissed the Context card in this session; callers use it to avoid forcing it open again.</summary>
    internal bool ContextDismissed => _contextDismissed;

    public void SetComposerPlaceholder(string value) => Instruction.Placeholder = value;

    public void SetSending(bool sending, bool modelAvailable)
    {
        _isSending = sending;
        // Keep the composer editable while a response is streaming so the user can prepare
        // their next message without interrupting the active response.
        Instruction.SetValue(HavenProperties.Enabled, !_safetyLocked);
        SendButton.SetValue(HavenProperties.Enabled, !_safetyLocked && (sending || modelAvailable));
        SendButton.Accessibility.AccessibleName = sending ? "Stop response" : "Send message";
        SendIcon.Key = sending ? "close" : "arrow-up";
    }

    public void SetSafetyLocked(bool locked)
    {
        _safetyLocked = locked;
        Instruction.SetValue(HavenProperties.Enabled, !locked);
        SendButton.SetValue(HavenProperties.Enabled, !locked && (_isSending || !string.IsNullOrWhiteSpace(Instruction.Text)));
        AddButton.SetValue(HavenProperties.Enabled, !locked);
        if (locked) HideAddMenu();
    }

    public void SetStatus(string? value)
    {
        Status.Content = value ?? string.Empty;
        Status.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(value) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    public void SetAttachmentStatus(string? value)
    {
        var hasAttachments = !string.IsNullOrWhiteSpace(value);
        AttachmentStatus.Content = value ?? string.Empty;
        AttachmentStatus.SetValue(HavenProperties.Visibility, hasAttachments ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        ManageAttachments.SetValue(HavenProperties.Visibility, hasAttachments ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    public void SetTaskMode(bool enabled) =>
        TaskActions.SetValue(HavenProperties.Visibility, enabled ? HavenVisibility.Visible : HavenVisibility.Collapsed);

    /// <summary>
    /// Updates the one-line context summary shown on the Context card header; callers label token estimates clearly.
    /// </summary>
    public void SetContextSummary(string summary)
    {
        var text = summary ?? string.Empty;
        ContextSummaryText.Content = text;
        ContextSummaryText.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(text) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        ContextToggle.Accessibility.AccessibleName = string.IsNullOrWhiteSpace(text)
            ? "Conversation context"
            : $"Conversation context: {text}";
    }

    /// <summary>
    /// Expands or collapses the scrollable context entry list while keeping the header reachable.
    /// </summary>
    public void SetContextExpanded(bool expanded)
    {
        _isContextExpanded = expanded;
        ContextBody.SetValue(HavenProperties.Visibility, expanded ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        ContextToggle.Content = expanded ? "Hide context" : "Show context";
        ContextToggle.Accessibility.AccessibleName = expanded
            ? "Hide conversation context details"
            : "Show conversation context details";
    }

    /// <summary>
    /// Rebuilds the Context card from the current persisted rows; the card stays hidden when the conversation has none.
    /// </summary>
    public void SetContextEntries(string summaryLine, IReadOnlyList<ChatSceneContextEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _contextDismissed = false;
        SetContextSummary(summaryLine);
        foreach (var child in ContextBody.Children.ToArray()) ContextBody.Remove(child);
        foreach (var entry in entries) ContextBody.Add(CreateContextRow(entry));
        var visible = entries.Count > 0;
        ContextCard.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        if (!visible) return;
        SetContextExpanded(_isContextExpanded);
    }

    private Container CreateContextRow(ChatSceneContextEntry entry)
    {
        var row = new Container { Layout = HavenLayout.Horizontal, Name = "ContextEntryRow" };
        row.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        row.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        var textColumn = new Container { Layout = HavenLayout.Vertical, Name = "ContextEntryText" };
        textColumn.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        textColumn.SetValue(HavenProperties.Gap, HavenLength.Px(2));
        var category = new HavenText { Content = entry.CategoryLabel };
        category.SetValue(HavenProperties.FontSize, 11d);
        category.SetValue(HavenProperties.Foreground, "TextSecondary");
        category.Accessibility.AccessibleName = $"{entry.CategoryLabel}: {entry.Title}";
        textColumn.Add(category);
        if (!string.IsNullOrWhiteSpace(entry.Preview))
        {
            var preview = new HavenText { Content = entry.Preview };
            preview.SetValue(HavenProperties.FontSize, 11d);
            preview.SetValue(HavenProperties.Foreground, "TextSoft");
            textColumn.Add(preview);
        }
        row.Add(textColumn);
        if (entry.IsRemovable)
        {
            var remove = new HavenButton { Content = "Remove", Variant = ButtonVariant.Ghost };
            remove.SetValue(HavenProperties.MinHeight, HavenLength.Px(28));
            remove.SetValue(HavenProperties.FontSize, 12d);
            remove.Accessibility.AccessibleName = $"Remove {entry.CategoryLabel}: {entry.Title}";
            remove.Invoked += (_, _) => ContextRemoveRequested?.Invoke(this, entry.EntryId);
            row.Add(remove);
        }
        return row;
    }

    private void OnContextToggleInvoked(object? sender, EventArgs e) => SetContextExpanded(!_isContextExpanded);

    private void OnContextCloseInvoked(object? sender, EventArgs e)
    {
        _contextDismissed = true;
        SetContextExpanded(false);
        ContextCard.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        ContextDismissRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetHasMessages(bool hasMessages)
    {
        EmptyState.SetValue(HavenProperties.Visibility, hasMessages ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        Messages.SetValue(HavenProperties.Visibility, hasMessages ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        ResolveProblems.SetValue(HavenProperties.Visibility, hasMessages ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    public void SetAddEnabled(bool enabled) => Chatbox.SetComponentEnabled("AddMenu", enabled);

    public void SetCatalogue(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<CapabilityDefinition> capabilities,
        IReadOnlyList<PromptDefinition> instructions,
        IReadOnlyList<ModeDefinition> apps) =>
        _addMenu.SetCatalogue(agents, capabilities, instructions, apps);

    public void SetResponseState(string agentName, ChatActionMode actionMode, GenerativeUiResponseMode visualMode) =>
        _addMenu.SetResponseState(agentName, actionMode, visualMode);

    public void SyncMessages(IReadOnlyList<ChatSceneMessage> messages)
    {
        var expectedMessages = messages.Select(message => message.Id).ToHashSet();
        foreach (var stale in _messageItems.Keys.Where(id => !expectedMessages.Contains(id)).ToArray())
        {
            _dynamicUi.DeleteItem("Messages", stale.ToString("N"));
            _messageItems.Remove(stale);
            _generatedContent.Remove(stale);
        }

        var expectedTools = messages.SelectMany(message => message.ToolActivities ?? []).Select(activity => activity.Id).ToHashSet();
        foreach (var stale in _toolItems.Keys.Where(id => !expectedTools.Contains(id)).ToArray()) DeleteToolActivity(stale);

        var transcriptIndex = 0;
        foreach (var message in messages)
        {
            if (!_messageItems.TryGetValue(message.Id, out var item))
            {
                item = CreateMessage(message, transcriptIndex);
                _messageItems[message.Id] = item;
            }
            else
            {
                UpdateMessage(item, message);
                var currentIndex = Messages.Items.ToList().IndexOf(item);
                if (currentIndex != transcriptIndex) _dynamicUi.MoveItem("Messages", item.InstanceID, transcriptIndex);
            }
            transcriptIndex++;
            transcriptIndex = SyncToolActivities(message, transcriptIndex);
            RestoreGeneratedContent(message.Id, item);
        }
        SetHasMessages(messages.Count > 0);
    }

    public void UpdateMessage(ChatSceneMessage message)
    {
        if (!_messageItems.TryGetValue(message.Id, out var item))
        {
            item = CreateMessage(message, Messages.Items.Count);
            _messageItems[message.Id] = item;
        }
        else UpdateMessage(item, message);
        var messageIndex = Messages.Items.ToList().IndexOf(item);
        SyncToolActivities(message, Math.Max(0, messageIndex + 1));
        RestoreGeneratedContent(message.Id, item);
        SetHasMessages(true);
    }

    public void SetGeneratedContent(Guid messageId, IReadOnlyList<HavenElement> elements)
    {
        _generatedContent[messageId] = elements;
        if (_messageItems.TryGetValue(messageId, out var item)) RestoreGeneratedContent(messageId, item);
    }

    public void ClearGeneratedContent(Guid messageId)
    {
        _generatedContent.Remove(messageId);
        if (!_messageItems.TryGetValue(messageId, out var item)) return;
        if (TryGeneratedHost(item, out var host))
            foreach (var child in host.Children.ToArray()) host.Remove(child);
    }

    public void ScrollToEnd() => Messages.ScrollY = Messages.MaxScrollY;

    public void ShowResolveProblemsMenu(string title, string description, IReadOnlyList<(string Label, Action Action)> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ShowAnchoredChoiceMenu(ResolveProblems, title, choices, 270d);
    }

    public void ShowMessageChoiceMenu(Guid messageId, string title, IReadOnlyList<(string Label, Action Action)> choices)
    {
        if (!_messageItems.TryGetValue(messageId, out var item)) return;
        ShowAnchoredChoiceMenu(item.GetComponent<HavenButton>("MessageMenu"), title, choices, 260d);
    }

    private void ShowAnchoredChoiceMenu(HavenElement anchor, string title, IReadOnlyList<(string Label, Action Action)> choices, double width)
    {
        ArgumentNullException.ThrowIfNull(choices);
        _activeMessagePopup?.Dismiss();
        foreach (var existing in Root.Children.OfType<PopupMenu>().ToArray()) existing.Dismiss();
        var actions = choices.Select(choice => new PopupMenuItem(choice.Label, choice.Action)).ToArray();
        var popup = new PopupMenu(anchor, Root, actions, width, title);
        popup.Dismissed += (_, _) =>
        {
            if (ReferenceEquals(_activeMessagePopup, popup)) _activeMessagePopup = null;
        };
        _activeMessagePopup = popup;
        Root.Add(popup);
    }

    public void ShowChoicePrompt(string title, string description, IReadOnlyList<(string Label, Action Action)> choices)
    {
        var overlay = CreateModalOverlay(title, description, out var card);
        foreach (var (label, action) in choices)
        {
            var button = RowButton(label);
            button.Invoked += (_, _) =>
            {
                CloseModal(overlay);
                action();
            };
            card.Add(button);
        }
        card.Add(CancelButton(overlay));
        Root.Add(overlay);
    }

    public void ShowTextPrompt(string title, string description, string initialValue, string confirmLabel, Func<string, Task> confirm)
    {
        var overlay = CreateModalOverlay(title, description, out var card);
        var editor = new Input
        {
            Text = initialValue ?? string.Empty,
            Multiline = true,
            SubmitOnEnter = false,
            Placeholder = title
        };
        editor.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        editor.SetValue(HavenProperties.MinHeight, HavenLength.Px(96));
        card.Add(editor);
        var confirmButton = new HavenButton { Content = confirmLabel, Variant = ButtonVariant.Primary };
        confirmButton.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        confirmButton.Invoked += async (_, _) =>
        {
            var value = editor.Text.Trim();
            if (string.IsNullOrWhiteSpace(value)) return;
            try
            {
                await confirm(value);
                CloseModal(overlay);
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message);
            }
        };
        card.Add(confirmButton);
        card.Add(CancelButton(overlay));
        Root.Add(overlay);
    }

    private HavenButton CancelButton(Container overlay)
    {
        var cancel = new HavenButton { Content = "Cancel", Variant = ButtonVariant.Ghost };
        cancel.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        cancel.SetValue(HavenProperties.MinHeight, HavenLength.Px(34));
        cancel.Invoked += (_, _) => CloseModal(overlay);
        return cancel;
    }

    private Container CreateModalOverlay(string title, string description, out Container card)
    {
        var overlay = new Container { Layout = HavenLayout.Overlay, Name = "ChatModalOverlay" };
        overlay.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        overlay.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        overlay.SetValue(HavenProperties.Background, "Overlay");
        overlay.SetValue(HavenProperties.Opacity, .82d);
        overlay.SetValue(HavenProperties.ZIndex, 200);
        card = new Container { Layout = HavenLayout.Vertical, Name = "ChatModalCard" };
        card.SetValue(HavenProperties.Width, HavenLength.Px(420));
        card.SetValue(HavenProperties.MaxWidth, HavenLength.Percent(90));
        card.SetValue(HavenProperties.MaxHeight, HavenLength.Percent(84));
        card.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        card.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(20)));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(20)));
        card.SetValue(HavenProperties.Shadow, "Card");
        card.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        card.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        var heading = new HavenText { Content = title, Level = TextLevel.H2 };
        card.Add(heading);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var copy = new HavenText { Content = description };
            copy.SetValue(HavenProperties.Foreground, "TextSecondary");
            card.Add(copy);
        }
        overlay.Add(card);
        return overlay;
    }

    private void CloseModal(Container overlay)
    {
        if (ReferenceEquals(overlay.Parent, Root)) Root.Remove(overlay);
    }

    private int SyncToolActivities(ChatSceneMessage message, int startIndex)
    {
        var activities = message.ToolActivities ?? [];
        var expected = activities.Select(activity => activity.Id).ToHashSet();
        foreach (var stale in _toolOwners.Where(pair => pair.Value == message.Id && !expected.Contains(pair.Key)).Select(pair => pair.Key).ToArray())
            DeleteToolActivity(stale);

        var index = startIndex;
        foreach (var activity in activities)
        {
            if (!_toolItems.TryGetValue(activity.Id, out var item))
            {
                item = _dynamicUi.CreateItem("ChatToolActivity", "Messages", "tool-" + activity.Id.ToString("N"), ValuesFor(activity), index);
                _toolItems[activity.Id] = item;
                _toolOwners[activity.Id] = message.Id;
            }
            else
            {
                item.SetVariables(ValuesFor(activity));
                _toolOwners[activity.Id] = message.Id;
                var currentIndex = Messages.Items.ToList().IndexOf(item);
                if (currentIndex != index) _dynamicUi.MoveItem("Messages", item.InstanceID, index);
            }
            index++;
        }
        return index;
    }

    private void DeleteToolActivity(Guid activityId)
    {
        if (_toolItems.Remove(activityId, out var item))
            _dynamicUi.DeleteItem("Messages", item.InstanceID);
        _toolOwners.Remove(activityId);
    }

    private static Dictionary<string, object?> ValuesFor(ToolActivity activity) => new()
    {
        ["STATUS"] = activity.Succeeded ? "Completed" : "Failed",
        ["TITLE"] = activity.Title,
        ["DETAIL"] = activity.Detail,
        ["META"] = ToolActivityMeta(activity)
    };

    private static string ToolActivityMeta(ToolActivity activity)
    {
        var parts = new List<string>();
        if (activity.Duration > TimeSpan.Zero) parts.Add($"{activity.Duration.TotalSeconds:0.#}s");
        if (activity.LinesAdded != 0 || activity.LinesRemoved != 0) parts.Add($"+{activity.LinesAdded} -{activity.LinesRemoved}");
        return string.Join(" Â· ", parts);
    }

    private DynamicUIItem CreateMessage(ChatSceneMessage message, int index)
    {
        var values = ValuesFor(message);
        var template = message.Role == MessageRole.Assistant ? "ChatAssistantMessage" : "ChatUserMessage";
        var item = _dynamicUi.CreateItem(template, "Messages", message.Id.ToString("N"), values, index);
        WireMessageActions(item, message.Id, message.Role);
        return item;
    }

    private static void UpdateMessage(DynamicUIItem item, ChatSceneMessage message) => item.SetVariables(ValuesFor(message));

    private static Dictionary<string, object?> ValuesFor(ChatSceneMessage message)
    {
        if (message.Role != MessageRole.Assistant)
        {
            var userValues = new Dictionary<string, object?> { ["CONTENT"] = message.Content };
            ApplyAvatarValues(userValues, MessageRole.User);
            return userValues;
        }
        var values = new Dictionary<string, object?>
        {
            ["CONTENT"] = message.Content,
            ["AGENT"] = string.IsNullOrWhiteSpace(message.AgentName) ? "Haven" : message.AgentName,
            ["THINKING"] = message.Thinking,
            ["THINKINGVISIBILITY"] = string.IsNullOrWhiteSpace(message.Thinking) ? "Collapsed" : "Visible"
        };
        ApplyAvatarValues(values, MessageRole.Assistant);
        return values;
    }

    private static void ApplyAvatarValues(Dictionary<string, object?> values, MessageRole role)
    {
        var havenSide = role == MessageRole.Assistant;
        var enabled = havenSide ? HavenPersonalisation.HavenAvatarEnabled : HavenPersonalisation.UserAvatarEnabled;
        var available = AvatarStore.Current?.Has(havenSide ? HavenAvatarKind.Haven : HavenAvatarKind.User) == true;
        values["AVATAR"] = havenSide
            ? Haven.Desktop.HavenUI.Backend.HavenDesktopImageResolver.HavenAvatarSource
            : Haven.Desktop.HavenUI.Backend.HavenDesktopImageResolver.UserAvatarSource;
        values["AVATARVISIBILITY"] = enabled && available ? "Visible" : "Collapsed";
    }

    private void WireMessageActions(DynamicUIItem item, Guid messageId, MessageRole role)
    {
        var menu = item.GetComponent<HavenButton>("MessageMenu");
        menu.Accessibility.AccessibleName = role == MessageRole.Assistant ? "Response actions" : "Message actions";
        menu.Invoked += (_, _) => ShowMessageMenu(menu, messageId, role);
        var markdown = item.GetComponent<Markdown>("Body");
        markdown.CodeActionRequested += (_, request) => MarkdownCodeActionRequested?.Invoke(this, new ChatMarkdownCodeActionRequest(messageId, request));
    }

    private void ShowMessageMenu(HavenElement anchor, Guid messageId, MessageRole role)
    {
        _activeMessagePopup?.Dismiss();
        IReadOnlyList<PopupMenuItem> actions = role == MessageRole.Assistant
            ?
            [
                new PopupMenuItem("Re-generate", () => RequestMessageAction(messageId, ChatMessageAction.Regenerate), Enabled: !_safetyLocked),
                new PopupMenuItem("Copy", () => RequestMessageAction(messageId, ChatMessageAction.Copy)),
                new PopupMenuItem("Branch", () => RequestMessageAction(messageId, ChatMessageAction.Branch), Enabled: !_safetyLocked),
                new PopupMenuItem("Delete from memory", () => RequestMessageAction(messageId, ChatMessageAction.Forget), true)
            ]
            :
            [
                new PopupMenuItem("Edit", () => RequestMessageAction(messageId, ChatMessageAction.Edit), Enabled: !_safetyLocked),
                new PopupMenuItem("Copy", () => RequestMessageAction(messageId, ChatMessageAction.Copy)),
                new PopupMenuItem("Branch", () => RequestMessageAction(messageId, ChatMessageAction.Branch), Enabled: !_safetyLocked),
                new PopupMenuItem("Delete", () => RequestMessageAction(messageId, ChatMessageAction.Delete), true)
            ];
        var popup = new PopupMenu(anchor, Root, actions, 190d, role == MessageRole.Assistant ? "Response actions" : "Message actions");
        popup.Dismissed += (_, _) =>
        {
            if (ReferenceEquals(_activeMessagePopup, popup)) _activeMessagePopup = null;
        };
        _activeMessagePopup = popup;
        Root.Add(popup);
    }

    private void RequestMessageAction(Guid messageId, ChatMessageAction action) =>
        MessageActionRequested?.Invoke(this, new ChatMessageActionRequest(messageId, action));

    private void RestoreGeneratedContent(Guid messageId, DynamicUIItem item)
    {
        if (!TryGeneratedHost(item, out var host)) return;
        var expected = _generatedContent.TryGetValue(messageId, out var content) ? content : [];
        if (host.Children.SequenceEqual(expected)) return;
        foreach (var child in host.Children.ToArray()) host.Remove(child);
        foreach (var child in expected) host.Add(child);
    }

    private static bool TryGeneratedHost(DynamicUIItem item, out Container host)
    {
        try
        {
            host = item.GetComponent<Container>("GeneratedContent");
            return true;
        }
        catch (KeyNotFoundException)
        {
            host = null!;
            return false;
        }
    }

    private static HavenButton RowButton(string label)
    {
        var button = new HavenButton { Content = label, Variant = ButtonVariant.Navigation };
        button.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(38));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 10px"));
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(11)));
        button.SetValue(HavenProperties.FontSize, 13d);
        button.Accessibility.AccessibleName = label;
        return button;
    }

    private void OnAddInvoked(object? sender, EventArgs e) => ShowAddMenu();
    private void OnSendInvoked(object? sender, EventArgs e)
    {
        if (_isSending)
        {
            StopRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        SendRequested?.Invoke(this, EventArgs.Empty);
    }
    private void OnResolveInvoked(object? sender, EventArgs e) => ResolveProblemsRequested?.Invoke(this, EventArgs.Empty);

    public void ShowAddMenu() => _addMenu.Show();
    public void ShowMentionSearch(string query) => _addMenu.ShowMentionSearch(query);

    public void HideAddMenu() => _addMenu.Hide();

    public void FilterCatalogue(string query) => _addMenu.FilterCatalogue(query);

    private void OnSharedAddActionSelected(object? sender, AddMenu.AddMenuAction action) =>
        AddActionSelected?.Invoke(this, action);

    private void OnSharedCatalogItemSelected(object? sender, AddMenuSelection selection) =>
        CatalogItemSelected?.Invoke(this, selection);

    private Page BuildRoot()
    {
        const string markup = """
            <Page Name="ChatRoot" Layout="Overlay" Background="Transparent">
              <Container Name="Workspace" Layout="Grid" Width="100%" Height="100%" Rows="1fr Auto" Padding="32px 10px 32px 4px" Gap="12px">
                <Container Name="ConversationViewport" Row="0" Layout="Overlay" Width="100%" Overflow="Scroll" Clip="true">
                  <Container Name="EmptyState" Layout="Vertical" HorizontalAlignment="Center" VerticalAlignment="Center" Gap="10px">
                    <Text Content="How can I help?" Level="H1" HorizontalAlignment="Center" />
                    <Text Content="Start a conversation or attach context below." FontSize="13" Foreground="TextSecondary" HorizontalAlignment="Center" />
                  </Container>
                  <DynamicUIRuntime Name="Messages" Width="100%" MaxWidth="980px" HorizontalAlignment="Center" />
                </Container>
                <Container Name="Footer" Row="1" Layout="Vertical" Width="100%" MaxWidth="900px" HorizontalAlignment="Center" Gap="7px">
                  <Container Name="ContextCard" Layout="Vertical" Width="100%" HorizontalAlignment="Center" Gap="6px" Padding="12px 14px" Visibility="Collapsed">
                    <Container Name="ContextHeaderRow" Layout="Horizontal" Width="100%" Gap="8px">
                      <Button Name="ContextToggle" Variant="Ghost" Content="Show context" MinHeight="30px" />
                      <Text Name="ContextSummary" Content="" FontSize="11" Foreground="TextSecondary" VerticalAlignment="Center" Visibility="Collapsed" />
                      <Button Name="ContextClose" Variant="Ghost" Content="Close" MinHeight="30px" />
                    </Container>
                    <Container Name="ContextBody" Layout="Vertical" Width="100%" MaxHeight="260px" Overflow="Scroll" Clip="true" Gap="4px" Visibility="Collapsed" />
                  </Container>
                  <Container Name="TaskActions" Layout="Horizontal" HorizontalAlignment="Center" Gap="6px" Visibility="Collapsed">
                    <Button Name="NewTask" Variant="Ghost" Content="New task" MinHeight="34px" />
                    <Button Name="TaskHistory" Variant="Ghost" Content="Task history" MinHeight="34px" />
                  </Container>
                  <Button Name="ResolveProblems" Variant="Ghost" Content="Resolve problems" MinHeight="34px" HorizontalAlignment="Center" Visibility="Collapsed" />
                  <Text Name="Status" Content="" FontSize="11" Foreground="TextSecondary" HorizontalAlignment="Center" Visibility="Collapsed" />
                  <Text Name="AttachmentStatus" Content="" FontSize="11" Foreground="TextSecondary" HorizontalAlignment="Center" Visibility="Collapsed" />
                  <Button Name="ManageAttachments" Variant="Ghost" Content="Manage attachments" MinHeight="32px" HorizontalAlignment="Center" Visibility="Collapsed" />
                  <Prefab InstID="Main-Chatbox" ID="Chatbox" />
                </Container>
              </Container>
            </Page>
            """;
        return (Page)new HavenMarkupParser(_prefabs).Parse(markup, "ChatPage.hui");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _activeMessagePopup?.Dismiss();
        _activeMessagePopup = null;
        _messageItems.Clear();
        _generatedContent.Clear();
        AddButton.Invoked -= OnAddInvoked;
        SendButton.Invoked -= OnSendInvoked;
        ResolveProblems.Invoked -= OnResolveInvoked;
        ContextToggle.Invoked -= OnContextToggleInvoked;
        ContextClose.Invoked -= OnContextCloseInvoked;
        _addMenu.AddActionSelected -= OnSharedAddActionSelected;
        _addMenu.CatalogItemSelected -= OnSharedCatalogItemSelected;
        _addMenu.Dispose();
    }
}
