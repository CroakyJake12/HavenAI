/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Planner/StudyPlannerService.cs, in the Application layer.
 * What: Implements Study assignment workflows by linking Study context to canonical PlannerTask records.
 * How: Subject/lesson ownership is validated through IContainerRepository; task state is created and mutated only through IPlannerRepository.
 * Why: A Study assignment should be the same task/deadline the user sees in Plan, not a duplicated record.
 * Maintenance: Do not introduce assignment persistence here; reserved task metadata is only a relationship marker.
 */

using Haven.Core;

namespace Haven.Application;

public sealed class StudyPlannerService(
    IPlannerRepository planner,
    IContainerRepository containers,
    IPlannerAvailabilityService availability) : IStudyPlannerService
{
    public async Task<PlannerStudyAssignment> CreateAsync(StudyPlanAssignmentDraft draft, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ValidateDraft(draft);
        await ValidateContextAsync(draft.SubjectId, draft.LessonId, cancellationToken).ConfigureAwait(false);
        return await PersistAssignmentAsync(draft, now, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlannerStudyAssignment> ScheduleRevisionAsync(
        StudyRevisionScheduleRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SubjectId == Guid.Empty) throw new ArgumentException("Study subject ID is required.", nameof(request));
        if (request.CollectionId == Guid.Empty) throw new ArgumentException("Plan collection ID is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Title)) throw new ArgumentException("Revision title is required.", nameof(request));
        if (request.DurationMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(request), "Revision duration must be positive.");
        if (request.WindowEnd <= request.WindowStart) throw new ArgumentException("Revision scheduling window must have a positive duration.", nameof(request));
        if (request.DueAt is { } deadline && deadline <= request.WindowStart)
            throw new ArgumentException("Revision deadline must be after the scheduling window starts.", nameof(request));

        var timeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId) ? "UTC" : request.TimeZoneId.Trim();
        _ = PlannerDayTimeline.GetDayBounds(request.WindowStart, timeZoneId);
        await ValidateContextAsync(request.SubjectId, request.LessonId, cancellationToken).ConfigureAwait(false);

        var required = TimeSpan.FromMinutes(request.DurationMinutes);
        var effectiveWindowEnd = request.DueAt is { } dueAt && dueAt < request.WindowEnd ? dueAt : request.WindowEnd;
        var cursor = request.WindowStart;
        while (cursor < effectiveWindowEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (_, dayEnd) = PlannerDayTimeline.GetDayBounds(cursor, timeZoneId);
            var searchEnd = effectiveWindowEnd < dayEnd ? effectiveWindowEnd : dayEnd;
            if (cursor < searchEnd)
            {
                var free = await availability.GetFreeWindowsAsync(
                    cursor,
                    now,
                    timeZoneId,
                    cursor,
                    searchEnd,
                    required,
                    cancellationToken).ConfigureAwait(false);
                var slot = free.FirstOrDefault();
                if (slot is not null)
                {
                    var draft = new StudyPlanAssignmentDraft(
                        request.SubjectId,
                        request.LessonId,
                        request.CollectionId,
                        request.Title,
                        request.Notes,
                        request.DueAt,
                        request.ReminderAt,
                        request.DurationMinutes,
                        request.Priority,
                        slot.StartsAt,
                        timeZoneId);
                    ValidateDraft(draft);
                    return await PersistAssignmentAsync(draft, now, cancellationToken).ConfigureAwait(false);
                }
            }

            if (dayEnd <= cursor) break;
            cursor = dayEnd;
        }

        throw new InvalidOperationException("No Plan free window can fit this revision session before the requested deadline.");
    }

    private async Task<PlannerStudyAssignment> PersistAssignmentAsync(
        StudyPlanAssignmentDraft draft,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var task = new PlannerTask(
            Guid.NewGuid(),
            draft.CollectionId,
            null,
            draft.Title.Trim(),
            draft.Notes?.Trim() ?? string.Empty,
            draft.Priority,
            PlannerTaskStatus.Planned,
            PlannerStudyAssignmentTags.Attach("[]", draft.SubjectId, draft.LessonId),
            draft.EstimatedMinutes,
            draft.StartsAt,
            draft.DueAt,
            null,
            draft.ReminderAt,
            null,
            0,
            now,
            now,
            string.IsNullOrWhiteSpace(draft.TimeZoneId) ? "UTC" : draft.TimeZoneId.Trim());
        await planner.UpsertTaskAsync(task, cancellationToken).ConfigureAwait(false);
        return new(new PlannerStudyLink(draft.SubjectId, draft.LessonId), task);
    }

    private static void ValidateDraft(StudyPlanAssignmentDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.SubjectId == Guid.Empty) throw new ArgumentException("Study subject ID is required.", nameof(draft));
        if (draft.CollectionId == Guid.Empty) throw new ArgumentException("Plan collection ID is required.", nameof(draft));
        if (string.IsNullOrWhiteSpace(draft.Title)) throw new ArgumentException("Assignment title is required.", nameof(draft));
        if (draft.EstimatedMinutes < 0) throw new ArgumentException("Estimated minutes cannot be negative.", nameof(draft));
        if (draft.StartsAt is not null && draft.DueAt is not null && draft.DueAt < draft.StartsAt)
            throw new ArgumentException("Assignment due time cannot be before its start.", nameof(draft));
    }

    public async Task<PlannerStudyAssignment> LinkExistingAsync(
        Guid planTaskId,
        Guid subjectId,
        Guid? lessonId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        if (planTaskId == Guid.Empty) throw new ArgumentException("Plan task ID is required.", nameof(planTaskId));
        await ValidateContextAsync(subjectId, lessonId, cancellationToken).ConfigureAwait(false);
        var task = await planner.GetTaskAsync(planTaskId, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("The Plan task no longer exists.");
        var updated = task with
        {
            TagsJson = PlannerStudyAssignmentTags.Attach(task.TagsJson, subjectId, lessonId),
            UpdatedAt = updatedAt
        };
        await planner.UpsertTaskAsync(updated, cancellationToken).ConfigureAwait(false);
        return new(new PlannerStudyLink(subjectId, lessonId), updated);
    }

    public async Task<IReadOnlyList<PlannerStudyAssignment>> GetAssignmentsAsync(
        Guid subjectId,
        bool includeCompleted,
        CancellationToken cancellationToken)
    {
        if (subjectId == Guid.Empty) throw new ArgumentException("Study subject ID is required.", nameof(subjectId));
        var tasks = await planner.GetTasksAsync(new PlannerTaskQuery(IncludeCompleted: includeCompleted), cancellationToken).ConfigureAwait(false);
        return tasks
            .Select(task => PlannerStudyAssignmentTags.TryRead(task.TagsJson, out var link)
                ? new PlannerStudyAssignment(link, task)
                : null)
            .Where(item => item is not null && item.Link.SubjectId == subjectId)
            .Select(item => item!)
            .OrderBy(item => item.Task.DueAt ?? item.Task.StartsAt ?? DateTimeOffset.MaxValue)
            .ThenBy(item => item.Task.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<PlannerStudyAssignment> UpdateDeadlineAsync(
        Guid planTaskId,
        DateTimeOffset? dueAt,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        var task = await planner.GetTaskAsync(planTaskId, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("The Plan task no longer exists.");
        if (!PlannerStudyAssignmentTags.TryRead(task.TagsJson, out var link))
            throw new InvalidOperationException("The Plan task is not linked to a Study assignment.");
        if (task.StartsAt is { } startsAt && dueAt is { } deadline && deadline < startsAt)
            throw new ArgumentException("Assignment due time cannot be before its start.", nameof(dueAt));

        var updated = task with { DueAt = dueAt, UpdatedAt = updatedAt };
        await planner.UpsertTaskAsync(updated, cancellationToken).ConfigureAwait(false);
        return new(link, updated);
    }

    public async Task<PlannerStudyAssignment> CompleteAsync(Guid planTaskId, DateTimeOffset completedAt, CancellationToken cancellationToken)
    {
        var task = await planner.GetTaskAsync(planTaskId, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("The Plan task no longer exists.");
        if (!PlannerStudyAssignmentTags.TryRead(task.TagsJson, out var link))
            throw new InvalidOperationException("The Plan task is not linked to a Study assignment.");

        await planner.CompleteTaskAsync(planTaskId, completedAt, cancellationToken).ConfigureAwait(false);
        var updated = await planner.GetTaskAsync(planTaskId, cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("The completed Plan task no longer exists.");
        return new(link, updated);
    }

    public async Task UnlinkAsync(Guid planTaskId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        var task = await planner.GetTaskAsync(planTaskId, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("The Plan task no longer exists.");
        if (!PlannerStudyAssignmentTags.TryRead(task.TagsJson, out _)) return;
        await planner.UpsertTaskAsync(task with
        {
            TagsJson = PlannerStudyAssignmentTags.Detach(task.TagsJson),
            UpdatedAt = updatedAt
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateContextAsync(Guid subjectId, Guid? lessonId, CancellationToken cancellationToken)
    {
        if (subjectId == Guid.Empty) throw new ArgumentException("Study subject ID is required.", nameof(subjectId));
        if (lessonId == Guid.Empty) throw new ArgumentException("Study lesson ID cannot be empty.", nameof(lessonId));

        var subjects = await containers.GetByModeAsync(HavenMode.Study, cancellationToken).ConfigureAwait(false);
        if (!subjects.Any(subject => subject.Id == subjectId))
            throw new InvalidOperationException("The Study subject does not exist or is archived.");
        if (lessonId is null) return;

        var lessons = await containers.GetLessonsAsync(subjectId, cancellationToken).ConfigureAwait(false);
        if (!lessons.Any(lesson => lesson.Id == lessonId.Value))
            throw new InvalidOperationException("The Study lesson does not belong to the selected subject.");
    }
}
