/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/GoogleCalendarProviderTransport.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns GoogleCalendarProviderTransport, SyncCounts. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents google calendar provider transport and keeps its related state and behavior together.
/// </summary>
public sealed class GoogleCalendarProviderTransport(
    CalendarProviderConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IPlannerRepository repository,
    ICalendarSyncStore store,
    ICalendarTokenStore tokenStore)
    : OAuthCalendarTransportBase(configuration, httpClientFactory, repository, store, tokenStore)
{
    /// <summary>
    /// Gets or updates authorization endpoint, the bindable or domain state represented by this property.
    /// </summary>
    protected override Uri AuthorizationEndpoint { get; } = new("https://accounts.google.com/o/oauth2/v2/auth");
    /// <summary>
    /// Gets or updates token endpoint, the bindable or domain state represented by this property.
    /// </summary>
    protected override Uri TokenEndpoint { get; } = new("https://oauth2.googleapis.com/token");

    protected override async Task<(string Identifier, string DisplayName)> GetIdentityAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(new Uri("https://openidconnect.googleapis.com/v1/userinfo"), accessToken, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var identifier = root.TryGetProperty("email", out var email) ? email.GetString() : root.GetProperty("sub").GetString();
        if (string.IsNullOrWhiteSpace(identifier)) throw new JsonException("Google identity response omitted the account identifier.");
        var displayName = root.TryGetProperty("name", out var name) ? name.GetString() : null;
        return (identifier, string.IsNullOrWhiteSpace(displayName) ? identifier : displayName);
    }

    /// <summary>
    /// Performs sync core async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    protected override async Task<CalendarSyncResult> SyncCoreAsync(CalendarAccount account, CalendarTokenEnvelope token, CalendarSyncRequest request, CancellationToken cancellationToken)
    {
        await DrainOutboxAsync(account, token, ApplyOutboxAsync, cancellationToken).ConfigureAwait(false);
        var calendars = await GetCalendarsAsync(account, token.AccessToken, cancellationToken).ConfigureAwait(false);
        var added = 0;
        var updated = 0;
        var deleted = 0;
        var conflicts = 0;
        foreach (var calendar in calendars)
        {
            var result = await SyncCalendarAsync(account, calendar, token.AccessToken, request, cancellationToken).ConfigureAwait(false);
            added += result.Added;
            updated += result.Updated;
            deleted += result.Deleted;
            conflicts += result.Conflicts;
        }
        var message = $"Google Calendar synced {added + updated} event{(added + updated == 1 ? string.Empty : "s")}; {deleted} removed"
                      + (conflicts == 0 ? "." : $"; {conflicts} conflict{(conflicts == 1 ? string.Empty : "s")} need review.");
        return new(conflicts == 0, CalendarSyncStatus.Ready, added, updated, deleted, conflicts, message);
    }

    /// <summary>
    /// Retrieves calendars async for the current operation.
    /// </summary>
    private async Task<IReadOnlyList<PlannerCalendar>> GetCalendarsAsync(CalendarAccount account, string accessToken, CancellationToken cancellationToken)
    {
        var result = new List<PlannerCalendar>();
        string? pageToken = null;
        do
        {
            var uri = WithQuery(new Uri("https://www.googleapis.com/calendar/v3/users/me/calendarList"), new Dictionary<string, string?> { ["maxResults"] = "250", ["pageToken"] = pageToken });
            using var document = await GetJsonAsync(uri, accessToken, cancellationToken).ConfigureAwait(false);
            foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
            {
                var remoteId = RequiredString(item, "id");
                var existing = await Store.GetCalendarByProviderIdAsync(account.Id, remoteId, cancellationToken).ConfigureAwait(false);
                var permission = item.TryGetProperty("accessRole", out var access) ? access.GetString() switch
                {
                    "owner" => CalendarPermission.Owner,
                    "writer" => CalendarPermission.Writer,
                    _ => CalendarPermission.Reader
                } : CalendarPermission.Reader;
                var calendar = new PlannerCalendar(existing?.Id ?? Guid.NewGuid(), account.Id, CalendarProviderKind.Google, remoteId,
                    item.TryGetProperty("summary", out var summary) ? summary.GetString() ?? "Google Calendar" : "Google Calendar",
                    item.TryGetProperty("backgroundColor", out var color) ? color.GetString() ?? "#4285F4" : "#4285F4",
                    permission, existing?.IsVisible ?? true, DateTimeOffset.UtcNow);
                await Repository.UpsertCalendarAsync(calendar, cancellationToken).ConfigureAwait(false);
                result.Add(calendar);
            }
            pageToken = document.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (!string.IsNullOrWhiteSpace(pageToken));
        return result;
    }

    /// <summary>
    /// Performs sync calendar async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<SyncCounts> SyncCalendarAsync(CalendarAccount account, PlannerCalendar calendar, string accessToken, CalendarSyncRequest request, CancellationToken cancellationToken)
    {
        var cursor = request.FullSync ? null : await Store.GetSyncCursorAsync(account.Id, calendar.Id, cancellationToken).ConfigureAwait(false);
        try { return await SyncCalendarPageLoopAsync(account, calendar, accessToken, request, cursor, cancellationToken).ConfigureAwait(false); }
        catch (CalendarHttpException ex) when (ex.CalendarStatusCode == HttpStatusCode.Gone && cursor?.SyncCursor is not null)
        {
            return await SyncCalendarPageLoopAsync(account, calendar, accessToken, request, null, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Performs sync calendar page loop async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<SyncCounts> SyncCalendarPageLoopAsync(CalendarAccount account, PlannerCalendar calendar, string accessToken, CalendarSyncRequest request,
        CalendarSyncCursor? cursor, CancellationToken cancellationToken)
    {
        var counts = new SyncCounts();
        string? pageToken = null;
        string? nextSyncToken = null;
        do
        {
            var values = new Dictionary<string, string?> { ["showDeleted"] = "true", ["maxResults"] = "2500", ["pageToken"] = pageToken };
            if (!string.IsNullOrWhiteSpace(cursor?.SyncCursor)) values["syncToken"] = cursor.SyncCursor;
            else
            {
                values["singleEvents"] = "true";
                values["timeMin"] = request.WindowStart.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                values["timeMax"] = request.WindowEnd.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            }
            var uri = WithQuery(new Uri($"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendar.ProviderCalendarId)}/events"), values);
            using var document = await GetJsonAsync(uri, accessToken, cancellationToken).ConfigureAwait(false);
            foreach (var remote in document.RootElement.GetProperty("items").EnumerateArray())
                await ApplyRemoteEventAsync(account, calendar, cursor, remote, counts, cancellationToken).ConfigureAwait(false);
            pageToken = document.RootElement.TryGetProperty("nextPageToken", out var nextPage) ? nextPage.GetString() : null;
            nextSyncToken = document.RootElement.TryGetProperty("nextSyncToken", out var nextSync) ? nextSync.GetString() : nextSyncToken;
        } while (!string.IsNullOrWhiteSpace(pageToken));

        await Store.UpsertSyncCursorAsync(new CalendarSyncCursor(account.Id, calendar.Id, nextSyncToken, null, request.WindowStart, request.WindowEnd, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
        return counts;
    }

    /// <summary>
    /// Performs apply remote event async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ApplyRemoteEventAsync(CalendarAccount account, PlannerCalendar calendar, CalendarSyncCursor? cursor, JsonElement remote, SyncCounts counts, CancellationToken cancellationToken)
    {
        var remoteId = RequiredString(remote, "id");
        var existing = await Store.GetEventByProviderIdAsync(calendar.Id, remoteId, cancellationToken).ConfigureAwait(false);
        if (existing is not null && await Store.HasUnresolvedConflictAsync(existing.Id, cancellationToken).ConfigureAwait(false))
        {
            counts.Conflicts++;
            return;
        }
        var etag = remote.TryGetProperty("etag", out var etagElement) ? etagElement.GetString() : null;
        if (remote.TryGetProperty("status", out var status) && status.GetString() == "cancelled")
        {
            if (existing is not null)
            {
                var deletedAt = DateTimeOffset.UtcNow;
                if (cursor?.LastSyncedAt is not null && existing.UpdatedAt > cursor.LastSyncedAt)
                {
                    var providerDeletion = existing with { ProviderETag = etag, UpdatedAt = deletedAt, DeletedAt = deletedAt };
                    await Store.AddConflictAsync(new CalendarConflict(Guid.NewGuid(), existing.Id, account.Id,
                        JsonSerializer.Serialize(existing), JsonSerializer.Serialize(providerDeletion), deletedAt, null, null), cancellationToken).ConfigureAwait(false);
                    counts.Conflicts++;
                }
                else
                {
                    await Store.DeleteProviderEventAsync(calendar.Id, remoteId, deletedAt, cancellationToken).ConfigureAwait(false);
                    counts.Deleted++;
                }
            }
            return;
        }

        var startsAt = ParseDate(remote.GetProperty("start"), out var isAllDay);
        var endsAt = ParseDate(remote.GetProperty("end"), out _);
        var hasAttendees = remote.TryGetProperty("attendees", out var attendees) && attendees.ValueKind == JsonValueKind.Array && attendees.GetArrayLength() > 0;
        var organiserSelf = !remote.TryGetProperty("organizer", out var organizer) || !organizer.TryGetProperty("self", out var self) || self.GetBoolean();
        var readOnly = calendar.Permission == CalendarPermission.Reader || hasAttendees || !organiserSelf;
        DateTimeOffset? reminderAt = null;
        if (remote.TryGetProperty("reminders", out var reminders) && reminders.TryGetProperty("overrides", out var overrides) && overrides.ValueKind == JsonValueKind.Array)
        {
            var minutes = overrides.EnumerateArray().Select(item => item.TryGetProperty("minutes", out var value) ? value.GetInt32() : int.MaxValue).DefaultIfEmpty(int.MaxValue).Min();
            if (minutes != int.MaxValue) reminderAt = startsAt.AddMinutes(-minutes);
        }
        var plannerEvent = new PlannerEvent(existing?.Id ?? Guid.NewGuid(), calendar.Id,
            remote.TryGetProperty("summary", out var summary) ? summary.GetString() ?? "Untitled event" : "Untitled event",
            remote.TryGetProperty("description", out var notes) ? notes.GetString() ?? string.Empty : string.Empty,
            remote.TryGetProperty("location", out var location) ? location.GetString() ?? string.Empty : string.Empty,
            startsAt, endsAt, isAllDay, null, reminderAt, readOnly, remoteId, etag,
            existing?.CreatedAt ?? DateTimeOffset.UtcNow,
            remote.TryGetProperty("updated", out var updated) && DateTimeOffset.TryParse(updated.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt) ? updatedAt : DateTimeOffset.UtcNow,
            null, TimeZoneInfo.Local.Id);
        if (existing is not null && cursor?.LastSyncedAt is not null && existing.UpdatedAt > cursor.LastSyncedAt && existing.ProviderETag != etag)
        {
            await Store.AddConflictAsync(new CalendarConflict(Guid.NewGuid(), existing.Id, account.Id,
                JsonSerializer.Serialize(existing), JsonSerializer.Serialize(plannerEvent), DateTimeOffset.UtcNow, null, null), cancellationToken).ConfigureAwait(false);
            counts.Conflicts++;
            return;
        }
        await Store.UpsertProviderEventAsync(plannerEvent, cancellationToken).ConfigureAwait(false);
        if (existing is null) counts.Added++; else counts.Updated++;
    }

    /// <summary>
    /// Performs apply outbox async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ApplyOutboxAsync(CalendarOutboxItem outbox, PlannerEvent item, string accessToken, CancellationToken cancellationToken)
    {
        var calendar = await Store.GetCalendarAsync(item.CalendarId, cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidOperationException("The event's provider calendar no longer exists.");
        if (outbox.Operation == "delete")
        {
            if (string.IsNullOrWhiteSpace(item.ProviderEventId)) return;
            using var delete = new HttpRequestMessage(HttpMethod.Delete,
                new Uri($"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendar.ProviderCalendarId)}/events/{Uri.EscapeDataString(item.ProviderEventId)}"));
            delete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await Client.SendAsync(delete, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                throw new CalendarHttpException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return;
        }

        var body = GoogleEventBody(item);
        var creating = string.IsNullOrWhiteSpace(item.ProviderEventId);
        var uri = creating
            ? new Uri($"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendar.ProviderCalendarId)}/events")
            : new Uri($"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendar.ProviderCalendarId)}/events/{Uri.EscapeDataString(item.ProviderEventId!)}");
        using var request = new HttpRequestMessage(creating ? HttpMethod.Post : HttpMethod.Patch, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var document = await SendJsonAsync(request, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        await Store.UpsertProviderEventAsync(item with
        {
            ProviderEventId = RequiredString(root, "id"),
            ProviderETag = root.TryGetProperty("etag", out var etag) ? etag.GetString() : item.ProviderETag,
            DeletedAt = null,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the google event body step owned by this component.
    /// </summary>
    private static object GoogleEventBody(PlannerEvent item) => new
    {
        summary = item.Title,
        description = item.Notes,
        location = item.Location,
        start = new { dateTime = item.StartsAt.ToString("O", CultureInfo.InvariantCulture), timeZone = item.TimeZoneId },
        end = new { dateTime = item.EndsAt.ToString("O", CultureInfo.InvariantCulture), timeZone = item.TimeZoneId },
        recurrence = string.IsNullOrWhiteSpace(item.RecurrenceRule) ? null : new[] { "RRULE:" + item.RecurrenceRule },
        reminders = item.ReminderAt is null ? new { useDefault = true, overrides = (object?)null } : new
        {
            useDefault = false,
            overrides = (object?)new[] { new { method = "popup", minutes = Math.Max(0, (int)(item.StartsAt - item.ReminderAt.Value).TotalMinutes) } }
        }
    };

    /// <summary>
    /// Performs the parse date step owned by this component.
    /// </summary>
    private static DateTimeOffset ParseDate(JsonElement value, out bool isAllDay)
    {
        if (value.TryGetProperty("dateTime", out var dateTime) && DateTimeOffset.TryParse(dateTime.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
        {
            isAllDay = false;
            return result;
        }
        if (value.TryGetProperty("date", out var date) && DateTime.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            isAllDay = true;
            return new DateTimeOffset(day, TimeSpan.Zero);
        }
        throw new JsonException("Google event contained an invalid date.");
    }

    /// <summary>
    /// Performs the required string step owned by this component.
    /// </summary>
    private static string RequiredString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && !string.IsNullOrWhiteSpace(property.GetString()) ? property.GetString()! : throw new JsonException($"Google response omitted {name}.");

    /// <summary>
    /// Represents sync counts and keeps its related state and behavior together.
    /// </summary>
    private sealed class SyncCounts { public int Added; public int Updated; public int Deleted; public int Conflicts; }
}
