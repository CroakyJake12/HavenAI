/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/ChatGroupPageViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns ChatGroupPageViewModel, ChatGroupConversationViewModel, ContainerResourceItemViewModel. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents chat group page view model and keeps its related state and behavior together.
/// </summary>
public sealed class ChatGroupPageViewModel : ObservableObject
{
    /// <summary>
    /// Stores conversations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationRepository _conversations;
    /// <summary>
    /// Stores containers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IContainerRepository _containers;
    /// <summary>
    /// Stores resources locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IContainerResourceRepository _resources;
    /// <summary>
    /// Stores new chat locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Func<ContainerDefinition, Task> _newChat;
    /// <summary>
    /// Stores open chat locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Func<Conversation, Task> _openChat;
    /// <summary>
    /// Stores open settings locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Func<ContainerDefinition, Task>? _openSettings;
    /// <summary>
    /// Stores closed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Func<Task>? _closed;
    /// <summary>
    /// Stores definition locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ContainerDefinition _definition;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Loading Chat Group…";
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;
    /// <summary>
    /// Stores chat count locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _chatCount;
    /// <summary>
    /// Stores pinned count locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _pinnedCount;
    /// <summary>
    /// Stores last activity locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Gets or updates definition, the bindable or domain state represented by this property.
    /// </summary>
    public ContainerDefinition Definition => _definition;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => _definition.Name;
    /// <summary>
    /// Gets or updates context, the bindable or domain state represented by this property.
    /// </summary>
    public string Context => string.IsNullOrWhiteSpace(_definition.Context) ? "No shared context yet." : _definition.Context;
    /// <summary>
    /// Gets or updates instructions, the bindable or domain state represented by this property.
    /// </summary>
    public string Instructions => string.IsNullOrWhiteSpace(_definition.Instructions) ? "No group instructions yet." : _definition.Instructions;
    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
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
    /// <summary>
    /// Gets or updates chat count, the bindable or domain state represented by this property.
    /// </summary>
    public int ChatCount { get => _chatCount; private set { if (SetProperty(ref _chatCount, value)) RaisePropertyChanged(nameof(ChatCountLabel)); } }
    /// <summary>
    /// Gets or updates pinned count, the bindable or domain state represented by this property.
    /// </summary>
    public int PinnedCount { get => _pinnedCount; private set { if (SetProperty(ref _pinnedCount, value)) RaisePropertyChanged(nameof(PinnedCountLabel)); } }
    /// <summary>
    /// Gets or updates last activity, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset? LastActivity { get => _lastActivity; private set { if (SetProperty(ref _lastActivity, value)) RaisePropertyChanged(nameof(LastActivityLabel)); } }
    /// <summary>
    /// Gets or updates chat count label, the bindable or domain state represented by this property.
    /// </summary>
    public string ChatCountLabel => $"{ChatCount} chat{(ChatCount == 1 ? string.Empty : "s")}";
    /// <summary>
    /// Gets or updates pinned count label, the bindable or domain state represented by this property.
    /// </summary>
    public string PinnedCountLabel => $"{PinnedCount} pinned";
    /// <summary>
    /// Gets or updates last activity label, the bindable or domain state represented by this property.
    /// </summary>
    public string LastActivityLabel => LastActivity is null ? "No activity yet" : $"Active {LastActivity.Value.LocalDateTime:g}";
    /// <summary>
    /// Reports whether has recent chats is true for the current state.
    /// </summary>
    public bool HasRecentChats => RecentChats.Count > 0;
    /// <summary>
    /// Reports whether has resources is true for the current state.
    /// </summary>
    public bool HasResources => Resources.Count > 0;

    /// <summary>
    /// Gets or updates recent chats, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ChatGroupConversationViewModel> RecentChats { get; } = [];
    /// <summary>
    /// Gets or updates resources, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ContainerResourceItemViewModel> Resources { get; } = [];
    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates new chat command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NewChatCommand { get; }
    /// <summary>
    /// Gets or updates open chat command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<ChatGroupConversationViewModel> OpenChatCommand { get; }
    /// <summary>
    /// Gets or updates settings command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SettingsCommand { get; }
    /// <summary>
    /// Gets or updates archive command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ArchiveCommand { get; }
    /// <summary>
    /// Gets or updates remove resource command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand<ContainerResourceItemViewModel> RemoveResourceCommand { get; }

    /// <summary>
    /// Performs initialize async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken) => await RefreshAsync(cancellationToken);

    /// <summary>
    /// Performs refresh async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task RefreshAsync() => await RefreshAsync(CancellationToken.None);

    /// <summary>
    /// Performs refresh async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs add references async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs delete permanently async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeletePermanentlyAsync(CancellationToken cancellationToken)
    {
        foreach (var resource in await _resources.GetByContainerAsync(_definition.Id, cancellationToken))
            await _resources.DeleteAsync(resource.Id, cancellationToken);
        await _containers.DeleteAndDetachConversationsAsync(_definition.Id, cancellationToken);
        Status = "Chat Group deleted. Its conversations are preserved in General Chat.";
        if (_closed is not null) await _closed();
    }

    /// <summary>
    /// Performs remove resource async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RemoveResourceAsync(ContainerResourceItemViewModel? item)
    {
        if (item is null) return;
        await _resources.DeleteAsync(item.Id, CancellationToken.None);
        Resources.Remove(item);
        RaisePropertyChanged(nameof(HasResources));
        Status = $"Removed {item.Name}.";
    }

    /// <summary>
    /// Performs archive async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ArchiveAsync()
    {
        _definition = _definition with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow };
        await _containers.UpsertAsync(_definition, CancellationToken.None);
        RaisePropertyChanged(nameof(Definition));
        Status = "Chat Group archived. Its chats and references are preserved.";
        if (_closed is not null) await _closed();
    }
}

/// <summary>
/// Represents chat group conversation view model and keeps its related state and behavior together.
/// </summary>
public sealed record ChatGroupConversationViewModel(Conversation Definition)
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id => Definition.Id;
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title => Definition.Title;
    /// <summary>
    /// Gets or updates updated label, the bindable or domain state represented by this property.
    /// </summary>
    public string UpdatedLabel => Definition.UpdatedAt.LocalDateTime.ToString("g");
    /// <summary>
    /// Reports whether is pinned is true for the current state.
    /// </summary>
    public bool IsPinned => Definition.IsPinned;
}

/// <summary>
/// Represents container resource item view model and keeps its related state and behavior together.
/// </summary>
public sealed record ContainerResourceItemViewModel(ContainerResource Definition)
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid Id => Definition.Id;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name => Definition.Name;
    /// <summary>
    /// Gets or updates type label, the bindable or domain state represented by this property.
    /// </summary>
    public string TypeLabel => Definition.Kind switch
    {
        ContainerResourceKind.Text => "Text",
        ContainerResourceKind.Document => "Document",
        ContainerResourceKind.Image => "Image",
        _ => "File"
    };
    /// <summary>
    /// Gets or updates size label, the bindable or domain state represented by this property.
    /// </summary>
    public string SizeLabel => Definition.SizeBytes < 1024 * 1024
        ? $"{Math.Max(1, Definition.SizeBytes / 1024d):0.#} KB"
        : $"{Definition.SizeBytes / 1024d / 1024d:0.#} MB";
}
