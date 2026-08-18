using System.Globalization;
using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Shell.NativePresentation;

internal sealed record StudyRevisionLessonOption(Guid? LessonId, string Label);

internal sealed record StudyRevisionForm(
    Guid? LessonId,
    string Title,
    string DurationMinutes,
    string WindowStart,
    string WindowEnd,
    string Deadline);

/// <summary>
/// Adds Study's revision-scheduling action to the existing Haven.UI assignment section while keeping
/// all scheduling and persistence in the canonical Plan-backed Study planner service.
/// </summary>
internal sealed class StudyRevisionSchedulingCoordinator
{
    private readonly NativeChatSidebar _sidebar;
    private readonly IContainerRepository _containers;
    private readonly IStudyPlannerService _studyPlanner;
    private readonly Func<Task> _refresh;

    private StudyRevisionSchedulingCoordinator(
        StudyAssignmentsHavenSection section,
        NativeChatSidebar sidebar,
        IContainerRepository containers,
        IStudyPlannerService studyPlanner,
        Func<Task> refresh)
    {
        _sidebar = sidebar;
        _containers = containers;
        _studyPlanner = studyPlanner;
        _refresh = refresh;

        ScheduleRevision = new HavenButton
        {
            Name = "ScheduleRevision",
            Content = "Schedule revision",
            IconKey = "calendar",
            Variant = ButtonVariant.Primary
        };
        ScheduleRevision.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        ScheduleRevision.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        ScheduleRevision.Accessibility.AccessibleName = "Schedule revision in a free Plan window";
        ScheduleRevision.Invoked += OnScheduleRevisionInvoked;
        section.Section.Add(ScheduleRevision);
    }

    internal HavenButton ScheduleRevision { get; }

    public static void Attach(
        StudyAssignmentsHavenSection section,
        NativeChatSidebar sidebar,
        IContainerRepository containers,
        IStudyPlannerService studyPlanner,
        Func<Task> refresh) =>
        _ = new StudyRevisionSchedulingCoordinator(section, sidebar, containers, studyPlanner, refresh);

    private async void OnScheduleRevisionInvoked(object? sender, EventArgs e)
    {
        if (_sidebar.CurrentMode != HavenMode.Study || _sidebar.ActiveGroupId is not Guid subjectId)
        {
            _sidebar.Scene.SetStatus("Select a Study subject before scheduling revision.");
            return;
        }

        try
        {
            var lessonsTask = _containers.GetLessonsAsync(subjectId, CancellationToken.None);
            var assignmentsTask = _studyPlanner.GetAssignmentsAsync(subjectId, includeCompleted: true, CancellationToken.None);
            await Task.WhenAll(lessonsTask, assignmentsTask);

            var lessonOptions = new List<StudyRevisionLessonOption>
            {
                new(null, "General subject revision")
            };
            lessonOptions.AddRange(lessonsTask.Result
                .OrderBy(item => item.TopicGroup, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => new StudyRevisionLessonOption(
                    item.Id,
                    string.IsNullOrWhiteSpace(item.TopicGroup) ? item.Name : $"{item.TopicGroup} · {item.Name}")));

            var collectionId = assignmentsTask.Result.FirstOrDefault()?.Task.CollectionId ?? PlannerDefaults.CollegeCollectionId;
            ShowRevisionModal(
                _sidebar.Scene.Root,
                lessonOptions,
                collectionId,
                subjectId,
                TimeZoneInfo.Local.Id);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            _sidebar.Scene.SetStatus(exception.Message);
        }
    }

    private void ShowRevisionModal(
        Page root,
        IReadOnlyList<StudyRevisionLessonOption> lessons,
        Guid collectionId,
        Guid subjectId,
        string timeZoneId)
    {
        var now = DateTimeOffset.Now;
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        if (start <= now) start = start.AddHours(1);
        var end = start.AddDays(7);

        var overlay = new HavenContainer { Name = "StudyRevisionModal", Layout = HavenLayout.Overlay };
        overlay.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        overlay.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        overlay.SetValue(HavenProperties.Background, "Overlay");
        overlay.SetValue(HavenProperties.Opacity, .82d);
        overlay.SetValue(HavenProperties.ZIndex, 210);

        var card = new HavenContainer { Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Width, HavenLength.Px(330));
        card.SetValue(HavenProperties.MaxWidth, HavenLength.Percent(94));
        card.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        card.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(16)));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        card.SetValue(HavenProperties.Shadow, "Card");
        card.SetValue(HavenProperties.Gap, HavenLength.Px(6));

        card.Add(new HavenText { Content = "Schedule revision", Level = TextLevel.H2 });
        var hint = new HavenText
        {
            Content = "Haven will place this in the first free Plan slot inside your window.",
            Level = TextLevel.Paragraph
        };
        hint.SetValue(HavenProperties.Foreground, "TextSecondary");
        hint.SetValue(HavenProperties.FontSize, 11d);
        card.Add(hint);

        var lesson = new Select
        {
            Name = "RevisionLesson",
            Items = lessons.Select(item => item.Label).ToArray(),
            SelectedIndex = 0
        };
        lesson.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        lesson.Accessibility.AccessibleName = "Revision lesson";
        card.Add(lesson);

        var title = Field("RevisionTitle", "Revision title", "Revision");
        var duration = Field("RevisionDuration", "Duration in minutes", "45");
        var windowStart = Field("RevisionWindowStart", "Earliest start (yyyy-MM-dd HH:mm)", start.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        var windowEnd = Field("RevisionWindowEnd", "Latest finish (yyyy-MM-dd HH:mm)", end.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        var deadline = Field("RevisionDeadline", "Deadline (yyyy-MM-dd HH:mm or none)", "none");
        card.Add(title);
        card.Add(duration);
        card.Add(windowStart);
        card.Add(windowEnd);
        card.Add(deadline);

        var validation = new HavenText { Name = "RevisionValidation", Content = string.Empty };
        validation.SetValue(HavenProperties.Foreground, "Danger");
        validation.SetValue(HavenProperties.FontSize, 11d);
        validation.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        card.Add(validation);

        var submit = new HavenButton { Name = "ConfirmRevisionSchedule", Content = "Find free time", Variant = ButtonVariant.Primary };
        submit.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        submit.Invoked += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(title.Text))
                    throw new ArgumentException("Revision title is required.");
                if (!int.TryParse(duration.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationMinutes) || durationMinutes <= 0)
                    throw new FormatException("Duration must be a positive number of minutes.");

                var selectedLesson = lesson.SelectedIndex >= 0 && lesson.SelectedIndex < lessons.Count
                    ? lessons[lesson.SelectedIndex].LessonId
                    : null;
                var request = new StudyRevisionScheduleRequest(
                    subjectId,
                    selectedLesson,
                    collectionId,
                    title.Text.Trim(),
                    string.Empty,
                    ParseRequiredPlannerTime(windowStart.Text, timeZoneId, "Earliest start"),
                    ParseRequiredPlannerTime(windowEnd.Text, timeZoneId, "Latest finish"),
                    durationMinutes,
                    ParseDeadline(deadline.Text, timeZoneId),
                    ReminderAt: null,
                    Priority: PlannerPriority.Medium,
                    TimeZoneId: timeZoneId);

                var scheduled = await _studyPlanner.ScheduleRevisionAsync(request, DateTimeOffset.Now, CancellationToken.None);
                _sidebar.Scene.SetStatus(
                    $"Scheduled {scheduled.Task.Title} for {FormatPlannerTime(scheduled.Task.StartsAt ?? request.WindowStart, scheduled.Task.TimeZoneId)}.");
                await _refresh();
                if (ReferenceEquals(overlay.Parent, root)) root.Remove(overlay);
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                validation.Content = exception.Message;
                validation.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
            }
        };
        card.Add(submit);

        var cancel = new HavenButton { Name = "CancelRevisionSchedule", Content = "Cancel", Variant = ButtonVariant.Ghost };
        cancel.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        cancel.Invoked += (_, _) =>
        {
            if (ReferenceEquals(overlay.Parent, root)) root.Remove(overlay);
        };
        card.Add(cancel);

        overlay.Add(card);
        root.Add(overlay);
    }

    private static Input Field(string name, string accessibleName, string value)
    {
        var input = new Input { Name = name, Text = value, Placeholder = accessibleName };
        input.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        input.Accessibility.AccessibleName = accessibleName;
        return input;
    }

    private static DateTimeOffset ParseRequiredPlannerTime(string value, string timeZoneId, string fieldName)
    {
        var parsed = ParseDeadline(value, timeZoneId);
        return parsed ?? throw new FormatException($"{fieldName} must use yyyy-MM-dd HH:mm.");
    }

    private static DateTimeOffset? ParseDeadline(string value, string timeZoneId)
    {
        var text = value.Trim();
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
            throw new FormatException("Use yyyy-MM-dd HH:mm, or 'none' for no deadline.");

        var zone = ResolveTimeZone(timeZoneId);
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        var offset = zone.GetUtcOffset(local);
        if (zone.IsAmbiguousTime(local)) offset = zone.GetAmbiguousTimeOffsets(local).Max();
        return new DateTimeOffset(local, offset);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new FormatException($"Unknown time zone '{id}'."); }
        catch (InvalidTimeZoneException) { throw new FormatException($"Invalid time zone '{id}'."); }
    }

    private static string FormatPlannerTime(DateTimeOffset value, string? timeZoneId)
    {
        TimeZoneInfo zone;
        try { zone = ResolveTimeZone(timeZoneId ?? "UTC"); }
        catch (FormatException) { zone = TimeZoneInfo.Utc; }
        return TimeZoneInfo.ConvertTime(value, zone).ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture);
    }
}
