using System.Globalization;
using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Shell.NativePresentation;

internal sealed record StudyAssignmentLessonOption(Guid? LessonId, string Label);

/// <summary>
/// Adds Study assignment creation to the native Study sidebar while keeping the canonical PlannerTask
/// as the only persisted assignment, deadline, reminder and completion record.
/// </summary>
internal sealed class StudyAssignmentCreationCoordinator
{
    private static readonly PlannerPriority[] Priorities =
    [
        PlannerPriority.None,
        PlannerPriority.Low,
        PlannerPriority.Medium,
        PlannerPriority.High,
        PlannerPriority.Urgent
    ];

    private readonly NativeChatSidebar _sidebar;
    private readonly IContainerRepository _containers;
    private readonly IStudyPlannerService _studyPlanner;
    private readonly Func<Task> _refresh;

    private StudyAssignmentCreationCoordinator(
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

        AddAssignment = new HavenButton
        {
            Name = "AddStudyAssignment",
            Content = "Add assignment",
            IconKey = "plus",
            Variant = ButtonVariant.Primary
        };
        AddAssignment.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        AddAssignment.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        AddAssignment.Accessibility.AccessibleName = "Add homework or assignment to Study and Plan";
        AddAssignment.Invoked += OnAddAssignmentInvoked;
        section.Section.Add(AddAssignment);
    }

    internal HavenButton AddAssignment { get; }

    public static void Attach(
        StudyAssignmentsHavenSection section,
        NativeChatSidebar sidebar,
        IContainerRepository containers,
        IStudyPlannerService studyPlanner,
        Func<Task> refresh) =>
        _ = new StudyAssignmentCreationCoordinator(section, sidebar, containers, studyPlanner, refresh);

    private async void OnAddAssignmentInvoked(object? sender, EventArgs e)
    {
        if (_sidebar.CurrentMode != HavenMode.Study || _sidebar.ActiveGroupId is not Guid subjectId)
        {
            _sidebar.Scene.SetStatus("Select a Study subject before adding an assignment.");
            return;
        }

        try
        {
            var lessonsTask = _containers.GetLessonsAsync(subjectId, CancellationToken.None);
            var assignmentsTask = _studyPlanner.GetAssignmentsAsync(subjectId, includeCompleted: true, CancellationToken.None);
            await Task.WhenAll(lessonsTask, assignmentsTask);

            var lessonOptions = new List<StudyAssignmentLessonOption>
            {
                new(null, "General subject assignment")
            };
            lessonOptions.AddRange(lessonsTask.Result
                .OrderBy(item => item.TopicGroup, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => new StudyAssignmentLessonOption(
                    item.Id,
                    string.IsNullOrWhiteSpace(item.TopicGroup) ? item.Name : $"{item.TopicGroup} · {item.Name}")));

            var collectionId = assignmentsTask.Result.FirstOrDefault()?.Task.CollectionId ?? PlannerDefaults.CollegeCollectionId;
            ShowAssignmentModal(_sidebar.Scene.Root, lessonOptions, collectionId, subjectId, TimeZoneInfo.Local.Id);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            _sidebar.Scene.SetStatus(exception.Message);
        }
    }

    private void ShowAssignmentModal(
        Page root,
        IReadOnlyList<StudyAssignmentLessonOption> lessons,
        Guid collectionId,
        Guid subjectId,
        string timeZoneId)
    {
        var overlay = new HavenContainer { Name = "StudyAssignmentModal", Layout = HavenLayout.Overlay };
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

        card.Add(new HavenText { Content = "Add assignment", Level = TextLevel.H2 });
        var hint = new HavenText
        {
            Content = "This creates the same task used by Plan, including its deadline and reminder.",
            Level = TextLevel.Paragraph
        };
        hint.SetValue(HavenProperties.Foreground, "TextSecondary");
        hint.SetValue(HavenProperties.FontSize, 11d);
        card.Add(hint);

        var lesson = new Select
        {
            Name = "AssignmentLesson",
            Items = lessons.Select(item => item.Label).ToArray(),
            SelectedIndex = 0
        };
        lesson.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        lesson.Accessibility.AccessibleName = "Assignment lesson";
        card.Add(lesson);

        var title = Field("AssignmentTitle", "Assignment title", string.Empty);
        var notes = Field("AssignmentNotes", "Notes (optional)", string.Empty);
        var deadline = Field("AssignmentDeadline", "Deadline (yyyy-MM-dd HH:mm or none)", "none");
        var reminder = Field("AssignmentReminder", "Reminder (yyyy-MM-dd HH:mm or none)", "none");
        var estimate = Field("AssignmentEstimate", "Estimated minutes (or none)", "none");
        var priority = new Select
        {
            Name = "AssignmentPriority",
            Items = Priorities.Select(item => item.ToString()).ToArray(),
            SelectedIndex = Array.IndexOf(Priorities, PlannerPriority.Medium)
        };
        priority.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        priority.Accessibility.AccessibleName = "Assignment priority";
        card.Add(title);
        card.Add(notes);
        card.Add(deadline);
        card.Add(reminder);
        card.Add(estimate);
        card.Add(priority);

        var validation = new HavenText { Name = "AssignmentValidation", Content = string.Empty };
        validation.SetValue(HavenProperties.Foreground, "Danger");
        validation.SetValue(HavenProperties.FontSize, 11d);
        validation.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        card.Add(validation);

        var submit = new HavenButton { Name = "ConfirmStudyAssignment", Content = "Add to Study + Plan", Variant = ButtonVariant.Primary };
        submit.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        submit.Invoked += async (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(title.Text))
                    throw new ArgumentException("Assignment title is required.");

                var selectedLesson = lesson.SelectedIndex >= 0 && lesson.SelectedIndex < lessons.Count
                    ? lessons[lesson.SelectedIndex].LessonId
                    : null;
                var selectedPriority = priority.SelectedIndex >= 0 && priority.SelectedIndex < Priorities.Length
                    ? Priorities[priority.SelectedIndex]
                    : PlannerPriority.None;
                var draft = new StudyPlanAssignmentDraft(
                    subjectId,
                    selectedLesson,
                    collectionId,
                    title.Text.Trim(),
                    notes.Text.Trim(),
                    ParsePlannerTime(deadline.Text, timeZoneId),
                    ParsePlannerTime(reminder.Text, timeZoneId),
                    ParseEstimate(estimate.Text),
                    selectedPriority,
                    StartsAt: null,
                    TimeZoneId: timeZoneId);

                var created = await _studyPlanner.CreateAsync(draft, DateTimeOffset.Now, CancellationToken.None);
                _sidebar.Scene.SetStatus($"Added {created.Task.Title} to Study and Plan.");
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

        var cancel = new HavenButton { Name = "CancelStudyAssignment", Content = "Cancel", Variant = ButtonVariant.Ghost };
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

    private static int? ParseEstimate(string value)
    {
        var text = value.Trim();
        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes <= 0)
            throw new FormatException("Estimated minutes must be a positive number, or 'none'.");
        return minutes;
    }

    private static DateTimeOffset? ParsePlannerTime(string value, string timeZoneId)
    {
        var text = value.Trim();
        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
            throw new FormatException("Use yyyy-MM-dd HH:mm, or 'none'.");

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
}
