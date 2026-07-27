using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// AXAML-defined actions flyout control. Replaces the code-generated flyout in DynamicActionToolbar.
/// </summary>
public sealed partial class ActionsFlyoutControl : UserControl
{
    private static readonly string[] Categories = ["Featured", "File", "Edit", "View", "Chat", "Project", "Tools", "Help"];
    private readonly List<DynamicActionToolbar.ToolbarAction> _availableActions = [];
    private Action? _editActions;

    public ActionsFlyoutControl()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => RebuildSections();
        EditActionsButton.Click += (_, _) =>
        {
            _editActions?.Invoke();
        };
    }

    public void SetActions(IReadOnlyList<DynamicActionToolbar.ToolbarAction> actions)
    {
        _availableActions.Clear();
        _availableActions.AddRange(actions);
        RebuildSections();
    }

    public void SetEditActionsHandler(Action onExecute) => _editActions = onExecute;

    public void FocusSearch() => SearchBox.Focus();

    private void RebuildSections()
    {
        SectionsPanel.Children.Clear();
        var query = SearchBox.Text?.Trim() ?? string.Empty;

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
                SectionsPanel.Children.Add(new TextBlock
                {
                    Text = "Featured",
                    FontWeight = FontWeight.ExtraBold,
                    FontSize = 12,
                    Margin = new Thickness(10, 7, 10, 2)
                });
                SectionsPanel.Children.Add(rows);
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
            SectionsPanel.Children.Add(new Border
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

    private Button BuildActionRow(DynamicActionToolbar.ToolbarAction action)
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
        button.Click += (_, _) => action.OnExecute();
        return button;
    }

    private static string ResolveCategory(DynamicActionToolbar.ToolbarAction action)
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

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}
