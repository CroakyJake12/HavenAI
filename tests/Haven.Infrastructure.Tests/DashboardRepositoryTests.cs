/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/DashboardRepositoryTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns DashboardRepositoryTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents dashboard repository tests and keeps its related state and behavior together.
/// </summary>
public sealed class DashboardRepositoryTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the aggregate snapshot includes agenda recent work and every feature counter step owned by this component.
    /// </summary>
    [Fact]
    public async Task AggregateSnapshotIncludesAgendaRecentWorkAndEveryFeatureCounter()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var conversations = new ConversationRepository(database);
        var containers = new ContainerRepository(database);
        var planner = new PlannerRepository(database);
        var calls = new CallRepository(database);
        var automations = new AutomationRepository(database);
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.FromHours(1));
        var day = new DateTimeOffset(now.Date, now.Offset);

        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Dashboard chat",
            null, null, false, false, now.AddMinutes(-20), now.AddMinutes(-10));
        await conversations.UpsertConversationAsync(conversation, CancellationToken.None);
        await conversations.AddMessageAsync(new(Guid.NewGuid(), conversation.Id, MessageRole.User, "Hello", null, null, null, now), CancellationToken.None);

        foreach (var container in new[]
                 {
                     new ContainerDefinition(Guid.NewGuid(), HavenMode.Chat, "Group", null, "", "", now, now),
                     new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, "Subject", null, "", "", now, now),
                     new ContainerDefinition(Guid.NewGuid(), HavenMode.Studio, "Project", _paths.DataDirectory, "", "", now, now)
                 })
            await containers.UpsertAsync(container, CancellationToken.None);

        await planner.EnsureDefaultsAsync(CancellationToken.None);
        var dueTask = TaskAt("Due today", day.AddHours(17), now);
        var overdueTask = TaskAt("Overdue work", day.AddHours(-2), now);
        var completedTask = TaskAt("Completed", day.AddDays(-1), now);
        await planner.UpsertTaskAsync(dueTask, CancellationToken.None);
        await planner.UpsertTaskAsync(overdueTask, CancellationToken.None);
        await planner.UpsertTaskAsync(completedTask, CancellationToken.None);
        await planner.CompleteTaskAsync(completedTask.Id, now.AddMinutes(-30), CancellationToken.None);
        await planner.UpsertEventAsync(new PlannerEvent(
            Guid.NewGuid(), PlannerDefaults.LocalCalendarId, "Seminar", "", "Room 2", day.AddHours(13), day.AddHours(14),
            false, null, null, false, null, null, now, now, null, "Europe/London"), CancellationToken.None);

        var callConversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Call, "Call",
            null, null, false, false, now.AddMinutes(-8), now.AddMinutes(-6));
        await conversations.UpsertConversationAsync(callConversation, CancellationToken.None);
        await calls.UpsertAsync(new CallSession(Guid.NewGuid(), callConversation.Id, "qwen", null, null, null,
            CallInputMode.HandsFree, false, CallSessionStatus.Completed, now.AddMinutes(-8), now.AddMinutes(-6)), CancellationToken.None);
        await automations.UpsertAsync(new AutomationDefinition(Guid.NewGuid(), "Daily brief", HavenMode.Tasks, "Brief me",
            AutomationScheduleKind.Daily, "{}", now.AddDays(1), null, true, now, now), CancellationToken.None);

        var snapshot = await new DashboardRepository(database).GetSnapshotAsync(now, CancellationToken.None);

        Assert.Equal(2, snapshot.ConversationsToday);
        Assert.Equal(1, snapshot.MessagesThisWeek);
        Assert.Equal(1, snapshot.ActiveProjects);
        Assert.Equal(1, snapshot.ChatGroups);
        Assert.Equal(1, snapshot.StudySubjects);
        Assert.Equal(1, snapshot.TasksDueToday);
        Assert.Equal(1, snapshot.OverdueTasks);
        Assert.Equal(1, snapshot.TasksCompletedThisWeek);
        Assert.Equal(1, snapshot.UpcomingEvents);
        Assert.Equal(1, snapshot.EnabledAutomations);
        Assert.Equal(1, snapshot.CallsThisWeek);
        Assert.InRange(snapshot.CallDurationThisWeek.TotalSeconds, 119, 121);
        Assert.Contains(snapshot.Agenda, item => item.Title == "Overdue work" && item.IsOverdue);
        Assert.Contains(snapshot.Agenda, item => item.Title == "Seminar");
        Assert.Contains(snapshot.RecentWork, item => item.Kind == "group" && item.Title == "Group");
        Assert.Contains(snapshot.RecentWork, item => item.Kind == "call");
    }

    /// <summary>
    /// Performs the versioned layout normalizes order visibility size and duplicate keys step owned by this component.
    /// </summary>
    [Fact]
    public async Task VersionedLayoutNormalizesOrderVisibilitySizeAndDuplicateKeys()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var layouts = new DashboardLayoutRepository(database);

        await layouts.SaveAsync(
        [
            new(1, "plan", 9, false, DashboardTileSize.Wide),
            new(1, "chat", 2, true, DashboardTileSize.Compact),
            new(1, "plan", 3, true, DashboardTileSize.Standard),
            new(2, "ignored", 0, true, DashboardTileSize.Wide)
        ], CancellationToken.None);

        var loaded = await layouts.GetAsync(CancellationToken.None);
        Assert.Equal(2, loaded.Count);
        Assert.Collection(loaded,
            item =>
            {
                Assert.Equal("plan", item.Key);
                Assert.Equal(0, item.Order);
                Assert.True(item.IsVisible);
                Assert.Equal(DashboardTileSize.Standard, item.Size);
            },
            item =>
            {
                Assert.Equal("chat", item.Key);
                Assert.Equal(1, item.Order);
                Assert.Equal(DashboardTileSize.Compact, item.Size);
            });
    }

    /// <summary>
    /// Performs the latest migration creates scoped dashboard call planner and sync storage step owned by this component.
    /// </summary>
    [Fact]
    public async Task LatestMigrationCreatesScopedDashboardCallPlannerAndSyncStorage()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        await using var connection = await database.OpenAsync(CancellationToken.None);

        await using var version = connection.CreateCommand();
        version.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        Assert.Equal((long)Migrations.LatestVersion, (long)(await version.ExecuteScalarAsync(CancellationToken.None))!);

        await using var objects = connection.CreateCommand();
        objects.CommandText = "SELECT name FROM sqlite_master WHERE name IN ('ix_conversations_scope_updated','container_resources','call_sessions','planner_tasks','planner_events','calendar_sync_state','calendar_outbox','calendar_conflicts','conversation_branches','message_versions','message_attachments','conversation_drafts','shared_sessions') ORDER BY name;";
        var names = new List<string>();
        await using var reader = await objects.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None)) names.Add(reader.GetString(0));
        Assert.Equal(13, names.Count);
    }

    /// <summary>
    /// Performs the task at step owned by this component.
    /// </summary>
    private static PlannerTask TaskAt(string title, DateTimeOffset dueAt, DateTimeOffset now) => new(
        Guid.NewGuid(), PlannerDefaults.PersonalCollectionId, null, title, "", PlannerPriority.Medium,
        PlannerTaskStatus.Planned, "[]", 30, null, dueAt, null, null, null, 0, now, now, "Europe/London");

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-dashboard-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
        }

        /// <summary>
        /// Gets or updates data directory, the bindable or domain state represented by this property.
        /// </summary>
        public string DataDirectory { get; }
        /// <summary>
        /// Gets or updates database path, the bindable or domain state represented by this property.
        /// </summary>
        public string DatabasePath { get; }
        /// <summary>
        /// Gets or updates browser profile directory, the bindable or domain state represented by this property.
        /// </summary>
        public string BrowserProfileDirectory { get; }
        /// <summary>
        /// Gets or updates attachments directory, the bindable or domain state represented by this property.
        /// </summary>
        public string AttachmentsDirectory { get; }
        /// <summary>
        /// Gets or updates logs directory, the bindable or domain state represented by this property.
        /// </summary>
        public string LogsDirectory { get; }
        /// <summary>
        /// Gets or updates legacy state path, the bindable or domain state represented by this property.
        /// </summary>
        public string LegacyStatePath { get; }
        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
