using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// Searchable contextual action catalogue. Actions are grouped and rendered in
/// the mockup's stable three-column layout; the header itself never hosts pins.
/// </summary>
public sealed partial class ActionsFlyoutControl : UserControl
{
    private static readonly string[] CategoryOrder =
    [
        "Pinned", "Recommended", "General", "Chat", "Study", "Tasks", "Studio", "Browser",
        "Plan", "Data", "Media", "File", "View", "Tools", "Help"
    ];

    private readonly List<DynamicActionToolbar.ToolbarAction> _availableActions = [];
    private Action? _editActions;

    public ActionsFlyoutControl()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => RebuildSections();
        EditActionsButton.Click += (_, _) =>
        {
            _editActions?.Invoke();
            ActionInvoked?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? ActionInvoked;

    public void SetActions(IReadOnlyList<DynamicActionToolbar.ToolbarAction> actions)
    {
        _availableActions.Clear();
        _availableActions.AddRange(actions);
        RebuildSections();
    }

    public void SetEditActionsHandler(Action onExecute) => _editActions = onExecute;

    public void FocusSearch()
    {
        SearchBox.SelectAll();
        SearchBox.Focus();
    }

    private void RebuildSections()
    {
        SectionsPanel.Children.Clear();
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var matches = _availableActions
            .Where(action => string.IsNullOrWhiteSpace(query)
                             || action.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                             || action.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                             || action.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var category in CategoryOrder)
        {
            var actions = matches.Where(action => ResolveCategory(action) == category).ToArray();
            if (actions.Length == 0) continue;

            SectionsPanel.Children.Add(new TextBlock
            {
                Text = category,
                FontWeight = Avalonia.Media.FontWeight.ExtraBold,
                FontSize = 13,
                Margin = new Thickness(5, 8, 5, 2)
            });
            SectionsPanel.Children.Add(BuildActionGrid(actions));
        }

        if (matches.Length == 0)
        {
            SectionsPanel.Children.Add(new HavenAdaptiveSurface
            {
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(18),
                Child = new TextBlock
                {
                    Text = "No capabilities match this search in the current App.",
                    Classes = { "muted" },
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            });
        }
    }

    private Grid BuildActionGrid(IReadOnlyList<DynamicActionToolbar.ToolbarAction> actions)
    {
        const int columns = 3;
        var rowCount = (int)Math.Ceiling(actions.Count / (double)columns);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("Auto", rowCount))),
            ColumnSpacing = 8,
            RowSpacing = 8
        };

        for (var index = 0; index < actions.Count; index++)
        {
            var button = BuildActionTile(actions[index]);
            Grid.SetColumn(button, index % columns);
            Grid.SetRow(button, index / columns);
            grid.Children.Add(button);
        }
        return grid;
    }

    private Button BuildActionTile(DynamicActionToolbar.ToolbarAction action)
    {
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10
        };
        content.Children.Add(new HavenIcon
        {
            IconKey = action.IconKey,
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center
        });
        var label = new TextBlock
        {
            Text = action.Label,
            FontWeight = Avalonia.Media.FontWeight.ExtraBold,
            FontSize = 13,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            MaxLines = 2
        };
        Grid.SetColumn(label, 1);
        content.Children.Add(label);

        var button = new HavenButton
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 68,
            Padding = new Thickness(14, 11),
            CornerRadius = new CornerRadius(14),
            Background = ResourceBrush("HavenPanel2Brush", Avalonia.Media.Color.Parse("#FFF3F3F3")),
            BorderBrush = ResourceBrush("HavenLineBrush", Avalonia.Media.Color.FromArgb(24, 0, 0, 0)),
            BorderThickness = new Thickness(1)
        };
        var automationKey = string.Concat(action.Label.Where(char.IsLetterOrDigit));
        AutomationProperties.SetAutomationId(button, $"CapabilityAction_{automationKey}");
        AutomationProperties.SetName(button, action.Label);
        var normalBackground = button.Background;
        button.PointerEntered += (_, _) =>
            button.Background = ResourceBrush("HavenAccentSoftBrush", Avalonia.Media.Color.Parse("#FFE2F7F5"));
        button.PointerExited += (_, _) => button.Background = normalBackground;
        ToolTip.SetTip(button, string.IsNullOrWhiteSpace(action.Description)
            ? action.Tooltip ?? action.Label
            : action.Description + (string.IsNullOrWhiteSpace(action.Shortcut) ? string.Empty : $" · {action.Shortcut}"));
        button.Click += (_, _) =>
        {
            action.OnExecute();
            ActionInvoked?.Invoke(this, EventArgs.Empty);
        };
        return button;
    }

    private static Avalonia.Media.IBrush ResourceBrush(string key, Avalonia.Media.Color fallback) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true
        && value is Avalonia.Media.IBrush brush
            ? brush
            : new Avalonia.Media.SolidColorBrush(fallback);

    private static string ResolveCategory(DynamicActionToolbar.ToolbarAction action)
    {
        if (action.IsFeatured) return "Pinned";
        if (CategoryOrder.Contains(action.Category, StringComparer.OrdinalIgnoreCase))
            return CategoryOrder.First(category => category.Equals(action.Category, StringComparison.OrdinalIgnoreCase));

        var name = action.Label.ToLowerInvariant();
        if (name.Contains("branch") || name.Contains("chat") || name.Contains("agent") || name.Contains("plugin") || name.Contains("response")) return "Chat";
        if (name.Contains("project") || name.Contains("build") || name.Contains("test") || name.Contains("git")) return "Studio";
        if (name.Contains("browser") || name.Contains("page") || name.Contains("bookmark")) return "Browser";
        if (name.Contains("task") || name.Contains("timer") || name.Contains("automation")) return "Tasks";
        if (name.Contains("plan") || name.Contains("calendar")) return "Plan";
        if (name.Contains("save") || name.Contains("new") || name.Contains("delete") || name.Contains("archive")) return "File";
        if (name.Contains("sidebar") || name.Contains("tab") || name.Contains("zoom")) return "View";
        return "Tools";
    }
}
