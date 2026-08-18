using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Shell.NativePresentation;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Tests;

public sealed class StudyAssignmentCreationSidebarTests
{
    [AvaloniaFact]
    public async Task Add_assignment_uses_selected_subject_lesson_and_existing_plan_collection()
    {
        var subjectId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var collectionId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 17, 18, 0, 0, TimeSpan.Zero);
        var subject = new ContainerDefinition(subjectId, HavenMode.Study, "Maths", null, string.Empty, string.Empty, now, now);
        var lesson = new Lesson(lessonId, subjectId, "Pure", "Algebra", "{}", 0, now, now);
        var study = new RecordingStudyPlannerService(Assignment(subjectId, lessonId, collectionId, "Existing homework", now));
        var containers = new StudyContainerRepository(subject, lesson);

        using var sidebar = new NativeChatSidebar(
            new EmptyConversationRepository(), containers,
            _ => Task.CompletedTask, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask);
        using var coordinator = new StudyAssignmentsSidebarCoordinator(sidebar, containers, study, () => { });
        var window = new Window { Width = 440, Height = 940, Content = sidebar };
        try
        {
            window.Show();
            sidebar.SetMode(HavenMode.Study);
            sidebar.SetActiveConversation(null, subjectId);
            await WaitUntilAsync(() => coordinator.Section.Rows.Items.Count == 1);
            window.UpdateLayout();

            var router = new HavenInputRouter(sidebar.Scene.Root);
            var add = coordinator.Section.Section.Children.OfType<HavenButton>().Single(item => item.Name == "AddStudyAssignment");
            Click(router, add);
            await WaitUntilAsync(() => sidebar.Scene.Root.DescendantsAndSelf().OfType<Container>().Any(item => item.Name == "StudyAssignmentModal"));
            window.UpdateLayout();

            var modal = sidebar.Scene.Root.DescendantsAndSelf().OfType<Container>().Single(item => item.Name == "StudyAssignmentModal");
            modal.DescendantsAndSelf().OfType<Select>().Single(item => item.Name == "AssignmentLesson").SelectedIndex = 1;
            modal.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "AssignmentTitle").Text = "Trigonometry homework";
            modal.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "AssignmentNotes").Text = "Questions 1-10";
            modal.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "AssignmentDeadline").Text = "2026-08-20 17:00";
            modal.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "AssignmentReminder").Text = "2026-08-20 15:00";
            modal.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "AssignmentEstimate").Text = "25";
            modal.DescendantsAndSelf().OfType<Select>().Single(item => item.Name == "AssignmentPriority").SelectedIndex = 3;
            Click(router, modal.DescendantsAndSelf().OfType<HavenButton>().Single(item => item.Name == "ConfirmStudyAssignment"));

            await WaitUntilAsync(() => study.CreateCount == 1);
            await WaitUntilAsync(() => !sidebar.Scene.Root.DescendantsAndSelf().OfType<Container>().Any(item => item.Name == "StudyAssignmentModal"));
            var draft = Assert.IsType<StudyPlanAssignmentDraft>(study.LastDraft);
            Assert.Equal(subjectId, draft.SubjectId);
            Assert.Equal(lessonId, draft.LessonId);
            Assert.Equal(collectionId, draft.CollectionId);
            Assert.Equal("Trigonometry homework", draft.Title);
            Assert.Equal("Questions 1-10", draft.Notes);
            Assert.Equal(25, draft.EstimatedMinutes);
            Assert.Equal(PlannerPriority.High, draft.Priority);
            Assert.NotNull(draft.DueAt);
            Assert.NotNull(draft.ReminderAt);
            await WaitUntilAsync(() => coordinator.Section.Rows.Items.Count == 2);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task First_assignment_uses_canonical_college_collection()
    {
        var subjectId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 17, 18, 0, 0, TimeSpan.Zero);
        var subject = new ContainerDefinition(subjectId, HavenMode.Study, "Law", null, string.Empty, string.Empty, now, now);
        var lesson = new Lesson(lessonId, subjectId, "Criminal", "Duress", "{}", 0, now, now);
        var study = new RecordingStudyPlannerService(null);
        var containers = new StudyContainerRepository(subject, lesson);

        using var sidebar = new NativeChatSidebar(
            new EmptyConversationRepository(), containers,
            _ => Task.CompletedTask, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask);
        using var coordinator = new StudyAssignmentsSidebarCoordinator(sidebar, containers, study, () => { });
        var window = new Window { Width = 440, Height = 940, Content = sidebar };
        try
        {
            window.Show();
            sidebar.SetMode(HavenMode.Study);
            sidebar.SetActiveConversation(null, subjectId);
            await WaitUntilAsync(() => coordinator.Section.Section.GetValue(HavenProperties.Visibility) == HavenVisibility.Visible);
            window.UpdateLayout();

            var router = new HavenInputRouter(sidebar.Scene.Root);
            var add = coordinator.Section.Section.Children.OfType<HavenButton>().Single(item => item.Name == "AddStudyAssignment");
            Click(router, add);
            await WaitUntilAsync(() => sidebar.Scene.Root.DescendantsAndSelf().OfType<Container>().Any(item => item.Name == "StudyAssignmentModal"));
            window.UpdateLayout();
            var modal = sidebar.Scene.Root.DescendantsAndSelf().OfType<Container>().Single(item => item.Name == "StudyAssignmentModal");
            modal.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "AssignmentTitle").Text = "Duress problem question";
            Click(router, modal.DescendantsAndSelf().OfType<HavenButton>().Single(item => item.Name == "ConfirmStudyAssignment"));

            await WaitUntilAsync(() => study.CreateCount == 1);
            Assert.Equal(PlannerDefaults.CollegeCollectionId, Assert.IsType<StudyPlanAssignmentDraft>(study.LastDraft).CollectionId);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static PlannerStudyAssignment Assignment(Guid subjectId, Guid? lessonId, Guid collectionId, string title, DateTimeOffset now) =>
        new(
            new PlannerStudyLink(subjectId, lessonId),
            new PlannerTask(
                Guid.NewGuid(), collectionId, null, title, string.Empty, PlannerPriority.Medium, PlannerTaskStatus.Planned,
                PlannerStudyAssignmentTags.Attach("[]", subjectId, lessonId), 30, null, now.AddDays(2), null, null, null, 0, now, now, "UTC"));

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

    private sealed class RecordingStudyPlannerService(PlannerStudyAssignment? assignment) : IStudyPlannerService
    {
        private readonly List<PlannerStudyAssignment> _assignments = assignment is null ? [] : [assignment];
        public int CreateCount { get; private set; }
        public StudyPlanAssignmentDraft? LastDraft { get; private set; }

        public Task<PlannerStudyAssignment> CreateAsync(StudyPlanAssignmentDraft draft, DateTimeOffset now, CancellationToken cancellationToken)
        {
            CreateCount++;
            LastDraft = draft;
            var created = new PlannerStudyAssignment(
                new PlannerStudyLink(draft.SubjectId, draft.LessonId),
                new PlannerTask(
                    Guid.NewGuid(), draft.CollectionId, null, draft.Title, draft.Notes, draft.Priority, PlannerTaskStatus.Planned,
                    PlannerStudyAssignmentTags.Attach("[]", draft.SubjectId, draft.LessonId), draft.EstimatedMinutes, draft.StartsAt, draft.DueAt,
                    null, draft.ReminderAt, null, 0, now, now, draft.TimeZoneId));
            _assignments.Add(created);
            return Task.FromResult(created);
        }

        public Task<IReadOnlyList<PlannerStudyAssignment>> GetAssignmentsAsync(Guid subjectId, bool includeCompleted, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlannerStudyAssignment>>(_assignments.Where(item => item.Link.SubjectId == subjectId).ToArray());

        public Task<PlannerStudyAssignment> ScheduleRevisionAsync(StudyRevisionScheduleRequest request, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PlannerStudyAssignment> LinkExistingAsync(Guid planTaskId, Guid subjectId, Guid? lessonId, DateTimeOffset updatedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PlannerStudyAssignment> UpdateDeadlineAsync(Guid planTaskId, DateTimeOffset? dueAt, DateTimeOffset updatedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PlannerStudyAssignment> CompleteAsync(Guid planTaskId, DateTimeOffset completedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
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
