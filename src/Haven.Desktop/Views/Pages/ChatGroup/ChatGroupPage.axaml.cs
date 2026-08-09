using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Pages.ChatGroup;

/// <summary>
/// Chat Group page. Displays group conversations, resources, and stats.
/// </summary>
public sealed partial class ChatGroupPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly IConversationRepository _conversations;
    private readonly IContainerRepository _containers;
    private readonly IContainerResourceRepository _resources;
    private readonly ContainerDefinition _definition;
    private readonly Func<ContainerDefinition, Task> _newChat;
    private readonly Func<Conversation, Task> _openChat;
    private readonly Func<ContainerDefinition, Task>? _openSettings;
    private readonly Func<Task>? _closed;

    public ChatGroupPage(
        HavenEventBus bus,
        IConversationRepository conversations,
        IContainerRepository containers,
        IContainerResourceRepository resources,
        ContainerDefinition group,
        Func<ContainerDefinition, Task> newChat,
        Func<Conversation, Task> openChat,
        Func<ContainerDefinition, Task>? openSettings = null,
        Func<Task>? closed = null)
    {
        _bus = bus;
        _conversations = conversations;
        _containers = containers;
        _resources = resources;
        _definition = group;
        _newChat = newChat;
        _openChat = openChat;
        _openSettings = openSettings;
        _closed = closed;

        InitializeComponent();
        NameText.Text = group.Name;
        WireEvents();
        _ = RefreshAsync();
    }

    private void WireEvents()
    {
        _bus.RegisterElement("ChatGroup.Actions.Back", BackButton);
        _bus.WirePointerEvents("ChatGroup.Actions.Back", BackButton);
        BackButton.Click += async (_, _) =>
        {
            _bus.Fire("ChatGroup.Actions.Back");
            if (_closed is not null) await _closed();
        };

        _bus.RegisterElement("ChatGroup.Actions.Refresh", RefreshButton);
        _bus.WirePointerEvents("ChatGroup.Actions.Refresh", RefreshButton);
        RefreshButton.Click += async (_, _) =>
        {
            _bus.Fire("ChatGroup.Actions.Refresh");
            await RefreshAsync();
        };

        _bus.RegisterElement("ChatGroup.Actions.Settings", SettingsButton);
        _bus.WirePointerEvents("ChatGroup.Actions.Settings", SettingsButton);
        SettingsButton.Click += async (_, _) =>
        {
            _bus.Fire("ChatGroup.Actions.Settings");
            if (_openSettings is not null) await _openSettings(_definition);
        };

        _bus.RegisterElement("ChatGroup.Actions.NewChat", NewChatButton);
        _bus.WirePointerEvents("ChatGroup.Actions.NewChat", NewChatButton);
        NewChatButton.Click += async (_, _) =>
        {
            _bus.Fire("ChatGroup.Actions.NewChat");
            await _newChat(_definition);
        };

        _bus.RegisterElement("ChatGroup.Actions.AddReferences", AddReferencesButton);
        _bus.WirePointerEvents("ChatGroup.Actions.AddReferences", AddReferencesButton);
        AddReferencesButton.Click += async (_, _) =>
        {
            _bus.Fire("ChatGroup.Actions.AddReferences");
            await AddReferencesAsync();
        };

        _bus.RegisterElement("ChatGroup.Actions.Archive", ArchiveButton);
        _bus.WirePointerEvents("ChatGroup.Actions.Archive", ArchiveButton);
        ArchiveButton.Click += async (_, _) =>
        {
            _bus.Fire("ChatGroup.Actions.Archive");
            await ArchiveAsync();
        };
    }

    private async Task RefreshAsync()
    {
        StatusText.Text = "Loading Chat Group...";
        try
        {
            var chatsTask = _conversations.GetRecentInScopeAsync(
                ConversationScope.ForChatGroup(_definition.Id), 500, CancellationToken.None);
            var resourcesTask = _resources.GetByContainerAsync(_definition.Id, CancellationToken.None);
            await Task.WhenAll(chatsTask, resourcesTask);

            var chats = await chatsTask;
            var references = await resourcesTask;

            RecentChatsPanel.Children.Clear();
            foreach (var chat in chats.Take(12))
            {
                var titleText = new TextBlock { Text = chat.Title };
                var updatedText = new TextBlock { Text = chat.UpdatedAt.LocalDateTime.ToString("g"), Classes = { "muted" } };
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                grid.Children.Add(titleText);
                Grid.SetColumn(updatedText, 1);
                grid.Children.Add(updatedText);

                var button = new HavenButton { Classes = { "sidebar" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Content = grid };
                var capturedChat = chat;
                button.Click += async (_, _) => await _openChat(capturedChat);
                RecentChatsPanel.Children.Add(button);
            }

            ChatCountText.Text = $"{chats.Count} chat{(chats.Count == 1 ? "" : "s")}";
            PinnedCountText.Text = $"{chats.Count(c => c.IsPinned)} pinned";
            LastActivityText.Text = chats.FirstOrDefault() is { } first
                ? $"Active {first.UpdatedAt.LocalDateTime:g}"
                : "No activity yet";
            NoChatsText.IsVisible = chats.Count == 0;
            StatusText.Text = chats.Count == 0 ? "Start the first chat in this group." : "Chat Group ready.";
        }
        catch (Exception ex) { StatusText.Text = $"Error: {ex.Message}"; }
    }

    private async Task AddReferencesAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add Chat Group references",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Haven references")
                {
                    Patterns = ["*.txt", "*.md", "*.json", "*.csv", "*.tsv", "*.pdf", "*.docx", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp", "*.cs", "*.axaml", "*.js", "*.ts", "*.py", "*.html", "*.css", "*.sql", "*.yaml", "*.yml"]
                }
            ]
        });
        var added = 0;
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) continue;
            try
            {
                await _resources.AddAsync(_definition.Id, path, CancellationToken.None);
                added++;
            }
            catch { }
        }
        if (added > 0)
        {
            StatusText.Text = $"Added {added} reference file{(added == 1 ? "" : "s")}.";
            await RefreshAsync();
        }
    }

    private async Task ArchiveAsync()
    {
        var updated = _definition with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow };
        await _containers.UpsertAsync(updated, CancellationToken.None);
        StatusText.Text = "Chat Group archived. Its chats and references are preserved.";
        if (_closed is not null) await _closed();
    }
}
