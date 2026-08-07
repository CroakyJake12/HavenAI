using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Haven.Core;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private void InstallMobileChatSidebarPolicy()
    {
        // Keep the shared desktop sidebar and its commands intact. On Android it
        // overlays the content column instead of reserving a desktop-width column.
        SidebarControl.IsVisible = false;
        ConfigureMobileSidebarHost(NativeSidebarHost);
        PropertyChanged -= OnMobileSidebarOwnerPropertyChanged;
        PropertyChanged += OnMobileSidebarOwnerPropertyChanged;
        ApplyMobileChatSidebarState();
    }

    private void ConfigureMobileSidebarHost(Control host)
    {
        host.HorizontalAlignment = HorizontalAlignment.Left;
        host.VerticalAlignment = VerticalAlignment.Stretch;
        host.Margin = new Thickness(0);
        host.ZIndex = 40;
        host.ClipToBounds = true;
        host.IsHitTestVisible = true;
    }

    private void OnMobileSidebarOwnerPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
        => Dispatcher.UIThread.Post(ApplyMobileChatSidebarState);

    private void ApplyMobileChatSidebarState()
    {
        if (!_mobileLayoutApplied)
            return;

        // Classic's sidebar remains a desktop-only layout. New Haven's existing
        // native chat sidebar stays functional and opens above, rather than behind,
        // the mobile workspace. Posting this state after shared shell updates prevents
        // the desktop layout pass from placing the host underneath mobile content.
        SidebarControl.IsVisible = false;
        ConfigureMobileSidebarHost(NativeSidebarHost);
        NativeSidebarHost.IsVisible = CurrentSurface == HavenSurface.Chat && IsSidebarOpen;
    }
}
