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
}
