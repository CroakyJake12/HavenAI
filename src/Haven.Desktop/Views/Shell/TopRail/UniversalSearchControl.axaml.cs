using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell.TopRail;

public sealed partial class UniversalSearchControl : UserControl
{
    private static readonly string[] GroupOrder = ["Recommended", "Apps", "Chats", "Projects", "Documents", "Tasks", "Tabs", "Commands"];
    private readonly List<UniversalSearchItem> _items = [];
    private readonly List<(UniversalSearchItem Item, HavenButton Button)> _rendered = [];
    private int _selectedIndex = -1;

    public UniversalSearchControl()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => Rebuild();
        SearchBox.KeyDown += OnSearchKeyDown;
        ViewAllButton.Click += (_, _) => ViewAllRequested?.Invoke(this, EventArgs.Empty);
        SearchSettingsButton.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ItemInvoked;
    public event EventHandler? ViewAllRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? CloseRequested;

    public void SetItems(IReadOnlyList<UniversalSearchItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        Rebuild();
    }

    public void FocusSearch()
    {
        SearchBox.SelectAll();
        SearchBox.Focus();
    }

    public static IReadOnlyList<UniversalSearchItem> FilterItems(IEnumerable<UniversalSearchItem> items, string? query)
    {
        ArgumentNullException.ThrowIfNull(items);
        var value = query?.Trim() ?? string.Empty;
        return items.Where(item => string.IsNullOrWhiteSpace(value)
                                   || item.Title.Contains(value, StringComparison.OrdinalIgnoreCase)
                                   || item.Detail.Contains(value, StringComparison.OrdinalIgnoreCase)
                                   || item.Group.Contains(value, StringComparison.OrdinalIgnoreCase)
                                   || item.KindLabel.Contains(value, StringComparison.OrdinalIgnoreCase)
                                   || (item.SearchKeywords?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToArray();
    }

    public static int MoveSelectionIndex(IReadOnlyList<UniversalSearchItem> items, int currentIndex, int direction)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0 || direction == 0 || !items.Any(item => item.IsEnabled)) return -1;
        var index = currentIndex;
        for (var attempt = 0; attempt < items.Count; attempt++)
        {
            index = index < 0
                ? direction > 0 ? 0 : items.Count - 1
                : (index + Math.Sign(direction) + items.Count) % items.Count;
            if (items[index].IsEnabled) return index;
        }
        return -1;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs args)
    {
        switch (args.Key)
        {
            case Key.Down:
                MoveSelection(1);
                args.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                args.Handled = true;
                break;
            case Key.Enter:
            {
                var item = _selectedIndex >= 0 && _selectedIndex < _rendered.Count
                    ? _rendered[_selectedIndex].Item
                    : _rendered.Select(entry => entry.Item).FirstOrDefault(candidate => candidate.IsEnabled);
                if (item is not null) Invoke(item);
                args.Handled = true;
                break;
            }
            case Key.Escape:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                args.Handled = true;
                break;
        }
    }

    private void Rebuild()
    {
        ResultsPanel.Children.Clear();
        _rendered.Clear();
        _selectedIndex = -1;
        var items = FilterItems(_items, SearchBox.Text);
        foreach (var group in GroupOrder)
        {
            var matches = items.Where(item => item.Group.Equals(group, StringComparison.OrdinalIgnoreCase)).Take(8).ToArray();
            if (matches.Length == 0) continue;
            ResultsPanel.Children.Add(new TextBlock
            {
                Text = group,
                FontWeight = FontWeight.ExtraBold,
                FontSize = 13,
                Margin = new Thickness(7, 8, 7, 2)
            });
            foreach (var item in matches)
            {
                var button = BuildResult(item);
                _rendered.Add((item, button));
                ResultsPanel.Children.Add(button);
            }
        }

        if (items.Count == 0)
        {
            ResultsPanel.Children.Add(new TextBlock
            {
                Text = "No apps, chats, projects, documents, tasks, tabs, or commands match this search.",
                Classes = { "muted" },
                Margin = new Thickness(10, 18),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        _selectedIndex = MoveSelectionIndex(_rendered.Select(entry => entry.Item).ToArray(), -1, 1);
        UpdateSelectionVisual();
    }

    private HavenButton BuildResult(UniversalSearchItem item)
    {
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        layout.Children.Add(new HavenIcon
        {
            IconKey = item.IconKey,
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center
        });
        var detail = item.IsEnabled || string.IsNullOrWhiteSpace(item.DisabledReason)
            ? item.Detail
            : $"{item.Detail} Â· {item.DisabledReason}";
        var copy = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock { Text = item.Title, FontWeight = FontWeight.ExtraBold, FontSize = 13 },
                new TextBlock { Text = detail, Classes = { "muted" }, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis }
            }
        };
        Grid.SetColumn(copy, 1);
        layout.Children.Add(copy);
        if (!string.IsNullOrWhiteSpace(item.KindLabel))
        {
            var kind = new HavenAdaptiveSurface
            {
                Background = Avalonia.Application.Current?.Resources["HavenPanel2Brush"] as IBrush,
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(10, 4),
                Child = new TextBlock
                {
                    Text = item.IsEnabled ? item.KindLabel : $"{item.KindLabel} Â· unavailable",
                    FontSize = 10,
                    FontWeight = FontWeight.Bold
                }
            };
            Grid.SetColumn(kind, 2);
            layout.Children.Add(kind);
        }

        var button = new HavenButton
        {
            Content = layout,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 58,
            Padding = new Thickness(14, 9),
            CornerRadius = new CornerRadius(16),
            IsEnabled = item.IsEnabled
        };
        AutomationProperties.SetName(button, $"{item.KindLabel}: {item.Title}");
        AutomationProperties.SetAutomationId(button, $"LauncherResult-{item.Group}-{item.Title}");
        button.Classes.Add("sidebar");
        button.Click += (_, _) => Invoke(item);
        return button;
    }

    private void MoveSelection(int direction)
    {
        _selectedIndex = MoveSelectionIndex(_rendered.Select(entry => entry.Item).ToArray(), _selectedIndex, direction);
        UpdateSelectionVisual();
    }

    private void UpdateSelectionVisual()
    {
        var accent = Avalonia.Application.Current?.Resources["HavenAccentBrush"] as IBrush;
        for (var index = 0; index < _rendered.Count; index++)
        {
            var selected = index == _selectedIndex;
            _rendered[index].Button.BorderThickness = selected ? new Thickness(2) : new Thickness(0);
            _rendered[index].Button.BorderBrush = selected ? accent : null;
        }
    }

    private void Invoke(UniversalSearchItem item)
    {
        if (!item.IsEnabled) return;
        item.OnExecute();
        ItemInvoked?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record UniversalSearchItem(
    string Group,
    string Title,
    string Detail,
    string IconKey,
    string KindLabel,
    Action OnExecute,
    bool IsEnabled = true,
    string? DisabledReason = null,
    string? SearchKeywords = null);
