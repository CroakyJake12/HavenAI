using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;

namespace Haven.Desktop.Views.Shell.NativePresentation;

/// <summary>
/// Platform host and production coordinator for the Haven-owned Chat sidebar. Avalonia supplies only
/// the single scene host; visible sidebar controls and runtime rows are Haven.UI/DynamicUI.
/// </summary>
internal sealed class NativeChatSidebar : UserControl, IDisposable
{
    private readonly IConversationRepository _conversations;
    private readonly IContainerRepository _containers;
    private readonly Func<Conversation, Task> _openConversation;
    private readonly Func<HavenMode, Guid?, Task> _startChat;
    private readonly Func<ContainerDefinition, Task> _openGroup;
    private readonly NativeChatUiStateStore _stateStore;
    private readonly IConversationProductionRepository? _production;
    private readonly SpaceRegistry? _spaces;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ChatSidebarHavenScene _scene;

    private IReadOnlyList<Conversation> _conversationRows = [];
    private IReadOnlyList<ContainerDefinition> _groupRows = [];
    private IReadOnlyList<MessageAttachment> _fileRows = [];
    private IReadOnlyList<SpaceDefinition> _spaceRows = [];
    private IReadOnlyDictionary<Guid, NativeChatItemState> _states = new Dictionary<Guid, NativeChatItemState>();
    private Guid? _activeConversationId;
    private Guid? _activeGroupId;
    private Guid? _currentSpaceId;
    private HavenMode _currentMode = HavenMode.Chat;
    private string _query = string.Empty;
    private bool _refreshing;
    private bool _refreshPending;
    private bool _startingChat;
    private bool _disposed;

    public NativeChatSidebar(
        IConversationRepository conversations,
        IContainerRepository containers,
        Func<Conversation, Task> openConversation,
        Func<HavenMode, Guid?, Task> startChat,
        Func<ContainerDefinition, Task> openGroup,
        NativeChatUiStateStore? stateStore = null,
        IConversationProductionRepository? production = null,
        SpaceRegistry? spaces = null)
    {
        _conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _openConversation = openConversation ?? throw new ArgumentNullException(nameof(openConversation));
        _startChat = startChat ?? throw new ArgumentNullException(nameof(startChat));
        _openGroup = openGroup ?? throw new ArgumentNullException(nameof(openGroup));
        _stateStore = stateStore ?? new NativeChatUiStateStore();
        _production = production;
        _spaces = spaces;

        _scene = new ChatSidebarHavenScene();
        SceneHost = new HavenSceneControl { Root = _scene.Root };
        AutomationProperties.SetAutomationId(this, "HavenNativeChatSidebar");
        AutomationProperties.SetName(this, "Haven-native Chat sidebar");
        AutomationProperties.SetAutomationId(SceneHost, "HavenNativeChatSidebarScene");
        Content = SceneHost;

        _scene.SearchChanged += OnSearchChanged;
        _scene.NewChatRequested += OnNewChatRequested;
        _scene.NewGroupRequested += OnNewGroupRequested;
        _scene.SpacePickerRequested += OnSpacePickerRequested;
        _scene.ConversationActionRequested += OnConversationActionRequested;
        _scene.GroupActionRequested += OnGroupActionRequested;
        _scene.FileRequested += OnFileRequested;
        _ = LoadSpaceScopeAsync();
        _ = RefreshAsync();
    }

    internal HavenSceneControl SceneHost { get; }
    internal ChatSidebarHavenScene Scene => _scene;

    /// <summary>The Space the sidebar is currently scoped to, or null for unscoped Chat.</summary>
    internal Guid? CurrentSpaceId => _currentSpaceId;

    /// <summary>Raised when the user asks to manage Spaces from the sidebar picker.</summary>
    internal event EventHandler? ManageSpacesRequested;

    private async Task LoadSpaceScopeAsync()
    {
        if (_spaces is null || _disposed) return;
        try
        {
            _spaceRows = await _spaces.GetAllAsync(false, _lifetime.Token).ConfigureAwait(false);
            _currentSpaceId = await _spaces.GetCurrentSpaceIdAsync(_lifetime.Token).ConfigureAwait(false);
            if (_disposed) return;
            ApplySpaceScopeToScene();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
        }
    }

    private void ApplySpaceScopeToScene()
    {
        var name = _currentSpaceId is { } id
            ? _spaceRows.FirstOrDefault(space => space.Id == id)?.Name
            : null;
        if (_currentSpaceId is { } && name is null) _currentSpaceId = null;
        _scene.SetSpaceScope(name);
    }

    private async void OnSpacePickerRequested(object? sender, EventArgs e)
    {
        if (_spaces is null) return;
        try
        {
            _spaceRows = await _spaces.GetAllAsync(false, _lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _scene.SetStatus("Spaces could not be loaded: " + exception.Message);
            return;
        }
        if (_disposed) return;

        var choices = new List<(string Label, Action Action)>
        {
            ("General (no Space)", () => _ = SelectSpaceAsync(null))
        };
        choices.AddRange(_spaceRows
            .OrderBy(space => space.Name, StringComparer.OrdinalIgnoreCase)
            .Select(space => (space.Name, (Action)(() => _ = SelectSpaceAsync(space.Id)))));
        choices.Add(("Manage Spaces…", () => ManageSpacesRequested?.Invoke(this, EventArgs.Empty)));
        _scene.ShowChoices("Spaces", choices);
    }

    internal Task SelectSpaceFromShellAsync(Guid? spaceId) => SelectSpaceAsync(spaceId);

    internal async Task ReloadSpaceScopeAsync()
    {
        await LoadSpaceScopeAsync().ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    private async Task SelectSpaceAsync(Guid? spaceId)
    {
        if (_spaces is not null)
            await _spaces.SetCurrentSpaceIdAsync(spaceId, _lifetime.Token).ConfigureAwait(false);
        _currentSpaceId = spaceId;
        if (_disposed) return;
        ApplySpaceScopeToScene();
        await RefreshAsync();

        var existing = _conversationRows
            .Where(chat => !chat.IsArchived && chat.Kind != ConversationKind.Call)
            .Where(chat => spaceId is null ? chat.SpaceId is null : chat.SpaceId == spaceId)
            .OrderByDescending(chat => chat.UpdatedAt)
            .FirstOrDefault();
        if (existing is not null)
        {
            await _stateStore.MarkReadAsync(existing.Id, DateTimeOffset.UtcNow, _lifetime.Token).ConfigureAwait(false);
            _activeConversationId = existing.Id;
            _activeGroupId = existing.ContainerId;
            await _openConversation(existing).ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
            return;
        }

        _scene.SetStatus("This Space has no conversations yet. Starting a new Chat in this Space.");
        await StartChatAsync(null).ConfigureAwait(false);
    }

    public async Task RefreshAsync()
    {
        if (_disposed) return;
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
                var conversationTask = _conversations.GetRecentAsync(_currentMode, 500, _lifetime.Token);
                var groupTask = _containers.GetByModeAsync(_currentMode, _lifetime.Token);
                var stateTask = _stateStore.GetAllAsync(_lifetime.Token);
                var fileTask = _production is null || _currentMode != HavenMode.Chat
                    ? Task.FromResult<IReadOnlyList<MessageAttachment>>([])
                    : _production.GetRecentAttachmentsAsync(100, _lifetime.Token);
                await Task.WhenAll(conversationTask, groupTask, stateTask, fileTask).ConfigureAwait(false);

                _conversationRows = conversationTask.Result
                    .Where(item => !item.IsArchived && item.Kind != ConversationKind.Call)
                    .ToArray();
                _groupRows = groupTask.Result.Where(item => !item.IsArchived).ToArray();
                _fileRows = fileTask.Result;
                _states = stateTask.Result;

                if (Dispatcher.UIThread.CheckAccess()) Render();
                else await Dispatcher.UIThread.InvokeAsync(Render);
            }
            while (_refreshPending && !_disposed);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => _scene.SetStatus("Chat history could not be refreshed: " + exception.Message));
        }
        finally
        {
            _refreshing = false;
        }
    }

    internal HavenMode CurrentMode => _currentMode;
    internal Guid? ActiveGroupId => _activeGroupId;
    internal event EventHandler<NativeChatSidebarContext>? ContextChanged;

    public void SetActiveConversation(Guid? conversationId, Guid? groupId)
    {
        _activeConversationId = conversationId;
        _activeGroupId = groupId;
        if (_disposed) return;
        Render();
        ContextChanged?.Invoke(this, new NativeChatSidebarContext(_currentMode, _activeGroupId));
    }

    public void SetMode(HavenMode mode)
    {
        if (mode == HavenMode.Studio || _currentMode == mode) return;
        _currentMode = mode;
        _activeConversationId = null;
        _activeGroupId = null;
        _scene.SetMode(mode);
        ContextChanged?.Invoke(this, new NativeChatSidebarContext(_currentMode, _activeGroupId));
        _ = RefreshAsync();
    }

    private void Render()
    {
        if (_disposed) return;
        bool Matches(string value) => _query.Length == 0 || value.Contains(_query, StringComparison.OrdinalIgnoreCase);
        bool InScope(Conversation chat) => _currentSpaceId is null
            ? chat.SpaceId is null
            : chat.SpaceId == _currentSpaceId;

        var scoped = _currentSpaceId is not null;
        var groups = scoped
            ? []
            : _groupRows
                .Where(group => Matches(group.Name))
                .OrderByDescending(GroupUpdatedAt)
                .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        var conversations = _conversationRows
            .Where(chat => InScope(chat) && Matches(chat.Title))
            .OrderByDescending(chat => chat.UpdatedAt)
            .ThenBy(chat => chat.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var pinned = new List<ChatSidebarEntry>();
        pinned.AddRange(groups.Where(IsGroupPinned).Select(GroupEntry));
        pinned.AddRange(conversations.Where(chat => chat.IsPinned).Select(chat => ConversationEntry(chat, false)));

        var unread = conversations
            .Where(chat => !chat.IsPinned && IsUnread(chat))
            .Select(chat => ConversationEntry(chat, false))
            .ToArray();

        var groupEntries = new List<ChatSidebarEntry>();
        foreach (var group in groups.Where(group => !IsGroupPinned(group)))
        {
            var state = State(group.Id);
            groupEntries.Add(GroupEntry(group));
            if (!state.IsExpanded) continue;
            groupEntries.AddRange(_conversationRows
                .Where(chat => chat.ContainerId == group.Id && !chat.IsArchived && chat.Kind != ConversationKind.Call && InScope(chat))
                .OrderByDescending(chat => chat.UpdatedAt)
                .Select(chat => ConversationEntry(chat, true)));
        }

        var sourceConversationIds = conversations.Select(chat => chat.Id).ToHashSet();
        var files = _currentMode == HavenMode.Chat
            ? _fileRows
                .Where(file => sourceConversationIds.Contains(file.ConversationId) && Matches(file.OriginalName))
                .Take(20)
                .Select(FileEntry)
                .ToArray()
            : [];

        var chats = conversations
            .Where(chat => chat.ContainerId is null && !chat.IsPinned && !IsUnread(chat))
            .Select(chat => ConversationEntry(chat, false))
            .ToArray();

        _scene.SetRows(pinned, unread, groupEntries, files, chats);
        _scene.SetStatus(conversations.Length == 0 && groups.Length == 0 && files.Length == 0
            ? $"No saved {ModeName(_currentMode)} chats or {GroupName(_currentMode, plural: true)} yet."
            : null);
    }

    private ChatSidebarEntry ConversationEntry(Conversation chat, bool indented) => new(
        ChatSidebarEntryKind.Conversation,
        chat.Id,
        chat.Title,
        _activeConversationId == chat.Id,
        IsUnread(chat),
        chat.IsPinned,
        false,
        indented);

    private ChatSidebarEntry FileEntry(MessageAttachment file) => new(
        ChatSidebarEntryKind.File,
        file.Id,
        file.OriginalName,
        _activeConversationId == file.ConversationId,
        false,
        false);

    private ChatSidebarEntry GroupEntry(ContainerDefinition group)
    {
        var state = State(group.Id);
        return new ChatSidebarEntry(
            ChatSidebarEntryKind.Group,
            group.Id,
            group.Name,
            _activeGroupId == group.Id,
            IsGroupUnread(group),
            state.IsPinned,
            state.IsExpanded);
    }

    private void OnSearchChanged(object? sender, string query)
    {
        _query = query;
        Render();
    }

    private async void OnNewChatRequested(object? sender, EventArgs e)
    {
        try { await StartChatAsync(null); }
        catch (Exception exception) when (exception is InvalidOperationException or IOException) { _scene.SetStatus(exception.Message); }
    }

    private void OnNewGroupRequested(object? sender, EventArgs e) => ShowCreateGroupPrompt();

    private async void OnFileRequested(object? sender, Guid attachmentId)
    {
        var file = _fileRows.FirstOrDefault(item => item.Id == attachmentId);
        if (file is null) return;
        var chat = _conversationRows.FirstOrDefault(item => item.Id == file.ConversationId);
        if (chat is null)
        {
            _scene.SetStatus("The chat that owns this file is no longer available.");
            return;
        }

        try
        {
            await _stateStore.MarkReadAsync(chat.Id, DateTimeOffset.UtcNow, _lifetime.Token);
            _activeConversationId = chat.Id;
            _activeGroupId = chat.ContainerId;
            await _openConversation(chat);
            await RefreshAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _scene.SetStatus(exception.Message);
        }
    }

    private async void OnConversationActionRequested(object? sender, ChatSidebarConversationRequest request)
    {
        var chat = _conversationRows.FirstOrDefault(item => item.Id == request.ConversationId);
        if (chat is null) return;
        try
        {
            switch (request.Action)
            {
                case ChatSidebarConversationAction.Open:
                    await _stateStore.MarkReadAsync(chat.Id, DateTimeOffset.UtcNow, _lifetime.Token);
                    _activeConversationId = chat.Id;
                    _activeGroupId = chat.ContainerId;
                    await _openConversation(chat);
                    await RefreshAsync();
                    break;
                case ChatSidebarConversationAction.Rename:
                    _scene.ShowTextPrompt("Rename chat", chat.Title, "Save", async title =>
                    {
                        await _conversations.UpsertConversationAsync(chat with { Title = title, UpdatedAt = DateTimeOffset.UtcNow }, _lifetime.Token);
                        await RefreshAsync();
                    });
                    break;
                case ChatSidebarConversationAction.TogglePin:
                    await ToggleConversationPinAsync(chat);
                    break;
                case ChatSidebarConversationAction.ToggleRead:
                    await ToggleConversationReadAsync(chat);
                    break;
                case ChatSidebarConversationAction.Move:
                    ShowMoveChoices(chat);
                    break;
                case ChatSidebarConversationAction.MoveToSpace:
                    ShowSpaceChoices(chat);
                    break;
                case ChatSidebarConversationAction.Archive:
                    await ArchiveConversationAsync(chat);
                    break;
                case ChatSidebarConversationAction.Delete:
                    await DeleteConversationAsync(chat);
                    break;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _scene.SetStatus(exception.Message);
        }
    }

    private async void OnGroupActionRequested(object? sender, ChatSidebarGroupRequest request)
    {
        var group = _groupRows.FirstOrDefault(item => item.Id == request.GroupId);
        if (group is null) return;
        var state = State(group.Id);
        try
        {
            switch (request.Action)
            {
                case ChatSidebarGroupAction.Open:
                    await _stateStore.MarkReadAsync(group.Id, DateTimeOffset.UtcNow, _lifetime.Token);
                    _activeGroupId = group.Id;
                    await _openGroup(group);
                    await RefreshAsync();
                    break;
                case ChatSidebarGroupAction.Toggle:
                case ChatSidebarGroupAction.ToggleExpand:
                    await _stateStore.SetExpandedAsync(group.Id, !state.IsExpanded, _lifetime.Token);
                    await RefreshAsync();
                    break;
                case ChatSidebarGroupAction.Rename:
                    _scene.ShowTextPrompt($"Rename {GroupName(_currentMode, false)}", group.Name, "Save", async name =>
                    {
                        await _containers.UpsertAsync(group with { Name = name, UpdatedAt = DateTimeOffset.UtcNow }, _lifetime.Token);
                        await RefreshAsync();
                    });
                    break;
                case ChatSidebarGroupAction.TogglePin:
                    await _stateStore.SetPinnedAsync(group.Id, !state.IsPinned, _lifetime.Token);
                    await RefreshAsync();
                    break;
                case ChatSidebarGroupAction.NewChat:
                    await StartChatAsync(group.Id);
                    break;
                case ChatSidebarGroupAction.Archive:
                    await ArchiveGroupAsync(group);
                    break;
                case ChatSidebarGroupAction.Delete:
                    await DeleteGroupAsync(group);
                    break;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _scene.SetStatus(exception.Message);
        }
    }

    private void ShowCreateGroupPrompt()
    {
        var groupName = GroupName(_currentMode, false);
        _scene.ShowTextPrompt($"New {groupName}", string.Empty, $"Create {groupName}", async name =>
        {
            var now = DateTimeOffset.UtcNow;
            var group = new ContainerDefinition(Guid.NewGuid(), _currentMode, name, null, string.Empty, string.Empty, now, now);
            if (_currentMode == HavenMode.Study) await _containers.CreateSubjectAsync(group, _lifetime.Token);
            else await _containers.UpsertAsync(group, _lifetime.Token);
            await _stateStore.SetExpandedAsync(group.Id, true, _lifetime.Token);
            await _openGroup(group);
            _activeGroupId = group.Id;
            await RefreshAsync();
        });
    }

    private void ShowMoveChoices(Conversation chat)
    {
        var choices = new List<(string Label, Action Action)>
        {
            ("No group", () => _ = MoveConversationAsync(chat, null))
        };
        choices.AddRange(_groupRows
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.Name, (Action)(() => _ = MoveConversationAsync(chat, group.Id)))));
        _scene.ShowChoices($"Move to {GroupName(_currentMode, false)}", choices);
    }

    private void ShowSpaceChoices(Conversation chat)
    {
        var current = chat.SpaceId;
        var choices = new List<(string Label, Action Action)>
        {
            ("No Space", () => _ = MoveConversationToSpaceAsync(chat, null))
        };
        choices.AddRange(_spaceRows
            .OrderBy(space => space.Name, StringComparer.OrdinalIgnoreCase)
            .Where(space => space.Id != current)
            .Select(space => (space.Name, (Action)(() => _ = MoveConversationToSpaceAsync(chat, space.Id)))));
        _scene.ShowChoices("Move to Space", choices);
    }

    private async Task MoveConversationToSpaceAsync(Conversation chat, Guid? spaceId)
    {
        await _conversations.UpsertConversationAsync(chat with { SpaceId = spaceId, UpdatedAt = DateTimeOffset.UtcNow }, _lifetime.Token);
        await RefreshAsync();
    }

    private async Task StartChatAsync(Guid? groupId)
    {
        if (_startingChat) return;
        _startingChat = true;
        _scene.SetNewChatBusy(true);
        try
        {
            if (groupId is Guid id) await _stateStore.SetExpandedAsync(id, true, _lifetime.Token);
            await _startChat(_currentMode, groupId);
            _activeGroupId = groupId;
            await RefreshAsync();
        }
        finally
        {
            _startingChat = false;
            _scene.SetNewChatBusy(false);
        }
    }

    private async Task ToggleConversationPinAsync(Conversation chat)
    {
        await _conversations.UpsertConversationAsync(chat with { IsPinned = !chat.IsPinned, UpdatedAt = DateTimeOffset.UtcNow }, _lifetime.Token);
        await RefreshAsync();
    }

    private async Task ToggleConversationReadAsync(Conversation chat)
    {
        if (IsUnread(chat)) await _stateStore.MarkReadAsync(chat.Id, DateTimeOffset.UtcNow, _lifetime.Token);
        else await _stateStore.MarkUnreadAsync(chat.Id, _lifetime.Token);
        await RefreshAsync();
    }

    private async Task MoveConversationAsync(Conversation chat, Guid? groupId)
    {
        await _conversations.UpsertConversationAsync(chat with { ContainerId = groupId, UpdatedAt = DateTimeOffset.UtcNow }, _lifetime.Token);
        await RefreshAsync();
    }

    private async Task ArchiveConversationAsync(Conversation chat)
    {
        await _conversations.UpsertConversationAsync(chat with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, _lifetime.Token);
        await RefreshAsync();
    }

    private async Task DeleteConversationAsync(Conversation chat)
    {
        var wasActive = _activeConversationId == chat.Id;
        await _conversations.DeleteConversationAsync(chat.Id, _lifetime.Token);
        if (wasActive) await StartChatAsync(null);
        else await RefreshAsync();
    }

    private async Task ArchiveGroupAsync(ContainerDefinition group)
    {
        await _containers.UpsertAsync(group with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, _lifetime.Token);
        await RefreshAsync();
    }

    private async Task DeleteGroupAsync(ContainerDefinition group)
    {
        await _containers.DeleteAndDetachConversationsAsync(group.Id, _lifetime.Token);
        if (_activeGroupId == group.Id) _activeGroupId = null;
        await RefreshAsync();
    }

    private NativeChatItemState State(Guid id) => _states.TryGetValue(id, out var state) ? state : NativeChatItemState.Empty;
    private bool IsUnread(Conversation chat) => _activeConversationId != chat.Id && State(chat.Id).IsUnread(chat.UpdatedAt);
    private bool IsGroupPinned(ContainerDefinition group) => State(group.Id).IsPinned;
    private bool IsGroupUnread(ContainerDefinition group) => _activeGroupId != group.Id && State(group.Id).IsUnread(GroupUpdatedAt(group));

    private DateTimeOffset GroupUpdatedAt(ContainerDefinition group)
    {
        var childUpdate = _conversationRows
            .Where(chat => chat.ContainerId == group.Id)
            .Select(chat => chat.UpdatedAt)
            .DefaultIfEmpty(group.UpdatedAt)
            .Max();
        return childUpdate > group.UpdatedAt ? childUpdate : group.UpdatedAt;
    }

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scene.SearchChanged -= OnSearchChanged;
        _scene.NewChatRequested -= OnNewChatRequested;
        _scene.NewGroupRequested -= OnNewGroupRequested;
        _scene.SpacePickerRequested -= OnSpacePickerRequested;
        _scene.ConversationActionRequested -= OnConversationActionRequested;
        _scene.GroupActionRequested -= OnGroupActionRequested;
        _scene.FileRequested -= OnFileRequested;
        _scene.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        SceneHost.Root = null;
    }
}
