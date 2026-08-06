using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// AXAML-defined catalogue flyout control. Replaces the code-generated catalogue in AddMenu.
/// </summary>
public sealed partial class AddMenuCatalogueControl : UserControl
{
    private IReadOnlyList<AgentDefinition> _agents = [];
    private IReadOnlyList<PluginDefinition> _plugins = [];
    private IReadOnlyList<PromptDefinition> _instructions = [];
    private IReadOnlyList<ModeDefinition> _apps = [];
    private AddMenu.AddMenuAction _currentAction;

    public AddMenuCatalogueControl()
    {
        InitializeComponent();
        SearchBox.TextChanged += (_, _) => Rebuild();
    }

    public event EventHandler<AddMenuSelection>? ItemSelected;

    public void Configure(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<PluginDefinition> plugins,
        IReadOnlyList<PromptDefinition> instructions,
        IReadOnlyList<ModeDefinition> apps)
    {
        _agents = agents;
        _plugins = plugins;
        _instructions = instructions;
        _apps = apps;
    }

    public void ShowCatalogue(AddMenu.AddMenuAction action)
    {
        _currentAction = action;
        TitleText.Text = action switch
        {
            AddMenu.AddMenuAction.Agent => "Agents",
            AddMenu.AddMenuAction.Plugin => "Plugins",
            AddMenu.AddMenuAction.Instruction => "Instructions",
            _ => "Apps"
        };
        FooterText.Text = action switch
        {
            AddMenu.AddMenuAction.Agent => "Create new Agents in Studio",
            AddMenu.AddMenuAction.Plugin => "Create new Plugins in Studio",
            AddMenu.AddMenuAction.Instruction => "Create new Instructions in Studio",
            _ => "Manage Apps"
        };
        SearchBox.Text = string.Empty;
        Rebuild();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => SearchBox.Focus(), Avalonia.Threading.DispatcherPriority.Background);
    }

    private void Rebuild()
    {
        ResultsPanel.Children.Clear();
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        switch (_currentAction)
        {
            case AddMenu.AddMenuAction.Agent:
                AddAgentSection("Personalities", _agents.Where(IsPersonality), query);
                AddAgentSection("Tools", _agents.Where(item => !IsPersonality(item)), query);
                break;
            case AddMenu.AddMenuAction.Plugin:
                AddPluginSection("General", _plugins.Where(item => !item.IsAgentic), query);
                AddPluginSection("Productivity", _plugins.Where(item => item.IsAgentic), query);
                break;
            case AddMenu.AddMenuAction.Instruction:
                AddInstructionSection("Instructions", _instructions, query);
                break;
            case AddMenu.AddMenuAction.App:
                AddAppSection("Apps", _apps, query);
                break;
        }
    }

    private void AddAgentSection(string heading, IEnumerable<AgentDefinition> source, string query)
    {
        var items = source.Where(item => Matches(item.Name, item.Description, query)).ToArray();
        AddHeading(heading, items.Length);
        foreach (var item in items)
        {
            var row = BuildRow(item.IconKey, item.Name, item.Description);
            row.Click += (_, _) => Select(new AddMenuSelection(AddMenu.AddMenuAction.Agent, item));
            ResultsPanel.Children.Add(row);
        }
    }

    private void AddPluginSection(string heading, IEnumerable<PluginDefinition> source, string query)
    {
        var items = source.Where(item => Matches(item.Name, item.Description, query)).ToArray();
        AddHeading(heading, items.Length);
        foreach (var item in items)
        {
            var row = BuildRow(item.IconKey, item.Name, item.Description);
            row.Click += (_, _) => Select(new AddMenuSelection(AddMenu.AddMenuAction.Plugin, item));
            ResultsPanel.Children.Add(row);
        }
    }

    private void AddInstructionSection(string heading, IEnumerable<PromptDefinition> source, string query)
    {
        var items = source.Where(item => Matches(item.Name, item.Description, query)).ToArray();
        AddHeading(heading, items.Length);
        foreach (var item in items)
        {
            var row = BuildRow(item.IconKey, item.Name, item.Description);
            row.Click += (_, _) => Select(new AddMenuSelection(AddMenu.AddMenuAction.Instruction, item));
            ResultsPanel.Children.Add(row);
        }
    }

    private void AddAppSection(string heading, IEnumerable<ModeDefinition> source, string query)
    {
        var items = source.Where(item => Matches(item.Name, item.Description, query)).ToArray();
        AddHeading(heading, items.Length);
        foreach (var item in items)
        {
            var row = BuildRow(item.IconKey, item.Name, item.Description);
            row.Click += (_, _) => Select(new AddMenuSelection(AddMenu.AddMenuAction.App, item));
            ResultsPanel.Children.Add(row);
        }
    }

    private void Select(AddMenuSelection selection)
    {
        ItemSelected?.Invoke(this, selection);
    }

    private void AddHeading(string title, int count)
    {
        if (count == 0) return;
        ResultsPanel.Children.Add(new TextBlock
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
}
