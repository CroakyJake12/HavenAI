using Haven.Core;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private async Task SwitchNativeChatModeAsync(HavenMode mode)
    {
        if (mode == HavenMode.Studio)
        {
            await NavigateModeAsync(HavenMode.Studio, true);
            return;
        }

        await OpenNewChatAsync();
        if (_newChatPage is null)
        {
            return;
        }

        if (_nativeChatSidebar is not null)
        {
            _nativeChatSidebar.SetMode(mode);
        }

        var recent = (await _conversations.GetRecentAsync(mode, 1, CancellationToken.None))
            .FirstOrDefault(item => !item.IsArchived && item.Kind != ConversationKind.Call);

        if (recent is not null)
        {
            await _newChatPage.LoadConversationAsync(recent);
            if (_nativeChatSidebar is not null)
            {
                _nativeChatSidebar.SetActiveConversation(recent.Id, recent.ContainerId);
            }
        }
        else
        {
            await _newChatPage.StartFreshConversationAsync(mode, null);
            if (_nativeChatSidebar is not null)
            {
                _nativeChatSidebar.SetActiveConversation(_newChatPage.ConversationId, null);
            }
        }

        await RefreshNativeChatSidebarAsync();
        ApplyShellVisualState();
    }
}
