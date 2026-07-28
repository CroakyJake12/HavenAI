using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// Mockup-defined composer Add menu. Catalogues open as a second panel beside
/// the Add panel; no legacy catalogue page or inline dropdown is reused.
/// </summary>
public sealed class AddMenu : Button, IDisposable
{
    private IReadOnlyList<AgentDefinition> _agents = [];
    private IReadOnlyList<PluginDefinition> _plugins = [];
    private IReadOnlyList<PromptDefinition> _instructions = [];
    private IReadOnlyList<ModeDefinition> _apps = [];
    private readonly List<Button> _topLevelItems = [];
    private Flyout? _flyout;
    private Flyout? _catalogFlyout;
    private bool _disposed;

    public AddMenu()
    {
        Content = new HavenIcon { IconKey = "plus", Width = 18, Height = 18 };
        Classes.Add("chrome");
        ToolTip.SetTip(this, "Add");
        Click += OnClick;
    }

    public event EventHandler<AddMenuAction>? ActionSelected;
    public event EventHandler<AddMenuSelection>? CatalogItemSelected;

    public void ShowMenu()
    {
        _flyout ??= BuildFlyout();
        _flyout.ShowAt(this);
    }

    public void SetCatalogue(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<PluginDefinition> plugins,
        IReadOnlyList<PromptDefinition> instructions,
        IReadOnlyList<ModeDefinition> apps)
    {
        _agents = agents.Where(item => item.IsEnabled).OrderBy(item => item.Name).ToArray();
        _plugins = plugins.Where(item => item.IsEnabled).OrderBy(item => item.Name).ToArray();
        _instructions = instructions.Where(item => item.IsEnabled).OrderBy(item => item.Name).ToArray();
        _apps = apps.Where(item => item.IsEnabled).OrderBy(item => item.Name).ToArray();
    }

    private void OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ShowMenu();
    }

    private Flyout BuildFlyout()
    {
        _topLevelItems.Clear();
        var panel = new StackPanel { Width = 260, Spacing = 3, Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = "Add",
            FontSize = 20,
            FontWeight = FontWeight.ExtraBold,
            Margin = new Thickness(10, 5, 10, 8)
        });
        panel.Children.Add(BuildTopLevelItem("File", "file", AddMenuAction.File));
        panel.Children.Add(BuildTopLevelItem("Agent", "agents", AddMenuAction.Agent));
        panel.Children.Add(BuildTopLevelItem("Plugin", "plugin", AddMenuAction.Plugin));
        panel.Children.Add(BuildTopLevelItem("Instruction", "prompt", AddMenuAction.Instruction));
        panel.Children.Add(BuildTopLevelItem("App", "rocket", AddMenuAction.App));
        return new Flyout { Placement = PlacementMode.TopEdgeAlignedLeft, Content = panel };
    }

    private Button BuildTopLevelItem(string label, string iconKey, AddMenuAction action)
    {
        var button = BuildRow(iconKey, label);
        _topLevelItems.Add(button);
        button.Click += (_, _) =>
        {
            if (action == AddMenuAction.File)
            {
                _catalogFlyout?.Hide();
                _flyout?.Hide();
                ActionSelected?.Invoke(this, action);
                return;
            }

            foreach (var item in _topLevelItems) item.Classes.Remove("sidebarActive");
            button.Classes.Add("sidebarActive");
            ActionSelected?.Invoke(this, action);
            ShowCatalogue(button, action);
        };
        return button;
    }

    private void ShowCatalogue(Control anchor, AddMenuAction action)
    {
        _catalogFlyout?.Hide();
        var search = new TextBox
        {
            PlaceholderText = "Search",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(38, 10, 12, 10)
        };
        var results = new StackPanel { Spacing = 3, Margin = new Thickness(6, 0, 10, 6) };
        void Rebuild()
        {
            results.Children.Clear();
            var query = search.Text?.Trim() ?? string.Empty;
            switch (action)
            {
                case AddMenuAction.Agent:
                    AddAgentSection(results, "Personalities", _agents.Where(IsPersonality), query);
                    AddAgentSection(results, "Tools", _agents.Where(item => !IsPersonality(item)), query);
                    break;
                case AddMenuAction.Plugin:
                    AddPluginSection(results, "General", _plugins.Where(item => !item.IsAgentic), query);
                    AddPluginSection(results, "Productivity", _plugins.Where(item => item.IsAgentic), query);
                    break;
                case AddMenuAction.Instruction:
                    AddInstructionSection(results, "Instructions", _instructions, query);
                    break;
                case AddMenuAction.App:
                    AddAppSection(results, "Apps", _apps, query);
                    break;
            }
        }
        search.TextChanged += (_, _) => Rebuild();

        var searchHost = new Grid();
        searchHost.Children.Add(search);
        searchHost.Children.Add(new HavenIcon
        {
            IconKey = "search", Width = 18, Height = 18, Margin = new Thickness(13, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, Opacity = 0.72
        });

        var title = action switch
        {
            AddMenuAction.Agent => "Agents",
            AddMenuAction.Plugin => "Plugins",
            AddMenuAction.Instruction => "Instructions",
            _ => "Apps"
        };
        var footer = action switch
        {
            AddMenuAction.Agent => "Create new Agents in Studio",
            AddMenuAction.Plugin => "Create new Plugins in Studio",
            AddMenuAction.Instruction => "Create new Instructions in Studio",
            _ => "Manage Apps"
        };

        var panel = new StackPanel
        {
            Width = 340,
            Spacing = 9,
            Margin = new Thickness(10),
            Children =
            {
                new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.ExtraBold, Margin = new Thickness(10, 5, 10, 0) },
                searchHost,
                new ScrollViewer
                {
                    MaxHeight = 420,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = results
                },
                new TextBlock
                {
                    Text = footer,
                    Classes = { "muted" },
                    FontSize = 11,
                    FontStyle = FontStyle.Italic,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(4, 8)
                }
            }
        };
        Rebuild();
        _catalogFlyout = new Flyout { Placement = PlacementMode.RightEdgeAlignedBottom, Content = panel };
        _catalogFlyout.ShowAt(anchor);
        search.Focus();
    }

    private void AddAgentSection(StackPanel panel, string heading, IEnumerable<AgentDefinition> source, string query)
    {
        var items = source.Where(item => Matches(item.Name, item.Description, query)).ToArray();
        AddHeading(panel, heading, items.Length);
        foreach (var item in items)
        {
            var row = BuildRow(item.IconKey, item.Name, item.Description);
            row.Click += (_, _) => Select(new AddMenuSelection(AddMenuAction.Agent, item));
            panel.Children.Add(row);
        }
    }

    private void AddPluginSection(StackPanel panel, string heading, IEnumerable<PluginDefinition> source, string query)
    {
        var items = source.Where(item => Matches(item.Name, item.Description, query)).ToArray();
        AddHeading(panel, heading, items.Length);
        foreach (var item in items)
        {
            var row = BuildRow(item.IconKey, item.Name, item.Description);
            row.Click += (_, _) => Select(new AddMenuSelection(AddMenuAction.Plugin, item));
            panel.Children.Add(row);
        }
    }

    private void AddInstructionSection(StackPanel panel, string heading, IEnumerable<PromptDefinition> source, string query)
    {
        var items = source.Where(item => Matches(item.Name, item.Description, query)).ToArray();
        AddHeading(panel, heading, items.Length);
        foreach (var item in items)
        {
            var row = BuildRow(item.IconKey, item.Name, item.Description);
            row.Click += (_, _) => Select(new AddMenuSelection(AddMenuAction.Instruction, item));
            panel.Children.Add(row);
        }
    }

    private void AddAppSection(StackPanel panel, string heading, IEnumerable<ModeDefinition> source, string query)
    {
        var items = source.Where(item => Matches(item.Name, item.Description, query)).ToArray();
        AddHeading(panel, heading, items.Length);
        foreach (var item in items)
        {
            var row = BuildRow(item.IconKey, item.Name, item.Description);
            row.Click += (_, _) => Select(new AddMenuSelection(AddMenuAction.App, item));
            panel.Children.Add(row);
        }
    }

    private void Select(AddMenuSelection selection)
    {
        _catalogFlyout?.Hide();
        _flyout?.Hide();
        CatalogItemSelected?.Invoke(this, selection);
    }

    private static void AddHeading(StackPanel panel, string title, int count)
    {
        if (count == 0) return;
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.ExtraBold,
            FontSize = 12,
            Margin = new Thickness(9, 8, 9, 3)
        });
    }

    private static Button BuildRow(string iconKey, string label, string? description = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        grid.Children.Add(new HavenIcon { IconKey = iconKey, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center });
        var text = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock { Text = label, FontWeight = FontWeight.ExtraBold, FontSize = 14 },
                new TextBlock { Text = description ?? string.Empty, Classes = { "muted" }, FontSize = 10, IsVisible = !string.IsNullOrWhiteSpace(description), TextWrapping = TextWrapping.Wrap }
            }
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 10),
            MinHeight = 48,
            CornerRadius = new CornerRadius(14),
            Content = grid
        };
        button.Classes.Add("sidebar");
        return button;
    }

    private static bool IsPersonality(AgentDefinition item) =>
        string.IsNullOrWhiteSpace(item.DetectionRules) && item.PermissionsJson.Trim() is "{}" or "";

    private static bool Matches(string name, string description, string query) =>
        string.IsNullOrWhiteSpace(query)
        || name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || description.Contains(query, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Click -= OnClick;
        _catalogFlyout?.Hide();
        _flyout?.Hide();
    }

    public enum AddMenuAction { File, Agent, Plugin, Instruction, App }
}

public sealed record AddMenuSelection(AddMenu.AddMenuAction Kind, object Item);
