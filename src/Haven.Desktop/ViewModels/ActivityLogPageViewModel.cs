using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

public sealed class ActivityLogPageViewModel : ObservableObject
{
    private readonly IConversationRepository _conversations;
    private readonly Action<string> _navigateToChat;

    private string _statusMessage = "Loading activity…";
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    private string _searchQuery = string.Empty;
    public string SearchQuery { get => _searchQuery; set { if (SetProperty(ref _searchQuery, value)) ApplyFilter(); } }

    private ActivityLogItem? _selectedItem;
    public ActivityLogItem? SelectedItem { get => _selectedItem; set => SetProperty(ref _selectedItem, value); }

    public ObservableCollection<ActivityLogItem> Items { get; } = [];
    public ObservableCollection<ActivityLogItem> FilteredItems { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand<ActivityLogItem> OpenItemCommand { get; }

    public ActivityLogPageViewModel(IConversationRepository conversations, Action<string> navigateToChat)
    {
        _conversations = conversations;
        _navigateToChat = navigateToChat;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        OpenItemCommand = new RelayCommand<ActivityLogItem>(item => { if (item is not null) _navigateToChat(item.ConversationId.ToString("N")); });
    }

    public async Task InitializeAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        Items.Clear();
        FilteredItems.Clear();
        try
        {
            var conversations = await _conversations.GetRecentAsync(null, 50, CancellationToken.None);
            foreach (var c in conversations.OrderByDescending(c => c.UpdatedAt))
            {
                var messages = await _conversations.GetMessagesAsync(c.Id, CancellationToken.None);
                var lastMessage = messages.LastOrDefault();
                Items.Add(new ActivityLogItem
                {
                    ConversationId = c.Id,
                    Title = c.Title ?? "Untitled",
                    Mode = c.Mode.ToString(),
                    UpdatedAt = c.UpdatedAt,
                    MessageCount = messages.Count,
                    LastMessagePreview = lastMessage is not null ? Truncate(lastMessage.Content, 120) : "",
                    LastRole = lastMessage?.Role.ToString() ?? ""
                });
            }
            ApplyFilter();
            StatusMessage = $"{Items.Count} conversations loaded";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        var query = SearchQuery?.Trim() ?? "";
        foreach (var item in Items)
        {
            if (string.IsNullOrEmpty(query) ||
                item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.LastMessagePreview.Contains(query, StringComparison.OrdinalIgnoreCase))
                FilteredItems.Add(item);
        }
        StatusMessage = FilteredItems.Count == Items.Count
            ? $"{Items.Count} conversations"
            : $"{FilteredItems.Count} of {Items.Count} conversations";
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}

public sealed class ActivityLogItem
{
    public Guid ConversationId { get; init; }
    public string Title { get; init; } = "";
    public string Mode { get; init; } = "";
    public DateTimeOffset UpdatedAt { get; init; }
    public int MessageCount { get; init; }
    public string LastMessagePreview { get; init; } = "";
    public string LastRole { get; init; } = "";
}
