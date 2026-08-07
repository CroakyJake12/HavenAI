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

        if (Content is Grid rootGrid)
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

        if (TabStrip.Parent is not ScrollViewer scroller
            || scroller.Parent is not Grid tabGrid
            || tabGrid.Parent is not Border tabHost
            || tabHost.Parent is not Grid headerGrid)
        {
            return;
        }

        tabHost.MinWidth = 0;
        tabHost.Height = 52;
        tabHost.Padding = new Thickness(4, 3);

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
