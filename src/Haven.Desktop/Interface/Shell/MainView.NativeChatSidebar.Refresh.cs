using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private async Task RefreshNativeChatSidebarAsync()
    {
        if (_nativeChatSidebar is null)
        {
            return;
        }

        if (CurrentPage is NewChatPage page)
        {
            _nativeChatSidebar.SetMode(page.CurrentConversation.Mode);
            _nativeChatSidebar.SetActiveConversation(
                page.ConversationId,
                page.CurrentConversation.ContainerId);
        }
        else
        {
            _nativeChatSidebar.SetActiveConversation(null, ActiveNativeChatGroupId());
        }

        await _nativeChatSidebar.RefreshAsync();
    }

    private Guid? ActiveNativeChatGroupId()
    {
        foreach (var pair in _groupPages)
        {
            if (ReferenceEquals(pair.Value, CurrentPage))
            {
                return pair.Key;
            }
        }

        return null;
    }
}
