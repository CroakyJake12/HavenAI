using Haven.Core;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private async Task OpenNativeConversationAsync(Conversation conversation)
    {
        if (_edition == HavenShellEdition.New)
        {
            await OpenScopedNewChatPageAsync(
                conversation.Mode,
                conversation.ContainerId,
                $"conversation-{conversation.Id:N}",
                conversation.Title,
                SurfaceForMode(conversation.Mode),
                conversation);
            return;
        }

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
