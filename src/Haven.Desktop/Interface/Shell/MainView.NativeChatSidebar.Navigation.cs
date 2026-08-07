using Haven.Core;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private async Task OpenNativeConversationAsync(Conversation conversation)
    {
        await OpenNewChatAsync();
        if (_newChatPage is null)
        {
            return;
        }

        await _newChatPage.LoadConversationAsync(conversation);
        _nativeChatSidebar?.SetMode(conversation.Mode);
        _nativeChatSidebar?.SetActiveConversation(conversation.Id, conversation.ContainerId);
        ApplyShellVisualState();
    }
}
