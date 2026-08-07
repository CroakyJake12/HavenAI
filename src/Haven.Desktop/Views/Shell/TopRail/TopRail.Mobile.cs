using Avalonia;
using Avalonia.Controls;

namespace Haven.Desktop.Views.Shell.TopRail;

public sealed partial class TopRail
{
    /// <summary>
    /// Keeps the desktop TopRail implementation on narrow mobile windows while
    /// releasing non-essential chrome width. Tabs, Actions and Model continue to
    /// use the exact desktop controls and event flow.
    /// </summary>
    public void ApplyMobileCompactLayout()
    {
        MinWidth = 0;

        LogoButton.Width = 46;
        LogoButton.Height = 46;
        LogoButton.Padding = new Thickness(3);

        BackButton.IsVisible = false;
        ForwardButton.IsVisible = false;
        AppsButton.IsVisible = false;
        NotificationsButton.IsVisible = false;
        SearchButton.IsVisible = false;
        TabViewButton.IsVisible = false;

        AddTabButton.Width = 38;
        AddTabButton.Height = 38;

        UniversalModelButton.MinWidth = 104;
        UniversalModelButton.MaxWidth = 128;
        UniversalModelButton.Padding = new Thickness(8, 7);
        UniversalModelName.MaxWidth = 66;

        if (TabStrip.Parent is ScrollViewer scroller
            && scroller.Parent is Grid tabGrid
            && tabGrid.Parent is Border tabHost)
        {
            tabHost.MinWidth = 0;
            tabHost.Height = 54;
            tabHost.Padding = new Thickness(6, 3);
        }
    }
}
