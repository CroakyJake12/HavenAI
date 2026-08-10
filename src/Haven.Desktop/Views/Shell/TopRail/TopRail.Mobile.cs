using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Haven.Desktop.Views.Shell.TopRail;

public sealed partial class TopRail
{
    /// <summary>
    /// Keeps the desktop TopRail controls and interactions on Android, but composes them
    /// into a phone-safe two-row header: primary controls first, tabs second.
    /// </summary>
    public void ApplyMobileCompactLayout()
    {
        MinWidth = 0;

        if (Content is not Grid rootGrid)
            return;

        rootGrid.Height = 142;
        LogoButton.Width = 42;
        LogoButton.Height = 42;
        LogoButton.Padding = new Thickness(2);

        AppsButton.IsVisible = false;
        NotificationsButton.IsVisible = false;
        SearchButton.IsVisible = false;
        TabViewButton.IsVisible = false;

        AddTabButton.Width = 38;
        AddTabButton.Height = 38;

        ActionToolbar.MinWidth = 0;
        ActionToolbar.MaxWidth = 104;

        UniversalModelButton.MinWidth = 100;
        UniversalModelButton.MaxWidth = 112;
        UniversalModelButton.Padding = new Thickness(7, 6);
        UniversalModelName.MaxWidth = 54;

        // Do not depend on logical Parent chains here: ScrollViewer inserts presentation
        // elements on Android. Walk the stable XAML structure from the root instead.
        var chrome = rootGrid.Children.OfType<Border>().FirstOrDefault();
        if (chrome?.Child is not Grid headerGrid)
            return;

        var tabHost = headerGrid.Children
            .OfType<Border>()
            .FirstOrDefault(border =>
                border.Child is Grid tabGrid
                && tabGrid.Children.OfType<ScrollViewer>()
                    .Any(scroller => ReferenceEquals(scroller.Content, TabStrip)));

        if (tabHost is null)
            return;

        tabHost.MinWidth = 0;
        tabHost.Height = 52;
        tabHost.Padding = new Thickness(4, 3);
        tabHost.HorizontalAlignment = HorizontalAlignment.Stretch;

        headerGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
        headerGrid.RowSpacing = 4;
        headerGrid.ColumnSpacing = 4;

        foreach (var child in headerGrid.Children)
        {
            if (!ReferenceEquals(child, tabHost))
                Grid.SetRow(child, 0);
        }

        Grid.SetRow(tabHost, 1);
        Grid.SetColumn(tabHost, 0);
        Grid.SetColumnSpan(tabHost, Math.Max(1, headerGrid.ColumnDefinitions.Count));
    }
}
