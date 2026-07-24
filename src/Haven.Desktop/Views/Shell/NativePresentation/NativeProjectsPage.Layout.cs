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

namespace Haven.Desktop.Views.Shell.NativePresentation;

internal sealed partial class NativeProjectsPage
{

    private Control BuildLayout()
    {
        var title = Heading("Projects", 32);
        var subtitle = new TextBlock
        {
            Text = "Your active workspaces, recent activity, and the next useful step.",
            Foreground = MutedBrush,
            FontSize = 15,
            Margin = new Thickness(0, 3, 0, 0)
        };

        var headerText = new StackPanel
        {
            Spacing = 0,
            Children = { title, subtitle }
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _searchBox, _refreshButton, _newProjectButton }
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 26)
        };
        header.Children.Add(headerText);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        var pinnedSection = new StackPanel
        {
            Spacing = 12,
            Children = { _pinnedHeading, _pinnedPanel }
        };

        var allSection = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(0, 24, 0, 0),
            Children = { _projectHeading, _projectPanel, _emptyState }
        };

        var content = new StackPanel
        {
            Spacing = 0,
            MaxWidth = 1320,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                header,
                pinnedSection,
                allSection,
                _status
            }
        };

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border
            {
                Padding = new Thickness(42, 34, 42, 54),
                Child = content
            }
        };
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

    private Control BuildPinnedRow(ProjectRow row)
    {
        var open = LinkButton(row.Name);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Click += async (_, _) => await OpenProjectAsync(row);

        var unread = new Border
        {
            Background = CyanBrush,
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = row.IsUnread
        };

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Children =
            {
                unread,
                open,
                new TextBlock
                {
                    Text = FormatActivity(row.UpdatedAt),
                    Foreground = MutedBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12
                }
            }
        };
        Grid.SetColumn(open, 1);
        Grid.SetColumn(content.Children[2], 2);

        return new Border
        {
            Background = CardBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10),
            Child = content
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
            FontWeight = FontWeight.SemiBold
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
            Foreground = primary ? Brushes.White : Brushes.Black,
            BorderBrush = primary ? AccentBrush : BorderBrush,
            BorderThickness = new Thickness(1)
        };
        return button;
    }

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
}
