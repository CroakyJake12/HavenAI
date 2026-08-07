using Avalonia.Threading;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private void InstallMobileChatSidebarPolicy()
    {
        PropertyChanged -= OnMobileSidebarOwnerPropertyChanged;
        PropertyChanged += OnMobileSidebarOwnerPropertyChanged;
        ApplyMobileChatSidebarState();
    }

    private void OnMobileSidebarOwnerPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
        => Dispatcher.UIThread.Post(ApplyMobileChatSidebarState);

    private void ApplyMobileChatSidebarState()
    {
        if (!_mobileLayoutApplied)
            return;

        // The desktop sidebar remains fully implemented for desktop. Android deliberately
        // removes both sidebar hosts from its visual presentation; chat history/options live
        // in the composer swipe sheet instead.
        SidebarControl.IsVisible = false;
        NativeSidebarHost.IsVisible = false;
    }
}
