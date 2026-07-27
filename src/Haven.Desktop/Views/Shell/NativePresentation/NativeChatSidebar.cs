using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell.NativePresentation;

/// <summary>
/// New Haven's repository-backed Chat sidebar. Every visual and interaction is
/// constructed in code-behind; no binding or Classic sidebar control participates.
/// </summary>
internal sealed class NativeChatSidebar : UserControl, IDisposable
{
    private static readonly IBrush SurfaceBrush = Solid("#FAFCF8");
    private static readonly IBrush HoverBrush = Solid("#EDF3ED");
    private static readonly IBrush ActiveBrush = Solid("#BCEFF3");
    private static readonly IBrush DividerBrush = Solid("#E1E8E0");
    private static readonly IBrush MutedBrush = Solid("#5F6862");
    private static readonly IBrush UnreadBrush = Solid("#F278D1");

    private readonly IConversationRepository _conversations;
    private readonly IContainerRepository _containers;
    private readonly Func<Conversation, Task> _openConversation;
    private readonly Func<HavenMode, Guid?, Task> _startChat;
    private readonly Func<ContainerDefinition, Task> _openGroup;
    private readonly Func<HavenMode, Task> _switchMode;
    private readonly NativeChatUiStateStore _stateStore;
    private readonly CancellationTokenSource _lifetime = new();

    private readonly TextBox _searchBox;
    private readonly StackPanel _pinnedPanel;
    private readonly StackPanel _unreadPanel;
    private readonly StackPanel _groupsPanel;
    private readonly StackPanel _chatsPanel;
    private readonly TextBlock _pinnedHeading;
    private readonly TextBlock _unreadHeading;
    private readonly TextBlock _groupsHeading;
    private readonly TextBlock _chatsHeading;
    private readonly TextBlock _status;
    private readonly TextBlock _modeLabel;

    private IReadOnlyList<Conversation> _conversationRows = [];
    private IReadOnlyList<ContainerDefinition> _groupRows = [];
    private IReadOnlyDictionary<Guid, NativeChatItemState> _states =
        new Dictionary<Guid, NativeChatItemState>();
    private Guid? _activeConversationId;
    private Guid? _activeGroupId;
    private HavenMode _currentMode = HavenMode.Chat;
    private bool _showAllGroups;
    private bool _refreshing;
    private bool _refreshPending;
    private bool _disposed;

    public NativeChatSidebar(
        IConversationRepository conversations,
        IContainerRepository containers,
        Func<Conversation, Task> openConversation,
        Func<HavenMode, Guid?, Task> startChat,
        Func<ContainerDefinition, Task> openGroup,
        Func<HavenMode, Task> switchMode,
        NativeChatUiStateStore? stateStore = null)
    {
        _conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _openConversation = openConversation ?? throw new ArgumentNullException(nameof(openConversation));
        _startChat = startChat ?? throw new ArgumentNullException(nameof(startChat));
        _openGroup = openGroup ?? throw new ArgumentNullException(nameof(openGroup));
        _switchMode = switchMode ?? throw new ArgumentNullException(nameof(switchMode));
        _stateStore = stateStore ?? new NativeChatUiStateStore();

        _searchBox = new TextBox
        {
            PlaceholderText = "Search",
            MinHeight = 44,
            Padding = new Thickness(40, 9, 12, 9),
            CornerRadius = new CornerRadius(13),
            Background = Solid("#F3F5F2"),
            BorderThickness = new Thickness(0),
            FontWeight = FontWeight.SemiBold
        };
        _searchBox.TextChanged += OnSearchChanged;
        AutomationProperties.SetName(_searchBox, "Search chats and Chat Groups");

        _pinnedHeading = SectionHeading("Pinned");
        _unreadHeading = SectionHeading("Unread Notifications");
        _groupsHeading = SectionHeading("Chat Groups");
        _chatsHeading = SectionHeading("Chats");
        _pinnedPanel = SectionPanel();
        _unreadPanel = SectionPanel();
        _groupsPanel = SectionPanel();
        _chatsPanel = SectionPanel();
        _status = new TextBlock
        {
            FontSize = 11,
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 6, 4, 0)
        };
        _modeLabel = new TextBlock { Text = "Chat", FontWeight = FontWeight.Bold };

        Content = BuildLayout();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    public async Task RefreshAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_refreshing)
        {
            _refreshPending = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshPending = false;
                var conversationTask = _conversations.GetRecentAsync(
                    _currentMode,
                    500,
                    _lifetime.Token);
                var groupTask = _containers.GetByModeAsync(_currentMode, _lifetime.Token);
                var stateTask = _stateStore.GetAllAsync(_lifetime.Token);
                await Task.WhenAll(conversationTask, groupTask, stateTask);

                _conversationRows = conversationTask.Result
                    .Where(item => !item.IsArchived && item.Kind != ConversationKind.Call)
                    .ToArray();
                _groupRows = groupTask.Result.Where(item => !item.IsArchived).ToArray();
                _states = stateTask.Result;

                if (Dispatcher.UIThread.CheckAccess())
                {
                    Render();
                }
                else
                {
                    await Dispatcher.UIThread.InvokeAsync(Render);
                }
            }
            while (_refreshPending && !_disposed);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _status.Text = "Chat history could not be refreshed: " + exception.Message);
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void SetActiveConversation(Guid? conversationId, Guid? groupId)
    {
        _activeConversationId = conversationId;
        _activeGroupId = groupId;
        if (!_disposed)
        {
            Render();
        }
    }

    public void SetMode(HavenMode mode)
    {
        if (mode == HavenMode.Studio || _currentMode == mode)
        {
            return;
        }

        _currentMode = mode;
        _modeLabel.Text = ModeName(mode);
        AutomationProperties.SetName(_modeLabel, $"Current mode: {ModeName(mode)}");
        _groupsHeading.Text = GroupName(mode, plural: true);
        _chatsHeading.Text = mode == HavenMode.Teach ? "Study Chats" : mode == HavenMode.Do ? "Research Chats" : "Chats";
        _searchBox.PlaceholderText = $"Search {ModeName(mode)}";
        _activeConversationId = null;
        _activeGroupId = null;
        _ = RefreshAsync();
    }

    private Control BuildLayout()
    {
        var modeButton = new Button
        {
            Padding = new Thickness(10, 7),
            Background = Solid("#F3F5F2"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(999),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    _modeLabel,
                    new HavenIcon { IconKey = "chevron-down", Width = 12, Height = 12 }
                }
            }
        };
        modeButton.Flyout = BuildModeFlyout();
        AutomationProperties.SetName(modeButton, "Select Chat mode");

        var modeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "Mode:",
                    FontWeight = FontWeight.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                },
                modeButton
            }
        };

        var searchHost = new Grid();
        searchHost.Children.Add(_searchBox);
        searchHost.Children.Add(new HavenIcon
        {
            IconKey = "search",
            Width = 20,
            Height = 20,
            Margin = new Thickness(12, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Opacity = 0.72
        });

        var sections = new StackPanel
        {
            Spacing = 7,
            Children =
            {
                _pinnedHeading,
                _pinnedPanel,
                _unreadHeading,
                _unreadPanel,
                _groupsHeading,
                _groupsPanel,
                _chatsHeading,
                _chatsPanel,
                _status
            }
        };

        var newChat = ActionButton("plus", NewChatLabel(_currentMode));
        newChat.Click += async (_, _) => await StartChatAsync(null);
        var newGroup = ActionButton("folder", $"New {GroupName(_currentMode, plural: false)}");
        newGroup.Click += (_, _) => ShowCreateGroupFlyout(newGroup);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8,
            Children = { newChat, WithColumn(newGroup, 1) }
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 12,
            Children =
            {
                modeRow,
                WithRow(searchHost, 1),
                WithRow(new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = sections
                }, 2),
                WithRow(footer, 3)
            }
        };

        return new Border
        {
            Width = 286,
            Padding = new Thickness(14),
            Background = SurfaceBrush,
            BorderBrush = DividerBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Child = root
        };
    }

    private Flyout BuildModeFlyout()
    {
        var panel = new StackPanel { Width = 210, Spacing = 2, Margin = new Thickness(6) };
        panel.Children.Add(ModeButton("chat", "Chat", HavenMode.Chat));
        panel.Children.Add(ModeButton("teach", "Study", HavenMode.Teach));
        panel.Children.Add(ModeButton("search", "Research", HavenMode.Do));
        return new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft, Content = panel };
    }

    private Button ModeButton(string icon, string label, HavenMode mode)
    {
        var button = NavigationButton(icon, label, false, false);
        button.Click += async (_, _) => await _switchMode(mode);
        return button;
    }

    private void Render()
    {
        if (_disposed)
        {
            return;
        }

        var query = _searchBox.Text?.Trim() ?? string.Empty;
        bool Matches(string value) => query.Length == 0 || value.Contains(query, StringComparison.OrdinalIgnoreCase);

        var groups = _groupRows
            .Where(group => Matches(group.Name))
            .OrderByDescending(GroupUpdatedAt)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var conversations = _conversationRows
            .Where(chat => Matches(chat.Title))
            .OrderByDescending(chat => chat.UpdatedAt)
            .ThenBy(chat => chat.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _pinnedPanel.Children.Clear();
        foreach (var group in groups.Where(IsGroupPinned))
        {
            _pinnedPanel.Children.Add(BuildGroupRow(group, showChildren: false));
        }
        foreach (var chat in conversations.Where(chat => chat.IsPinned))
        {
            _pinnedPanel.Children.Add(BuildConversationRow(chat, indent: false));
        }

        _unreadPanel.Children.Clear();
        foreach (var chat in conversations.Where(chat => !chat.IsPinned && IsUnread(chat)))
        {
            _unreadPanel.Children.Add(BuildConversationRow(chat, indent: false));
        }

        _groupsPanel.Children.Clear();
        var regularGroups = groups.Where(group => !IsGroupPinned(group)).ToArray();
        var visibleGroups = _showAllGroups ? regularGroups : regularGroups.Take(4).ToArray();
        foreach (var group in visibleGroups)
        {
            _groupsPanel.Children.Add(BuildGroupRow(group, showChildren: true));
        }
        if (regularGroups.Length > 4)
        {
            var viewAll = TextButton(_showAllGroups ? "Show Less" : "View All");
            viewAll.Click += (_, _) =>
            {
                _showAllGroups = !_showAllGroups;
                Render();
            };
            _groupsPanel.Children.Add(viewAll);
        }

        _chatsPanel.Children.Clear();
        foreach (var chat in conversations.Where(chat => chat.ContainerId is null && !chat.IsPinned && !IsUnread(chat)))
        {
            _chatsPanel.Children.Add(BuildConversationRow(chat, indent: false));
        }

        SetSectionVisibility(_pinnedHeading, _pinnedPanel);
        SetSectionVisibility(_unreadHeading, _unreadPanel);
        _groupsHeading.IsVisible = true;
        _groupsPanel.IsVisible = true;
        _chatsHeading.IsVisible = true;
        _chatsPanel.IsVisible = true;
        _status.Text = conversations.Length == 0 && groups.Length == 0
            ? $"No saved {ModeName(_currentMode)} chats or {GroupName(_currentMode, plural: true)} yet."
            : string.Empty;
    }

    private Control BuildGroupRow(ContainerDefinition group, bool showChildren)
    {
        var state = State(group.Id);
        var expanded = showChildren && state.IsExpanded;
        var groupButton = NavigationButton("folder", group.Name, _activeGroupId == group.Id, IsGroupUnread(group));
        groupButton.ContextMenu = BuildGroupContextMenu(group, state);
        groupButton.Click += async (_, _) =>
        {
            await _stateStore.MarkReadAsync(group.Id, DateTimeOffset.UtcNow, _lifetime.Token);
            await _openGroup(group);
            _activeGroupId = group.Id;
            await RefreshAsync();
        };

        if (!showChildren)
        {
            return groupButton;
        }

        var toggle = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = new HavenIcon
            {
                IconKey = expanded ? "chevron-down" : "chevron-right",
                Width = 11,
                Height = 11
            }
        };
        toggle.Click += async (_, _) =>
        {
            await _stateStore.SetExpandedAsync(group.Id, !expanded, _lifetime.Token);
            await RefreshAsync();
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { toggle, WithColumn(groupButton, 1) }
        };

        var stack = new StackPanel { Spacing = 3, Children = { header } };
        if (expanded)
        {
            foreach (var chat in _conversationRows
                         .Where(item => item.ContainerId == group.Id && !item.IsArchived && item.Kind != ConversationKind.Call)
                         .OrderByDescending(item => item.UpdatedAt))
            {
                stack.Children.Add(BuildConversationRow(chat, indent: true));
            }

            var add = TextButton("+ New chat in " + group.Name);
            add.Margin = new Thickness(32, 0, 0, 0);
            add.Click += async (_, _) => await StartChatAsync(group.Id);
            stack.Children.Add(add);
        }

        return stack;
    }

    private Button BuildConversationRow(Conversation chat, bool indent)
    {
        var button = NavigationButton("chat", chat.Title, _activeConversationId == chat.Id, IsUnread(chat));
        if (indent)
        {
            button.Margin = new Thickness(32, 0, 0, 0);
        }
        button.ContextMenu = BuildConversationContextMenu(chat);
        button.Click += async (_, _) =>
        {
            await _stateStore.MarkReadAsync(chat.Id, DateTimeOffset.UtcNow, _lifetime.Token);
            _activeConversationId = chat.Id;
            _activeGroupId = chat.ContainerId;
            await _openConversation(chat);
            await RefreshAsync();
        };
        return button;
    }

    private ContextMenu BuildConversationContextMenu(Conversation chat)
    {
        var rename = MenuItem("Rename", () => ShowRenameConversation(chat));
        var pin = MenuItem(chat.IsPinned ? "Unpin" : "Pin", () => _ = ToggleConversationPinAsync(chat));
        var read = MenuItem(IsUnread(chat) ? "Mark read" : "Mark unread", () => _ = ToggleConversationReadAsync(chat));
        var move = new MenuItem { Header = $"Move to {GroupName(_currentMode, plural: false)}" };
        var moveItems = new List<MenuItem>
        {
            MenuItem("No group", () => _ = MoveConversationAsync(chat, null))
        };
        moveItems.AddRange(_groupRows
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => MenuItem(group.Name, () => _ = MoveConversationAsync(chat, group.Id))));
        move.ItemsSource = moveItems;
        var archive = MenuItem("Archive", () => _ = ArchiveConversationAsync(chat));
        var delete = MenuItem("Delete", () => _ = DeleteConversationAsync(chat));
        return new ContextMenu { ItemsSource = new object[] { rename, pin, read, move, archive, delete } };
    }

    private ContextMenu BuildGroupContextMenu(ContainerDefinition group, NativeChatItemState state)
    {
        var rename = MenuItem("Rename", () => ShowRenameGroup(group));
        var pin = MenuItem(state.IsPinned ? "Unpin" : "Pin", () => _ = ToggleGroupPinAsync(group, state));
        var expand = MenuItem(state.IsExpanded ? "Collapse" : "Expand", () => _ = ToggleGroupExpandedAsync(group, state));
        var create = MenuItem("New chat", () => _ = StartChatAsync(group.Id));
        var archive = MenuItem("Archive", () => _ = ArchiveGroupAsync(group));
        var delete = MenuItem("Delete and detach chats", () => _ = DeleteGroupAsync(group));
        return new ContextMenu { ItemsSource = new object[] { rename, pin, expand, create, archive, delete } };
    }

    private async Task StartChatAsync(Guid? groupId)
    {
        if (groupId is Guid id)
        {
            await _stateStore.SetExpandedAsync(id, true, _lifetime.Token);
        }
        await _startChat(_currentMode, groupId);
        _activeConversationId = null;
        _activeGroupId = groupId;
        await RefreshAsync();
    }

    private void ShowCreateGroupFlyout(Control anchor)
    {
        var groupName = GroupName(_currentMode, plural: false);
        var editor = new TextBox { PlaceholderText = groupName + " name", MinWidth = 250 };
        var create = new Button { Content = "Create " + groupName, HorizontalAlignment = HorizontalAlignment.Stretch };
        var flyout = new Flyout
        {
            Placement = PlacementMode.TopEdgeAlignedLeft,
            Content = new StackPanel
            {
                Width = 280,
                Spacing = 10,
                Margin = new Thickness(8),
                Children = { SectionHeading("New " + groupName), editor, create }
            }
        };
        create.Click += async (_, _) =>
        {
            var name = editor.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }
            var now = DateTimeOffset.UtcNow;
            var group = new ContainerDefinition(Guid.NewGuid(), _currentMode, name, null, string.Empty, string.Empty, now, now);
            if (_currentMode == HavenMode.Teach)
            {
                await _containers.CreateSubjectAsync(group, _lifetime.Token);
            }
            else
            {
                await _containers.UpsertAsync(group, _lifetime.Token);
            }
            await _stateStore.SetExpandedAsync(group.Id, true, _lifetime.Token);
            flyout.Hide();
            await _openGroup(group);
            _activeGroupId = group.Id;
            await RefreshAsync();
        };
        flyout.ShowAt(anchor);
        Dispatcher.UIThread.Post(() => editor.Focus(), DispatcherPriority.Background);
    }

    private void ShowRenameConversation(Conversation chat)
    {
        ShowRenameFlyout(chat.Title, async title =>
        {
            await _conversations.UpsertConversationAsync(
                chat with { Title = title, UpdatedAt = DateTimeOffset.UtcNow },
                _lifetime.Token);
            await RefreshAsync();
        });
    }

    private void ShowRenameGroup(ContainerDefinition group)
    {
        ShowRenameFlyout(group.Name, async name =>
        {
            await _containers.UpsertAsync(
                group with { Name = name, UpdatedAt = DateTimeOffset.UtcNow },
                _lifetime.Token);
            await RefreshAsync();
        });
    }

    private void ShowRenameFlyout(string currentName, Func<string, Task> save)
    {
        var editor = new TextBox { Text = currentName, MinWidth = 260 };
        var apply = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Stretch };
        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Width = 290,
                Spacing = 10,
                Margin = new Thickness(8),
                Children = { SectionHeading("Rename"), editor, apply }
            }
        };
        apply.Click += async (_, _) =>
        {
            var value = editor.Text?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            await save(value);
            flyout.Hide();
        };
        flyout.ShowAt(_searchBox);
        Dispatcher.UIThread.Post(() => editor.Focus(), DispatcherPriority.Background);
    }

    private async Task ToggleConversationPinAsync(Conversation chat)
    {
        await _conversations.UpsertConversationAsync(
            chat with { IsPinned = !chat.IsPinned, UpdatedAt = DateTimeOffset.UtcNow },
            _lifetime.Token);
        await RefreshAsync();
    }

    private async Task ToggleConversationReadAsync(Conversation chat)
    {
        if (IsUnread(chat))
        {
            await _stateStore.MarkReadAsync(chat.Id, DateTimeOffset.UtcNow, _lifetime.Token);
        }
        else
        {
            await _stateStore.MarkUnreadAsync(chat.Id, _lifetime.Token);
        }
        await RefreshAsync();
    }

    private async Task MoveConversationAsync(Conversation chat, Guid? groupId)
    {
        await _conversations.UpsertConversationAsync(
            chat with { ContainerId = groupId, UpdatedAt = DateTimeOffset.UtcNow },
            _lifetime.Token);
        await RefreshAsync();
    }

    private async Task ArchiveConversationAsync(Conversation chat)
    {
        await _conversations.UpsertConversationAsync(
            chat with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow },
            _lifetime.Token);
        await RefreshAsync();
    }

    private async Task DeleteConversationAsync(Conversation chat)
    {
        await _conversations.DeleteConversationAsync(chat.Id, _lifetime.Token);
        if (_activeConversationId == chat.Id)
        {
            await _startChat(_currentMode, null);
            _activeConversationId = null;
            _activeGroupId = null;
        }
        await RefreshAsync();
    }

    private async Task ToggleGroupPinAsync(ContainerDefinition group, NativeChatItemState state)
    {
        await _stateStore.SetPinnedAsync(group.Id, !state.IsPinned, _lifetime.Token);
        await RefreshAsync();
    }

    private async Task ToggleGroupExpandedAsync(ContainerDefinition group, NativeChatItemState state)
    {
        await _stateStore.SetExpandedAsync(group.Id, !state.IsExpanded, _lifetime.Token);
        await RefreshAsync();
    }

    private async Task ArchiveGroupAsync(ContainerDefinition group)
    {
        await _containers.UpsertAsync(
            group with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow },
            _lifetime.Token);
        await RefreshAsync();
    }

    private async Task DeleteGroupAsync(ContainerDefinition group)
    {
        await _containers.DeleteAndDetachConversationsAsync(group.Id, _lifetime.Token);
        if (_activeGroupId == group.Id)
        {
            _activeGroupId = null;
        }
        await RefreshAsync();
    }

    private NativeChatItemState State(Guid id) =>
        _states.TryGetValue(id, out var state) ? state : NativeChatItemState.Empty;

    private bool IsUnread(Conversation chat) =>
        _activeConversationId != chat.Id && State(chat.Id).IsUnread(chat.UpdatedAt);

    private bool IsGroupPinned(ContainerDefinition group) => State(group.Id).IsPinned;

    private bool IsGroupUnread(ContainerDefinition group) =>
        _activeGroupId != group.Id && State(group.Id).IsUnread(GroupUpdatedAt(group));

    private DateTimeOffset GroupUpdatedAt(ContainerDefinition group)
    {
        var childUpdate = _conversationRows
            .Where(chat => chat.ContainerId == group.Id)
            .Select(chat => chat.UpdatedAt)
            .DefaultIfEmpty(group.UpdatedAt)
            .Max();
        return childUpdate > group.UpdatedAt ? childUpdate : group.UpdatedAt;
    }

    private static Button NavigationButton(string icon, string text, bool active, bool unread)
    {
        var iconControl = new HavenIcon
        {
            IconKey = icon,
            Width = 18,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var unreadDot = new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = UnreadBrush,
            IsVisible = unread,
            VerticalAlignment = VerticalAlignment.Center
        };
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 9,
            Children = { iconControl, WithColumn(label, 1), WithColumn(unreadDot, 2) }
        };
        var button = new Button
        {
            Content = content,
            MinHeight = 39,
            Padding = new Thickness(10, 7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = active ? ActiveBrush : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(11)
        };
        button.PointerEntered += (_, _) =>
        {
            if (!active) button.Background = HoverBrush;
        };
        button.PointerExited += (_, _) =>
        {
            if (!active) button.Background = Brushes.Transparent;
        };
        AutomationProperties.SetName(button, text + (unread ? ", unread" : string.Empty));
        return button;
    }

    private static Button ActionButton(string icon, string text)
    {
        var button = NavigationButton(icon, text, false, false);
        button.Background = Solid("#F3F5F2");
        return button;
    }

    private static Button TextButton(string text) => new()
    {
        Content = text,
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(8, 6),
        HorizontalAlignment = HorizontalAlignment.Left,
        HorizontalContentAlignment = HorizontalAlignment.Left,
        FontWeight = FontWeight.SemiBold
    };

    private static TextBlock SectionHeading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.Bold,
        FontSize = 12,
        Margin = new Thickness(6, 8, 0, 0)
    };

    private static string ModeName(HavenMode mode) => mode switch
    {
        HavenMode.Teach => "Study",
        HavenMode.Do => "Research",
        _ => "Chat"
    };

    private static string GroupName(HavenMode mode, bool plural) => mode switch
    {
        HavenMode.Teach => plural ? "Subjects" : "Subject",
        HavenMode.Do => plural ? "Research Groups" : "Research Group",
        _ => plural ? "Chat Groups" : "Chat Group"
    };

    private static string NewChatLabel(HavenMode mode) => mode switch
    {
        HavenMode.Teach => "New Study Chat",
        HavenMode.Do => "New Research",
        _ => "New Chat"
    };

    private static StackPanel SectionPanel() => new() { Spacing = 3 };

    private static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static void SetSectionVisibility(Control heading, Panel panel)
    {
        var visible = panel.Children.Count > 0;
        heading.IsVisible = visible;
        panel.IsVisible = visible;
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => Render();

    private async void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => await RefreshAsync();

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
    }

    private static IBrush Solid(string color) => new SolidColorBrush(Color.Parse(color));

    private static T WithRow<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        AttachedToVisualTree -= OnAttached;
        DetachedFromVisualTree -= OnDetached;
        _searchBox.TextChanged -= OnSearchChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
