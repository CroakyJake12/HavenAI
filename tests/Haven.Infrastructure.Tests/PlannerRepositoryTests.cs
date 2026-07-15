using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class PlannerRepositoryTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task DefaultsTasksHierarchyAndEventsRoundTrip()
    {
        var (database, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var collections = await repository.GetCollectionsAsync(false, CancellationToken.None);
        Assert.Equal(["Personal", "College", "Work"], collections.Select(item => item.Name));

        var now = DateTimeOffset.UtcNow;
        var parent = NewTask(PlannerDefaults.CollegeCollectionId, "Write dissertation", now);
        var child = NewTask(PlannerDefaults.CollegeCollectionId, "Draft introduction", now) with { ParentTaskId = parent.Id };
        await repository.UpsertTaskAsync(parent, CancellationToken.None);
        await repository.UpsertTaskAsync(child, CancellationToken.None);
        var tasks = await repository.GetTasksAsync(new PlannerTaskQuery(PlannerDefaults.CollegeCollectionId), CancellationToken.None);
        Assert.Equal(2, tasks.Count);
        Assert.Equal(parent.Id, tasks.Single(item => item.Id == child.Id).ParentTaskId);

        var plannerEvent = new PlannerEvent(Guid.NewGuid(), PlannerDefaults.LocalCalendarId, "Tutorial", "Bring notes", "Room 2",
            now.AddHours(1), now.AddHours(2), false, null, now.AddMinutes(30), false, null, null, now, now);
        await repository.UpsertEventAsync(plannerEvent, CancellationToken.None);
        var events = await repository.GetEventsAsync(now, now.AddDays(1), null, CancellationToken.None);
        Assert.Single(events);
        Assert.Equal("Tutorial", events[0].Title);

        _ = database;
    }

    [Fact]
    public async Task RejectsCyclesAndReadOnlyEventEdits()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var parent = NewTask(PlannerDefaults.PersonalCollectionId, "Parent", now);
        var child = NewTask(PlannerDefaults.PersonalCollectionId, "Child", now) with { ParentTaskId = parent.Id };
        await repository.UpsertTaskAsync(parent, CancellationToken.None);
        await repository.UpsertTaskAsync(child, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpsertTaskAsync(parent with { ParentTaskId = child.Id }, CancellationToken.None));

        var readOnly = new PlannerEvent(Guid.NewGuid(), PlannerDefaults.LocalCalendarId, "Imported meeting", string.Empty, string.Empty,
            now, now.AddHours(1), false, null, null, true, "remote-1", "etag", now, now);
        await InsertProviderEventAsync(readOnly);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpsertEventAsync(readOnly with { Title = "Changed" }, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DeleteEventAsync(readOnly.Id, now, CancellationToken.None));

        var accountId = Guid.NewGuid();
        await repository.UpsertCalendarAccountAsync(new CalendarAccount(accountId, CalendarProviderKind.Google, "College", "student@example.test",
            CalendarSyncStatus.Ready, null, now, now, now), CancellationToken.None);
        var calendar = new PlannerCalendar(Guid.NewGuid(), accountId, CalendarProviderKind.Google, "remote-calendar", "Shared timetable",
            "#5588EE", CalendarPermission.Reader, true, now);
        await repository.UpsertCalendarAsync(calendar, CancellationToken.None);
        var newRemoteEvent = new PlannerEvent(Guid.NewGuid(), calendar.Id, "Cannot write", string.Empty, string.Empty,
            now.AddDays(1), now.AddDays(1).AddHours(1), false, null, null, false, null, null, now, now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpsertEventAsync(newRemoteEvent, CancellationToken.None));
    }

    [Fact]
    public async Task CompletingRecurringTaskAdvancesItAndRecordsHistory()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var due = new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.FromHours(1));
        var task = NewTask(PlannerDefaults.PersonalCollectionId, "Daily review", due) with
        {
            DueAt = due,
            RecurrenceRule = "FREQ=DAILY;INTERVAL=1",
            TimeZoneId = TimeZoneInfo.Local.Id
        };
        await repository.UpsertTaskAsync(task, CancellationToken.None);
        await repository.CompleteTaskAsync(task.Id, due.AddHours(1), CancellationToken.None);
        var loaded = await repository.GetTaskAsync(task.Id, CancellationToken.None);
        var history = await repository.GetCompletionHistoryAsync(task.Id, CancellationToken.None);
        Assert.Equal(PlannerTaskStatus.Planned, loaded?.Status);
        Assert.NotNull(loaded?.DueAt);
        Assert.True(loaded!.DueAt > due);
        Assert.Single(history);
        Assert.Equal(due.ToUniversalTime(), history[0].OccurrenceDueAt?.ToUniversalTime());
    }

    [Fact]
    public async Task RecurringEventAppearsInLaterCalendarRange()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var first = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var now = DateTimeOffset.UtcNow;
        var item = new PlannerEvent(Guid.NewGuid(), PlannerDefaults.LocalCalendarId, "Weekly seminar", string.Empty, string.Empty,
            first, first.AddHours(1), false, "FREQ=WEEKLY", null, false, null, null, now, now);
        await repository.UpsertEventAsync(item, CancellationToken.None);
        var events = await repository.GetEventsAsync(first.AddDays(13), first.AddDays(15), null, CancellationToken.None);
        var occurrence = Assert.Single(events);
        Assert.Equal(first.AddDays(14), occurrence.StartsAt);
    }

    [Fact]
    public async Task DueReminderIsReturnedOnlyUntilMarkedDelivered()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var task = NewTask(PlannerDefaults.WorkCollectionId, "Submit report", now) with { DueAt = now.AddHours(1), ReminderAt = now.AddMinutes(-1) };
        await repository.UpsertTaskAsync(task, CancellationToken.None);
        var reminder = Assert.Single(await repository.GetDueRemindersAsync(now, 20, CancellationToken.None));
        Assert.Equal(task.Id, reminder.EntityId);
        await repository.MarkReminderDeliveredAsync(reminder, now, CancellationToken.None);
        Assert.Empty(await repository.GetDueRemindersAsync(now, 20, CancellationToken.None));
    }

    [Fact]
    public async Task RemoteCalendarWritesQueueOutboxAndSyncStateRoundTrips()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var account = new CalendarAccount(Guid.NewGuid(), CalendarProviderKind.Google, "Work", "worker@example.test", CalendarSyncStatus.Ready, null, now, now, now);
        await repository.UpsertCalendarAccountAsync(account, CancellationToken.None);
        var calendar = new PlannerCalendar(Guid.NewGuid(), account.Id, CalendarProviderKind.Google, "work-calendar", "Work", "#4285F4", CalendarPermission.Writer, true, now);
        await repository.UpsertCalendarAsync(calendar, CancellationToken.None);
        var item = new PlannerEvent(Guid.NewGuid(), calendar.Id, "Publish update", string.Empty, string.Empty, now.AddDays(1), now.AddDays(1).AddHours(1), false, null, null, false, null, null, now, now);
        await repository.UpsertEventAsync(item, CancellationToken.None);
        var outbox = Assert.Single(await repository.GetDueOutboxAsync(account.Id, now.AddMinutes(1), 20, CancellationToken.None));
        Assert.Equal("create", outbox.Operation);
        await repository.CompleteOutboxAsync(outbox.Id, CancellationToken.None);
        Assert.Empty(await repository.GetDueOutboxAsync(account.Id, now.AddMinutes(1), 20, CancellationToken.None));

        var cursor = new CalendarSyncCursor(account.Id, calendar.Id, "google-token", null, now.AddMonths(-1), now.AddMonths(6), now);
        await repository.UpsertSyncCursorAsync(cursor, CancellationToken.None);
        Assert.Equal("google-token", (await repository.GetSyncCursorAsync(account.Id, calendar.Id, CancellationToken.None))?.SyncCursor);
    }

    [Fact]
    public async Task ProviderIngestionDoesNotEchoBackIntoOutbox()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var account = new CalendarAccount(Guid.NewGuid(), CalendarProviderKind.Microsoft, "College", "student@example.test", CalendarSyncStatus.Ready, null, now, now, now);
        await repository.UpsertCalendarAccountAsync(account, CancellationToken.None);
        var calendar = new PlannerCalendar(Guid.NewGuid(), account.Id, CalendarProviderKind.Microsoft, "remote", "College", "#5588EE", CalendarPermission.Writer, true, now);
        await repository.UpsertCalendarAsync(calendar, CancellationToken.None);
        var item = new PlannerEvent(Guid.NewGuid(), calendar.Id, "Imported", string.Empty, string.Empty, now, now.AddHours(1), false, null, null, false, "remote-event", "etag", now, now);
        await repository.UpsertProviderEventAsync(item, CancellationToken.None);
        Assert.Empty(await repository.GetDueOutboxAsync(account.Id, now.AddMinutes(1), 20, CancellationToken.None));
        Assert.Equal(item.Id, (await repository.GetEventByProviderIdAsync(calendar.Id, "remote-event", CancellationToken.None))?.Id);
    }

    [Fact]
    public async Task ConflictKeepProviderAppliesRemoteVersionAndCancelsPendingWrite()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var account = new CalendarAccount(Guid.NewGuid(), CalendarProviderKind.Google, "Work", "worker@example.test", CalendarSyncStatus.Ready, null, now, now, now);
        await repository.UpsertCalendarAccountAsync(account, CancellationToken.None);
        var calendar = new PlannerCalendar(Guid.NewGuid(), account.Id, CalendarProviderKind.Google, "work", "Work", "#4285F4", CalendarPermission.Writer, true, now);
        await repository.UpsertCalendarAsync(calendar, CancellationToken.None);
        var original = new PlannerEvent(Guid.NewGuid(), calendar.Id, "Original", string.Empty, string.Empty, now.AddDays(1), now.AddDays(1).AddHours(1), false, null, null, false, "remote-1", "etag-1", now, now);
        await repository.UpsertProviderEventAsync(original, CancellationToken.None);
        var haven = original with { Title = "Haven edit", UpdatedAt = now.AddMinutes(1) };
        await repository.UpsertEventAsync(haven, CancellationToken.None);
        var provider = original with { Title = "Provider edit", ProviderETag = "etag-2", UpdatedAt = now.AddMinutes(2) };
        var conflict = new CalendarConflict(Guid.NewGuid(), original.Id, account.Id, JsonSerializer.Serialize(haven), JsonSerializer.Serialize(provider), now.AddMinutes(2), null, null);
        await repository.AddConflictAsync(conflict, CancellationToken.None);
        Assert.True(await repository.HasUnresolvedConflictAsync(original.Id, CancellationToken.None));

        await repository.ResolveConflictAsync(conflict.Id, CalendarConflictResolution.KeepProvider, now.AddMinutes(3), CancellationToken.None);

        Assert.Equal("Provider edit", (await repository.GetEventAsync(original.Id, CancellationToken.None))?.Title);
        Assert.Empty(await repository.GetDueOutboxAsync(account.Id, now.AddHours(1), 20, CancellationToken.None));
        Assert.Empty(await repository.GetUnresolvedConflictsAsync(CancellationToken.None));
        Assert.False(await repository.HasUnresolvedConflictAsync(original.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ConflictKeepHavenQueuesChosenEditAndRecreatesRemoteDeletion()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var account = new CalendarAccount(Guid.NewGuid(), CalendarProviderKind.Microsoft, "College", "student@example.test", CalendarSyncStatus.Ready, null, now, now, now);
        await repository.UpsertCalendarAccountAsync(account, CancellationToken.None);
        var calendar = new PlannerCalendar(Guid.NewGuid(), account.Id, CalendarProviderKind.Microsoft, "college", "College", "#5588EE", CalendarPermission.Writer, true, now);
        await repository.UpsertCalendarAsync(calendar, CancellationToken.None);
        var original = new PlannerEvent(Guid.NewGuid(), calendar.Id, "Original", string.Empty, string.Empty, now.AddDays(1), now.AddDays(1).AddHours(1), false, null, null, false, "remote-2", "etag-1", now, now);
        await repository.UpsertProviderEventAsync(original, CancellationToken.None);
        var haven = original with { Title = "Keep this", UpdatedAt = now.AddMinutes(1) };
        await repository.UpsertEventAsync(haven, CancellationToken.None);
        var providerDeletion = original with { DeletedAt = now.AddMinutes(2), UpdatedAt = now.AddMinutes(2) };
        var conflict = new CalendarConflict(Guid.NewGuid(), original.Id, account.Id, JsonSerializer.Serialize(haven), JsonSerializer.Serialize(providerDeletion), now.AddMinutes(2), null, null);
        await repository.AddConflictAsync(conflict, CancellationToken.None);

        await repository.ResolveConflictAsync(conflict.Id, CalendarConflictResolution.KeepHaven, now.AddMinutes(3), CancellationToken.None);

        var retained = await repository.GetEventAsync(original.Id, CancellationToken.None);
        Assert.Equal("Keep this", retained?.Title);
        Assert.Null(retained?.ProviderEventId);
        Assert.Equal("create", Assert.Single(await repository.GetDueOutboxAsync(account.Id, now.AddHours(1), 20, CancellationToken.None)).Operation);
    }

    [Fact]
    public async Task ConflictDuplicateKeepsProviderAndCreatesPrivateHavenCopy()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var account = new CalendarAccount(Guid.NewGuid(), CalendarProviderKind.Google, "Personal", "person@example.test", CalendarSyncStatus.Ready, null, now, now, now);
        await repository.UpsertCalendarAccountAsync(account, CancellationToken.None);
        var calendar = new PlannerCalendar(Guid.NewGuid(), account.Id, CalendarProviderKind.Google, "personal", "Personal", "#4285F4", CalendarPermission.Writer, true, now);
        await repository.UpsertCalendarAsync(calendar, CancellationToken.None);
        var original = new PlannerEvent(Guid.NewGuid(), calendar.Id, "Original", string.Empty, string.Empty, now.AddDays(1), now.AddDays(1).AddHours(1), false, null, null, false, "remote-3", "etag-1", now, now);
        await repository.UpsertProviderEventAsync(original, CancellationToken.None);
        var haven = original with { Title = "Haven edit", UpdatedAt = now.AddMinutes(1) };
        await repository.UpsertEventAsync(haven, CancellationToken.None);
        var provider = original with { Title = "Provider edit", ProviderETag = "etag-2", UpdatedAt = now.AddMinutes(2) };
        var conflict = new CalendarConflict(Guid.NewGuid(), original.Id, account.Id, JsonSerializer.Serialize(haven), JsonSerializer.Serialize(provider), now.AddMinutes(2), null, null);
        await repository.AddConflictAsync(conflict, CancellationToken.None);

        await repository.ResolveConflictAsync(conflict.Id, CalendarConflictResolution.Duplicate, now.AddMinutes(3), CancellationToken.None);

        Assert.Equal("Provider edit", (await repository.GetEventAsync(original.Id, CancellationToken.None))?.Title);
        var all = await repository.GetEventsAsync(now, now.AddDays(2), null, CancellationToken.None);
        var copy = Assert.Single(all, item => item.Id != original.Id);
        Assert.Equal(PlannerDefaults.LocalCalendarId, copy.CalendarId);
        Assert.Equal("Haven edit (Haven copy)", copy.Title);
        Assert.Null(copy.ProviderEventId);
        Assert.Empty(await repository.GetDueOutboxAsync(account.Id, now.AddHours(1), 20, CancellationToken.None));
    }

    [Fact]
    public async Task WindowsTokenStoreEncryptsAndRoundTripsForCurrentUser()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsCalendarTokenStore(_paths);
        var accountId = Guid.NewGuid();
        var token = new CalendarTokenEnvelope("access-token", "refresh-token", DateTimeOffset.UtcNow.AddHours(1), "Calendars.ReadWrite");
        await store.SaveAsync(accountId, token, CancellationToken.None);
        var loaded = await store.GetAsync(accountId, CancellationToken.None);
        Assert.Equal(token.AccessToken, loaded?.AccessToken);
        Assert.Equal(token.RefreshToken, loaded?.RefreshToken);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(_paths.DataDirectory, "CalendarTokens", accountId.ToString("N") + ".token"));
        Assert.DoesNotContain(System.Text.Encoding.UTF8.GetBytes("access-token"), bytes);
        await store.DeleteAsync(accountId, CancellationToken.None);
        Assert.Null(await store.GetAsync(accountId, CancellationToken.None));
    }

    [Fact]
    public async Task ProposalApplicationIsAtomic()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var first = new PlannerProposedChange(Guid.NewGuid(), PlannerChangeKind.CreateTask, null,
            JsonSerializer.Serialize(new { collectionId = PlannerDefaults.PersonalCollectionId, title = "Should roll back" }), "Create task");
        var second = new PlannerProposedChange(Guid.NewGuid(), PlannerChangeKind.CreateEvent, null,
            JsonSerializer.Serialize(new { calendarId = Guid.NewGuid(), title = "Invalid calendar", startsAt = DateTimeOffset.UtcNow, endsAt = DateTimeOffset.UtcNow.AddHours(1) }), "Create event");
        var proposal = new PlannerChangeProposal(Guid.NewGuid(), "Atomic changes", [first, second], DateTimeOffset.UtcNow);
        await Assert.ThrowsAnyAsync<Exception>(() => repository.ApplyProposalAsync(proposal, CancellationToken.None));
        Assert.DoesNotContain(await repository.GetTasksAsync(new PlannerTaskQuery(IncludeCompleted: true), CancellationToken.None), task => task.Title == "Should roll back");
    }

    [Fact]
    public async Task ProposalValidationAndUnconfiguredProviderFailSafely()
    {
        var (_, repository) = await CreateAsync();
        var service = new PlannerProposalService(repository);
        var invalid = new PlannerChangeProposal(Guid.NewGuid(), string.Empty,
            [new(Guid.NewGuid(), PlannerChangeKind.CreateEvent, null, "{}", string.Empty)], DateTimeOffset.UtcNow);
        var validation = service.Validate(invalid);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Contains("summary", StringComparison.OrdinalIgnoreCase));

        var provider = new CalendarSyncProvider(new CalendarProviderConfiguration(CalendarProviderKind.Google, null,
            new Uri("http://127.0.0.1/callback"), []));
        var result = await provider.ConnectAsync(CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(CalendarSyncStatus.NotConfigured, result.Status);
        Assert.Contains("not configured", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(SqliteDatabase Database, PlannerRepository Repository)> CreateAsync()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        await using var connection = await database.OpenAsync(CancellationToken.None);
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='planner_tasks';";
            if (Convert.ToInt32(await check.ExecuteScalarAsync(CancellationToken.None), System.Globalization.CultureInfo.InvariantCulture) == 0)
            {
                await using var migrate = connection.CreateCommand();
                migrate.CommandText = PlannerMigration.Sql;
                await migrate.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
        return (database, new PlannerRepository(database));
    }

    private async Task InsertProviderEventAsync(PlannerEvent item)
    {
        var database = new SqliteDatabase(_paths);
        await using var connection = await database.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO planner_events(id,calendar_id,title,notes,location,starts_at,ends_at,is_all_day,recurrence_rule,reminder_at,is_read_only,provider_event_id,provider_etag,time_zone_id,created_at,updated_at,deleted_at)
            VALUES($id,$calendarId,$title,'','',$startsAt,$endsAt,0,NULL,NULL,1,$providerEventId,$providerETag,'UTC',$createdAt,$updatedAt,NULL);
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$calendarId", item.CalendarId.ToString());
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$startsAt", item.StartsAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$endsAt", item.EndsAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$providerEventId", item.ProviderEventId!);
        command.Parameters.AddWithValue("$providerETag", item.ProviderETag!);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", item.UpdatedAt.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static PlannerTask NewTask(Guid collectionId, string title, DateTimeOffset now) =>
        new(Guid.NewGuid(), collectionId, null, title, string.Empty, PlannerPriority.None, PlannerTaskStatus.Planned, "[]", null,
            null, null, null, null, null, 0, now, now);

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-planner-tests-" + Guid.NewGuid().ToString("N"));
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
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
