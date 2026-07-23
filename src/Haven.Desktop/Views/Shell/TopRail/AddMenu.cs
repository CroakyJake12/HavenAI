using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// The Add (+) menu shown in the composer area. Provides quick access to
/// creating new Files, Agents, Plugins, Instructions, and Apps.
/// </summary>
public sealed class AddMenu : Button, IDisposable
{
    private Flyout? _flyout;
    private bool _disposed;

    /// <summary>
    /// Raised when the user selects a top-level add action.
    /// </summary>
    public event EventHandler<AddMenuAction>? ActionSelected;

    public AddMenu()
    {
        Content = new HavenIcon { IconKey = "plus", Width = 16, Height = 16 };
        Classes.Add("chrome");
        ToolTip.SetTip(this, "Add new item");
        Click += OnClick;
    }

    private void OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_flyout is null) _flyout = BuildFlyout();
        _flyout.ShowAt(this);
    }

    private Flyout BuildFlyout()
    {
        var content = new StackPanel
        {
            Width = 280,
            Spacing = 2,
            Margin = new Thickness(6)
        };

        content.Children.Add(BuildMenuItem("File", "file", AddMenuAction.File));
        content.Children.Add(BuildAgentMenuItem());
        content.Children.Add(BuildMenuItem("Plugin", "plugin", AddMenuAction.Plugin));
        content.Children.Add(BuildMenuItem("Instruction", "prompt", AddMenuAction.Instruction));
        content.Children.Add(BuildMenuItem("App", "all-modes", AddMenuAction.App));

        return new Flyout
        {
            Placement = PlacementMode.TopEdgeAlignedLeft,
            Content = content
        };
    }

    private Button BuildMenuItem(string label, string iconKey, AddMenuAction action)
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = 10,
                Children =
                {
                    new HavenIcon { IconKey = iconKey, Width = 16, Height = 16, Opacity = 0.76, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        btn.Classes.Add("sidebar");
        btn.Click += (_, _) =>
        {
            _flyout?.Hide();
            ActionSelected?.Invoke(this, action);
        };
        return btn;
    }

    private Button BuildAgentMenuItem()
    {
        var agentMenu = new StackPanel { Spacing = 2, Margin = new Thickness(8, 2, 0, 0) };
        agentMenu.Children.Add(BuildSubMenuItem("Search", "search", AddMenuAction.AgentSearch));
        agentMenu.Children.Add(BuildSubMenuItem("Personalities", "agents", AddMenuAction.AgentPersonalities));
        agentMenu.Children.Add(BuildSubMenuItem("Tools", "settings", AddMenuAction.AgentTools));

        var subMenuBorder = new Border
        {
            Child = agentMenu,
            IsVisible = false,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var mainBtn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 10,
                Children =
                {
                    new HavenIcon { IconKey = "agents", Width = 16, Height = 16, Opacity = 0.76, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = "Agent", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                    new HavenIcon { IconKey = "chevron-down", Width = 12, Height = 12, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        mainBtn.Classes.Add("sidebar");
        if (mainBtn.Content is Grid agentGrid)
        {
            Grid.SetColumn(agentGrid.Children[1], 1);
            Grid.SetColumn(agentGrid.Children[2], 2);
        }
        mainBtn.Click += (_, _) =>
        {
            subMenuBorder.IsVisible = !subMenuBorder.IsVisible;
        };

        var wrapper = new StackPanel
        {
            Spacing = 0,
            Children = { mainBtn, subMenuBorder }
        };

        var host = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = wrapper,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        };
        return host;
    }

    private Button BuildSubMenuItem(string label, string iconKey, AddMenuAction action)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8,
            Margin = new Thickness(4, 0, 0, 0)
        };
        grid.Children.Add(new HavenIcon { IconKey = iconKey, Width = 14, Height = 14, Opacity = 0.68, VerticalAlignment = VerticalAlignment.Center });
        var textBlock = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        Grid.SetColumn(textBlock, 1);
        grid.Children.Add(textBlock);

        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = grid
        };
        btn.Classes.Add("sidebar");
        btn.Click += (_, _) =>
        {
            _flyout?.Hide();
            ActionSelected?.Invoke(this, action);
        };
        return btn;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Click -= OnClick;
    }

    public enum AddMenuAction
    {
        File,
        AgentSearch,
        AgentPersonalities,
        AgentTools,
        Plugin,
        Instruction,
        App
    }
}
