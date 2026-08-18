using System.Globalization;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Shell.NativePresentation;

internal sealed record NativeChatSidebarContext(HavenMode Mode, Guid? GroupId);

/// <summary>
/// Bridges the existing Study subject/chat sidebar to canonical Plan assignment state without introducing a second Study store.
/// </summary>
internal sealed class StudyAssignmentsSidebarCoordinator : IDisposable
{
    private readonly NativeChatSidebar _sidebar;
    private readonly IContainerRepository _containers;
    private readonly IStudyPlannerService _studyPlanner;
    private readonly Action _openPlan;
    private readonly StudyAssignmentsHavenSection _section;
    private IReadOnlyList<PlannerStudyAssignment> _assignments = [];
    private IReadOnlyList<Lesson> _lessons = [];
    private HavenMode _mode;
    private Guid? _subjectId;
    private string _query = string.Empty;
    private int _refreshVersion;
    private bool _disposed;

    public StudyAssignmentsSidebarCoordinator(
        NativeChatSidebar sidebar,
        IContainerRepository containers,
        IStudyPlannerService studyPlanner,
        Action openPlan)
    {
        _sidebar = sidebar ?? throw new ArgumentNullException(nameof(sidebar));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _studyPlanner = studyPlanner ?? throw new ArgumentNullException(nameof(studyPlanner));
        _openPlan = openPlan ?? throw new ArgumentNullException(nameof(openPlan));
        _section = new StudyAssignmentsHavenSection(sidebar.Scene.Root);
        _mode = sidebar.CurrentMode;
        _subjectId = sidebar.ActiveGroupId;
        _query = sidebar.Scene.Search.Text;

        _sidebar.ContextChanged += OnContextChanged;
        _sidebar.Scene.GroupActionRequested += OnGroupActionRequested;
        _sidebar.Scene.SearchChanged += OnSearchChanged;
        _section.ActionRequested += OnAssignmentActionRequested;
        StudyAssignmentCreationCoordinator.Attach(_section, _sidebar, _containers, _studyPlanner, RefreshAsync);
        StudyRevisionSchedulingCoordinator.Attach(_section, _sidebar, _containers, _studyPlanner, RefreshAsync);
        _ = RefreshAsync();
    }

    internal StudyAssignmentsHavenSection Section => _section;

    private void OnContextChanged(object? sender, NativeChatSidebarContext context)
    {
        _mode = context.Mode;
        _subjectId = context.GroupId;
        _ = RefreshAsync();
    }

    private void OnGroupActionRequested(object? sender, ChatSidebarGroupRequest request)
    {
        if (_mode != HavenMode.Study) return;
        if (request.Action is not (ChatSidebarGroupAction.Open or ChatSidebarGroupAction.NewChat)) return;
        _subjectId = request.GroupId;
        _ = RefreshAsync();
    }

    private void OnSearchChanged(object? sender, string query)
    {
        _query = query;
        Render();
    }

    private async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        if (_disposed) return;
        if (_mode != HavenMode.Study || _subjectId is not Guid subjectId)
        {
            _assignments = [];
            _lessons = [];
            await Dispatcher.UIThread.InvokeAsync(Render);
            return;
        }

        try
        {
            var assignmentsTask = _studyPlanner.GetAssignmentsAsync(subjectId, includeCompleted: true, CancellationToken.None);
            var lessonsTask = _containers.GetLessonsAsync(subjectId, CancellationToken.None);
            await Task.WhenAll(assignmentsTask, lessonsTask).ConfigureAwait(false);
            if (_disposed || version != Volatile.Read(ref _refreshVersion) || _subjectId != subjectId) return;
            _assignments = assignmentsTask.Result;
            _lessons = lessonsTask.Result;
            await Dispatcher.UIThread.InvokeAsync(Render);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            if (_disposed || version != Volatile.Read(ref _refreshVersion)) return;
            await Dispatcher.UIThread.InvokeAsync(() => _sidebar.Scene.SetStatus(exception.Message));
        }
    }

    private void Render()
    {
        if (_disposed) return;
        var isStudy = _mode == HavenMode.Study;
        var selected = isStudy && _subjectId is Guid;
        var now = DateTimeOffset.UtcNow;
        var rows = selected
            ? _assignments
                .Select(item => ToEntry(item, now))
                .Where(item => string.IsNullOrWhiteSpace(_query)
                    || item.Title.Contains(_query, StringComparison.OrdinalIgnoreCase)
                    || item.Subtitle.Contains(_query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Completed)
                .ThenBy(item => _assignments.First(assignment => assignment.PlanTaskId == item.PlanTaskId).Task.DueAt ?? DateTimeOffset.MaxValue)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
        _section.SetContext(isStudy, selected, rows);
    }

    private StudyAssignmentSidebarEntry ToEntry(PlannerStudyAssignment assignment, DateTimeOffset now)
    {
        var task = assignment.Task;
        var completed = task.Status == PlannerTaskStatus.Completed || task.CompletedAt is not null;
        var overdue = !completed && task.DueAt is { } dueAt && dueAt < now;
        var lessonName = assignment.Link.LessonId is Guid lessonId
            ? _lessons.FirstOrDefault(item => item.Id == lessonId)?.Name
            : null;
        var state = completed
            ? task.CompletedAt is { } completedAt ? $"Completed {FormatPlannerTime(completedAt, task.TimeZoneId)}" : "Completed"
            : task.DueAt is { } deadline ? $"{(overdue ? "Overdue" : "Due")} {FormatPlannerTime(deadline, task.TimeZoneId)}" : "No deadline";
        if (!string.IsNullOrWhiteSpace(lessonName)) state = $"{lessonName} · {state}";
        return new StudyAssignmentSidebarEntry(task.Id, task.Title, state, completed, overdue);
    }

    private async void OnAssignmentActionRequested(object? sender, StudyAssignmentSidebarRequest request)
    {
        var assignment = _assignments.FirstOrDefault(item => item.PlanTaskId == request.PlanTaskId);
        if (assignment is null) return;
        try
        {
            switch (request.Action)
            {
                case StudyAssignmentSidebarAction.OpenPlan:
                    _openPlan();
                    break;
                case StudyAssignmentSidebarAction.EditDeadline:
                    ShowDeadlinePrompt(assignment);
                    break;
                case StudyAssignmentSidebarAction.Complete:
                    if (assignment.Task.Status != PlannerTaskStatus.Completed && assignment.Task.CompletedAt is null)
                    {
                        await _studyPlanner.CompleteAsync(assignment.PlanTaskId, DateTimeOffset.UtcNow, CancellationToken.None);
                        await RefreshAsync();
                    }
                    break;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException or IOException or UnauthorizedAccessException)
        {
            _sidebar.Scene.SetStatus(exception.Message);
        }
    }

    private void ShowDeadlinePrompt(PlannerStudyAssignment assignment)
    {
        var initial = assignment.Task.DueAt is null
            ? "none"
            : TimeZoneInfo.ConvertTime(assignment.Task.DueAt.Value, ResolveTimeZone(assignment.Task.TimeZoneId))
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        _sidebar.Scene.ShowTextPrompt("Deadline (yyyy-MM-dd HH:mm or none)", initial, "Save deadline", async value =>
        {
            try
            {
                var dueAt = ParseDeadline(value, assignment.Task.TimeZoneId);
                await _studyPlanner.UpdateDeadlineAsync(assignment.PlanTaskId, dueAt, DateTimeOffset.UtcNow, CancellationToken.None);
                await RefreshAsync();
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
            {
                throw new InvalidOperationException(exception.Message, exception);
            }
        });
    }

    private static string FormatPlannerTime(DateTimeOffset value, string? timeZoneId)
    {
        TimeZoneInfo zone;
        try { zone = ResolveTimeZone(timeZoneId); }
        catch (FormatException) { zone = TimeZoneInfo.Utc; }
        return TimeZoneInfo.ConvertTime(value, zone).ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseDeadline(string value, string? timeZoneId)
    {
        var text = value.Trim();
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
            throw new FormatException("Use yyyy-MM-dd HH:mm, or 'none' to clear the deadline.");

        var zone = ResolveTimeZone(timeZoneId);
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        var offset = zone.GetUtcOffset(local);
        if (zone.IsAmbiguousTime(local)) offset = zone.GetAmbiguousTimeOffsets(local).Max();
        return new DateTimeOffset(local, offset);
    }

    private static TimeZoneInfo ResolveTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new FormatException($"Unknown time zone '{id}'."); }
        catch (InvalidTimeZoneException) { throw new FormatException($"Invalid time zone '{id}'."); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _refreshVersion);
        _sidebar.ContextChanged -= OnContextChanged;
        _sidebar.Scene.GroupActionRequested -= OnGroupActionRequested;
        _sidebar.Scene.SearchChanged -= OnSearchChanged;
        _section.ActionRequested -= OnAssignmentActionRequested;
        _section.Dispose();
    }
}
