/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/ActivityLogPageViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns ActivityLogPageViewModel, ActivityLogItem. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents activity log page view model and keeps its related state and behavior together.
/// </summary>
public sealed class ActivityLogPageViewModel : ObservableObject
{
    /// <summary>
    /// Stores conversations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationRepository _conversations;
    /// <summary>
    /// Stores navigate to chat locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Action<string> _navigateToChat;

    /// <summary>
    /// Stores status message locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _statusMessage = "Loading activity…";
    /// <summary>
    /// Gets or updates status message, the bindable or domain state represented by this property.
    /// </summary>
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    /// <summary>
    /// Stores search query locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _searchQuery = string.Empty;
    /// <summary>
    /// Gets or updates search query, the bindable or domain state represented by this property.
    /// </summary>
    public string SearchQuery { get => _searchQuery; set { if (SetProperty(ref _searchQuery, value)) ApplyFilter(); } }

    /// <summary>
    /// Stores selected item locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private ActivityLogItem? _selectedItem;
    /// <summary>
    /// Gets or updates selected item, the bindable or domain state represented by this property.
    /// </summary>
    public ActivityLogItem? SelectedItem { get => _selectedItem; set => SetProperty(ref _selectedItem, value); }

    /// <summary>
    /// Gets or updates items, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ActivityLogItem> Items { get; } = [];
    /// <summary>
    /// Gets or updates filtered items, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<ActivityLogItem> FilteredItems { get; } = [];

    /// <summary>
    /// Gets or updates refresh command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>
    /// Gets or updates open item command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<ActivityLogItem> OpenItemCommand { get; }

    public ActivityLogPageViewModel(IConversationRepository conversations, Action<string> navigateToChat)
    {
        _conversations = conversations;
        _navigateToChat = navigateToChat;
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        OpenItemCommand = new RelayCommand<ActivityLogItem>(item => { if (item is not null) _navigateToChat(item.ConversationId.ToString("N")); });
    }

    /// <summary>
    /// Performs initialize async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task InitializeAsync() => await LoadAsync();

    /// <summary>
    /// Performs load async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the apply filter step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the truncate step owned by this component.
    /// </summary>
    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}

/// <summary>
/// Represents activity log item and keeps its related state and behavior together.
/// </summary>
public sealed class ActivityLogItem
{
    /// <summary>
    /// Gets or updates conversation id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid ConversationId { get; init; }
    /// <summary>
    /// Gets or updates title, the bindable or domain state represented by this property.
    /// </summary>
    public string Title { get; init; } = "";
    /// <summary>
    /// Gets or updates mode, the bindable or domain state represented by this property.
    /// </summary>
    public string Mode { get; init; } = "";
    /// <summary>
    /// Gets or updates updated at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }
    /// <summary>
    /// Gets or updates message count, the bindable or domain state represented by this property.
    /// </summary>
    public int MessageCount { get; init; }
    /// <summary>
    /// Gets or updates last message preview, the bindable or domain state represented by this property.
    /// </summary>
    public string LastMessagePreview { get; init; } = "";
    /// <summary>
    /// Gets or updates last role, the bindable or domain state represented by this property.
    /// </summary>
    public string LastRole { get; init; } = "";
}
