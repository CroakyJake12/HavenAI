using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Study;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private StudyHomePage? _studyHomePage;
    private readonly Dictionary<Guid, StudySubjectPage> _studySubjectPages = [];

    private IStudyPlannerService StudyPlanner =>
        (App.Services ?? throw new InvalidOperationException("Haven services are unavailable while opening Study."))
        .GetRequiredService<IStudyPlannerService>();

    internal static bool UsesNativeStudyWorkspace(ContainerDefinition container) =>
        container.Mode == HavenMode.Study;

    private Task OpenNativeContainerAsync(ContainerDefinition container) =>
        UsesNativeStudyWorkspace(container)
            ? OpenStudySubjectAsync(container)
            : OpenChatGroupAsync(container);

    private async Task OpenStudyHomeAsync()
    {
        _studyHomePage ??= new StudyHomePage(
            _containers,
            StudyPlanner,
            OpenStudySubjectAsync,
            OpenStudyQuickChatAsync,
            OpenPlan);

        AddOrSelectTab("study-home", "Study", _studyHomePage, false, HavenSurface.Study);
        _nativeChatSidebar?.SetMode(HavenMode.Study);
        await RefreshNativeChatSidebarAsync();
    }

    private async Task OpenStudySubjectByIdAsync(Guid subjectId)
    {
        var subjects = await _containers.GetByModeAsync(HavenMode.Study, CancellationToken.None);
        var subject = subjects.FirstOrDefault(item => item.Id == subjectId && !item.IsArchived);
        if (subject is null) return;
        await OpenStudySubjectAsync(subject);
    }

    private Task OpenStudySubjectAsync(ContainerDefinition subject)
    {
        if (!_studySubjectPages.TryGetValue(subject.Id, out var page))
        {
            page = new StudySubjectPage(
                subject,
                _containers,
                _containerResources,
                _conversations,
                StudyPlanner,
                OpenStudyHomeAsync,
                (lesson, prompt) => OpenStudyLessonChatAsync(subject, lesson, prompt),
                conversation => OpenStudyConversationAsync(subject, conversation),
                OpenPlan);
            _studySubjectPages[subject.Id] = page;
        }

        AddOrSelectTab($"study-subject-{subject.Id:N}", subject.Name, page, false, HavenSurface.Study);
        _nativeChatSidebar?.SetMode(HavenMode.Study);
        return RefreshNativeChatSidebarAsync();
    }

    private async Task OpenStudyQuickChatAsync()
    {
        var subjects = await _containers.GetByModeAsync(HavenMode.Study, CancellationToken.None);
        var subject = subjects.FirstOrDefault(item => !item.IsArchived);
        if (subject is null)
        {
            await OpenStudyHomeAsync();
            return;
        }

        var lessons = await _containers.GetLessonsAsync(subject.Id, CancellationToken.None);
        await OpenStudyLessonChatAsync(subject, lessons.OrderBy(item => item.SortOrder).FirstOrDefault(), null);
    }

    private async Task OpenStudyLessonChatAsync(ContainerDefinition subject, Lesson? lesson, string? prompt)
    {
        lesson ??= (await _containers.GetLessonsAsync(subject.Id, CancellationToken.None))
            .OrderBy(item => item.SortOrder)
            .FirstOrDefault();
        if (lesson is null)
        {
            await OpenStudySubjectAsync(subject);
            return;
        }

        var page = CreateNewChatPage();
        await ConfigureAddMenuAsync(page);
        await page.StartFreshConversationAsync(HavenMode.Study, subject.Id, lesson.Id);

        var key = $"study-{subject.Id:N}-{lesson.Id:N}-{Guid.NewGuid():N}";
        AddOrSelectTab(key, $"{subject.Name} • {lesson.Name}", page, false, HavenSurface.Study, forceNewTab: true);
        _nativeChatSidebar?.SetMode(HavenMode.Study);
        _nativeChatSidebar?.SetActiveConversation(page.ConversationId, subject.Id);

        if (!string.IsNullOrWhiteSpace(prompt))
            page.Submit(prompt);
        else
            page.FocusComposer();

        await RefreshNativeChatSidebarAsync();
    }

    private Task OpenStudyConversationAsync(ContainerDefinition subject, Conversation conversation) =>
        OpenScopedNewChatPageAsync(
            HavenMode.Study,
            subject.Id,
            $"conversation-{conversation.Id:N}",
            string.IsNullOrWhiteSpace(conversation.Title) ? subject.Name : conversation.Title,
            HavenSurface.Study,
            conversation);

    private void ForgetStudySubject(Guid subjectId)
    {
        if (_studySubjectPages.Remove(subjectId, out var page))
            page.Dispose();
    }
}
