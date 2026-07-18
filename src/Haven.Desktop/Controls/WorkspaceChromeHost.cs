using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Haven.Desktop.Controls;

/// <summary>
/// Reframes the existing MainWindow content without duplicating its bindings. The original
/// tab strip, command menu and status controls are detached from the legacy header and reused
/// inside one compact top bar; the existing page and sidebar tree remains authoritative.
/// </summary>
public sealed class WorkspaceChromeHost : Grid, IDisposable
{
    private readonly ExperienceShellHost _experienceShell;
    private bool _disposed;

    public WorkspaceChromeHost(Control existingShell)
    {
        ArgumentNullException.ThrowIfNull(existingShell);

        RowDefinitions = new RowDefinitions("58,*");
        Background = Brushes.Transparent;

        var chrome = ExtractLegacyChrome(existingShell);
        var topBar = BuildTopBar(chrome.TabStrip, chrome.Menu, chrome.StatusControls);
        Children.Add(topBar);

        _experienceShell = new ExperienceShellHost(existingShell);
        Grid.SetRow(_experienceShell, 1);
        Children.Add(_experienceShell);
    }

    private static ExtractedChrome ExtractLegacyChrome(Control existingShell)
    {
        if (existingShell is not Grid root)
            return new ExtractedChrome(null, null, null);

        var tabStrip = root.Children
            .OfType<Border>()
            .FirstOrDefault(border => Grid.GetRow(border) == 0 && Grid.GetRowSpan(border) == 1);

        var headerBorder = root.Children
            .OfType<Border>()
            .FirstOrDefault(border => Grid.GetRow(border) == 1 && border.Child is Grid);

        Menu? menu = null;
        StackPanel? statusControls = null;
        if (headerBorder?.Child is Grid headerGrid)
        {
            var leftHeader = headerGrid.Children
                .OfType<StackPanel>()
                .FirstOrDefault(panel => Grid.GetColumn(panel) == 0);
            menu = leftHeader?.Children.OfType<Menu>().FirstOrDefault();
            if (menu is not null) leftHeader!.Children.Remove(menu);

            statusControls = headerGrid.Children
                .OfType<StackPanel>()
                .FirstOrDefault(panel => Grid.GetColumn(panel) == 2);
            if (statusControls is not null) headerGrid.Children.Remove(statusControls);
        }

        if (tabStrip is not null)
        {
            root.Children.Remove(tabStrip);
            tabStrip.Margin = new Thickness(0);
            tabStrip.HorizontalAlignment = HorizontalAlignment.Stretch;
            tabStrip.VerticalAlignment = VerticalAlignment.Stretch;
        }

        if (headerBorder is not null) root.Children.Remove(headerBorder);

        // The page/content area already occupies row 2. Collapsing the two extracted rows lets
        // it fill the workspace while preserving all existing bindings and overlays.
        root.RowDefinitions = new RowDefinitions("0,0,*");

        return new ExtractedChrome(tabStrip, menu, statusControls);
    }

    private static Border BuildTopBar(Border? tabStrip, Menu? legacyMenu, StackPanel? statusControls)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 10,
            Margin = new Thickness(18, 8, 12, 7)
        };

        var brand = new TextBlock
        {
            Text = "Haven",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 8, 0)
        };
        grid.Children.Add(brand);

        if (tabStrip is not null)
        {
            Grid.SetColumn(tabStrip, 1);
            grid.Children.Add(tabStrip);
        }

        if (statusControls is not null)
        {
            Grid.SetColumn(statusControls, 2);
            statusControls.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(statusControls);
        }

        var actions = BuildActionsButton(legacyMenu);
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        return new Border
        {
            Background = ResourceBrush("HavenPanelBrush", Color.FromArgb(245, 38, 45, 61)),
            BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(54, 255, 255, 255)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private static Button BuildActionsButton(Menu? legacyMenu)
    {
        var commandPalette = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(14, 11),
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock { Text = "Commands", FontWeight = FontWeight.SemiBold },
                            new TextBlock { Text = "Search every Haven action", FontSize = 10, Opacity = 0.68 }
                        }
                    },
                    WithColumn(new TextBlock
                    {
                        Text = "Ctrl+K",
                        FontSize = 10,
                        Opacity = 0.65,
                        VerticalAlignment = VerticalAlignment.Center
                    }, 1)
                }
            }
        };
        commandPalette.Classes.Add("sidebar");
        commandPalette.Bind(Button.CommandProperty, new Binding("OpenCommandPaletteCommand"));

        var flyoutContent = new StackPanel
        {
            Width = 590,
            Spacing = 8,
            Margin = new Thickness(10)
        };
        flyoutContent.Children.Add(commandPalette);
        flyoutContent.Children.Add(new Separator());

        if (legacyMenu is not null)
        {
            legacyMenu.HorizontalAlignment = HorizontalAlignment.Stretch;
            flyoutContent.Children.Add(legacyMenu);
        }
        else
        {
            flyoutContent.Children.Add(new TextBlock
            {
                Text = "No additional actions are available for this surface.",
                Opacity = 0.68,
                Margin = new Thickness(8)
            });
        }

        var button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Actions", FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "⌄", FontSize = 12, VerticalAlignment = VerticalAlignment.Center }
                }
            },
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(16, 9),
            Flyout = new Flyout
            {
                Placement = PlacementMode.BottomEdgeAlignedRight,
                Content = flyoutContent
            }
        };
        button.Classes.Add("primary");
        ToolTip.SetTip(button, "Commands, File, Edit, View, Chat, Project, Tools and Help");
        return button;
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

    private static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _experienceShell.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record ExtractedChrome(Border? TabStrip, Menu? Menu, StackPanel? StatusControls);
}
