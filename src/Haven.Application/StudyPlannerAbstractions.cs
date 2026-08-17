/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/StudyPlannerAbstractions.cs, in the Application layer.
 * What: Defines the use-case contract for linking Study assignments to canonical Plan tasks.
 * How: Callers create/link/list/complete assignments while all mutable deadline and completion state remains a PlannerTask.
 * Why: Study needs assignment workflows without a second assignment store that can drift from Plan.
 * Maintenance: Keep Plan task IDs authoritative and preserve cancellation across repository calls.
 */

using Haven.Core;

namespace Haven.Application;

public sealed record StudyPlanAssignmentDraft(
    Guid SubjectId,
    Guid? LessonId,
    Guid CollectionId,
    string Title,
    string Notes,
    DateTimeOffset? DueAt,
    DateTimeOffset? ReminderAt,
    int? EstimatedMinutes,
    PlannerPriority Priority = PlannerPriority.None,
    DateTimeOffset? StartsAt = null,
    string TimeZoneId = "UTC");

public interface IStudyPlannerService
{
    Task<PlannerStudyAssignment> CreateAsync(StudyPlanAssignmentDraft draft, DateTimeOffset now, CancellationToken cancellationToken);
    Task<PlannerStudyAssignment> LinkExistingAsync(Guid planTaskId, Guid subjectId, Guid? lessonId, DateTimeOffset updatedAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlannerStudyAssignment>> GetAssignmentsAsync(Guid subjectId, bool includeCompleted, CancellationToken cancellationToken);
    Task<PlannerStudyAssignment> CompleteAsync(Guid planTaskId, DateTimeOffset completedAt, CancellationToken cancellationToken);
    Task UnlinkAsync(Guid planTaskId, DateTimeOffset updatedAt, CancellationToken cancellationToken);
}
