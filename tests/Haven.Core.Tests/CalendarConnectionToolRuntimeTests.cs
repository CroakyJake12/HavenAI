using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class CalendarConnectionToolRuntimeTests
{
    [Fact]
    public async Task DefinitionsRequireAttachedConnection()
    {
        var fixture = Fixture();
        var runtime = new CalendarConnectionToolRuntime(fixture.Repository, fixture.Registry);

        Assert.Empty(await runtime.GetDefinitionsAsync([], CancellationToken.None));
        var definitions = await runtime.GetDefinitionsAsync([Active(fixture.Account.Id)], CancellationToken.None);

        Assert.Equal(2, definitions.Count);
        Assert.Contains(definitions, item => item.Name.EndsWith("calendar_list_events", StringComparison.Ordinal));
        Assert.Contains(definitions, item => item.Name.EndsWith("calendar_sync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadEventsOnlyReturnsAttachedAccountCalendars()
    {
        var fixture = Fixture();
        var runtime = new CalendarConnectionToolRuntime(fixture.Repository, fixture.Registry);
        var active = Active(fixture.Account.Id);
        var definition = (await runtime.GetDefinitionsAsync([active], CancellationToken.None)).Single(item => item.Name.EndsWith("calendar_list_events", StringComparison.Ordinal));

        var result = await runtime.ExecuteAsync(Call(definition.Name, "2026-08-22T00:00:00Z", "2026-08-23T00:00:00Z"), [active], PermissionMode.Ask, CancellationToken.None);

        Assert.True(result.Activity.Succeeded);
        Assert.Contains("Physics", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Other account event", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncRequiresMutationPermissionAndDelegatesToExistingProvider()
    {
        var fixture = Fixture();
        var runtime = new CalendarConnectionToolRuntime(fixture.Repository, fixture.Registry);
        var active = Active(fixture.Account.Id);
        var definition = (await runtime.GetDefinitionsAsync([active], CancellationToken.None)).Single(item => item.Name.EndsWith("calendar_sync", StringComparison.Ordinal));
        var call = Call(definition.Name, "2026-08-22T00:00:00Z", "2026-08-23T00:00:00Z");

        var denied = await runtime.ExecuteAsync(call, [active], PermissionMode.Ask, CancellationToken.None);
        Assert.False(denied.Activity.Succeeded);
        Assert.Equal(0, fixture.Provider.SyncCalls);

        var allowed = await runtime.ExecuteAsync(call, [active], PermissionMode.AutoSafe, CancellationToken.None);
        Assert.True(allowed.Activity.Succeeded);
        Assert.Equal(1, fixture.Provider.SyncCalls);
        Assert.Equal(fixture.Account.Id, fixture.Provider.LastRequest?.AccountId);
    }

    private static (FakePlannerRepository Repository, FakeCalendarProviderRegistry Registry, FakeCalendarProvider Provider, CalendarAccount Account) Fixture()
    {
        var now = DateTimeOffset.Parse("2026-08-22T12:00:00Z");
        var account = new CalendarAccount(Guid.NewGuid(), CalendarProviderKind.Google, "Student calendar", "student@example.test", CalendarSyncStatus.Ready, null, now, now, now);
        var calendar = new PlannerCalendar(Guid.NewGuid(), account.Id, CalendarProviderKind.Google, "primary", "College", "#000000", CalendarPermission.Owner, true, now);
        var otherCalendar = new PlannerCalendar(Guid.NewGuid(), Guid.NewGuid(), CalendarProviderKind.Microsoft, "other", "Other", "#000000", CalendarPermission.Owner, true, now);
        var repository = new FakePlannerRepository
        {
            Accounts = [account],
            Calendars = [calendar, otherCalendar],
            Events =
            [
                new PlannerEvent(Guid.NewGuid(), calendar.Id, "Physics", "Revision", "Room 1", now, now.AddHours(1), false, null, null, false, "evt-1", null, now, now),
                new PlannerEvent(Guid.NewGuid(), otherCalendar.Id, "Other account event", "", "", now, now.AddHours(1), false, null, null, false, "evt-2", null, now, now)
            ]
        };
        var provider = new FakeCalendarProvider(CalendarProviderKind.Google);
        return (repository, new FakeCalendarProviderRegistry(provider), provider, account);
    }

    private static ActiveCapability Active(Guid id) => new(ExternalConnectionNaming.CapabilityKey(id), "Google Connection", "connection", "Use connection", "connection.calendar", "haven.connections");

    private static OllamaToolCall Call(string name, string start, string end)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { start, end }));
        return new OllamaToolCall(name, document.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone()));
    }

    private sealed class FakeCalendarProvider(CalendarProviderKind kind) : ICalendarSyncProvider
    {
        public CalendarProviderKind Kind { get; } = kind;
        public bool IsConfigured => true;
        public string ConfigurationStatus => "Ready";
        public int SyncCalls { get; private set; }
        public CalendarSyncRequest? LastRequest { get; private set; }
        public Task<CalendarAuthorizationResult> ConnectAsync(CancellationToken cancellationToken) => Task.FromResult(new CalendarAuthorizationResult(true, CalendarSyncStatus.Ready, "Ready"));
        public Task<CalendarSyncResult> SyncAsync(CalendarSyncRequest request, CancellationToken cancellationToken)
        {
            SyncCalls++;
            LastRequest = request;
            return Task.FromResult(new CalendarSyncResult(true, CalendarSyncStatus.Ready, 1, 0, 0, 0, "Synced"));
        }
        public Task DisconnectAsync(Guid accountId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCalendarProviderRegistry(params ICalendarSyncProvider[] providers) : ICalendarSyncProviderRegistry
    {
        public IReadOnlyList<ICalendarSyncProvider> Providers { get; } = providers;
        public ICalendarSyncProvider Get(CalendarProviderKind kind) => Providers.Single(item => item.Kind == kind);
    }

    private sealed class FakePlannerRepository : IPlannerRepository
    {
        public IReadOnlyList<CalendarAccount> Accounts { get; init; } = [];
        public IReadOnlyList<PlannerCalendar> Calendars { get; init; } = [];
        public IReadOnlyList<PlannerEvent> Events { get; init; } = [];
        public Task<IReadOnlyList<CalendarAccount>> GetCalendarAccountsAsync(CancellationToken cancellationToken) => Task.FromResult(Accounts);
        public Task<IReadOnlyList<PlannerCalendar>> GetCalendarsAsync(bool visibleOnly, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PlannerCalendar>>(visibleOnly ? Calendars.Where(item => item.IsVisible).ToArray() : Calendars);
        public Task<IReadOnlyList<PlannerEvent>> GetEventsAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, Guid? calendarId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PlannerEvent>>(Events.Where(item => item.StartsAt < rangeEnd && item.EndsAt > rangeStart && (calendarId is null || item.CalendarId == calendarId)).ToArray());
        public Task EnsureDefaultsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<PlannerCollection>> GetCollectionsAsync(bool includeArchived, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertCollectionAsync(PlannerCollection collection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ArchiveCollectionAsync(Guid id, bool archived, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PlannerTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerTask>> GetTasksAsync(PlannerTaskQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertTaskAsync(PlannerTask task, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteTaskAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteTaskAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerTaskCompletion>> GetCompletionHistoryAsync(Guid taskId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertCalendarAsync(PlannerCalendar calendar, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PlannerEvent?> GetEventAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertEventAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteEventAsync(Guid id, DateTimeOffset deletedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertCalendarAccountAsync(CalendarAccount account, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CalendarConflict>> GetUnresolvedConflictsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ResolveConflictAsync(Guid id, CalendarConflictResolution resolution, DateTimeOffset resolvedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlannerReminder>> GetDueRemindersAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkReminderDeliveredAsync(PlannerReminder reminder, DateTimeOffset deliveredAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ApplyProposalAsync(PlannerChangeProposal proposal, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
