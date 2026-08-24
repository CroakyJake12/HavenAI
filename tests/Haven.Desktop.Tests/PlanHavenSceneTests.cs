using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Plan;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class PlanHavenSceneTests
{
    [Fact]
    public void Plan_scene_projects_canonical_day_free_windows_countdowns_and_study_link()
    {
        using var scene = new PlanHavenScene();
        var dayStart = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero);
        var taskId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var task = new PlannerDayItem(taskId, PlannerDayItemKind.Task, "Maths past paper", dayStart.AddHours(10), dayStart.AddHours(11), dayStart.AddHours(12), false, false, false, false, PlannerDefaults.CollegeCollectionId, null);
        var calendarEvent = new PlannerDayItem(eventId, PlannerDayItemKind.Event, "College", dayStart.AddHours(13), dayStart.AddHours(14), null, false, false, false, true, null, PlannerDefaults.LocalCalendarId);
        var snapshot = new PlannerDaySnapshot(dayStart, dayStart.AddDays(1), dayStart.AddHours(10.5), [task, calendarEvent], [task], task, calendarEvent, task.StartsAt, calendarEvent.EndsAt, .25);
        var link = new PlannerStudyLink(subjectId, lessonId);
        scene.SetDay(snapshot, TimeZoneInfo.Utc, new Dictionary<Guid, PlannerStudyLink> { [taskId] = link }, new Dictionary<Guid, string> { [subjectId] = "Maths" });
        scene.SetFreeWindows([new PlannerFreeWindow(dayStart.AddHours(11), dayStart.AddHours(13))], TimeZoneInfo.Utc);
        scene.SetCountdowns([new PlannerCountdown(taskId, PlannerCountdownSourceKind.Task, "Maths past paper", dayStart.AddHours(12), dayStart.AddHours(10.5), PlannerCountdownState.Upcoming, null, PlannerDefaults.CollegeCollectionId, null, false)], TimeZoneInfo.Utc);
        Assert.Equal(2, scene.DayItems.Items.Count);
        var taskRow = scene.DayItems.GetItem($"task-{taskId:N}");
        Assert.Equal("Maths past paper", taskRow.GetComponent<Text>("Title").Content);
        Assert.Contains("Study · Maths", taskRow.GetComponent<Text>("Meta").Content);
        Assert.Equal(HavenVisibility.Visible, taskRow.GetComponent<Haven.UI.Components.Button>("Study").GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Visible, taskRow.GetComponent<Haven.UI.Components.Button>("Complete").GetValue(HavenProperties.Visibility));
        var eventRow = scene.DayItems.GetItem($"event-{eventId:N}");
        Assert.Equal(HavenVisibility.Collapsed, eventRow.GetComponent<Haven.UI.Components.Button>("Complete").GetValue(HavenProperties.Visibility));
        Assert.Single(scene.FreeWindows.Items);
        Assert.Single(scene.Countdowns.Items);
        Assert.Equal(25d, scene.Progress.Value, 3);
        Assert.Equal("Maths past paper", scene.CurrentTitle.Content);
        Assert.Equal("College", scene.NextTitle.Content);
    }

    [AvaloniaFact]
    public void Plan_scene_buttons_emit_semantic_intent_and_layout_stays_inside_host()
    {
        using var scene = new PlanHavenScene();
        var dayStart = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero);
        var taskId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var task = new PlannerDayItem(taskId, PlannerDayItemKind.Task, "Revision", dayStart.AddHours(10), dayStart.AddHours(11), dayStart.AddHours(12), false, false, false, false, PlannerDefaults.CollegeCollectionId, null);
        scene.SetDay(new PlannerDaySnapshot(dayStart, dayStart.AddDays(1), dayStart.AddHours(9), [task], [], null, task, task.StartsAt, task.EndsAt, 0), TimeZoneInfo.Utc, new Dictionary<Guid, PlannerStudyLink> { [taskId] = new(subjectId, lessonId) }, new Dictionary<Guid, string> { [subjectId] = "Maths" });
        var refreshes = 0;
        var month = 0;
        Guid? completed = null;
        PlannerStudyLink? study = null;
        scene.RefreshRequested += (_, _) => refreshes++;
        scene.MonthRequested += (_, _) => month++;
        scene.CompleteTaskRequested += (_, id) => completed = id;
        scene.StudyRequested += (_, link) => study = link;
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1180, Height = 820, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            var router = new HavenInputRouter(scene.Root);
            Click(router, scene.RefreshButton);
            Click(router, scene.MonthButton);
            var row = scene.DayItems.GetItem($"task-{taskId:N}");
            Click(router, row.GetComponent<Haven.UI.Components.Button>("Complete"));
            Click(router, row.GetComponent<Haven.UI.Components.Button>("Study"));
            Assert.Equal(1, refreshes);
            Assert.Equal(1, month);
            Assert.Equal(taskId, completed);
            Assert.NotNull(study);
            Assert.Equal(subjectId, study!.SubjectId);
            Assert.Equal(lessonId, study.LessonId);
            Assert.True(scene.Root.Bounds.Bottom <= host.SurfaceMetrics.Viewport.Height + 0.5);
            Assert.True(scene.Root.Bounds.Right <= host.SurfaceMetrics.Viewport.Width + 0.5);
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
}
