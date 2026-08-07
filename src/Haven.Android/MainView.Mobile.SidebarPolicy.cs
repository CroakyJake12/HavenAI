using Avalonia;
using Avalonia.Controls;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private void InstallMobileChatSidebarPolicy()
    {
        SidebarControl.IsVisible = false;
        NativeSidebarHost.IsVisible = false;
        SidebarControl.PropertyChanged += OnMobileSidebarHostPropertyChanged;
        NativeSidebarHost.PropertyChanged += OnMobileSidebarHostPropertyChanged;
    }

    private void OnMobileSidebarHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_mobileLayoutApplied || sender is not Control control || !control.IsVisible)
            return;

        // Shared desktop shell state can re-enable the conversation sidebar after navigation.
        // Android keeps the desktop sidebar implementation intact but collapsed out of the mobile visual tree.
        control.IsVisible = false;
    }
}
