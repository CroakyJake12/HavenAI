using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;

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

    private async Task StartNativeConversationAsync(HavenMode mode, Guid? chatGroupId)
    {
        await OpenNewChatAsync();
        if (_newChatPage is null)
        {
            return;
        }

        Guid? lessonId = null;
        if (mode == HavenMode.Teach && chatGroupId is Guid subjectId)
        {
            lessonId = (await _containers.GetLessonsAsync(subjectId, CancellationToken.None))
                .OrderBy(item => item.SortOrder)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefault();
        }

        await _newChatPage.StartFreshConversationAsync(mode, chatGroupId, lessonId);
        _nativeChatSidebar?.SetMode(mode);
        _nativeChatSidebar?.SetActiveConversation(null, chatGroupId);
        await RefreshNativeChatSidebarAsync();
        ApplyShellVisualState();
    }

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

        _nativeChatSidebar/.SetMode(mode);
        var recent = (await _conversations.GetRecentAsync(mode, 1, CancellationToken.None))
            .FirstOrDefault(item => !item.IsArchived && item.Kind != ConversationKind.Call);

        if (recent is not null)
        {
            await _newChatPage.LoadConversationAsync(recent);
            _nativeChatSidebar?.SetActiveConversation(recent.Id, recent.ContainerId);
        }
        else
        {
            await _newChatPage.StartFreshConversationAsync(mode, null);
            _nativeChatSidebar/.SetActiveConversation(_newChatPage.ConversationId, null);
        }

        await RefreshNativeChatSidebarAsync();
        ApplyShellVisualState();
    }

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
