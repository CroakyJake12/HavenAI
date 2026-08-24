using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Plan;
using Haven.Infrastructure;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class NativePlanPageTests
{
    [AvaloniaFact]
    public async Task Native_plan_reads_and_completes_canonical_persisted_study_task()
    {
        using var paths = new TestPaths();
        var database = new SqliteDatabase(paths);
        await database.InitializeAsync(CancellationToken.None);
        var planner = new PlannerRepository(database);
        await planner.EnsureDefaultsAsync(CancellationToken.None);
        var containers = new ContainerRepository(database, paths);
        var now = DateTimeOffset.Now;
        var subject = new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, "Maths", null, "A-Level Maths", string.Empty, now, now);
        var lesson = await containers.CreateSubjectAsync(subject, CancellationToken.None);
        var taskId = Guid.NewGuid();
        var tags = PlannerStudyAssignmentTags.Attach("[]", subject.Id, lesson.Id);
        var task = new PlannerTask(
            taskId, PlannerDefaults.CollegeCollectionId, null, "Native revision", "Past paper",
            PlannerPriority.High, PlannerTaskStatus.Planned, tags, 45, now.AddMinutes(2), now.AddMinutes(45),
            null, null, null, 0, now, now, TimeZoneInfo.Local.Id);
        await planner.UpsertTaskAsync(task, CancellationToken.None);

        using var page = new NativePlanPage(planner, containers);
        var window = new Window { Width = 1180, Height = 820, Content = page };
        try
        {
            window.Show();
            await page.RefreshNowAsync();
            window.UpdateLayout();

            var root = page.Scene.Root!;
            var dayItems = GetRuntime(root, "DayItems");
            var countdowns = GetRuntime(root, "Countdowns");
            var row = dayItems.GetItem($"task-{taskId:N}");
            Assert.Equal("Native revision", row.GetComponent<Text>("Title").Content);
            Assert.Contains("Study · Maths", row.GetComponent<Text>("Meta").Content);
            Assert.NotNull(countdowns.Items.FirstOrDefault(item => item.InstanceID == $"countdown-Task-{taskId:N}"));

            var complete = row.GetComponent<Haven.UI.Components.Button>("Complete");
            Click(new HavenInputRouter(root), complete);
            await WaitForAsync(async () => (await planner.GetTaskAsync(taskId, CancellationToken.None))?.Status == PlannerTaskStatus.Completed);

            Assert.Equal(PlannerTaskStatus.Completed, (await planner.GetTaskAsync(taskId, CancellationToken.None))?.Status);
            Assert.True(PlannerStudyAssignmentTags.TryRead((await planner.GetTaskAsync(taskId, CancellationToken.None))!.TagsJson, out var link));
            Assert.Equal(subject.Id, link.SubjectId);
            Assert.Equal(lesson.Id, link.LessonId);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static DynamicUIRuntime GetRuntime(HavenElement root, string name) =>
        root.DescendantsAndSelf().OfType<DynamicUIRuntime>().Single(item => item.Name == name);

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await condition()) return;
            await Task.Delay(50);
        }
        Assert.True(await condition(), "Timed out waiting for persisted planner state to update.");
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-native-plan-tests-" + Guid.NewGuid().ToString("N"));
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
