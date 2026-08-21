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

public sealed class StudyRevisionSchedulingSidebarTests
{
    [AvaloniaFact]
    public async Task Revision_scheduler_submits_subject_lesson_and_existing_plan_collection()
    {
        var subjectId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var collectionId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 17, 18, 0, 0, TimeSpan.Zero);
        var subject = new ContainerDefinition(subjectId, HavenMode.Study, "Maths", null, string.Empty, string.Empty, now, now);
        var lesson = new Lesson(lessonId, subjectId, "Pure", "Algebra", "{}", 0, now, now);
        var assignment = Assignment(subjectId, lessonId, collectionId, "Maths homework", now);
        var study = new RecordingStudyPlannerService(assignment);
        var containers = new StudyContainerRepository(subject, lesson);

        using var sidebar = new NativeChatSidebar(
            new EmptyConversationRepository(), containers,
            _ => Task.CompletedTask, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask);
        using var coordinator = new StudyAssignmentsSidebarCoordinator(sidebar, containers, study, () => { });
        var window = new Window { Width = 440, Height = 900, Content = sidebar };
        try
        {
            window.Show();
            sidebar.SetMode(HavenMode.Study);
            sidebar.SetActiveConversation(null, subjectId);
            await WaitUntilAsync(() => coordinator.Section.Rows.Items.Count == 1);
            window.UpdateLayout();

            var router = new HavenInputRouter(sidebar.Scene.Root);
            var schedule = coordinator.Section.Section.Children
                .OfType<HavenButton>()
                .Single(item => item.Name == "ScheduleRevision");
            Click(router, schedule);
            await WaitUntilAsync(() => sidebar.Scene.Root.DescendantsAndSelf().OfType<Container>().Any(item => item.Name == "StudyRevisionModal"));
            window.UpdateLayout();

            var modal = sidebar.Scene.Root.DescendantsAndSelf().OfType<Container>().Single(item => item.Name == "StudyRevisionModal");
            modal.DescendantsAndSelf().OfType<Select>().Single(item => item.Name == "RevisionLesson").SelectedIndex = 1;
            modal.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "RevisionTitle").Text = "Algebra revision";
            modal.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "RevisionDuration").Text = "60";
            modal.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "RevisionWindowStart").Text = "2026-08-18 09:00";
            modal.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "RevisionWindowEnd").Text = "2026-08-18 13:00";
            modal.DescendantsAndSelf().OfType<Input>().Single(item => item.Name == "RevisionDeadline").Text = "2026-08-20 17:00";
            Click(router, modal.DescendantsAndSelf().OfType<HavenButton>().Single(item => item.Name == "ConfirmRevisionSchedule"));

            await WaitUntilAsync(() => study.ScheduleRevisionCount == 1);
            await WaitUntilAsync(() => !sidebar.Scene.Root.DescendantsAndSelf().OfType<Container>().Any(item => item.Name == "StudyRevisionModal"));

            var request = Assert.IsType<StudyRevisionScheduleRequest>(study.LastRevisionRequest);
            Assert.Equal(subjectId, request.SubjectId);
            Assert.Equal(lessonId, request.LessonId);
            Assert.Equal(collectionId, request.CollectionId);
            Assert.Equal("Algebra revision", request.Title);
            Assert.Equal(60, request.DurationMinutes);
            Assert.Equal(TimeZoneInfo.Local.Id, request.TimeZoneId);
            Assert.Equal(new DateTime(2026, 8, 18, 9, 0, 0), request.WindowStart.DateTime);
            Assert.Equal(new DateTime(2026, 8, 18, 13, 0, 0), request.WindowEnd.DateTime);
            Assert.Equal(new DateTime(2026, 8, 20, 17, 0, 0), request.DueAt?.DateTime);
            await WaitUntilAsync(() => coordinator.Section.Rows.Items.Count == 2);
            Assert.Contains(
                coordinator.Section.Rows.Items,
                item => item.GetComponent<HavenButton>("Open").Content == "Algebra revision");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Revision_scheduler_keeps_modal_open_when_no_plan_slot_fits()
    {
        var subjectId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 17, 18, 0, 0, TimeSpan.Zero);
        var subject = new ContainerDefinition(subjectId, HavenMode.Study, "Maths", null, string.Empty, string.Empty, now, now);
        var lesson = new Lesson(lessonId, subjectId, "Pure", "Algebra", "{}", 0, now, now);
        var study = new RecordingStudyPlannerService(null) { RejectRevision = true };
        var containers = new StudyContainerRepository(subject, lesson);

        using var sidebar = new NativeChatSidebar(
            new EmptyConversationRepository(), containers,
            _ => Task.CompletedTask, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask);
        using var coordinator = new StudyAssignmentsSidebarCoordinator(sidebar, containers, study, () => { });
        var window = new Window { Width = 440, Height = 900, Content = sidebar };
        try
        {
            window.Show();
            sidebar.SetMode(HavenMode.Study);
            sidebar.SetActiveConversation(null, subjectId);
            await WaitUntilAsync(() => coordinator.Section.Section.GetValue(HavenProperties.Visibility) == HavenVisibility.Visible);
            window.UpdateLayout();
            var router = new HavenInputRouter(sidebar.Scene.Root);
            var schedule = coordinator.Section.Section.Children.OfType<HavenButton>().Single(item => item.Name == "ScheduleRevision");
            Click(router, schedule);
            await WaitUntilAsync(() => sidebar.Scene.Root.DescendantsAndSelf().OfType<Container>().Any(item => item.Name == "StudyRevisionModal"));
            window.UpdateLayout();

            var modal = sidebar.Scene.Root.DescendantsAndSelf().OfType<Container>().Single(item => item.Name == "StudyRevisionModal");
            Click(router, modal.DescendantsAndSelf().OfType<HavenButton>().Single(item => item.Name == "ConfirmRevisionSchedule"));
            await WaitUntilAsync(() => study.ScheduleRevisionCount == 1);
            await WaitUntilAsync(() => modal.DescendantsAndSelf().OfType<HavenText>().Single(item => item.Name == "RevisionValidation").Content.Contains("No suitable free window", StringComparison.Ordinal));

            Assert.Contains(modal, sidebar.Scene.Root.Children);
            Assert.Equal(PlannerDefaults.CollegeCollectionId, study.LastRevisionRequest?.CollectionId);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static PlannerStudyAssignment Assignment(Guid subjectId, Guid lessonId, Guid collectionId, string title, DateTimeOffset now)
    {
        var task = new PlannerTask(
            Guid.NewGuid(), collectionId, null, title, string.Empty, PlannerPriority.Medium, PlannerTaskStatus.Planned,
            PlannerStudyAssignmentTags.Attach("[]", subjectId, lessonId), 45, null, now.AddDays(2), null, null, null, 0, now, now, "UTC");
        return new PlannerStudyAssignment(new(subjectId, lessonId), task);
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

    private sealed class RecordingStudyPlannerService(PlannerStudyAssignment? assignment) : IStudyPlannerService
    {
        private readonly List<PlannerStudyAssignment> _assignments = assignment is null ? [] : [assignment];
        public int ScheduleRevisionCount { get; private set; }
        public StudyRevisionScheduleRequest? LastRevisionRequest { get; private set; }
        public bool RejectRevision { get; init; }

        public Task<IReadOnlyList<PlannerStudyAssignment>> GetAssignmentsAsync(Guid subjectId, bool includeCompleted, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlannerStudyAssignment>>(_assignments.Where(item => item.Link.SubjectId == subjectId).ToArray());

        public Task<PlannerStudyAssignment> ScheduleRevisionAsync(StudyRevisionScheduleRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            ScheduleRevisionCount++;
            LastRevisionRequest = request;
            if (RejectRevision) throw new InvalidOperationException("No suitable free window fits this revision session.");

            var scheduled = new PlannerStudyAssignment(
                new PlannerStudyLink(request.SubjectId, request.LessonId),
                new PlannerTask(
                    Guid.NewGuid(), request.CollectionId, null, request.Title, request.Notes, request.Priority, PlannerTaskStatus.Planned,
                    PlannerStudyAssignmentTags.Attach("[]", request.SubjectId, request.LessonId), request.DurationMinutes, request.WindowStart, request.DueAt,
                    null, request.ReminderAt, null, 0, now, now, request.TimeZoneId));
            _assignments.Add(scheduled);
            return Task.FromResult(scheduled);
        }

        public Task<PlannerStudyAssignment> CreateAsync(StudyPlanAssignmentDraft draft, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
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
