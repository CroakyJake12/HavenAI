/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/StudyPlannerServiceTests.cs, in the Core/Application automated test suite.
 * What: Protects Study-to-Plan deadline and completion transitions against canonical PlannerTask state.
 * How: A narrow recording planner repository verifies the Study service reads and writes only the linked Plan task.
 * Why: Study assignment state must never drift into a second deadline or completion store.
 * Maintenance: Keep these tests focused on cross-surface state transitions; persistence belongs to repository tests.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class StudyPlannerServiceTests
{
    [Fact]
    public async Task UpdateDeadlineAsyncUpdatesCanonicalPlanTaskAndReturnedAssignment()
    {
        var original = CreateTask(linked: true);
        var repository = new RecordingPlannerRepository(original);
        var service = new StudyPlannerService(repository, null!, new PlannerAvailabilityService(new PlannerDayService(repository)));
        var dueAt = original.StartsAt!.Value.AddHours(3);
        var updatedAt = original.UpdatedAt.AddMinutes(5);

        var assignment = await service.UpdateDeadlineAsync(original.Id, dueAt, updatedAt, CancellationToken.None);

        Assert.Equal(dueAt, repository.CurrentTask!.DueAt);
        Assert.Equal(updatedAt, repository.CurrentTask.UpdatedAt);
        Assert.Equal(repository.CurrentTask, assignment.Task);
        Assert.Equal(original.Id, assignment.PlanTaskId);
        Assert.Equal(1, repository.UpsertCount);
    }

    [Fact]
    public async Task UpdateDeadlineAsyncRejectsUnlinkedPlanTaskWithoutWriting()
    {
        var original = CreateTask(linked: false);
        var repository = new RecordingPlannerRepository(original);
        var service = new StudyPlannerService(repository, null!, new PlannerAvailabilityService(new PlannerDayService(repository)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateDeadlineAsync(original.Id, original.StartsAt!.Value.AddHours(2), original.UpdatedAt.AddMinutes(1), CancellationToken.None));

        Assert.Equal(0, repository.UpsertCount);
        Assert.Equal(original, repository.CurrentTask);
    }

    [Fact]
    public async Task UpdateDeadlineAsyncRejectsDeadlineBeforeScheduledStartWithoutWriting()
    {
        var original = CreateTask(linked: true);
        var repository = new RecordingPlannerRepository(original);
        var service = new StudyPlannerService(repository, null!, new PlannerAvailabilityService(new PlannerDayService(repository)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateDeadlineAsync(original.Id, original.StartsAt!.Value.AddMinutes(-1), original.UpdatedAt.AddMinutes(1), CancellationToken.None));

        Assert.Equal(0, repository.UpsertCount);
        Assert.Equal(original, repository.CurrentTask);
    }

    [Fact]
    public async Task CompleteAsyncReturnsCompletionReadBackFromCanonicalPlanTask()
    {
        var original = CreateTask(linked: true);
        var repository = new RecordingPlannerRepository(original);
        var service = new StudyPlannerService(repository, null!, new PlannerAvailabilityService(new PlannerDayService(repository)));
        var completedAt = original.UpdatedAt.AddHours(1);

        var assignment = await service.CompleteAsync(original.Id, completedAt, CancellationToken.None);

        Assert.Equal(PlannerTaskStatus.Completed, repository.CurrentTask!.Status);
        Assert.Equal(completedAt, repository.CurrentTask.CompletedAt);
        Assert.Equal(repository.CurrentTask, assignment.Task);
        Assert.Equal(1, repository.CompleteCount);
    }

    private static PlannerTask CreateTask(bool linked)
    {
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
        var subjectId = Guid.NewGuid();
        var tags = linked ? PlannerStudyAssignmentTags.Attach("[]", subjectId, null) : "[]";
        return new PlannerTask(
            Guid.NewGuid(),
            PlannerDefaults.CollegeCollectionId,
            null,
            "A-Level revision",
            "Review chapter",
            PlannerPriority.High,
            PlannerTaskStatus.Planned,
            tags,
            60,
            now.AddHours(1),
            now.AddHours(2),
            null,
            now.AddMinutes(30),
            null,
            0,
            now,
            now,
            "Europe/London");
    }

    private sealed class RecordingPlannerRepository(PlannerTask initial) : IPlannerRepository
    {
        public PlannerTask? CurrentTask { get; private set; } = initial;
        public int UpsertCount { get; private set; }
        public int CompleteCount { get; private set; }

        public Task<PlannerTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(CurrentTask?.Id == id ? CurrentTask : null);

        public Task UpsertTaskAsync(PlannerTask task, CancellationToken cancellationToken)
        {
            CurrentTask = task;
            UpsertCount++;
            return Task.CompletedTask;
        }

        public Task CompleteTaskAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken)
        {
            if (CurrentTask?.Id != id) throw new InvalidOperationException("Unknown task.");
            CurrentTask = CurrentTask with
            {
                Status = PlannerTaskStatus.Completed,
                CompletedAt = completedAt,
                UpdatedAt = completedAt
            };
            CompleteCount++;
            return Task.CompletedTask;
        }

        public Task EnsureDefaultsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerCollection>> GetCollectionsAsync(bool includeArchived, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertCollectionAsync(PlannerCollection collection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ArchiveCollectionAsync(Guid id, bool archived, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerTask>> GetTasksAsync(PlannerTaskQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteTaskAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerTaskCompletion>> GetCompletionHistoryAsync(Guid taskId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerCalendar>> GetCalendarsAsync(bool visibleOnly, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertCalendarAsync(PlannerCalendar calendar, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerEvent>> GetEventsAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, Guid? calendarId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PlannerEvent?> GetEventAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertEventAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteEventAsync(Guid id, DateTimeOffset deletedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CalendarAccount>> GetCalendarAccountsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertCalendarAccountAsync(CalendarAccount account, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CalendarConflict>> GetUnresolvedConflictsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ResolveConflictAsync(Guid id, CalendarConflictResolution resolution, DateTimeOffset resolvedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerReminder>> GetDueRemindersAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkReminderDeliveredAsync(PlannerReminder reminder, DateTimeOffset deliveredAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ApplyProposalAsync(PlannerChangeProposal proposal, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
