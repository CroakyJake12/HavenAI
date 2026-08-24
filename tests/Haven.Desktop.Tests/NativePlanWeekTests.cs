using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Plan;
using Haven.Infrastructure;
using Haven.UI;
using Haven.UI.Components;
using Button = Haven.UI.Components.Button;

namespace Haven.Desktop.Tests;

public sealed class NativePlanWeekTests
{
    [AvaloniaFact]
    public async Task Week_renders_temporal_grid_and_completes_cross_midnight_study_task()
    {
        using var paths = new TestPaths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(CancellationToken.None);
        var planner = new PlannerRepository(database);
        await planner.EnsureDefaultsAsync(CancellationToken.None);
        var containers = new ContainerRepository(database, paths);
        var zone = TimeZoneInfo.Local;
        var now = DateTimeOffset.Now;
        var localDate = TimeZoneInfo.ConvertTime(now, zone).Date;
        var mondayDate = localDate.AddDays(-(((int)localDate.DayOfWeek + 6) % 7));
        var mondayNoon = InZone(mondayDate.AddHours(12), zone);
        var (mondayStart, _) = PlannerDayTimeline.GetDayBounds(mondayNoon, zone.Id);

        var subject = new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, "Maths", null, "A-Level Maths", string.Empty, now, now);
        var lesson = await containers.CreateSubjectAsync(subject, CancellationToken.None);
        var taskId = Guid.NewGuid();
        var startsAt = mondayStart.AddHours(23).AddMinutes(30);
        var task = new PlannerTask(
            taskId,
            PlannerDefaults.CollegeCollectionId,
            null,
            "Cross-midnight revision",
            "Integration proof",
            PlannerPriority.High,
            PlannerTaskStatus.Planned,
            PlannerStudyAssignmentTags.Attach("[]", subject.Id, lesson.Id),
            120,
            startsAt,
            startsAt.AddHours(2),
            null,
            null,
            null,
            0,
            now,
            now,
            zone.Id);
        await planner.UpsertTaskAsync(task, CancellationToken.None);

        using var page = new NativePlanPage(planner, containers);
        var window = new Window { Width = 1280, Height = 860, Content = page };
        PlannerStudyLink? openedStudy = null;
        page.StudyRequested += (_, link) => openedStudy = link;
        try
        {
            window.Show();
            await page.RefreshNowAsync();
            window.UpdateLayout();

            var root = page.Scene.Root!;
            var todayViewport = root.DescendantsAndSelf().Single(item => item.Name == "Viewport");
            var weekRoot = root.DescendantsAndSelf().Single(item => item.Name == "PlanWeekRoot");
            Assert.Equal(HavenVisibility.Visible, todayViewport.GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Collapsed, weekRoot.GetValue(HavenProperties.Visibility));

            Click(root, root.DescendantsAndSelf().OfType<Button>().Single(item => item.Name == "Week"));
            await WaitForAsync(() =>
            {
                window.UpdateLayout();
                return weekRoot.GetValue(HavenProperties.Visibility) == HavenVisibility.Visible
                    && root.DescendantsAndSelf().Any(item => item.Name == "PlanWeekTemporalGrid")
                    && root.DescendantsAndSelf().Any(item => item.Name == $"PlanWeekTimed-Task-{taskId:N}-{mondayDate:yyyyMMdd}");
            });

            Assert.Equal(HavenVisibility.Collapsed, todayViewport.GetValue(HavenProperties.Visibility));
            var mondayRow = root.DescendantsAndSelf().Single(item => item.Name == $"PlanWeekTimed-Task-{taskId:N}-{mondayDate:yyyyMMdd}");
            var tuesday = mondayDate.AddDays(1);
            var tuesdayRow = root.DescendantsAndSelf().Single(item => item.Name == $"PlanWeekTimed-Task-{taskId:N}-{tuesday:yyyyMMdd}");
            Assert.Contains("Cross-midnight revision", mondayRow.DescendantsAndSelf().OfType<Text>().Select(text => text.Content));
            Assert.Contains("Cross-midnight revision", tuesdayRow.DescendantsAndSelf().OfType<Text>().Select(text => text.Content));

            var studyButton = new[] { mondayRow, tuesdayRow }.SelectMany(row => row.DescendantsAndSelf().OfType<Button>()).Single(item => item.Name == "Study");
            Click(root, studyButton);
            Assert.NotNull(openedStudy);
            Assert.Equal(subject.Id, openedStudy!.SubjectId);
            Assert.Equal(lesson.Id, openedStudy.LessonId);

            var completeButton = tuesdayRow.DescendantsAndSelf().OfType<Button>().Single(item => item.Name == "Complete");
            Click(root, completeButton);
            await WaitForAsync(async () => (await planner.GetTaskAsync(taskId, CancellationToken.None))?.Status == PlannerTaskStatus.Completed);
            Assert.Equal(PlannerTaskStatus.Completed, (await planner.GetTaskAsync(taskId, CancellationToken.None))?.Status);

            var range = root.DescendantsAndSelf().OfType<Text>().Single(item => item.Name == "PlanWeekRange");
            var thisWeekRange = range.Content;
            var nextButton = root.DescendantsAndSelf().OfType<Button>().Single(item => item.Name == "PlanWeekNext");
            await WaitForAsync(() => nextButton.GetValue(HavenProperties.Enabled));
            Click(root, nextButton);
            await WaitForAsync(() =>
            {
                window.UpdateLayout();
                return range.Content != thisWeekRange;
            });
            Click(root, root.DescendantsAndSelf().OfType<Button>().Single(item => item.Name == "PlanWeekThisWeek"));
            await WaitForAsync(() =>
            {
                window.UpdateLayout();
                return range.Content == thisWeekRange;
            });
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Native_authoring_creates_task_with_structured_controls_and_selected_list()
    {
        using var paths = new TestPaths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(CancellationToken.None);
        var planner = new PlannerRepository(database);
        await planner.EnsureDefaultsAsync(CancellationToken.None);
        var collections = await planner.GetCollectionsAsync(false, CancellationToken.None);
        var targetCollection = collections.OrderBy(x => x.SortOrder).First(x => x.Id != PlannerDefaults.PersonalCollectionId);
        var containers = new ContainerRepository(database, paths);
        using var page = new NativePlanPage(planner, containers);
        var window = new Window { Width = 1280, Height = 860, Content = page };
        try
        {
            window.Show();
            await page.RefreshNowAsync();
            window.UpdateLayout();
            var root = page.Scene.Root!;
            Click(root, root.DescendantsAndSelf().OfType<Button>().Single(x => x.Name == "NewTask"));
            var editor = root.DescendantsAndSelf().Single(x => x.Name == "PlanEditorRoot");
            await WaitForAsync(() =>
            {
                window.UpdateLayout();
                return editor.GetValue(HavenProperties.Visibility) == HavenVisibility.Visible;
            });
            Assert.Contains(root.DescendantsAndSelf().OfType<Text>(), x => x.Name == "PlanEditorDate" && !string.IsNullOrWhiteSpace(x.Content));
            var destination = root.DescendantsAndSelf().OfType<Text>().Single(x => x.Name == "PlanEditorDestination");
            var destinationButton = root.DescendantsAndSelf().OfType<Button>().Single(x => x.Name == "PlanEditorDestinationButton");
            for (var attempt = 0; attempt < collections.Count && !destination.Content.Contains(targetCollection.Name, StringComparison.Ordinal); attempt++)
                Click(root, destinationButton);
            Assert.Contains(targetCollection.Name, destination.Content, StringComparison.Ordinal);
            root.DescendantsAndSelf().OfType<Input>().Single(x => x.Name == "PlanEditorTitle").Text = "Native authoring task";
            Click(root, root.DescendantsAndSelf().OfType<Button>().Single(x => x.Name == "PlanEditorTomorrow"));
            Click(root, root.DescendantsAndSelf().OfType<Button>().Single(x => x.Name == "PlanEditorRecurrenceButton"));
            Click(root, root.DescendantsAndSelf().OfType<Button>().Single(x => x.Name == "PlanEditorPriorityButton"));
            Click(root, root.DescendantsAndSelf().OfType<Button>().Single(x => x.Name == "PlanEditorSave"));
            await WaitForAsync(async () => (await planner.GetTasksAsync(new PlannerTaskQuery(Search: "Native authoring task"), CancellationToken.None)).Count == 1);
            var task = Assert.Single(await planner.GetTasksAsync(new PlannerTaskQuery(Search: "Native authoring task"), CancellationToken.None));
            Assert.Equal(targetCollection.Id, task.CollectionId);
            Assert.Equal(PlannerTaskStatus.Planned, task.Status);
            Assert.Equal(PlannerPriority.Low, task.Priority);
            Assert.Equal("FREQ=DAILY", task.RecurrenceRule);
            Assert.NotNull(task.DueAt);
        }
        finally { window.Content = null; window.Close(); }
    }
    [AvaloniaFact]
    public async Task Native_authoring_creates_all_day_event_in_selected_writable_calendar()
    {
        using var paths = new TestPaths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(CancellationToken.None);
        var planner = new PlannerRepository(database);
        await planner.EnsureDefaultsAsync(CancellationToken.None);
        var studyCalendar = new PlannerCalendar(Guid.NewGuid(), null, CalendarProviderKind.Local, "study-secondary", "Study calendar", "#6C8CFF", CalendarPermission.Owner, true, DateTimeOffset.UtcNow);
        await planner.UpsertCalendarAsync(studyCalendar, CancellationToken.None);
        var containers = new ContainerRepository(database, paths);
        using var page = new NativePlanPage(planner, containers);
        var window = new Window { Width = 1280, Height = 860, Content = page };
        try
        {
            window.Show(); await page.RefreshNowAsync(); window.UpdateLayout();
            var root = page.Scene.Root!;
            Click(root, root.DescendantsAndSelf().OfType<Button>().Single(x => x.Name == "NewEvent"));
            var editor = root.DescendantsAndSelf().Single(x => x.Name == "PlanEditorRoot");
            await WaitForAsync(() =>
            {
                window.UpdateLayout();
                return editor.GetValue(HavenProperties.Visibility) == HavenVisibility.Visible;
            });
            var destination = root.DescendantsAndSelf().OfType<Text>().Single(x => x.Name == "PlanEditorDestination");
            var destinationButton = root.DescendantsAndSelf().OfType<Button>().Single(x => x.Name == "PlanEditorDestinationButton");
            for (var attempt = 0; attempt < 4 && !destination.Content.Contains(studyCalendar.Name, StringComparison.Ordinal); attempt++)
                Click(root, destinationButton);
            Assert.Contains(studyCalendar.Name, destination.Content, StringComparison.Ordinal);
            root.DescendantsAndSelf().OfType<Input>().Single(x => x.Name == "PlanEditorTitle").Text = "Native all-day event";
            root.DescendantsAndSelf().OfType<Input>().Single(x => x.Name == "PlanEditorLocation").Text = "Library";
            Click(root, root.DescendantsAndSelf().OfType<Button>().Single(x => x.Name == "PlanEditorAllDay"));
            Click(root, root.DescendantsAndSelf().OfType<Button>().Single(x => x.Name == "PlanEditorSave"));
            var rangeStart = DateTimeOffset.Now.AddDays(-1); var rangeEnd = DateTimeOffset.Now.AddDays(2);
            await WaitForAsync(async () => (await planner.GetEventsAsync(rangeStart, rangeEnd, null, CancellationToken.None)).Any(x => x.Title == "Native all-day event"));
            var item = Assert.Single(await planner.GetEventsAsync(rangeStart, rangeEnd, null, CancellationToken.None), x => x.Title == "Native all-day event");
            Assert.Equal(studyCalendar.Id, item.CalendarId);
            Assert.True(item.IsAllDay);
            Assert.Equal("Library", item.Location);
            Assert.Equal(TimeSpan.FromDays(1), item.EndsAt - item.StartsAt);
        }
        finally { window.Content = null; window.Close(); }
    }
    [AvaloniaFact]
    public async Task Native_authoring_requires_confirmation_before_deleting_event()
    {
        using var paths = new TestPaths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(CancellationToken.None);
        var planner = new PlannerRepository(database);
        await planner.EnsureDefaultsAsync(CancellationToken.None);
        var zone = TimeZoneInfo.Local;
        var startsAt = InZone(DateTime.Today.AddHours(11), zone);
        var eventId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await planner.UpsertEventAsync(new PlannerEvent(
            eventId, PlannerDefaults.LocalCalendarId, "Delete confirmation event", string.Empty, string.Empty, startsAt, startsAt.AddHours(1), false, null, null, false, null, null, now, now, null, zone.Id), CancellationToken.None);
        using var scene = new PlanHavenScene();
        using var authoring = new PlanAuthoringCoordinator(scene, planner, _ => Task.CompletedTask);
        var window = new Window { Width = 1280, Height = 860, Content = new HavenSceneControl { Root = scene.Root } };
        try
        {
            window.Show(); window.UpdateLayout();
            await authoring.OpenItemAsync(new PlanItemEditRequest(eventId, PlannerDayItemKind.Event));
            window.UpdateLayout();
            var root = scene.Root;
            var editor = root.DescendantsAndSelf().Single(x => x.Name == "PlanEditorRoot");
            Assert.Equal(HavenVisibility.Visible, editor.GetValue(HavenProperties.Visibility));
            var delete = root.DescendantsAndSelf().OfType<Button>().Single(x => x.Name == "PlanEditorDelete");
            Click(root, delete);
            Assert.NotNull(await planner.GetEventAsync(eventId, CancellationToken.None));
            Assert.Equal("Confirm delete event", delete.Content);
            var status = root.DescendantsAndSelf().OfType<Text>().Single(x => x.Name == "PlanEditorStatus");
            Assert.Contains("Delete is permanent", status.Content, StringComparison.Ordinal);
            window.UpdateLayout();
            Click(root, delete);
            await WaitForAsync(async () => (await planner.GetEventAsync(eventId, CancellationToken.None))?.DeletedAt is not null);
            var visible = await planner.GetEventsAsync(startsAt.AddHours(-1), startsAt.AddHours(2), null, CancellationToken.None);
            Assert.DoesNotContain(visible, x => x.Id == eventId);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Month_renders_six_week_grid_explicit_overflow_and_drills_to_week()
    {
        using var paths = new TestPaths();
        var database = new SqliteDatabase(paths); await database.InitializeAsync(CancellationToken.None);
        var planner = new PlannerRepository(database); await planner.EnsureDefaultsAsync(CancellationToken.None);
        var containers = new ContainerRepository(database, paths);
        var zone = TimeZoneInfo.Local; var localDate = DateTime.Today; var dayStart = InZone(localDate.AddHours(9), zone); var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            var start = dayStart.AddMinutes(i * 30);
            await planner.UpsertEventAsync(new PlannerEvent(Guid.NewGuid(), PlannerDefaults.LocalCalendarId, $"Dense event {i}", string.Empty, string.Empty, start, start.AddMinutes(25), false, null, null, false, null, null, now, now, null, zone.Id), CancellationToken.None);
        }
        using var page = new NativePlanPage(planner, containers); var window = new Window { Width = 1280, Height = 900, Content = page };        try
        {
            window.Show(); await page.RefreshNowAsync(); window.UpdateLayout(); var root = page.Scene.Root!;
            Click(root, root.DescendantsAndSelf().OfType<Button>().Single(x => x.Name == "Month"));
            await WaitForAsync(() => root.DescendantsAndSelf().Any(x => x.Name == "PlanMonthGrid")); window.UpdateLayout();
            var grid = Assert.IsType<Container>(root.DescendantsAndSelf().Single(x => x.Name == "PlanMonthGrid"));
            Assert.Equal(7, grid.ColumnTracks.Count); Assert.Equal(7, grid.RowTracks.Count);
            var moreName = $"PlanMonthMore-{localDate:yyyyMMdd}"; var more = root.DescendantsAndSelf().OfType<Button>().Single(x => x.Name == moreName);
            Assert.Equal("+2 more", more.Content); Click(root, more);
            await WaitForAsync(() => root.DescendantsAndSelf().Any(x => x.Name == "PlanWeekTemporalGrid"));
            Assert.Equal(HavenVisibility.Collapsed, root.DescendantsAndSelf().Single(x => x.Name == "PlanMonthRoot").GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Visible, root.DescendantsAndSelf().Single(x => x.Name == "PlanWeekRoot").GetValue(HavenProperties.Visibility));
        }
        finally { window.Content = null; window.Close(); }
    }
    private static DateTimeOffset InZone(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        var offset = zone.GetUtcOffset(local);
        if (zone.IsAmbiguousTime(local)) offset = zone.GetAmbiguousTimeOffsets(local).Max();
        return new DateTimeOffset(local, offset);
    }

    private static void Click(HavenElement root, HavenElement element)
    {
        var router = new HavenInputRouter(root);
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        Assert.True(condition(), "Timed out waiting for native Plan week UI to update.");
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        Assert.True(await condition(), "Timed out waiting for canonical planner state to update.");
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-native-plan-week-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (IOException) { }
        }
    }
}
