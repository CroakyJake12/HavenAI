using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell.NativePresentation;

internal sealed partial class NativeProjectsPage
{

    private Control BuildLayout()
    {
        var title = Heading("Projects", 46);
        title.HorizontalAlignment = HorizontalAlignment.Center;

        var searchHost = new Grid
        {
            MaxWidth = 1160,
            HorizontalAlignment = HorizontalAlignment.Center,
            ColumnDefinitions = new ColumnDefinitions("*"),
            Margin = new Thickness(0, 24, 0, 28)
        };
        searchHost.Children.Add(_searchBox);
        searchHost.Children.Add(new HavenIcon
        {
            IconKey = "search",
            Width = 25,
            Height = 25,
            Margin = new Thickness(18, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Opacity = 0.78
        });
        var pinnedSection = new StackPanel
        {
            Spacing = 12,
            Children = { _pinnedHeading, _pinnedPanel }
        };

        var unreadSection = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(0, 26, 0, 0),
            Children = { _unreadHeading, _unreadPanel }
        };

        var allSection = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(0, 26, 0, 0),
            Children = { _projectHeading, _projectPanel, _emptyState }
        };

        var sections = new StackPanel
        {
            Spacing = 0,
            MaxWidth = 1500,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                pinnedSection,
                unreadSection,
                allSection,
                _status
            }
        };

        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Children = { title }
        };
        Grid.SetRow(searchHost, 1);
        body.Children.Add(searchHost);
        var sectionScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = sections
        };
        Grid.SetRow(sectionScroll, 2);
        body.Children.Add(sectionScroll);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                new Border
                {
                    Padding = new Thickness(40, 30, 40, 12),
                    Child = body
                }
            }
        };
        var createHost = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(32, 12, 32, 22),
            Child = _newProjectButton
        };
        Grid.SetRow(createHost, 1);
        root.Children.Add(createHost);
        return root;
    }

    private Border BuildEmptyState()
    {
        var create = Button("Create project", true);
        create.Click += OnNewProjectClicked;

        var connect = Button("Connect existing folder", false);
        connect.Click += async (_, _) => await ConnectExistingAsync();

        return new Border
        {
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Background = CardBrush,
            Padding = new Thickness(28),
            IsVisible = false,
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 10,
                Children =
                {
                    Heading("No projects found", 20),
                    new TextBlock
                    {
                        Text = "Create a project or connect an existing local folder.",
                        Foreground = MutedBrush,
                        TextAlignment = TextAlignment.Center
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Spacing = 8,
                        Children = { create, connect }
                    }
                }
            }
        };
    }

    private static Control BuildMetric(string label, string value) =>
        new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = MutedBrush,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = value,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };

    private static TextBlock Heading(string text, double size) =>
        new()
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeight.Bold
        };

    private static Button Button(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 38,
            Padding = new Thickness(16, 8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(10),
            Background = primary ? AccentBrush : CardBrush,
            Foreground = primary ? AccentInkBrush : TextBrush,
            BorderBrush = primary ? AccentBrush : BorderBrush,
            BorderThickness = new Thickness(1)
        };
        return button;
    }

    private static Button IconButton(string icon, string accessibleName)
    {
        var button = new Button
        {
            Width = 52,
            Height = 52,
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(16),
            Background = CardBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Content = new HavenIcon { IconKey = icon, Width = 20, Height = 20 }
        };
        AutomationProperties.SetName(button, accessibleName);
        return button;
    }

    private static WrapPanel ProjectTilePanel() => new()
    {
        Orientation = Orientation.Horizontal,
        ItemWidth = 300,
        ItemHeight = 222
    };

    private static Button LinkButton(string text) =>
        new()
        {
            Content = text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontWeight = FontWeight.SemiBold
        };

    private static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));

    private static IBrush PaletteBrush(string key, string fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? Brush(fallback);
}
