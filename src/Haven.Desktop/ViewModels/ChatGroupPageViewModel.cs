using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class ChatGroupPageViewModel : ObservableObject
{
    private readonly IConversationRepository _conversations;
    private readonly IContainerRepository _containers;
    private readonly IContainerResourceRepository _resources;
    private readonly Func<ContainerDefinition, Task> _newChat;
    private readonly Func<Conversation, Task> _openChat;
    private readonly Func<ContainerDefinition, Task>? _openSettings;
    private readonly Func<Task>? _closed;
    private ContainerDefinition _definition;
    private string _status = "Loading Chat Group…";
    private bool _isBusy;
    private int _chatCount;
    private int _pinnedCount;
    private DateTimeOffset? _lastActivity;

    public ChatGroupPageViewModel(
        ContainerDefinition group,
        IConversationRepository conversations,
        IContainerRepository containers,
        IContainerResourceRepository resources,
        Func<ContainerDefinition, Task> newChat,
        Func<Conversation, Task> openChat,
        Func<ContainerDefinition, Task>? openSettings = null,
        Func<Task>? closed = null)
    {
        if (group.Mode != HavenMode.Chat) throw new ArgumentException("Chat Group pages require a Chat container.", nameof(group));
        _definition = group;
        _conversations = conversations;
        _containers = containers;
        _resources = resources;
        _newChat = newChat;
        _openChat = openChat;
        _openSettings = openSettings;
        _closed = closed;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        NewChatCommand = new AsyncRelayCommand(() => _newChat(_definition));
        OpenChatCommand = new AsyncRelayCommand<ChatGroupConversationViewModel>(item => item is null ? Task.CompletedTask : _openChat(item.Definition));
        SettingsCommand = new AsyncRelayCommand(() => _openSettings?.Invoke(_definition) ?? Task.CompletedTask);
        ArchiveCommand = new AsyncRelayCommand(ArchiveAsync);
        RemoveResourceCommand = new AsyncRelayCommand<ContainerResourceItemViewModel>(RemoveResourceAsync);
    }

    public ContainerDefinition Definition => _definition;
    public string Name => _definition.Name;
    public string Context => string.IsNullOrWhiteSpace(_definition.Context) ? "No shared context yet." : _definition.Context;
    public string Instructions => string.IsNullOrWhiteSpace(_definition.Instructions) ? "No group instructions yet." : _definition.Instructions;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RefreshCommand.RaiseCanExecuteChanged();
        }
    }
    public int ChatCount { get => _chatCount; private set { if (SetProperty(ref _chatCount, value)) RaisePropertyChanged(nameof(ChatCountLabel)); } }
    public int PinnedCount { get => _pinnedCount; private set { if (SetProperty(ref _pinnedCount, value)) RaisePropertyChanged(nameof(PinnedCountLabel)); } }
    public DateTimeOffset? LastActivity { get => _lastActivity; private set { if (SetProperty(ref _lastActivity, value)) RaisePropertyChanged(nameof(LastActivityLabel)); } }
    public string ChatCountLabel => $"{ChatCount} chat{(ChatCount == 1 ? string.Empty : "s")}";
    public string PinnedCountLabel => $"{PinnedCount} pinned";
    public string LastActivityLabel => LastActivity is null ? "No activity yet" : $"Active {LastActivity.Value.LocalDateTime:g}";
    public bool HasRecentChats => RecentChats.Count > 0;
    public bool HasResources => Resources.Count > 0;

    public ObservableCollection<ChatGroupConversationViewModel> RecentChats { get; } = [];
    public ObservableCollection<ContainerResourceItemViewModel> Resources { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand NewChatCommand { get; }
    public AsyncRelayCommand<ChatGroupConversationViewModel> OpenChatCommand { get; }
    public AsyncRelayCommand SettingsCommand { get; }
    public AsyncRelayCommand ArchiveCommand { get; }
    public AsyncRelayCommand<ContainerResourceItemViewModel> RemoveResourceCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken) => await RefreshAsync(cancellationToken);

    public async Task RefreshAsync() => await RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var chatsTask = _conversations.GetRecentInScopeAsync(ConversationScope.ForChatGroup(_definition.Id), 500, cancellationToken);
            var resourcesTask = _resources.GetByContainerAsync(_definition.Id, cancellationToken);
            await Task.WhenAll(chatsTask, resourcesTask);
            var chats = await chatsTask;
            var references = await resourcesTask;
            RecentChats.Clear();
            foreach (var chat in chats.Take(12)) RecentChats.Add(new ChatGroupConversationViewModel(chat));
            Resources.Clear();
            foreach (var resource in references) Resources.Add(new ContainerResourceItemViewModel(resource));
            ChatCount = chats.Count;
            PinnedCount = chats.Count(chat => chat.IsPinned);
            LastActivity = chats.FirstOrDefault()?.UpdatedAt;
            RaisePropertyChanged(nameof(HasRecentChats));
            RaisePropertyChanged(nameof(HasResources));
            Status = chats.Count == 0 ? "Start the first chat in this group." : "Chat Group ready.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task AddReferencesAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var added = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var resource = await _resources.AddAsync(_definition.Id, path, cancellationToken);
                if (Resources.All(item => item.Id != resource.Id))
                {
                    Resources.Add(new ContainerResourceItemViewModel(resource));
                    added++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                Status = $"Could not add {Path.GetFileName(path)}: {ex.Message}";
                RaisePropertyChanged(nameof(HasResources));
                return;
            }
        }
        RaisePropertyChanged(nameof(HasResources));
        Status = added == 0 ? "No new references were selected." : $"Added {added} reference file{(added == 1 ? string.Empty : "s")}.";
    }

    public async Task DeletePermanentlyAsync(CancellationToken cancellationToken)
    {
        foreach (var resource in await _resources.GetByContainerAsync(_definition.Id, cancellationToken))
            await _resources.DeleteAsync(resource.Id, cancellationToken);
        await _containers.DeleteAndDetachConversationsAsync(_definition.Id, cancellationToken);
        Status = "Chat Group deleted. Its conversations are preserved in General Chat.";
        if (_closed is not null) await _closed();
    }

    private async Task RemoveResourceAsync(ContainerResourceItemViewModel? item)
    {
        if (item is null) return;
        await _resources.DeleteAsync(item.Id, CancellationToken.None);
        Resources.Remove(item);
        RaisePropertyChanged(nameof(HasResources));
        Status = $"Removed {item.Name}.";
    }

    private async Task ArchiveAsync()
    {
        _definition = _definition with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow };
        await _containers.UpsertAsync(_definition, CancellationToken.None);
        RaisePropertyChanged(nameof(Definition));
        Status = "Chat Group archived. Its chats and references are preserved.";
        if (_closed is not null) await _closed();
    }
}

public sealed record ChatGroupConversationViewModel(Conversation Definition)
{
    public Guid Id => Definition.Id;
    public string Title => Definition.Title;
    public string UpdatedLabel => Definition.UpdatedAt.LocalDateTime.ToString("g");
    public bool IsPinned => Definition.IsPinned;
}

public sealed record ContainerResourceItemViewModel(ContainerResource Definition)
{
    public Guid Id => Definition.Id;
    public string Name => Definition.Name;
    public string TypeLabel => Definition.Kind switch
    {
        ContainerResourceKind.Text => "Text",
        ContainerResourceKind.Document => "Document",
        ContainerResourceKind.Image => "Image",
        _ => "File"
    };
    public string SizeLabel => Definition.SizeBytes < 1024 * 1024
        ? $"{Math.Max(1, Definition.SizeBytes / 1024d):0.#} KB"
        : $"{Definition.SizeBytes / 1024d / 1024d:0.#} MB";
}
