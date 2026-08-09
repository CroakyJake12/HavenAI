using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell.TopRail;

public sealed partial class UniversalSearchControl : UserControl
{
    private static readonly string[] GroupOrder = ["Recommended", "Apps", "Chats", "Tasks", "Tabs", "Actions"];
    private readonly List<UniversalSearchItem> _items = [];

    public UniversalSearchControl()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => Rebuild();
        SearchBox.KeyDown += (_, args) =>
        {
            if (args.Key != Avalonia.Input.Key.Enter) return;
            var first = FilteredItems().FirstOrDefault();
            if (first is null) return;
            Invoke(first);
            args.Handled = true;
        };
        ViewAllButton.Click += (_, _) => ViewAllRequested?.Invoke(this, EventArgs.Empty);
        SearchSettingsButton.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ItemInvoked;
    public event EventHandler? ViewAllRequested;
    public event EventHandler? SettingsRequested;

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

    private IEnumerable<UniversalSearchItem> FilteredItems()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        return _items.Where(item => string.IsNullOrWhiteSpace(query)
                                    || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                                    || item.Detail.Contains(query, StringComparison.OrdinalIgnoreCase)
                                    || item.Group.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void Rebuild()
    {
        ResultsPanel.Children.Clear();
        var items = FilteredItems().ToArray();
        foreach (var group in GroupOrder)
        {
            var matches = items.Where(item => item.Group.Equals(group, StringComparison.OrdinalIgnoreCase)).Take(8).ToArray();
            if (matches.Length == 0) continue;
            ResultsPanel.Children.Add(new TextBlock
            {
                Text = group,
                FontWeight = Avalonia.Media.FontWeight.ExtraBold,
                FontSize = 13,
                Margin = new Thickness(7, 8, 7, 2)
            });
            foreach (var item in matches) ResultsPanel.Children.Add(BuildResult(item));
        }

        if (items.Length == 0)
        {
            ResultsPanel.Children.Add(new TextBlock
            {
                Text = "No apps, chats, tasks, tabs, or actions match this search.",
                Classes = { "muted" },
                Margin = new Thickness(10, 18),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }
    }

    private Button BuildResult(UniversalSearchItem item)
    {
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        layout.Children.Add(new HavenIcon
        {
            IconKey = item.IconKey,
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center
        });
        var copy = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock { Text = item.Title, FontWeight = Avalonia.Media.FontWeight.ExtraBold, FontSize = 13 },
                new TextBlock { Text = item.Detail, Classes = { "muted" }, FontSize = 10, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis }
            }
        };
        Grid.SetColumn(copy, 1);
        layout.Children.Add(copy);
        if (!string.IsNullOrWhiteSpace(item.KindLabel))
        {
            var kind = new HavenAdaptiveSurface
            {
                Background = Avalonia.Application.Current?.Resources["HavenPanel2Brush"] as Avalonia.Media.IBrush,
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(10, 4),
                Child = new TextBlock { Text = item.KindLabel, FontSize = 10, FontWeight = Avalonia.Media.FontWeight.Bold }
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
            CornerRadius = new CornerRadius(16)
        };
        button.Classes.Add("sidebar");
        button.Click += (_, _) => Invoke(item);
        return button;
    }

    private void Invoke(UniversalSearchItem item)
    {
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
    Action OnExecute);
