using Haven.Desktop.Views.Shell.NativePresentation;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private NativeChatSidebar? _nativeChatSidebar;

    private void InitialiseNativeChatSidebar()
    {
        _nativeChatSidebar = new NativeChatSidebar(
            _conversations,
            _containers,
            OpenNativeConversationAsync,
            StartNativeConversationAsync,
            OpenChatGroupAsync,
            SwitchNativeChatModeAsync);

        NativeSidebarHost.Content = _nativeChatSidebar;
    }
}
