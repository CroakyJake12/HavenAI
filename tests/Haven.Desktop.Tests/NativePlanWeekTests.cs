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
    public async Task Full_planner_renders_canonical_week_and_completes_cross_midnight_study_task()
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

            Click(root, root.DescendantsAndSelf().OfType<Button>().Single(item => item.Name == "FullPlanner"));
            await WaitForAsync(() =>
            {
                window.UpdateLayout();
                return weekRoot.GetValue(HavenProperties.Visibility) == HavenVisibility.Visible
                    && root.DescendantsAndSelf().Any(item => item.Name == $"PlanWeekDay-{mondayDate:yyyyMMdd}");
            });

            Assert.Equal(HavenVisibility.Collapsed, todayViewport.GetValue(HavenProperties.Visibility));
            var mondayRow = root.DescendantsAndSelf().Single(item => item.Name == $"PlanWeekItem-Task-{taskId:N}-{mondayDate:yyyyMMdd}");
            var tuesday = mondayDate.AddDays(1);
            var tuesdayRow = root.DescendantsAndSelf().Single(item => item.Name == $"PlanWeekItem-Task-{taskId:N}-{tuesday:yyyyMMdd}");
            Assert.Contains("Cross-midnight revision", mondayRow.DescendantsAndSelf().OfType<Text>().Select(text => text.Content));
            Assert.Contains("Cross-midnight revision", tuesdayRow.DescendantsAndSelf().OfType<Text>().Select(text => text.Content));

            var studyButton = mondayRow.DescendantsAndSelf().OfType<Button>().Single(item => item.Name == "Study");
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
