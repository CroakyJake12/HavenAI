using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Shell.NativePresentation;

internal enum ChatSidebarEntryKind
{
    Conversation,
    Group,
    File
}

internal enum ChatSidebarConversationAction
{
    Open,
    Rename,
    TogglePin,
    ToggleRead,
    Move,
    Archive,
    Delete
}

internal enum ChatSidebarGroupAction
{
    Open,
    Toggle,
    Rename,
    TogglePin,
    ToggleExpand,
    NewChat,
    Archive,
    Delete
}

internal sealed record ChatSidebarEntry(
    ChatSidebarEntryKind Kind,
    Guid Id,
    string Title,
    bool Active,
    bool Unread,
    bool Pinned,
    bool Expanded = false,
    bool Indented = false);

internal sealed record ChatSidebarConversationRequest(Guid ConversationId, ChatSidebarConversationAction Action);
internal sealed record ChatSidebarGroupRequest(Guid GroupId, ChatSidebarGroupAction Action);
internal sealed record ChatSidebarFileEntry(Guid AttachmentId, Guid ConversationId, string Name, string MediaType, long SizeBytes, DateTimeOffset UpdatedAt);

/// <summary>
/// Haven-owned Chat sidebar scene. Avalonia may host this scene, but every visible sidebar control
/// and every runtime row belongs to Haven.UI. Repeated chat/group rows are DynamicUI instances.
/// </summary>
internal sealed class ChatSidebarHavenScene : IDisposable
{
    private readonly HavenDynamicUITemplateCatalog _templates;
    private readonly DynamicUI _dynamicUi;
    private readonly Dictionary<string, Dictionary<string, DynamicUIItem>> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, ChatSidebarEntry> _entryState = [];
    private IReadOnlyList<ChatSidebarFileEntry> _fileEntries = [];
    private PopupMenu? _activePopup;
    private HavenMode _mode = HavenMode.Chat;
    private bool _disposed;

    public ChatSidebarHavenScene()
    {
        _templates = HavenDynamicUITemplateCatalog.FromAssembly(typeof(ChatSidebarHavenScene).Assembly);
        Root = BuildRoot();
        _dynamicUi = new DynamicUI(Root, _templates);

        ModeOptions = Get<Container>("ModeOptions");
        ChatMode = Get<HavenButton>("ChatMode");
        StudyMode = Get<HavenButton>("StudyMode");
        TasksMode = Get<HavenButton>("TasksMode");
        SidebarTitle = Get<HavenText>("SidebarTitle");
        Search = Get<Input>("Search");
        PinnedHeading = Get<HavenText>("PinnedHeading");
        UnreadHeading = Get<HavenText>("UnreadHeading");
        GroupsHeading = Get<HavenText>("GroupsHeading");
        FilesHeading = Get<HavenText>("FilesHeading");
        FilesEmpty = Get<HavenText>("FilesEmpty");
        ChatsHeading = Get<HavenText>("ChatsHeading");
        PinnedRows = Get<DynamicUIRuntime>("PinnedRows");
        UnreadRows = Get<DynamicUIRuntime>("UnreadRows");
        GroupRows = Get<DynamicUIRuntime>("GroupRows");
        FileRows = Get<DynamicUIRuntime>("FileRows");
        ChatRows = Get<DynamicUIRuntime>("ChatRows");
        NewChat = Get<HavenButton>("NewChat");
        NewGroup = Get<HavenButton>("NewGroup");
        Status = Get<HavenText>("Status");

        ModeButton.Invoked += OnModeButtonInvoked;
        ChatMode.Invoked += OnChatModeInvoked;
        StudyMode.Invoked += OnStudyModeInvoked;
        TasksMode.Invoked += OnTasksModeInvoked;
        Search.TextChanged += OnSearchTextChanged;
        NewChat.Invoked += OnNewChatInvoked;
        NewGroup.Invoked += OnNewGroupInvoked;
        SetMode(HavenMode.Chat);
    }

    public Page Root { get; }
    public HavenButton ModeButton { get; }
    public Container ModeOptions { get; }
    public HavenButton ChatMode { get; }
    public HavenButton StudyMode { get; }
    public HavenButton TasksMode { get; }
    public HavenText SidebarTitle { get; }
    public Input Search { get; }
    public HavenText PinnedHeading { get; }
    public HavenText UnreadHeading { get; }
    public HavenText GroupsHeading { get; }
    public HavenText FilesHeading { get; }
    public HavenText FilesEmpty { get; }
    public HavenText ChatsHeading { get; }
    public DynamicUIRuntime PinnedRows { get; }
    public DynamicUIRuntime UnreadRows { get; }
    public DynamicUIRuntime GroupRows { get; }
    public DynamicUIRuntime FileRows { get; }
    public DynamicUIRuntime ChatRows { get; }
    public HavenButton NewChat { get; }
    public HavenButton NewGroup { get; }
    public HavenText Status { get; }

    public event EventHandler<string>? SearchChanged;
    public event EventHandler<HavenMode>? ModeRequested;
    public event EventHandler? NewChatRequested;
    public event EventHandler? NewGroupRequested;
    public event EventHandler<ChatSidebarConversationRequest>? ConversationActionRequested;
    public event EventHandler<ChatSidebarGroupRequest>? GroupActionRequested;
    public event EventHandler<Guid>? FileRequested;

    public void SetMode(HavenMode mode)
    {
        _mode = mode;
        var modeName = ModeName(mode);
        SidebarTitle.Content = modeName;
        ModeButton.Content = modeName;
        ModeButton.Accessibility.AccessibleName = $"Current mode: {modeName}";
        GroupsHeading.Content = GroupName(mode, plural: true);
        ChatsHeading.Content = mode switch
        {
            HavenMode.Study => "Study Chats",
            HavenMode.Tasks => "Task Chats",
            _ => "Chats"
        };
        Search.Placeholder = mode == HavenMode.Chat ? "Search chats, groups and files" : $"Search {modeName}";
        Search.Accessibility.AccessibleName = mode == HavenMode.Chat
            ? "Search chats, Chat Groups and File Library"
            : $"Search {modeName} chats and groups";
        NewChat.Content = NewChatLabel(mode);
        NewGroup.Content = string.Empty;
        NewGroup.Accessibility.AccessibleName = $"Create {GroupName(mode, plural: false)}";
        ModeOptions.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        SetHeadingVisibility(FilesHeading, mode == HavenMode.Chat);
        FilesEmpty.SetValue(HavenProperties.Visibility, mode == HavenMode.Chat && FileRows.Items.Count == 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    public void SetRows(
        IReadOnlyList<ChatSidebarEntry> pinned,
        IReadOnlyList<ChatSidebarEntry> unread,
        IReadOnlyList<ChatSidebarEntry> groups,
        IReadOnlyList<ChatSidebarEntry> chats) =>
        SetRows(pinned, unread, groups, [], chats);

    public void SetRows(
        IReadOnlyList<ChatSidebarEntry> pinned,
        IReadOnlyList<ChatSidebarEntry> unread,
        IReadOnlyList<ChatSidebarEntry> groups,
        IReadOnlyList<ChatSidebarEntry> files,
        IReadOnlyList<ChatSidebarEntry> chats)
    {
        _entryState.Clear();
        SyncRows("PinnedRows", pinned);
        SyncRows("UnreadRows", unread);
        SyncRows("GroupRows", groups);
        SyncRows("FileRows", files);
        SyncRows("ChatRows", chats);
        SetHeadingVisibility(PinnedHeading, pinned.Count > 0);
        SetHeadingVisibility(UnreadHeading, unread.Count > 0);
        SetHeadingVisibility(GroupsHeading, true);
        SetHeadingVisibility(FilesHeading, _mode == HavenMode.Chat);
        FilesEmpty.SetValue(HavenProperties.Visibility, _mode == HavenMode.Chat && files.Count == 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        SetHeadingVisibility(ChatsHeading, true);
    }

    public void SetNewChatBusy(bool busy) =>
        NewChat.SetValue(HavenProperties.Enabled, !busy);

    public void SetStatus(string? value)
    {
        Status.Content = value ?? string.Empty;
        Status.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(value) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    public void ShowTextPrompt(string title, string initialValue, string confirmLabel, Func<string, Task> confirm)
    {
        var overlay = CreateModal(title, out var card);
        var editor = new Input { Text = initialValue ?? string.Empty, Placeholder = title };
        editor.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        editor.Accessibility.AccessibleName = title;
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
            catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                SetStatus(exception.Message);
            }
        };
        card.Add(confirmButton);
        card.Add(CancelButton(overlay));
        Root.Add(overlay);
    }

    public void ShowChoices(string title, IReadOnlyList<(string Label, Action Action)> choices)
    {
        var overlay = CreateModal(title, out var card);
        foreach (var (label, action) in choices)
        {
            var button = new HavenButton { Content = label, Variant = ButtonVariant.Navigation };
            button.SetValue(HavenProperties.Width, HavenLength.Percent(100));
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

    private void SyncRows(string location, IReadOnlyList<ChatSidebarEntry> entries)
    {
        if (!_items.TryGetValue(location, out var byId))
        {
            byId = new Dictionary<string, DynamicUIItem>(StringComparer.Ordinal);
            _items[location] = byId;
        }

        var expected = entries.Select(InstanceId).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in byId.Keys.Where(id => !expected.Contains(id)).ToArray())
        {
            _dynamicUi.DeleteItem(location, stale);
            byId.Remove(stale);
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var id = InstanceId(entry);
            var values = Values(entry);
            if (!byId.TryGetValue(id, out var item))
            {
                var template = entry.Kind switch
                {
                    ChatSidebarEntryKind.Group => "ChatSidebarGroupRow",
                    ChatSidebarEntryKind.File => "ChatSidebarFileRow",
                    _ => "ChatSidebarConversationRow"
                };
                item = _dynamicUi.CreateItem(template, location, id, values, index);
                byId[id] = item;
                switch (entry.Kind)
                {
                    case ChatSidebarEntryKind.Group:
                        WireGroup(item, entry.Id);
                        break;
                    case ChatSidebarEntryKind.File:
                        WireFile(item, entry.Id);
                        break;
                    default:
                        WireConversation(item, entry.Id);
                        break;
                }
            }
            else
            {
                item.SetVariables(values);
                var currentIndex = _dynamicUi.GetRuntime(location).Items.ToList().IndexOf(item);
                if (currentIndex != index) _dynamicUi.MoveItem(location, id, index);
            }
            _entryState[entry.Id] = entry;
            RefreshAccessibility(item, entry);
        }
    }

    private void RefreshAccessibility(DynamicUIItem item, ChatSidebarEntry entry)
    {
        var open = item.GetComponent<HavenButton>("Open");
        open.Accessibility.AccessibleName = entry.Kind == ChatSidebarEntryKind.File
            ? $"Open source chat for file {entry.Title}"
            : entry.Title + (entry.Unread ? ", unread" : string.Empty);
        if (entry.Kind == ChatSidebarEntryKind.File) return;

        item.GetComponent<HavenButton>("More").Accessibility.AccessibleName = $"Manage {entry.Title}";
        if (entry.Kind == ChatSidebarEntryKind.Group)
            item.GetComponent<HavenButton>("Toggle").Accessibility.AccessibleName = entry.Expanded ? $"Collapse {entry.Title}" : $"Expand {entry.Title}";
    }

    private void WireConversation(DynamicUIItem item, Guid id)
    {
        Wire(item, "Open", () => ConversationActionRequested?.Invoke(this, new(id, ChatSidebarConversationAction.Open)));
        item.GetComponent<HavenButton>("More").Invoked += (_, _) =>
            ShowConversationMenu(item.GetComponent<HavenButton>("More"), id);
    }

    private void WireGroup(DynamicUIItem item, Guid id)
    {
        Wire(item, "Open", () => GroupActionRequested?.Invoke(this, new(id, ChatSidebarGroupAction.Open)));
        Wire(item, "Toggle", () => GroupActionRequested?.Invoke(this, new(id, ChatSidebarGroupAction.Toggle)));
        item.GetComponent<HavenButton>("More").Invoked += (_, _) =>
            ShowGroupMenu(item.GetComponent<HavenButton>("More"), id);
    }

    private void WireFile(DynamicUIItem item, Guid id) =>
        Wire(item, "Open", () => FileRequested?.Invoke(this, id));

    private void ShowConversationMenu(HavenButton anchor, Guid id)
    {
        if (!_entryState.TryGetValue(id, out var entry)) return;
        ShowPopup(anchor, $"Manage {entry.Title}",
        [
            new PopupMenuItem("Rename", () => ConversationActionRequested?.Invoke(this, new(id, ChatSidebarConversationAction.Rename))),
            new PopupMenuItem(entry.Pinned ? "Unpin" : "Pin", () => ConversationActionRequested?.Invoke(this, new(id, ChatSidebarConversationAction.TogglePin))),
            new PopupMenuItem(entry.Unread ? "Mark read" : "Mark unread", () => ConversationActionRequested?.Invoke(this, new(id, ChatSidebarConversationAction.ToggleRead))),
            new PopupMenuItem("Move to group", () => ConversationActionRequested?.Invoke(this, new(id, ChatSidebarConversationAction.Move))),
            new PopupMenuItem("Archive", () => ConversationActionRequested?.Invoke(this, new(id, ChatSidebarConversationAction.Archive))),
            new PopupMenuItem("Delete", () => ConversationActionRequested?.Invoke(this, new(id, ChatSidebarConversationAction.Delete)), true)
        ]);
    }

    private void ShowGroupMenu(HavenButton anchor, Guid id)
    {
        if (!_entryState.TryGetValue(id, out var entry)) return;
        ShowPopup(anchor, $"Manage {entry.Title}",
        [
            new PopupMenuItem("Rename", () => GroupActionRequested?.Invoke(this, new(id, ChatSidebarGroupAction.Rename))),
            new PopupMenuItem(entry.Pinned ? "Unpin" : "Pin", () => GroupActionRequested?.Invoke(this, new(id, ChatSidebarGroupAction.TogglePin))),
            new PopupMenuItem(entry.Expanded ? "Collapse" : "Expand", () => GroupActionRequested?.Invoke(this, new(id, ChatSidebarGroupAction.ToggleExpand))),
            new PopupMenuItem("New chat", () => GroupActionRequested?.Invoke(this, new(id, ChatSidebarGroupAction.NewChat))),
            new PopupMenuItem("Archive", () => GroupActionRequested?.Invoke(this, new(id, ChatSidebarGroupAction.Archive))),
            new PopupMenuItem("Delete and detach chats", () => GroupActionRequested?.Invoke(this, new(id, ChatSidebarGroupAction.Delete)), true)
        ]);
    }

    private void ShowPopup(HavenElement anchor, string accessibleName, IReadOnlyList<PopupMenuItem> items)
    {
        _activePopup?.Dismiss();
        var popup = new PopupMenu(anchor, Root, items, 220d, accessibleName);
        popup.Dismissed += (_, _) =>
        {
            if (ReferenceEquals(_activePopup, popup)) _activePopup = null;
        };
        _activePopup = popup;
        Root.Add(popup);
    }

    private static void Wire(DynamicUIItem item, string component, Action action) =>
        item.GetComponent<HavenButton>(component).Invoked += (_, _) => action();

    private static Dictionary<string, object?> Values(ChatSidebarEntry entry)
    {
        if (entry.Kind == ChatSidebarEntryKind.Group)
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["TITLE"] = entry.Title + (entry.Unread ? " •" : string.Empty),
                ["BACKGROUND"] = entry.Active ? "AccentMuted" : "Transparent",
                ["CHEVRON"] = entry.Expanded ? "chevron-down" : "chevron-right",
            };

        if (entry.Kind == ChatSidebarEntryKind.File)
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["TITLE"] = entry.Title,
                ["BACKGROUND"] = entry.Active ? "AccentMuted" : "Transparent",
            };

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["TITLE"] = entry.Title + (entry.Unread ? " •" : string.Empty),
            ["BACKGROUND"] = entry.Active ? "AccentMuted" : "Transparent",
            ["MARGIN"] = entry.Indented ? "28px 0px 0px 0px" : "0px",
        };
    }

    private Container CreateModal(string title, out Container card)
    {
        var overlay = new Container { Layout = HavenLayout.Overlay, Name = "ChatSidebarModal" };
        overlay.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        overlay.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        overlay.SetValue(HavenProperties.Background, "Overlay");
        overlay.SetValue(HavenProperties.Opacity, .82d);
        overlay.SetValue(HavenProperties.ZIndex, 200);

        card = new Container { Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Width, HavenLength.Px(300));
        card.SetValue(HavenProperties.MaxWidth, HavenLength.Percent(94));
        card.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        card.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(16)));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        card.SetValue(HavenProperties.Shadow, "Card");
        card.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        card.Add(new HavenText { Content = title, Level = TextLevel.H2 });
        overlay.Add(card);
        return overlay;
    }

    private HavenButton CancelButton(Container overlay)
    {
        var cancel = new HavenButton { Content = "Cancel", Variant = ButtonVariant.Ghost };
        cancel.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        cancel.SetValue(HavenProperties.MinHeight, HavenLength.Px(34));
        cancel.Invoked += (_, _) => CloseModal(overlay);
        return cancel;
    }

    private void CloseModal(Container overlay)
    {
        if (ReferenceEquals(overlay.Parent, Root)) Root.Remove(overlay);
    }

    private void OnModeButtonInvoked(object? sender, EventArgs e)
    {
        var visible = ModeOptions.GetValue(HavenProperties.Visibility) == HavenVisibility.Visible;
        ModeOptions.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private void OnChatModeInvoked(object? sender, EventArgs e) => ModeRequested?.Invoke(this, HavenMode.Chat);
    private void OnStudyModeInvoked(object? sender, EventArgs e) => ModeRequested?.Invoke(this, HavenMode.Study);
    private void OnTasksModeInvoked(object? sender, EventArgs e) => ModeRequested?.Invoke(this, HavenMode.Tasks);
    private void OnSearchTextChanged(object? sender, EventArgs e) => SearchChanged?.Invoke(this, Search.Text.Trim());
    private void OnNewChatInvoked(object? sender, EventArgs e) => NewChatRequested?.Invoke(this, EventArgs.Empty);
    private void OnNewGroupInvoked(object? sender, EventArgs e) => NewGroupRequested?.Invoke(this, EventArgs.Empty);

    private T Get<T>(string name) where T : HavenElement =>
        (T)Root.DescendantsAndSelf().Single(element => element.Name == name);

    private static string InstanceId(ChatSidebarEntry entry) =>
        $"{entry.Kind switch { ChatSidebarEntryKind.Group => "group", ChatSidebarEntryKind.File => "file", _ => "chat" }}-{entry.Id:N}";

    private static void SetHeadingVisibility(HavenText heading, bool visible) =>
        heading.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);

    private static string ModeName(HavenMode mode) => mode switch
    {
        HavenMode.Study => "Study",
        HavenMode.Tasks => "Tasks",
        _ => "Chat"
    };

    private static string GroupName(HavenMode mode, bool plural) => mode switch
    {
        HavenMode.Study => plural ? "Subjects" : "Subject",
        HavenMode.Tasks => plural ? "Task Groups" : "Task Group",
        _ => plural ? "Chat Groups" : "Chat Group"
    };

    private static string NewChatLabel(HavenMode mode) => mode switch
    {
        HavenMode.Study => "New Study Chat",
        HavenMode.Tasks => "New Task",
        _ => "New Chat"
    };

    private static Page BuildRoot()
    {
        const string markup = """
            <Page Name="ChatSidebarRoot" Layout="Grid" Width="100%" Height="100%" Rows="Auto Auto Auto 1fr Auto Auto" Gap="8px" Padding="14px" Background="Surface">
              <Container Name="ModeRow" Row="0" Layout="Horizontal" Width="100%" Gap="0px">
                <Text Name="SidebarTitle" Content="Chat" Level="H2" VerticalAlignment="Center" />
                <Button Name="ModeButton" Variant="Ghost" Content="Chat" Visibility="Collapsed" />
              </Container>
              <Container Name="ModeOptions" Row="1" Layout="Horizontal" Width="100%" Gap="4px" Visibility="Collapsed">
                <Button Name="ChatMode" Variant="Ghost" Content="Chat" MinHeight="32px" />
                <Button Name="StudyMode" Variant="Ghost" Content="Study" MinHeight="32px" />
                <Button Name="TasksMode" Variant="Ghost" Content="Tasks" MinHeight="32px" />
              </Container>
              <Input Name="Search" Row="2" Width="100%" Height="34px" MinHeight="34px" Placeholder="Search chats, groups and files" />
              <Container Name="ScrollHost" Row="3" Layout="Vertical" Width="100%" Overflow="Scroll" Clip="true" Gap="6px">
                <Container Name="GroupsHeader" Layout="Grid" Columns="1fr 32px" Width="100%" Gap="4px">
                  <Text Name="GroupsHeading" Content="Chat Groups" Level="H3" Column="0" VerticalAlignment="Center" />
                  <Button Name="NewGroup" Variant="Icon" IconKey="plus" Content="" Column="1" Width="32px" Height="32px" MinHeight="32px" />
                </Container>
                <DynamicUIRuntime Name="GroupRows" Width="100%" />
                <Text Name="FilesHeading" Content="File Library" Level="H3" />
                <DynamicUIRuntime Name="FileRows" Width="100%" />
                <Text Name="FilesEmpty" Content="Files attached to chats appear here." FontSize="11" Foreground="TextSecondary" Visibility="Collapsed" />
                <Text Name="PinnedHeading" Content="Pinned" Level="H3" Visibility="Collapsed" />
                <DynamicUIRuntime Name="PinnedRows" Width="100%" />
                <Text Name="UnreadHeading" Content="Unread Notifications" Level="H3" Visibility="Collapsed" />
                <DynamicUIRuntime Name="UnreadRows" Width="100%" />
                <Text Name="ChatsHeading" Content="Chats" Level="H3" />
                <DynamicUIRuntime Name="ChatRows" Width="100%" />
              </Container>
              <Container Name="Footer" Row="4" Layout="Grid" Columns="1fr" Width="100%">
                <Button Name="NewChat" Variant="Primary" IconKey="plus" Content="New Chat" Column="0" Width="100%" MinHeight="40px" />
              </Container>
              <Text Name="Status" Row="5" Content="" FontSize="11" Foreground="TextSecondary" Visibility="Collapsed" />
            </Page>
            """;
        return (Page)new HavenMarkupParser().Parse(markup, "ChatSidebar.hui");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ModeButton.Invoked -= OnModeButtonInvoked;
        ChatMode.Invoked -= OnChatModeInvoked;
        StudyMode.Invoked -= OnStudyModeInvoked;
        TasksMode.Invoked -= OnTasksModeInvoked;
        Search.TextChanged -= OnSearchTextChanged;
        NewChat.Invoked -= OnNewChatInvoked;
        NewGroup.Invoked -= OnNewGroupInvoked;
        _activePopup?.Dismiss();
        _activePopup = null;
        _entryState.Clear();
        _items.Clear();
    }
}
