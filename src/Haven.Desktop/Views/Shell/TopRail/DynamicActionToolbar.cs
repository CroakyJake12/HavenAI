using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// New Haven's searchable, grouped Actions surface. The shell supplies semantic
/// actions; this control owns only their mockup-defined presentation.
/// </summary>
public sealed class DynamicActionToolbar : StackPanel, IDisposable
{
    private static readonly string[] Categories = ["Featured", "File", "Edit", "View", "Chat", "Project", "Tools", "Help"];
    private readonly List<ToolbarAction> _availableActions = [];
    private readonly List<ToolbarAction> _pinnedActions = [];
    private readonly List<ToolbarAction> _contextActions = [];
    private readonly Button _actionsButton;
    private Flyout? _actionsFlyout;
    private TextBox? _searchBox;
    private StackPanel? _sections;
    private Action? _editActions;
    private bool _disposed;

    public event EventHandler? ActionsClicked;

    public DynamicActionToolbar()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 3;
        VerticalAlignment = VerticalAlignment.Center;
        _actionsButton = BuildActionsButton();
        Children.Add(_actionsButton);
    }

    public void SetActions(IReadOnlyList<ToolbarAction> actions)
    {
        _availableActions.Clear();
        _availableActions.AddRange(actions);
        RebuildActionSections();
    }

    public void SetEditActionsHandler(Action onExecute) => _editActions = onExecute;

    public void PinAction(string label, string iconKey, Action onExecute, string? tooltip = null)
    {
        _pinnedActions.Add(new ToolbarAction(label, iconKey, onExecute, tooltip));
        RebuildPinnedActions();
    }

    public void UnpinAction(string label)
    {
        _pinnedActions.RemoveAll(action => action.Label == label);
        RebuildPinnedActions();
    }

    public void SetContextActions(IReadOnlyList<ToolbarAction> actions)
    {
        _contextActions.Clear();
        _contextActions.AddRange(actions);
        RebuildPinnedActions();
    }

    public void ShowActionsFlyout()
    {
        _actionsFlyout ??= BuildActionsFlyout();
        RebuildActionSections();
        _actionsFlyout.ShowAt(_actionsButton);
        _searchBox?.Focus();
    }

    private Button BuildActionsButton()
    {
        var button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new HavenIcon { IconKey = "bolt", Width = 18, Height = 18 },
                    new TextBlock { Text = "Actions", FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 },
                    new HavenIcon { IconKey = "chevron-down", Width = 12, Height = 12, Opacity = 0.72 }
                }
            },
            VerticalAlignment = VerticalAlignment.Center,
            Height = 42,
            Padding = new Thickness(12, 6)
        };
        button.Classes.Add("chrome");
        ToolTip.SetTip(button, "Actions menu · Ctrl+K");
        button.Click += (_, _) =>
        {
            ActionsClicked?.Invoke(this, EventArgs.Empty);
            ShowActionsFlyout();
        };
        return button;
    }

    private void RebuildPinnedActions()
    {
        while (Children.Count > 1) Children.RemoveAt(0);
        foreach (var action in _pinnedActions.Concat(_contextActions))
        {
            var button = new Button
            {
                Content = new HavenIcon { IconKey = action.IconKey, Width = 18, Height = 18 },
                Width = 42,
                Height = 42,
                Padding = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            button.Classes.Add("chrome");
            ToolTip.SetTip(button, action.Tooltip ?? action.Label);
            button.Click += (_, _) => action.OnExecute();
            Children.Add(button);
        }
    }

    private Flyout BuildActionsFlyout()
    {
        _searchBox = new TextBox
        {
            PlaceholderText = "Search actions",
            Padding = new Thickness(38, 10, 12, 10),
            FontSize = 14
        };
        _sections = new StackPanel { Spacing = 4 };
        _searchBox.TextChanged += (_, _) => RebuildActionSections();

        var searchHost = new Grid();
        searchHost.Children.Add(_searchBox);
        searchHost.Children.Add(new HavenIcon
        {
            IconKey = "search",
            Width = 17,
            Height = 17,
            Margin = new Thickness(13, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7,
            IsHitTestVisible = false
        });

        var heading = new TextBlock { Text = "Actions", FontSize = 20, FontWeight = FontWeight.ExtraBold };

        var mainContent = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(16),
            Children =
            {
                heading,
                searchHost,
                new ScrollViewer
                {
                    MaxHeight = 350,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = _sections
                }
            }
        };

        var editButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(14, 11),
            Content = BuildEditActionsContent()
        };
        editButton.Classes.Add("sidebar");
        editButton.Click += (_, _) =>
        {
            _actionsFlyout?.Hide();
            _editActions?.Invoke();
        };

        var content = new StackPanel
        {
            Spacing = 9,
            Children =
            {
                new Border
                {
                    Width = 410,
                    Background = ResourceBrush("HavenElevatedBrush", Colors.White),
                    BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(30, 0, 0, 0)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(24),
                    Child = mainContent
                },
                new Border
                {
                    Width = 410,
                    Background = ResourceBrush("HavenElevatedBrush", Colors.White),
                    BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(30, 0, 0, 0)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(20),
                    Padding = new Thickness(4),
                    Child = editButton
                }
            }
        };

        return new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterTheme = FloatingPresenterTheme(),
            Content = content
        };
    }

    private void RebuildActionSections()
    {
        if (_sections is null) return;
        _sections.Children.Clear();
        var query = _searchBox?.Text?.Trim() ?? string.Empty;

        foreach (var category in Categories)
        {
            var actions = _availableActions
                .Where(action => ResolveCategory(action) == category)
                .Where(action => string.IsNullOrWhiteSpace(query)
                                 || action.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                                 || action.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                                 || category.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (!string.IsNullOrWhiteSpace(query) && actions.Length == 0) continue;

            var rows = new StackPanel { Spacing = 2, Margin = new Thickness(0, 3, 0, 6) };
            foreach (var action in actions) rows.Children.Add(BuildActionRow(action));
            if (actions.Length == 0)
            {
                rows.Children.Add(new TextBlock
                {
                    Text = category == "Help" ? "Ctrl+K opens Actions from anywhere." : "No actions in this section.",
                    Classes = { "muted" },
                    FontSize = 11,
                    Margin = new Thickness(12, 6)
                });
            }

            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
            header.Children.Add(new HavenIcon { IconKey = CategoryIcon(category), Width = 16, Height = 16, Opacity = 0.76 });
            var label = new TextBlock { Text = category, FontWeight = FontWeight.Bold, FontSize = 14 };
            Grid.SetColumn(label, 1);
            header.Children.Add(label);

            if (category == "Featured")
            {
                if (actions.Length == 0) continue;
                _sections.Children.Add(new TextBlock
                {
                    Text = "Featured",
                    FontWeight = FontWeight.ExtraBold,
                    FontSize = 12,
                    Margin = new Thickness(10, 7, 10, 2)
                });
                _sections.Children.Add(rows);
                continue;
            }

            var section = new Expander
            {
                Header = header,
                IsExpanded = !string.IsNullOrWhiteSpace(query),
                Content = rows,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            section.Classes.Add("actionSection");
            _sections.Children.Add(new Border
            {
                Background = ResourceBrush("HavenPanel2Brush", Color.Parse("#FFF8F8F8")),
                BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(26, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(4, 2),
                Child = section
            });
        }
    }

    private Button BuildActionRow(ToolbarAction action)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 11 };
        grid.Children.Add(new HavenIcon
        {
            IconKey = action.IconKey,
            Width = 17,
            Height = 17,
            Opacity = 0.8,
            VerticalAlignment = VerticalAlignment.Center
        });
        var text = new TextBlock
        {
            Text = action.Label,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var shortcut = new TextBlock
        {
            Text = action.Shortcut,
            Classes = { "muted" },
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = !string.IsNullOrWhiteSpace(action.Shortcut)
        };
        Grid.SetColumn(shortcut, 2);
        grid.Children.Add(shortcut);

        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 38,
            Padding = new Thickness(10, 7),
            Content = grid
        };
        button.Classes.Add("sidebar");
        button.Click += (_, _) =>
        {
            _actionsFlyout?.Hide();
            action.OnExecute();
        };
        return button;
    }

    private static string ResolveCategory(ToolbarAction action)
    {
        if (action.IsFeatured) return "Featured";
        if (Categories.Contains(action.Category, StringComparer.OrdinalIgnoreCase))
            return Categories.First(category => category.Equals(action.Category, StringComparison.OrdinalIgnoreCase));

        var name = action.Label.ToLowerInvariant();
        if (name.Contains("new ") || name.StartsWith("archive") || name.Contains("activity log") || name.Contains("delete")) return "File";
        if (name.Contains("rename") || name.Contains("copy") || name.Contains("undo") || name.Contains("redo") || name.Contains("save")) return "Edit";
        if (name.Contains("sidebar") || name.Contains("app library") || name.Contains("notification")) return "View";
        if (name.Contains("branch") || name.Contains("chat") || name.Contains("context") || name.Contains("model") || name.Contains("instruction") || name.Contains("plugin") || name.Contains("pin")) return "Chat";
        if (name.Contains("project") || name.Contains("macro") || name.Contains("extension")) return "Project";
        if (name.Contains("browse") || name.Contains("training") || name.Contains("scheduled") || name.Contains("refresh") || name.Contains("settings")) return "Tools";
        return "Help";
    }

    private static string CategoryIcon(string category) => category switch
    {
        "Featured" => "sparkles",
        "File" => "file",
        "Edit" => "edit",
        "View" => "browse",
        "Chat" => "chat",
        "Project" => "studio",
        "Tools" => "settings",
        _ => "info"
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _availableActions.Clear();
        _pinnedActions.Clear();
        _contextActions.Clear();
        _editActions = null;
    }

    private static Control BuildEditActionsContent() => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 12,
        Children =
        {
            new HavenIcon { IconKey = "settings", Width = 19, Height = 19 },
            new TextBlock { Text = "Edit Actions & Toolbar", FontWeight = FontWeight.ExtraBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center }
        }
    };

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    private static ControlTheme? FloatingPresenterTheme() =>
        Avalonia.Application.Current?.TryFindResource("HavenFloatingFlyoutPresenterTheme", out var value) == true
            ? value as ControlTheme
            : null;

    public sealed record ToolbarAction(
        string Label,
        string IconKey,
        Action OnExecute,
        string? Tooltip = null,
        string Category = "",
        string Description = "",
        string Shortcut = "",
        bool IsFeatured = false);
}
