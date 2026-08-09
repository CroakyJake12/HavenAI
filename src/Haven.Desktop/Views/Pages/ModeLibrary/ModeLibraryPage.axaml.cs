using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Pages.ModeLibrary;

public sealed partial class ModeLibraryPage : UserControl
{
    private readonly HavenEventBus _bus;
    private readonly IModeRegistry _modeRegistry;
    private readonly IModeUsageRepository _modeUsage;
    private readonly IPinRepository _pins;
    private IReadOnlyList<ModeDefinition> _allModes = [];
    private IReadOnlyList<ModePin> _allPins = [];
    private string _searchQuery = string.Empty;

    public ModeLibraryPage(HavenEventBus bus, IModeRegistry modeRegistry, IModeUsageRepository modeUsage, IPinRepository pins)
    {
        _bus = bus;
        _modeRegistry = modeRegistry;
        _modeUsage = modeUsage;
        _pins = pins;

        InitializeComponent();
        WireEvents();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => _ = RefreshAsync();

    private static IBrush? Brush(string key) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true ? value as IBrush : null;

    private void WireEvents()
    {
        _bus.RegisterElement("ModeLibrary.Actions.Refresh", RefreshButton);
        _bus.WirePointerEvents("ModeLibrary.Actions.Refresh", RefreshButton);
        RefreshButton.Click += async (_, _) =>
        {
            _bus.Fire("ModeLibrary.Actions.Refresh");
            await RefreshAsync();
        };

        _bus.RegisterElement("ModeLibrary.Actions.CreateInStudio", CreateInStudioButton);
        _bus.WirePointerEvents("ModeLibrary.Actions.CreateInStudio", CreateInStudioButton);
        CreateInStudioButton.Click += (_, _) =>
        {
            _bus.Fire("ModeLibrary.Actions.CreateInStudio");
        };

        SearchBox.TextChanged += (_, _) =>
        {
            _searchQuery = SearchBox.Text?.Trim() ?? "";
            _bus.Fire("ModeLibrary.Search.QueryChanged");
            _ = FilterAndDisplayAsync();
        };
    }

    private async Task RefreshAsync()
    {
        StatusText.Text = "Loading…";
        try
        {
            _allModes = await _modeRegistry.GetModesAsync(CancellationToken.None);
            _allPins = await _pins.GetPinsAsync(CancellationToken.None);
            await FilterAndDisplayAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load: {ex.Message}";
        }
    }

    private async Task FilterAndDisplayAsync()
    {
        ItemsPanel.Children.Clear();
        var modes = string.IsNullOrWhiteSpace(_searchQuery)
            ? _allModes
            : _allModes.Where(m => m.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)
                                   || m.Description.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var mode in modes)
            ItemsPanel.Children.Add(await CreateModeCardAsync(mode));

        StatusText.Text = $"{modes.Count} mode{(modes.Count == 1 ? "" : "s")}";
    }

    private async Task<Border> CreateModeCardAsync(ModeDefinition mode)
    {
        var qName = $"ModeLibrary.List.Item{ItemsPanel.Children.Count}";
        var isPinned = _allPins.Any(p => p.ModeId == mode.Id);

        var icon = new HavenIcon
        {
            IconKey = mode.IconKey, Width = 16, Height = 16,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("HavenAccentBrush")
        };
        var iconBorder = new HavenAdaptiveSurface
        {
            Width = 58, Height = 58, CornerRadius = new CornerRadius(18),
            Background = Brush("HavenAccentTertiaryBrush"),
            VerticalAlignment = VerticalAlignment.Top, Child = icon
        };
        icon.Width = 28;
        icon.Height = 28;

        var nameBlock = new TextBlock { Text = mode.Name, FontSize = 18, FontWeight = FontWeight.ExtraBold };
        var pinnedBadge = new HavenAdaptiveSurface
        {
            CornerRadius = new CornerRadius(4),
            Background = Brush("HavenAccentSoftBrush"),
            Padding = new Avalonia.Thickness(6, 2), IsVisible = isPinned,
            Child = new TextBlock { Text = "Pinned", FontSize = 9, Foreground = Brush("HavenAccentBrush"), FontWeight = FontWeight.SemiBold }
        };
        var sourceBadge = new HavenAdaptiveSurface
        {
            CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
            Padding = new Avalonia.Thickness(6, 2),
            Child = new TextBlock { Text = mode.Source.ToString(), FontSize = 9, Foreground = Brush("HavenMutedBrush") }
        };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        nameRow.Children.Add(nameBlock);
        nameRow.Children.Add(pinnedBadge);
        nameRow.Children.Add(sourceBadge);

        var descBlock = new TextBlock { Text = mode.Description, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Brush("HavenTextSoftBrush"), TextWrapping = TextWrapping.Wrap, MaxLines = 3 };

        var nameStack = new StackPanel { Spacing = 2 };
        nameStack.Children.Add(nameRow);
        nameStack.Children.Add(descBlock);

        var pinButton = new HavenTertiaryButton { MinHeight = 38, Padding = new Avalonia.Thickness(14, 7) };
        pinButton.Content = isPinned ? "Unpin" : "Pin";
        ToolTip.SetTip(pinButton, isPinned ? "Unpin from sidebar" : "Pin to sidebar");

        pinButton.RegisterWithEvents($"{qName}.Pin", _bus);
        pinButton.Click += async (_, _) =>
        {
            _bus.Fire($"{qName}.Pin");
            await TogglePinAsync(mode);
        };

        var useCount = await _modeUsage.GetTotalUseCountAsync(mode.Id, CancellationToken.None);
        var metaBlock = new TextBlock
        {
            Text = $"v{mode.Version} · by {mode.Author} · {useCount} uses",
            FontSize = 11, Foreground = Brush("HavenMutedBrush"),
            Margin = new Avalonia.Thickness(0, 4, 0, 0)
        };

        var metaRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        metaRow.Children.Add(metaBlock);

        var mainStack = new StackPanel { Spacing = 2 };
        mainStack.Children.Add(nameStack);
        mainStack.Children.Add(metaRow);

        var actionsStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Top };
        actionsStack.Children.Add(pinButton);

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14, RowSpacing = 14 };
        grid.Children.Add(iconBorder);
        Grid.SetColumn(mainStack, 1);
        grid.Children.Add(mainStack);
        Grid.SetRow(actionsStack, 2);
        Grid.SetColumnSpan(actionsStack, 2);
        actionsStack.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(actionsStack);

        var border = new HavenCard
        {
            Width = 330,
            MinHeight = 214,
            Padding = new Avalonia.Thickness(20),
            Margin = new Avalonia.Thickness(8),
            Child = grid
        };
        border.PointerEntered += (_, _) => _bus.Fire($"{qName}.Hover");
        border.PointerExited += (_, _) => _bus.Fire($"{qName}.Leave");
        return border;
    }

    private async Task TogglePinAsync(ModeDefinition mode)
    {
        try
        {
            var existing = _allPins.FirstOrDefault(p => p.ModeId == mode.Id);
            if (existing is not null)
            {
                await _pins.DeletePinAsync(mode.Id, CancellationToken.None);
            }
            else
            {
                var order = _allPins.Count;
                await _pins.UpsertPinAsync(new ModePin(Guid.NewGuid(), mode.Id, order, DateTimeOffset.UtcNow), CancellationToken.None);
            }
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Pin failed: {ex.Message}";
        }
    }
}
