/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/PlannerRepositoryTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns PlannerRepositoryTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents planner repository tests and keeps its related state and behavior together.
/// </summary>
public sealed class PlannerRepositoryTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the defaults tasks hierarchy and events round trip step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the rejects cycles and read only event edits step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the completing recurring task advances it and records history step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the recurring event appears in later calendar range step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the due reminder is returned only until marked delivered step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the remote calendar writes queue outbox and sync state round trips step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the provider ingestion does not echo back into outbox step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the conflict keep provider applies remote version and cancels pending write step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the conflict keep haven queues chosen edit and recreates remote deletion step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the conflict duplicate keeps provider and creates private haven copy step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the windows token store encrypts and round trips for current user step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the canonical day service persisted-state integration step owned by this component.
    /// </summary>
    [Fact]
    public async Task DayServiceBuildsNowNextAndProgressFromPersistedPlanState()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var dayStart = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var now = dayStart.AddHours(10.5);
        var created = dayStart.AddDays(-1);

        var revision = NewTask(PlannerDefaults.CollegeCollectionId, "Revision", created) with
        {
            StartsAt = dayStart.AddHours(10.25),
            DueAt = dayStart.AddHours(11.25),
            EstimatedMinutes = 60
        };
        await repository.UpsertTaskAsync(revision, CancellationToken.None);

        var maths = new PlannerEvent(
            Guid.NewGuid(), PlannerDefaults.LocalCalendarId, "Maths lesson", string.Empty, string.Empty,
            dayStart.AddHours(10), dayStart.AddHours(11), false, null, null, false, null, null, created, created);
        var law = new PlannerEvent(
            Guid.NewGuid(), PlannerDefaults.LocalCalendarId, "Law lesson", string.Empty, string.Empty,
            dayStart.AddHours(12), dayStart.AddHours(13), false, null, null, false, null, null, created, created);
        await repository.UpsertEventAsync(maths, CancellationToken.None);
        await repository.UpsertEventAsync(law, CancellationToken.None);

        var service = new PlannerDayService(repository);
        var snapshot = await service.GetDayAsync(dayStart, now, "UTC", CancellationToken.None);

        Assert.Equal(3, snapshot.Items.Count);
        Assert.Equal(2, snapshot.ActiveItems.Count);
        Assert.Equal(maths.Id, snapshot.CurrentItem?.EntityId);
        Assert.Equal(law.Id, snapshot.NextItem?.EntityId);
        Assert.Equal(dayStart.AddHours(10), snapshot.ScheduleStart);
        Assert.Equal(dayStart.AddHours(13), snapshot.ScheduleEnd);
        Assert.Equal(1d / 6d, snapshot.Progress, 6);
    }

    /// <summary>
    /// Ensures persisted timed tasks that begin before midnight remain visible when their estimated interval overlaps the requested day.
    /// </summary>
    [Fact]
    public async Task DayServiceIncludesPersistedTaskWhoseEstimateCrossesMidnight()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var dayStart = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var created = dayStart.AddDays(-1);
        var spanning = NewTask(PlannerDefaults.CollegeCollectionId, "Late revision", created) with
        {
            StartsAt = dayStart.AddMinutes(-30),
            DueAt = null,
            EstimatedMinutes = 90
        };
        var endedBeforeDay = NewTask(PlannerDefaults.CollegeCollectionId, "Earlier revision", created) with
        {
            StartsAt = dayStart.AddHours(-2),
            DueAt = null,
            EstimatedMinutes = 60
        };
        var dueOnly = NewTask(PlannerDefaults.CollegeCollectionId, "Submit form", created) with
        {
            DueAt = dayStart.AddHours(9)
        };
        await repository.UpsertTaskAsync(spanning, CancellationToken.None);
        await repository.UpsertTaskAsync(endedBeforeDay, CancellationToken.None);
        await repository.UpsertTaskAsync(dueOnly, CancellationToken.None);

        var ranged = await repository.GetTasksAsync(
            new PlannerTaskQuery(RangeStart: dayStart, RangeEnd: dayStart.AddDays(1), IncludeCompleted: true),
            CancellationToken.None);

        Assert.Contains(ranged, item => item.Id == spanning.Id);
        Assert.DoesNotContain(ranged, item => item.Id == endedBeforeDay.Id);
        Assert.Contains(ranged, item => item.Id == dueOnly.Id);

        var snapshot = await new PlannerDayService(repository).GetDayAsync(
            dayStart,
            dayStart.AddMinutes(15),
            "UTC",
            CancellationToken.None);

        Assert.Contains(snapshot.Items, item => item.EntityId == spanning.Id);
        Assert.Contains(snapshot.Items, item => item.EntityId == dueOnly.Id);
        Assert.Equal(spanning.Id, snapshot.CurrentItem?.EntityId);
    }

    /// <summary>
    /// Performs the persisted countdown projection integration step owned by this component.
    /// </summary>
    [Fact]
    public async Task CountdownServiceTracksPersistedDeadlineChangesWithoutDuplicateState()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var created = now.AddDays(-1);
        var deadline = NewTask(PlannerDefaults.CollegeCollectionId, "Coursework deadline", created) with
        {
            DueAt = now.AddDays(3),
            ReminderAt = now.AddDays(3).AddHours(-4)
        };
        var resultsDay = new PlannerEvent(
            Guid.NewGuid(), PlannerDefaults.LocalCalendarId, "Results Day", string.Empty, string.Empty,
            now.AddDays(5), now.AddDays(5).AddHours(1), false, null, now.AddDays(4), false, null, null, created, created);
        await repository.UpsertTaskAsync(deadline, CancellationToken.None);
        await repository.UpsertEventAsync(resultsDay, CancellationToken.None);

        var service = new PlannerCountdownService(repository);
        var initial = await service.GetCountdownsAsync(now.AddDays(-1), now.AddDays(10), now, CancellationToken.None);

        Assert.Equal([deadline.Id, resultsDay.Id], initial.Select(item => item.SourceId));
        Assert.Equal(now.AddDays(3), initial[0].TargetAt);
        Assert.Equal(deadline.ReminderAt, initial[0].ReminderAt);
        Assert.Equal(PlannerCountdownState.Upcoming, initial[0].State);

        var movedDeadline = deadline with { DueAt = now.AddDays(7), UpdatedAt = now.AddMinutes(1) };
        await repository.UpsertTaskAsync(movedDeadline, CancellationToken.None);
        var refreshed = await service.GetCountdownsAsync(now.AddDays(-1), now.AddDays(10), now, CancellationToken.None);
        var movedCountdown = Assert.Single(refreshed, item => item.SourceId == deadline.Id);

        Assert.Equal(now.AddDays(7), movedCountdown.TargetAt);
        Assert.Equal(movedDeadline.Id, movedCountdown.SourceId);
    }

    /// <summary>
    /// Performs the persisted availability service integration step owned by this component.
    /// </summary>
    [Fact]
    public async Task AvailabilityServiceFindsFreeWindowsFromPersistedPlanState()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var dayStart = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var created = dayStart.AddDays(-1);
        var lesson = new PlannerEvent(
            Guid.NewGuid(), PlannerDefaults.LocalCalendarId, "Maths lesson", string.Empty, string.Empty,
            dayStart.AddHours(10), dayStart.AddHours(11), false, null, null, false, null, null, created, created);
        var revision = NewTask(PlannerDefaults.CollegeCollectionId, "Revision", created) with
        {
            StartsAt = dayStart.AddHours(13),
            DueAt = dayStart.AddHours(14),
            EstimatedMinutes = 60
        };
        await repository.UpsertEventAsync(lesson, CancellationToken.None);
        await repository.UpsertTaskAsync(revision, CancellationToken.None);

        var service = new PlannerAvailabilityService(new PlannerDayService(repository));
        var free = await service.GetFreeWindowsAsync(
            dayStart,
            dayStart.AddHours(8),
            "UTC",
            dayStart.AddHours(9),
            dayStart.AddHours(17),
            TimeSpan.FromMinutes(45),
            CancellationToken.None);

        Assert.Equal(3, free.Count);
        Assert.Equal(new PlannerFreeWindow(dayStart.AddHours(9), dayStart.AddHours(10)), free[0]);
        Assert.Equal(new PlannerFreeWindow(dayStart.AddHours(11), dayStart.AddHours(13)), free[1]);
        Assert.Equal(new PlannerFreeWindow(dayStart.AddHours(14), dayStart.AddHours(17)), free[2]);
    }

    /// <summary>
    /// Ensures a persisted task spanning midnight blocks availability at the beginning of the following day.
    /// </summary>
    [Fact]
    public async Task AvailabilityServiceBlocksPersistedCrossMidnightTaskOnFollowingDay()
    {
        var (_, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var dayStart = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var created = dayStart.AddDays(-1);
        var spanning = NewTask(PlannerDefaults.CollegeCollectionId, "Late revision", created) with
        {
            StartsAt = dayStart.AddMinutes(-30),
            DueAt = null,
            EstimatedMinutes = 90
        };
        await repository.UpsertTaskAsync(spanning, CancellationToken.None);

        var service = new PlannerAvailabilityService(new PlannerDayService(repository));
        var free = await service.GetFreeWindowsAsync(
            dayStart,
            dayStart.AddMinutes(15),
            "UTC",
            dayStart,
            dayStart.AddHours(3),
            TimeSpan.FromMinutes(30),
            CancellationToken.None);

        var only = Assert.Single(free);
        Assert.Equal(dayStart.AddHours(1), only.StartsAt);
        Assert.Equal(dayStart.AddHours(3), only.EndsAt);
    }

    /// <summary>
    /// Ensures Study revision scheduling consumes persisted Plan availability and writes the chosen session back to canonical Plan state.
    /// </summary>
    [Fact]
    public async Task StudyPlannerSchedulesRevisionIntoPersistedPlanFreeWindow()
    {
        var (database, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var containers = new ContainerRepository(database, _paths);
        var dayStart = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var now = dayStart.AddHours(8);
        var subject = new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, "Maths", null, string.Empty, string.Empty, now, now);
        var lesson = await containers.CreateSubjectAsync(subject, CancellationToken.None);
        var morningLesson = new PlannerEvent(
            Guid.NewGuid(), PlannerDefaults.LocalCalendarId, "Maths lesson", string.Empty, string.Empty,
            dayStart.AddHours(9), dayStart.AddHours(10), false, null, null, false, null, null, now, now);
        var existingRevision = NewTask(PlannerDefaults.CollegeCollectionId, "Existing revision", now) with
        {
            StartsAt = dayStart.AddHours(11),
            DueAt = null,
            EstimatedMinutes = 60
        };
        await repository.UpsertEventAsync(morningLesson, CancellationToken.None);
        await repository.UpsertTaskAsync(existingRevision, CancellationToken.None);

        var availability = new PlannerAvailabilityService(new PlannerDayService(repository));
        var service = new StudyPlannerService(repository, containers, availability);
        var scheduled = await service.ScheduleRevisionAsync(new StudyRevisionScheduleRequest(
            subject.Id,
            lesson.Id,
            PlannerDefaults.CollegeCollectionId,
            "Pure maths revision",
            "Complete mixed exercise",
            dayStart.AddHours(9),
            dayStart.AddHours(13),
            60,
            dayStart.AddHours(18),
            null,
            PlannerPriority.High,
            "UTC"), now, CancellationToken.None);

        Assert.Equal(dayStart.AddHours(10), scheduled.Task.StartsAt);
        Assert.Equal(60, scheduled.Task.EstimatedMinutes);
        Assert.Equal(dayStart.AddHours(18), scheduled.Task.DueAt);
        Assert.Equal(subject.Id, scheduled.Link.SubjectId);
        Assert.Equal(lesson.Id, scheduled.Link.LessonId);
        var persisted = await repository.GetTaskAsync(scheduled.PlanTaskId, CancellationToken.None);
        Assert.Equal(scheduled.Task, persisted);

        var remaining = await availability.GetFreeWindowsAsync(
            dayStart,
            now,
            "UTC",
            dayStart.AddHours(9),
            dayStart.AddHours(13),
            TimeSpan.FromMinutes(30),
            CancellationToken.None);
        var only = Assert.Single(remaining);
        Assert.Equal(dayStart.AddHours(12), only.StartsAt);
        Assert.Equal(dayStart.AddHours(13), only.EndsAt);
    }

    /// <summary>
    /// Ensures Study relinking, completion and unlinking mutate one canonical Plan task without dropping unrelated tags.
    /// </summary>
    [Fact]
    public async Task StudyPlannerLifecyclePreservesCanonicalTaskAndUnrelatedTags()
    {
        var (database, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var containers = new ContainerRepository(database, _paths);
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var maths = new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, "Maths", null, string.Empty, string.Empty, now, now);
        var law = new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, "Law", null, string.Empty, string.Empty, now, now);
        var mathsLesson = await containers.CreateSubjectAsync(maths, CancellationToken.None);
        var lawLesson = await containers.CreateSubjectAsync(law, CancellationToken.None);
        var original = NewTask(PlannerDefaults.CollegeCollectionId, "Past paper", now) with
        {
            DueAt = now.AddHours(2),
            TagsJson = JsonSerializer.Serialize(new[] { "revision", "haven:other:metadata" })
        };
        await repository.UpsertTaskAsync(original, CancellationToken.None);
        var service = new StudyPlannerService(repository, containers, new PlannerAvailabilityService(new PlannerDayService(repository)));

        var linked = await service.LinkExistingAsync(original.Id, maths.Id, mathsLesson.Id, now.AddMinutes(1), CancellationToken.None);
        Assert.Equal(original.Id, linked.PlanTaskId);
        Assert.True(PlannerStudyAssignmentTags.TryRead(linked.Task.TagsJson, out var mathsLink));
        Assert.Equal(maths.Id, mathsLink.SubjectId);

        var relinked = await service.LinkExistingAsync(original.Id, law.Id, lawLesson.Id, now.AddMinutes(2), CancellationToken.None);
        Assert.Equal(original.Id, relinked.PlanTaskId);
        Assert.True(PlannerStudyAssignmentTags.TryRead(relinked.Task.TagsJson, out var lawLink));
        Assert.Equal(law.Id, lawLink.SubjectId);
        Assert.Equal(lawLesson.Id, lawLink.LessonId);
        Assert.Empty(await service.GetAssignmentsAsync(maths.Id, includeCompleted: true, CancellationToken.None));
        Assert.Equal(original.Id, Assert.Single(await service.GetAssignmentsAsync(law.Id, includeCompleted: true, CancellationToken.None)).PlanTaskId);

        var movedDeadline = now.AddHours(6);
        var rescheduled = await service.UpdateDeadlineAsync(original.Id, movedDeadline, now.AddMinutes(3), CancellationToken.None);
        Assert.Equal(movedDeadline, rescheduled.Task.DueAt);
        Assert.Equal(law.Id, rescheduled.Link.SubjectId);
        var persistedRescheduled = await repository.GetTaskAsync(original.Id, CancellationToken.None);
        Assert.NotNull(persistedRescheduled);
        Assert.Equal(movedDeadline, persistedRescheduled!.DueAt);
        Assert.Contains("haven:other:metadata", JsonSerializer.Deserialize<string[]>(persistedRescheduled.TagsJson)!);

        var completed = await service.CompleteAsync(original.Id, now.AddHours(3), CancellationToken.None);
        Assert.Equal(original.Id, completed.PlanTaskId);
        Assert.Equal(PlannerTaskStatus.Completed, completed.Task.Status);
        var completedTags = JsonSerializer.Deserialize<string[]>(completed.Task.TagsJson)!;
        Assert.Contains("revision", completedTags);
        Assert.Contains("haven:other:metadata", completedTags);

        await service.UnlinkAsync(original.Id, now.AddHours(4), CancellationToken.None);
        var final = await repository.GetTaskAsync(original.Id, CancellationToken.None);
        Assert.NotNull(final);
        Assert.Equal(original.Id, final.Id);
        Assert.Equal(PlannerTaskStatus.Completed, final.Status);
        Assert.False(PlannerStudyAssignmentTags.TryRead(final.TagsJson, out _));
        var finalTags = JsonSerializer.Deserialize<string[]>(final.TagsJson)!;
        Assert.Contains("revision", finalTags);
        Assert.Contains("haven:other:metadata", finalTags);
        Assert.Empty(await service.GetAssignmentsAsync(law.Id, includeCompleted: true, CancellationToken.None));
        Assert.Single(
            await repository.GetTasksAsync(new PlannerTaskQuery(IncludeCompleted: true), CancellationToken.None),
            task => task.Id == original.Id);
    }

    /// <summary>
    /// Performs the persisted Study-to-Plan assignment integration step owned by this component.
    /// </summary>
    [Fact]
    public async Task StudyPlannerServiceUsesCanonicalPersistedPlanTaskState()
    {
        var (database, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var containers = new ContainerRepository(database, _paths);
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var subject = new ContainerDefinition(
            Guid.NewGuid(), HavenMode.Study, "Maths", null, "A-Level Maths", string.Empty, now, now);
        var lesson = await containers.CreateSubjectAsync(subject, CancellationToken.None);
        var service = new StudyPlannerService(repository, containers, new PlannerAvailabilityService(new PlannerDayService(repository)));

        var created = await service.CreateAsync(new StudyPlanAssignmentDraft(
            subject.Id,
            lesson.Id,
            PlannerDefaults.CollegeCollectionId,
            "Complete integration exercise",
            "Chapter 3 questions",
            now.AddDays(2),
            now.AddDays(2).AddHours(-3),
            45,
            PlannerPriority.High,
            now.AddDays(1),
            "UTC"), now, CancellationToken.None);

        var persisted = await repository.GetTaskAsync(created.PlanTaskId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(created.PlanTaskId, persisted.Id);
        Assert.Equal(now.AddDays(2), persisted.DueAt);
        Assert.True(PlannerStudyAssignmentTags.TryRead(persisted.TagsJson, out var link));
        Assert.Equal(subject.Id, link.SubjectId);
        Assert.Equal(lesson.Id, link.LessonId);

        var listed = await service.GetAssignmentsAsync(subject.Id, includeCompleted: false, CancellationToken.None);
        var assignment = Assert.Single(listed);
        Assert.Equal(persisted.Id, assignment.PlanTaskId);
        Assert.Equal(persisted, assignment.Task);

        var completed = await service.CompleteAsync(persisted.Id, now.AddHours(1), CancellationToken.None);
        Assert.Equal(PlannerTaskStatus.Completed, completed.Task.Status);
        Assert.Equal(PlannerTaskStatus.Completed, (await repository.GetTaskAsync(persisted.Id, CancellationToken.None))?.Status);
        Assert.Empty(await service.GetAssignmentsAsync(subject.Id, includeCompleted: false, CancellationToken.None));
        Assert.Single(await service.GetAssignmentsAsync(subject.Id, includeCompleted: true, CancellationToken.None));
    }

    /// <summary>
    /// Performs Study lesson ownership validation before linking a Plan task.
    /// </summary>
    [Fact]
    public async Task StudyPlannerServiceRejectsLessonFromAnotherSubject()
    {
        var (database, repository) = await CreateAsync();
        await repository.EnsureDefaultsAsync(CancellationToken.None);
        var containers = new ContainerRepository(database, _paths);
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var maths = new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, "Maths", null, string.Empty, string.Empty, now, now);
        var law = new ContainerDefinition(Guid.NewGuid(), HavenMode.Study, "Law", null, string.Empty, string.Empty, now, now);
        await containers.CreateSubjectAsync(maths, CancellationToken.None);
        var lawLesson = await containers.CreateSubjectAsync(law, CancellationToken.None);
        var task = NewTask(PlannerDefaults.CollegeCollectionId, "Existing deadline", now) with { DueAt = now.AddDays(1) };
        await repository.UpsertTaskAsync(task, CancellationToken.None);
        var service = new StudyPlannerService(repository, containers, new PlannerAvailabilityService(new PlannerDayService(repository)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkExistingAsync(task.Id, maths.Id, lawLesson.Id, now.AddMinutes(1), CancellationToken.None));

        Assert.Contains("does not belong", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(PlannerStudyAssignmentTags.TryRead((await repository.GetTaskAsync(task.Id, CancellationToken.None))!.TagsJson, out _));
    }

    /// <summary>
    /// Performs the proposal application is atomic step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the proposal validation and unconfigured provider fail safely step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs insert provider event asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the new task step owned by this component.
    /// </summary>
    private static PlannerTask NewTask(Guid collectionId, string title, DateTimeOffset now) =>
        new(Guid.NewGuid(), collectionId, null, title, string.Empty, PlannerPriority.None, PlannerTaskStatus.Planned, "[]", null,
            null, null, null, null, null, 0, now, now);

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
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-planner-tests-" + Guid.NewGuid().ToString("N"));
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
