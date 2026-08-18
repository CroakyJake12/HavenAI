using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Shell.NativePresentation;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Tests;

public sealed class StudyAssignmentsSidebarTests
{
    [AvaloniaFact]
    public void Study_section_uses_haven_dynamic_rows_and_emits_plan_actions()
    {
        using var scene = new ChatSidebarHavenScene();
        using var section = new StudyAssignmentsHavenSection(scene.Root);
        var taskId = Guid.NewGuid();
        var requests = new List<StudyAssignmentSidebarRequest>();
        section.ActionRequested += (_, request) => requests.Add(request);
        section.SetContext(true, true, [new(taskId, "Maths homework", "Algebra · Due Thu 20 Aug, 15:00", false, false)]);

        Assert.Equal(HavenVisibility.Visible, section.Section.GetValue(HavenProperties.Visibility));
        var row = Assert.Single(section.Rows.Items);
        Assert.Equal("Maths homework", row.GetComponent<HavenButton>("Open").Content);
        Assert.Contains("Algebra", row.GetComponent<HavenText>("Subtitle").Content);

        Click(scene, row.GetComponent<HavenButton>("Open"));
        Click(scene, row.GetComponent<HavenButton>("Edit"));
        Click(scene, row.GetComponent<HavenButton>("Complete"));
        Assert.Equal(
            new[] { StudyAssignmentSidebarAction.OpenPlan, StudyAssignmentSidebarAction.EditDeadline, StudyAssignmentSidebarAction.Complete },
            requests.Select(item => item.Action).ToArray());

        section.SetContext(true, true, [new(taskId, "Maths homework", "Completed", true, false)]);
        row = Assert.Single(section.Rows.Items);
        Assert.Equal("Completed", row.GetComponent<HavenButton>("Complete").Content);
        Assert.False(row.GetComponent<HavenButton>("Complete").GetValue(HavenProperties.Enabled));

        section.SetContext(false, false, []);
        Assert.Equal(HavenVisibility.Collapsed, section.Section.GetValue(HavenProperties.Visibility));
        Assert.Empty(section.Rows.Items);
    }

    [AvaloniaFact]
    public async Task Study_sidebar_deadline_completion_and_open_plan_use_canonical_study_service()
    {
        var subjectId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 17, 18, 0, 0, TimeSpan.Zero);
        var subject = new ContainerDefinition(subjectId, HavenMode.Study, "Maths", null, string.Empty, string.Empty, now, now);
        var lesson = new Lesson(lessonId, subjectId, "Pure", "Algebra", "{}", 0, now, now);
        var task = new PlannerTask(
            Guid.NewGuid(), Guid.NewGuid(), null, "Maths homework", string.Empty, PlannerPriority.Medium, PlannerTaskStatus.Planned,
            PlannerStudyAssignmentTags.Attach("[]", subjectId, lessonId), 45, null, now.AddDays(2), null, null, null, 0, now, now, "UTC");
        var study = new RecordingStudyPlannerService(new PlannerStudyAssignment(new(subjectId, lessonId), task));
        var containers = new StudyContainerRepository(subject, lesson);
        var planOpened = false;

        using var sidebar = new NativeChatSidebar(
            new EmptyConversationRepository(), containers,
            _ => Task.CompletedTask, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask);
        using var coordinator = new StudyAssignmentsSidebarCoordinator(sidebar, containers, study, () => planOpened = true);
        var window = new Window { Width = 440, Height = 760, Content = sidebar };
        try
        {
            window.Show();
            sidebar.SetMode(HavenMode.Study);
            sidebar.SetActiveConversation(null, subjectId);
            await WaitUntilAsync(() => coordinator.Section.Rows.Items.Count == 1);
            window.UpdateLayout();
            var router = new HavenInputRouter(sidebar.Scene.Root);
            var row = Assert.Single(coordinator.Section.Rows.Items);
            Assert.Contains("Algebra", row.GetComponent<HavenText>("Subtitle").Content);

            Click(router, row.GetComponent<HavenButton>("Open"));
            Assert.True(planOpened);

            Click(router, row.GetComponent<HavenButton>("Edit"));
            window.UpdateLayout();
            var modal = sidebar.Scene.Root.DescendantsAndSelf().OfType<Container>().Single(item => item.Name == "ChatSidebarModal");
            var editor = modal.DescendantsAndSelf().OfType<Input>().Single();
            editor.Text = "2026-08-21 15:30";
            var save = modal.DescendantsAndSelf().OfType<HavenButton>().Single(button => button.Content == "Save deadline");
            Click(router, save);
            await WaitUntilAsync(() => study.UpdateDeadlineCount == 1);
            await WaitUntilAsync(() => !sidebar.Scene.Root.DescendantsAndSelf().OfType<Container>().Any(item => item.Name == "ChatSidebarModal"));
            Assert.Equal(new DateTimeOffset(2026, 8, 21, 15, 30, 0, TimeSpan.Zero), study.Current.Task.DueAt);

            await WaitUntilAsync(() => coordinator.Section.Rows.Items.Count == 1);
            window.UpdateLayout();
            row = Assert.Single(coordinator.Section.Rows.Items);
            Click(router, row.GetComponent<HavenButton>("Complete"));
            await WaitUntilAsync(() => study.CompleteCount == 1);
            Assert.Equal(PlannerTaskStatus.Completed, study.Current.Task.Status);
            await WaitUntilAsync(() => Assert.Single(coordinator.Section.Rows.Items).GetComponent<HavenButton>("Complete").Content == "Completed");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void Click(ChatSidebarHavenScene scene, HavenElement element)
    {
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 440, Height = 760, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            Click(new HavenInputRouter(scene.Root), element);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.True(condition());
    }

    private sealed class RecordingStudyPlannerService(PlannerStudyAssignment current) : IStudyPlannerService
    {
        public PlannerStudyAssignment Current { get; private set; } = current;
        public int UpdateDeadlineCount { get; private set; }
        public int CompleteCount { get; private set; }

        public Task<IReadOnlyList<PlannerStudyAssignment>> GetAssignmentsAsync(Guid subjectId, bool includeCompleted, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlannerStudyAssignment>>(Current.Link.SubjectId == subjectId ? [Current] : []);

        public Task<PlannerStudyAssignment> UpdateDeadlineAsync(Guid planTaskId, DateTimeOffset? dueAt, DateTimeOffset updatedAt, CancellationToken cancellationToken)
        {
            Assert.Equal(Current.PlanTaskId, planTaskId);
            UpdateDeadlineCount++;
            Current = Current with { Task = Current.Task with { DueAt = dueAt, UpdatedAt = updatedAt } };
            return Task.FromResult(Current);
        }

        public Task<PlannerStudyAssignment> CompleteAsync(Guid planTaskId, DateTimeOffset completedAt, CancellationToken cancellationToken)
        {
            Assert.Equal(Current.PlanTaskId, planTaskId);
            CompleteCount++;
            Current = Current with { Task = Current.Task with { Status = PlannerTaskStatus.Completed, CompletedAt = completedAt, UpdatedAt = completedAt } };
            return Task.FromResult(Current);
        }

        public Task<PlannerStudyAssignment> CreateAsync(StudyPlanAssignmentDraft draft, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PlannerStudyAssignment> ScheduleRevisionAsync(StudyRevisionScheduleRequest request, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PlannerStudyAssignment> LinkExistingAsync(Guid planTaskId, Guid subjectId, Guid? lessonId, DateTimeOffset updatedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UnlinkAsync(Guid planTaskId, DateTimeOffset updatedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StudyContainerRepository(ContainerDefinition subject, Lesson lesson) : IContainerRepository
    {
        public Task<IReadOnlyList<ContainerDefinition>> GetByModeAsync(HavenMode mode, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ContainerDefinition>>(mode == HavenMode.Study ? [subject] : []);
        public Task<IReadOnlyList<Lesson>> GetLessonsAsync(Guid subjectId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Lesson>>(subjectId == subject.Id ? [lesson] : []);
        public Task UpsertAsync(ContainerDefinition item, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Lesson> CreateSubjectAsync(ContainerDefinition item, CancellationToken cancellationToken) => Task.FromResult(lesson);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAndDetachConversationsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpsertLessonAsync(Lesson item, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteLessonAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptyConversationRepository : IConversationRepository
    {
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
