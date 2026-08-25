using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Components;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// Mockup-defined composer Add menu. Catalogues open as a second panel beside
/// the Add panel; no legacy catalogue page or inline dropdown is reused.
/// </summary>
public sealed class AddMenu : HavenIconButton, IDisposable
{
    private IReadOnlyList<AgentDefinition> _agents = [];
    private IReadOnlyList<CapabilityDefinition> _capabilities = [];
    private IReadOnlyList<PromptDefinition> _instructions = [];
    private IReadOnlyList<ModeDefinition> _apps = [];

    private Flyout? _flyout;
    private ContentControl? _catalogHost;
    
    private bool _disposed;

    public AddMenu()
    {
        Content = new HavenIcon { IconKey = "plus", Width = 18, Height = 18 };
        ToolTip.SetTip(this, "Add");
        Click += OnClick;
    }

    public event EventHandler<AddMenuAction>? ActionSelected;
    public event EventHandler<AddMenuSelection>? CatalogItemSelected;
    public Func<string>? CurrentAgentNameProvider { get; set; }

    public void ShowMenu()
    {
        _flyout ??= BuildFlyout();
        HideCatalogue();
        _flyout.ShowAt(this);
    }

    public void SetCatalogue(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<CapabilityDefinition> capabilities,
        IReadOnlyList<PromptDefinition> instructions,
        IReadOnlyList<ModeDefinition> apps)
    {
        _agents = agents.Where(item => item.IsEnabled).OrderBy(item => item.Name).ToArray();
        _capabilities = capabilities.Where(item => item.IsEnabled && item.IsAttachable).OrderBy(item => item.Name).ToArray();
        _instructions = instructions.Where(item => item.IsEnabled).OrderBy(item => item.Name).ToArray();
        _apps = apps.Where(item => item.IsEnabled).OrderBy(item => item.Name).ToArray();
    }

    private void OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ShowMenu();
    }

    private Flyout BuildFlyout()
    {

        var panel = new StackPanel { Width = 300, Spacing = 10 };

        panel.Children.Add(new TextBlock
        {
            Text = "Manage Responses",
            FontSize = 22,
            FontWeight = FontWeight.ExtraBold,
            Margin = new Thickness(10, 5, 10, 8)
        });

        panel.Children.Add(new TextBlock { Text = "Available Tools", FontSize = 14, FontWeight = FontWeight.ExtraBold, Margin = new Thickness(10, 0, 10, 0) });
        panel.Children.Add(BuildToolRow());
        
        
        panel.Children.Add(new TextBlock { Text = "Options", FontSize = 14, FontWeight = FontWeight.ExtraBold, Margin = new Thickness(10, 2, 10, 0) });
        panel.Children.Add(BuildOptionItem("Allow Actions", AddMenuAction.AllowActions));
        panel.Children.Add(BuildOptionItem("Prefer Visual Responses", AddMenuAction.VisualResponses));
        var card = new HavenDropdownCard
        {
            Width = 324,
            MinWidth = 324,
            Padding = new Thickness(14),
            Child = panel
        };
        var main = new StackPanel { Width = 324, Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom };
        main.Children.Add(BuildAttachFileButton());
        main.Children.Add(card);

        _catalogHost = new ContentControl
        {
            Width = 320,
            MinWidth = 320,
            IsVisible = true,
            Opacity = 0,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,6,Auto"),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        root.Children.Add(main);
        Grid.SetColumn(_catalogHost, 2);
        root.Children.Add(_catalogHost);
        return new HavenDropdown
        {
            Placement = PlacementMode.TopEdgeAlignedLeft,
            FlyoutPresenterTheme = FloatingPresenterTheme(),
            Content = root
        };
    }

    private HavenPrimaryButton BuildAttachFileButton()
    {
        var button = new HavenPrimaryButton
        {
            MinHeight = 46,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 8),
            Content = BuildInlineLabel("paperclip", "Attach File(s)", 14)
        };
        AutomationProperties.SetName(button, "Attach File(s)");
        button.Click += (_, _) =>
        {
            HideCatalogue();
            _flyout?.Hide();
            ActionSelected?.Invoke(this, AddMenuAction.File);
        };
        return button;
    }

    private StackPanel BuildToolRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(8, 0, 8, 10)
        };
        row.Children.Add(BuildToolButton("agents", "Agents", AddMenuAction.Agent));
        row.Children.Add(BuildToolButton("prompt", "Instructions", AddMenuAction.Instruction));
        row.Children.Add(BuildToolButton("bolt", "Capabilities", AddMenuAction.Capability));
        row.Children.Add(BuildToolButton("rocket", "Apps", AddMenuAction.App));
        return row;
    }

    private HavenIconButton BuildToolButton(string iconKey, string label, AddMenuAction action)
    {
        var button = new HavenIconButton
        {
            Width = 46,
            Height = 46,
            Content = new HavenIcon { IconKey = iconKey, Width = 22, Height = 22 }
        };
        button.Classes.Add("accent");
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            ActionSelected?.Invoke(this, action);
            ShowCatalogue(action);
        };
        return button;
    }

    private HavenDropdownItemButton BuildOptionItem(string label, AddMenuAction action)
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 16,
            FontWeight = FontWeight.ExtraBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var chevron = new HavenIcon
        {
            IconKey = "chevron-right",
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(chevron, 1);
        content.Children.Add(chevron);
        var button = new HavenDropdownItemButton
        {
            MinHeight = 58,
            Padding = new Thickness(14, 9),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = content
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            ActionSelected?.Invoke(this, action);
            ShowCatalogue(action);
        };
        return button;
    }

    private static Grid BuildInlineLabel(string iconKey, string label, double fontSize)
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
        content.Children.Add(new HavenIcon
        {
            IconKey = iconKey,
            Width = 28,
            Height = 28,
            VerticalAlignment = VerticalAlignment.Center
        });
        var text = new TextBlock
        {
            Text = label,
            FontSize = fontSize,
            FontWeight = FontWeight.ExtraBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        return content;
    }

    private Button BuildTopLevelItem(string label, string iconKey, AddMenuAction action)
    {
        var button = BuildRow(iconKey, label);

        button.Click += (_, _) =>
        {
            if (action == AddMenuAction.File)
            {
                HideCatalogue();
                _flyout?.Hide();
                ActionSelected?.Invoke(this, action);
                return;
            }


            button.Classes.Add("selected");
            ActionSelected?.Invoke(this, action);
            ShowCatalogue(action);
        };
        return button;
    }

    private void ShowCatalogue(AddMenuAction action)
    {
        HideCatalogue();
        var search = new HavenTextInput
        {
            PlaceholderText = action == AddMenuAction.Agent ? "Search Agents" : "Search",
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
                    AddCurrentAgentRow(results);
                    AddAgentSection(results, "Personalities", _agents.Where(IsPersonality), query);
                    AddAgentSection(results, "Tools", _agents.Where(item => !IsPersonality(item)), query);
                    break;
                case AddMenuAction.Capability:
                    AddCapabilitySection(results, "General", _capabilities.Where(item => item.OwnerAppKey.Equals(CapabilityRegistryCatalog.GeneralOwner, StringComparison.OrdinalIgnoreCase)), query);
                    foreach (var group in _capabilities
                                 .Where(item => !item.OwnerAppKey.Equals(CapabilityRegistryCatalog.GeneralOwner, StringComparison.OrdinalIgnoreCase))
                                 .GroupBy(item => item.OwnerAppKey, StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
                        AddCapabilitySection(results, group.Key, group, query);
                    break;
                case AddMenuAction.Instruction:
                    AddInstructionSection(results, "Instructions", _instructions, query);
                    break;
                case AddMenuAction.App:
                    AddAppSection(results, "Apps", _apps, query);
                    break;
                case AddMenuAction.AllowActions:
                    AddActionModeSection(results, query);
                    break;
                case AddMenuAction.VisualResponses:
                    AddVisualResponseModeSection(results, query);
                    break;
            }
        }
        search.TextChanged += (_, _) => Rebuild();

        var searchHost = new Grid { IsVisible = action is not AddMenuAction.AllowActions and not AddMenuAction.VisualResponses };
        searchHost.Children.Add(search);
        searchHost.Children.Add(new HavenIcon
        {
            IconKey = "search", Width = 18, Height = 18, Margin = new Thickness(13, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, Opacity = 0.72
        });

        var title = action switch
        {
            AddMenuAction.Agent => "Select Agent",
            AddMenuAction.Capability => "Capabilities",
            AddMenuAction.Instruction => "Instructions",
            AddMenuAction.AllowActions => "Allow Actions",
            AddMenuAction.VisualResponses => "Prefer Visual Responses",
            _ => "Apps"
        };
        var footer = action switch
        {
            AddMenuAction.Agent => "Create new Agents in Studio",
            AddMenuAction.Capability => "Create new Capabilities in Studio",
            AddMenuAction.Instruction => "Create new Instructions in Studio",
            AddMenuAction.AllowActions => "Applies to this chat only",
            AddMenuAction.VisualResponses => "Controls when Haven invokes Generative UI on its own",
            _ => "Manage Apps"
        };

        var panel = new StackPanel
        {
            Spacing = 9,
            Children =
            {
                new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeight.ExtraBold, Margin = new Thickness(8, 4, 8, 0) },
                searchHost,
                new ScrollViewer
                {
                    MaxHeight = 300,
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
        var card = new HavenDropdownCard
        {
            Width = 320,
            MinWidth = 320,
            BoxShadow = default,
            Child = panel
        };
        var catalogHost = _catalogHost;
        if (catalogHost is null) return;
        catalogHost.Content = card;
        catalogHost.IsVisible = true;
        catalogHost.Opacity = 1;
        catalogHost.IsHitTestVisible = true;
        
        if (searchHost.IsVisible)
            search.Focus();
    }

    private void AddVisualResponseModeSection(StackPanel panel, string query)
    {
        var options = new (GenerativeUiResponseMode Mode, string Label)[]
        {
            (GenerativeUiResponseMode.AlwaysVisual, "Always Visual"),
            (GenerativeUiResponseMode.PreferVisual, "Prefer Visual"),
            (GenerativeUiResponseMode.Auto, "Auto (Default)"),
            (GenerativeUiResponseMode.PreferText, "Prefer Text"),
            (GenerativeUiResponseMode.AlwaysText, "Always Text")
        };
        foreach (var option in options.Where(item => string.IsNullOrWhiteSpace(query) || item.Label.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            var row = BuildRow("prompt", option.Label);
            row.Click += (_, _) => Select(new AddMenuSelection(AddMenuAction.VisualResponses, option.Mode));
            panel.Children.Add(row);
        }
    }

    private void AddActionModeSection(StackPanel panel, string query)
    {
        var options = new (ChatActionMode Mode, string Label, string Description)[]
        {
            (ChatActionMode.AllowAllActions, "Allow All Actions", "Allow every action available to this chat."),
            (ChatActionMode.AllowBasicActions, "Allow Basic Actions (Default)", "Use Haven's basic code-interpreter-style action set."),
            (ChatActionMode.JustChat, "Just Chat", "Do not allow the chat to take actions.")
        };
        foreach (var option in options.Where(item => Matches(item.Label, item.Description, query)))
        {
            var row = BuildRow("checklist", option.Label, option.Description);
            row.Click += (_, _) => Select(new AddMenuSelection(AddMenuAction.AllowActions, option.Mode));
            panel.Children.Add(row);
        }
    }

    private void AddCurrentAgentRow(StackPanel panel)
    {
        var current = BuildRow("agents", $"Current: {CurrentAgentNameProvider?.Invoke() ?? "No Agent (Default)"}");
        current.Classes.Add("selected");
        panel.Children.Add(current);
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

    private void AddCapabilitySection(StackPanel panel, string heading, IEnumerable<CapabilityDefinition> source, string query)
    {
        var items = source.Where(item => Matches(item.Name, item.Description, query)).ToArray();
        AddHeading(panel, heading, items.Length);
        foreach (var item in items)
        {
            var row = BuildRow(item.IconKey, item.Name, item.Description);
            row.Click += (_, _) => Select(new AddMenuSelection(AddMenuAction.Capability, item));
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
        HideCatalogue();
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
        var button = new HavenDropdownItemButton
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 10),
            MinHeight = 52,
            CornerRadius = new CornerRadius(14),
            Content = grid
        };
        return button;
    }

    private static bool IsPersonality(AgentDefinition item) =>
        string.IsNullOrWhiteSpace(item.DetectionRules) && item.PermissionsJson.Trim() is "{}" or "";

    private static bool Matches(string name, string description, string query) =>
        string.IsNullOrWhiteSpace(query)
        || name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || description.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static ControlTheme? FloatingPresenterTheme() =>
        Avalonia.Application.Current?.TryFindResource("HavenFloatingFlyoutPresenterTheme", out var value) == true
            ? value as ControlTheme
            : null;

    private void HideCatalogue()
    {
        if (_catalogHost is null) return;
        _catalogHost.Content = null;
        _catalogHost.IsVisible = true;
        _catalogHost.Opacity = 0;
        _catalogHost.IsHitTestVisible = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Click -= OnClick;
        HideCatalogue();
        _flyout?.Hide();
    }

    public enum AddMenuAction { File, Agent, Capability, Instruction, App, MultipleResponses, AllowActions, VisualResponses }
}

public sealed record AddMenuSelection(AddMenu.AddMenuAction Kind, object Item);
