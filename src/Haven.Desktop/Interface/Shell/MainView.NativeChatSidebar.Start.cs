using Haven.Core;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private async Task StartNativeConversationAsync(HavenMode mode, Guid? chatGroupId)
    {
        await OpenNewChatAsync();
        if (_newChatPage is null)
        {
            return;
        }

        Guid? lessonId = null;
        if (mode == HavenMode.Study && chatGroupId is Guid subjectId)
        {
            lessonId = (await _containers.GetLessonsAsync(subjectId, CancellationToken.None))
                .OrderBy(item => item.SortOrder)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefault();
        }

        await _newChatPage.StartFreshConversationAsync(mode, chatGroupId, lessonId);
        if (_nativeChatSidebar?.CurrentSpaceId is { } spaceId)
        {
            await _conversations.UpsertConversationAsync(
                _newChatPage.CurrentConversation with { SpaceId = spaceId },
                CancellationToken.None);
        }
        _nativeChatSidebar?.SetMode(mode);
        _nativeChatSidebar?.SetActiveConversation(_newChatPage.CurrentConversation.Id, chatGroupId);
        await RefreshNativeChatSidebarAsync();
        ApplyShellVisualState();
    }
}
