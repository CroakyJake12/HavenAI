using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Tokens;
using Haven.Desktop.Views.Shell;
using System.Text.Json;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// New Haven's app launcher popup. Defined in AXAML for consistency.
/// </summary>
public sealed partial class AppLauncherControl : UserControl
{
    private IReadOnlyList<ModeDefinition> _apps = [];
    private IReadOnlySet<Guid> _pinnedIds = new HashSet<Guid>();
    private Action<ModeDefinition, bool>? _launch;
    private Action? _manage;
    private bool _openInNewTab;
    private readonly AppLauncherFinalScene _havenScene;
    private readonly Haven.Desktop.HavenUI.Backend.HavenSceneControl _havenHost;

    public AppLauncherControl()
    {
        InitializeComponent();
        _havenScene = new AppLauncherFinalScene();
        _havenHost = new Haven.Desktop.HavenUI.Backend.HavenSceneControl { Root = _havenScene.Root };
        Content = _havenHost;
        _havenScene.AppRequested += app => _launch?.Invoke(app, _openInNewTab);
        _havenScene.ManageRequested += () => _manage?.Invoke();
    }

    public void Configure(
        IReadOnlyList<ModeDefinition> apps,
        IReadOnlySet<Guid> pinnedIds,
        bool openInNewTab,
        Action<ModeDefinition, bool> launch,
        Action manage)
    {
        _apps = apps;
        _pinnedIds = pinnedIds;
        _openInNewTab = openInNewTab;
        _launch = launch;
        _manage = manage;
        _havenScene.Configure(apps, pinnedIds);
    }

    public bool FocusSearch() => _havenHost.FocusElement(_havenScene.Search);

    internal AppLauncherFinalScene HavenScene => _havenScene;
    internal Haven.Desktop.HavenUI.Backend.HavenSceneControl SceneHost => _havenHost;

    private void Rebuild()
    {
        SectionsPanel.Children.Clear();
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var filtered = _apps
            .Where(item => item.IsEnabled)
            .Where(item => string.IsNullOrWhiteSpace(query)
                           || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var pinned = filtered.Where(item => _pinnedIds.Contains(item.Id)).ToArray();
        var unpinned = filtered.Where(item => !_pinnedIds.Contains(item.Id)).ToArray();
        var recommended = unpinned.Take(6).ToArray();
        var remaining = unpinned.Skip(recommended.Length).ToArray();

        AddSection("Pinned", string.IsNullOrWhiteSpace(query) ? pinned.Take(6).ToArray() : pinned);
        if (string.IsNullOrWhiteSpace(query) && pinned.Length > 6)
            AddInlineAction("View all pinned", "pin", () => _manage?.Invoke());
        AddSection("Recommended", recommended);
        AddSection("General", remaining.Where(item => CategoryFor(item) == "General").ToArray());
        AddSection("Productivity", remaining.Where(item => CategoryFor(item) == "Productivity").ToArray());
        AddSection("Media & creativity", remaining.Where(item => CategoryFor(item) == "Media & creativity").ToArray());
        AddSection("More", remaining.Where(item => CategoryFor(item) == "More").ToArray());
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
            var tile = BuildAppButton(item, () => _launch?.Invoke(item, _openInNewTab));
            Grid.SetColumn(tile, i % columns);
            Grid.SetRow(tile, i / columns);
            grid.Children.Add(tile);
        }

        SectionsPanel.Children.Add(grid);
    }

    private static Control BuildAppButton(ModeDefinition item, Action launch)
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 11 };
        var surface = HavenAppRoutePolicy.Resolve(item).Surface;
        var icon = new HavenIcon
        {
            IconKey = item.IconKey,
            Width = 21,
            Height = 21,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new HavenPill
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(0),
            Child = icon
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
        var button = new HavenButton
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Height = 56,
            Margin = new Thickness(2, 0, 2, 8),
            Padding = new Thickness(12, 8),
            Background = ResourceBrush("HavenCardSurfaceBrush", Colors.Transparent),
            BorderBrush = ResourceBrush("HavenBorderSubtleBrush", Colors.Transparent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Content = content
        };
        button.Classes.Add("sidebar");
        button.Click += (_, _) => launch();
        return new HavenAccentScope
        {
            AccentSurface = surface,
            Content = button
        };
    }

    private void AddInlineAction(string label, string iconKey, Action action)
    {
        var button = new HavenButton
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(13, 9),
            CornerRadius = new CornerRadius(14),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 9,
                Children =
                {
                    new HavenIcon { IconKey = iconKey, Width = 17, Height = 17 },
                    new TextBlock { Text = label, FontWeight = FontWeight.ExtraBold }
                }
            }
        };
        button.Classes.Add("sidebar");
        button.Click += (_, _) => action();
        SectionsPanel.Children.Add(button);
    }

    internal static string CategoryFor(ModeDefinition item)
    {
        try
        {
            var tags = JsonSerializer.Deserialize<string[]>(item.TagsJson) ?? [];
            if (tags.Any(tag => tag.Equals("general", StringComparison.OrdinalIgnoreCase))) return "General";
            if (tags.Any(tag => tag.Equals("media", StringComparison.OrdinalIgnoreCase)
                                || tag.Equals("creative", StringComparison.OrdinalIgnoreCase))) return "Media & creativity";
            if (tags.Any(tag => tag.Equals("productivity", StringComparison.OrdinalIgnoreCase))) return "Productivity";
        }
        catch (JsonException)
        {
            // User Apps with invalid legacy tags remain launchable in More until migration repairs their metadata.
        }

        var key = item.Key.Trim().ToLowerInvariant();
        if (key is "chat" or "dashboard" or "go" or "launcher") return "General";
        if (key is "imagine" or "present" or "vision" or "play") return "Media & creativity";
        if (key is "data" or "plan" or "study" or "tasks" or "translate" or "studio") return "Productivity";
        return "More";
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}
