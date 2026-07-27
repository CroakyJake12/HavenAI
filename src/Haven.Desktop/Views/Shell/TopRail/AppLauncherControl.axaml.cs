using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// New Haven's app launcher popup. Defined in AXAML for consistency.
/// </summary>
public sealed partial class AppLauncherControl : UserControl
{
    private const string TasksKey = "tasks";

    private static readonly ModeDefinition TasksApp = new(
        Guid.Parse("7f0f5ef7-0ca5-4f51-a596-b0ed7d792417"),
        TasksKey,
        "Tasks",
        "Requests and reusable tasks",
        "tasks",
        HavenMode.Do,
        "[]",
        "[]",
        "[]",
        "[]",
        string.Empty,
        ModeSource.BuiltIn,
        ModeInstallState.BuiltIn,
        "Haven",
        "1.0.0",
        "[\"productivity\"]",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch);

    private IReadOnlyList<ModeDefinition> _apps = [];
    private IReadOnlySet<Guid> _pinnedIds = new HashSet<Guid>();
    private Action<ModeDefinition, bool>? _launch;
    private Action? _manage;

    public AppLauncherControl()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => Rebuild();
        ManageButton.Click += (_, _) =>
        {
            _manage?.Invoke();
        };
    }

    public void Configure(
        IReadOnlyList<ModeDefinition> apps,
        IReadOnlySet<Guid> pinnedIds,
        bool openInNewTab,
        Action<ModeDefinition, bool> launch,
        Action manage)
    {
        _apps = apps.Any(item => item.Key.Equals(TasksKey, StringComparison.OrdinalIgnoreCase))
            ? apps
            : apps.Concat([TasksApp]).ToArray();
        _pinnedIds = pinnedIds;
        _launch = launch;
        _manage = manage;
        Rebuild();
    }

    private void Rebuild()
    {
        SectionsPanel.Children.Clear();
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var filtered = _apps
            .Where(item => item.IsEnabled)
            .Where(item => string.IsNullOrWhiteSpace(query)
                           || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Name)
            .ToArray();
        AddSection("Pinned", filtered.Where(item => _pinnedIds.Contains(item.Id)).ToArray());
        AddSection("Productivity", filtered.Where(item => !_pinnedIds.Contains(item.Id)).ToArray());
    }

    private void AddSection(string title, IReadOnlyList<ModeDefinition> items)
    {
        if (items.Count == 0) return;
        SectionsPanel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.ExtraBold,
            FontSize = 12,
            Margin = new Thickness(4, 7, 4, 2)
        });

        // 3-column grid layout
        var columns = 3;
        var rows = (int)Math.Ceiling((double)items.Count / columns);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", rows))),
            ColumnSpacing = 8,
            RowSpacing = 8
        };

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var button = BuildAppButton(item);
            button.Click += (_, _) =>
            {
                if (item.Key.Equals(TasksKey, StringComparison.OrdinalIgnoreCase))
                {
                    OpenTasksDashboard();
                    return;
                }

                _launch?.Invoke(item, false);
            };
            Grid.SetColumn(button, i % columns);
            Grid.SetRow(button, i / columns);
            grid.Children.Add(button);
        }

        SectionsPanel.Children.Add(grid);
    }

    private static Button BuildAppButton(ModeDefinition item)
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 11 };
        content.Children.Add(new HavenIcon
        {
            IconKey = item.IconKey,
            Width = 22,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Center
        });
        var text = new TextBlock
        {
            Text = item.Name,
            FontWeight = FontWeight.ExtraBold,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Width = 190,
            Height = 56,
            Margin = new Thickness(2, 0, 2, 8),
            Padding = new Thickness(12, 8),
            Background = ResourceBrush("HavenPanel2Brush", Color.Parse("#FFF8F8F8")),
            BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(28, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Content = content
        };
        button.Classes.Add("sidebar");
        var normalBackground = button.Background;
        button.PointerEntered += (_, _) => button.Background = ResourceBrush("HavenAccentSoftBrush", Color.Parse("#FFE0F7FA"));
        button.PointerExited += (_, _) => button.Background = normalBackground;
        return button;
    }

    private static void OpenTasksDashboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.DataContext is MainView shell)
        {
            shell.OpenTasksDashboard();
        }
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}
