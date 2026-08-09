using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Pages.ActivityLog;

public sealed partial class ActivityLogPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly IConversationRepository _conversations;
    private IReadOnlyList<Conversation> _allItems = [];
    private string _searchQuery = string.Empty;

    public ActivityLogPage(HavenEventBus bus, IConversationRepository conversations)
    {
        _bus = bus;
        _conversations = conversations;

        InitializeComponent();
        WireEvents();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => _ = RefreshAsync();

    private static IBrush? Brush(string key) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true ? value as IBrush : null;

    private void WireEvents()
    {
        _bus.RegisterElement("ActivityLog.Actions.Refresh", RefreshButton);
        _bus.WirePointerEvents("ActivityLog.Actions.Refresh", RefreshButton);
        RefreshButton.Click += async (_, _) =>
        {
            _bus.Fire("ActivityLog.Actions.Refresh");
            await RefreshAsync();
        };

        SearchBox.TextChanged += (_, _) =>
        {
            _searchQuery = SearchBox.Text?.Trim() ?? "";
            _bus.Fire("ActivityLog.Search.QueryChanged");
            FilterAndDisplay();
        };
    }

    private async Task RefreshAsync()
    {
        StatusText.Text = "Loading…";
        try
        {
            _allItems = await _conversations.GetRecentAsync(null, 50, CancellationToken.None);
            FilterAndDisplay();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load: {ex.Message}";
        }
    }

    private void FilterAndDisplay()
    {
        ItemsPanel.Children.Clear();
        var items = string.IsNullOrWhiteSpace(_searchQuery)
            ? _allItems
            : _allItems.Where(c => c.Title.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var conv in items)
            ItemsPanel.Children.Add(CreateItemCard(conv));

        StatusText.Text = $"{items.Count} conversation{(items.Count == 1 ? "" : "s")}";
    }

    private Button CreateItemCard(Conversation conv)
    {
        var qName = $"ActivityLog.List.Item{ItemsPanel.Children.Count}";

        var titleBlock = new TextBlock
        {
            Text = conv.Title, FontWeight = FontWeight.SemiBold, FontSize = 13,
            MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis
        };
        var modeBadge = new HavenAdaptiveSurface
        {
            Background = Brush("StrokeBrush"),
            CornerRadius = new CornerRadius(4), Padding = new Avalonia.Thickness(6, 2),
            Child = new TextBlock { Text = conv.Mode.ToString(), FontSize = 10, Opacity = 0.7 }
        };
        var updatedText = new TextBlock
        {
            Text = conv.UpdatedAt.ToString("MMM dd, HH:mm"), FontSize = 10, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center
        };

        var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        metaRow.Children.Add(modeBadge);
        metaRow.Children.Add(updatedText);

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        headerGrid.Children.Add(titleBlock);
        Grid.SetColumn(metaRow, 1);
        headerGrid.Children.Add(metaRow);

        var contentGrid = new Grid { RowDefinitions = new RowDefinitions("Auto"), ColumnDefinitions = new ColumnDefinitions("*") };
        contentGrid.Children.Add(headerGrid);

        var button = new HavenButton
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(10, 8),
            Margin = new Avalonia.Thickness(0, 0, 0, 2),
            Content = contentGrid
        };

        button.RegisterWithEvents(qName, _bus);
        button.Click += (_, _) =>
        {
            _bus.Fire($"{qName}.Click");
        };

        return button;
    }
}
