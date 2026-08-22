using System.Globalization;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Exposes configured calendar accounts through Haven's shared tool loop without duplicating provider or OAuth logic.</summary>
public sealed class CalendarConnectionToolRuntime(IPlannerRepository planner, ICalendarSyncProviderRegistry providers)
{
    private enum CalendarRouteKind { ListEvents, Sync }
    private sealed record Route(Guid AccountId, CalendarRouteKind Kind);
    private readonly Dictionary<string, Route> _routes = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<OllamaToolDefinition>> GetDefinitionsAsync(
        IReadOnlyCollection<ActiveCapability> activeCapabilities,
        CancellationToken cancellationToken)
    {
        var activeIds = ParseActiveConnectionIds(activeCapabilities);
        if (activeIds.Count == 0) return [];
        var result = new List<OllamaToolDefinition>();
        foreach (var account in await planner.GetCalendarAccountsAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!activeIds.Contains(account.Id) || account.Status is CalendarSyncStatus.NotConfigured or CalendarSyncStatus.Disconnected) continue;
            var providerName = account.Provider == CalendarProviderKind.Google ? "Google" : "Microsoft";
            var listName = LocalToolName(account.Id, "calendar_list_events");
            var syncName = LocalToolName(account.Id, "calendar_sync");
            lock (_routes)
            {
                _routes[listName] = new Route(account.Id, CalendarRouteKind.ListEvents);
                _routes[syncName] = new Route(account.Id, CalendarRouteKind.Sync);
            }
            result.Add(new OllamaToolDefinition(listName,
                $"Read events available through the attached {providerName} calendar connection for a bounded time window.",
                DateRangeProperties(), ["start", "end"]));
            result.Add(new OllamaToolDefinition(syncName,
                $"Synchronize the attached {providerName} calendar connection for a bounded time window. This can change provider/local calendar state and is permission-gated.",
                new Dictionary<string, object>(DateRangeProperties(), StringComparer.Ordinal)
                {
                    ["full_sync"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Request a full provider sync for this bounded window." }
                }, ["start", "end"]));
        }
        return result;
    }

    public async Task<WorkspaceToolResult> ExecuteAsync(
        OllamaToolCall call,
        IReadOnlyCollection<ActiveCapability> activeCapabilities,
        PermissionMode mutationPermission,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        Route? route;
        lock (_routes) _routes.TryGetValue(call.Name, out route);
        if (route is null) return Failure(call.Name, "Calendar Connection tool route is stale. Refresh the attached connection.", started);
        if (!ParseActiveConnectionIds(activeCapabilities).Contains(route.AccountId))
            return Failure(call.Name, "The calendar connection is not attached to this conversation.", started);
        var account = (await planner.GetCalendarAccountsAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == route.AccountId);
        if (account is null || account.Status is CalendarSyncStatus.NotConfigured or CalendarSyncStatus.Disconnected)
            return Failure(call.Name, "The calendar connection is disconnected or unavailable.", started);
        try
        {
            var start = RequiredDate(call, "start");
            var end = RequiredDate(call, "end");
            if (end <= start) return Failure(call.Name, "end must be later than start.", started);
            if (end - start > TimeSpan.FromDays(366)) return Failure(call.Name, "Calendar Connection requests are limited to a 366-day window.", started);
            if (route.Kind == CalendarRouteKind.Sync)
            {
                if (mutationPermission == PermissionMode.Ask)
                    return Failure(call.Name, "Calendar synchronization can change external state and requires approval before execution.", started);
                var sync = await providers.Get(account.Provider)
                    .SyncAsync(new CalendarSyncRequest(account.Id, Boolean(call, "full_sync"), start, end), cancellationToken)
                    .ConfigureAwait(false);
                var output = JsonSerializer.Serialize(new { sync.Succeeded, sync.Status, sync.Added, sync.Updated, sync.Deleted, sync.Conflicts, sync.Message });
                return Result(call.Name, sync.Succeeded ? "Calendar synchronization completed." : "Calendar synchronization reported a failure.", output, sync.Succeeded, started);
            }
            var calendars = (await planner.GetCalendarsAsync(false, cancellationToken).ConfigureAwait(false)).Where(calendar => calendar.AccountId == account.Id).ToArray();
            var calendarIds = calendars.Select(calendar => calendar.Id).ToHashSet();
            var events = (await planner.GetEventsAsync(start, end, null, cancellationToken).ConfigureAwait(false))
                .Where(item => calendarIds.Contains(item.CalendarId) && item.DeletedAt is null).OrderBy(item => item.StartsAt).Take(500)
                .Select(item => new { item.Id, item.CalendarId, item.Title, item.Notes, item.Location, item.StartsAt, item.EndsAt, item.IsAllDay, item.RecurrenceRule, item.ReminderAt, item.IsReadOnly, item.TimeZoneId }).ToArray();
            var payload = JsonSerializer.Serialize(new
            {
                account = new { account.Id, account.Provider, account.DisplayName, account.Status, account.LastSyncedAt },
                calendars = calendars.Select(calendar => new { calendar.Id, calendar.Name, calendar.Permission, calendar.IsVisible }),
                events,
                truncated = events.Length == 500
            });
            return Result(call.Name, $"Read {events.Length} calendar events.", payload, true, started);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Failure(call.Name, "Calendar Connection action failed: " + Bound(ex.Message, 1000), started); }
    }

    public static string LocalToolName(Guid accountId, string action) => $"calendar_{accountId:N}_{action}";

    private static IReadOnlyDictionary<string, object> DateRangeProperties() => new Dictionary<string, object>(StringComparer.Ordinal)
    {
        ["start"] = new Dictionary<string, object> { ["type"] = "string", ["format"] = "date-time", ["description"] = "Inclusive window start as an ISO-8601 timestamp." },
        ["end"] = new Dictionary<string, object> { ["type"] = "string", ["format"] = "date-time", ["description"] = "Exclusive window end as an ISO-8601 timestamp." }
    };

    private static HashSet<Guid> ParseActiveConnectionIds(IEnumerable<ActiveCapability> capabilities)
    {
        var result = new HashSet<Guid>();
        foreach (var capability in capabilities)
        {
            if (!ExternalConnectionNaming.IsConnectionCapability(capability.Key)) continue;
            var raw = capability.Key["connection:".Length..];
            if (Guid.TryParseExact(raw, "N", out var id)) result.Add(id);
        }
        return result;
    }

    private static DateTimeOffset RequiredDate(OllamaToolCall call, string key)
    {
        if (!call.Arguments.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            throw new ArgumentException($"{key} must be an ISO-8601 date-time.");
        return parsed;
    }

    private static bool Boolean(OllamaToolCall call, string key) =>
        call.Arguments.TryGetValue(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private static WorkspaceToolResult Result(string name, string detail, string output, bool succeeded, DateTimeOffset started) =>
        new(new ToolActivity(Guid.NewGuid(), name.Replace('_', ' '), detail, succeeded, DateTimeOffset.UtcNow - started, DateTimeOffset.UtcNow), output);
    private static WorkspaceToolResult Failure(string name, string detail, DateTimeOffset started) => Result(name, detail, "Tool error: " + detail, false, started);
    private static string Bound(string value, int max) => value.Length <= max ? value : value[..max] + "...";
}
