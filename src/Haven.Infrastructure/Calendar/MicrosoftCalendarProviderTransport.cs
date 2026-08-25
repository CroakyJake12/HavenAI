/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/MicrosoftCalendarProviderTransport.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns MicrosoftCalendarProviderTransport, SyncCounts. Read the type and member comments below as a map of each responsibility.
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
/// Represents microsoft calendar provider transport and keeps its related state and behavior together.
/// </summary>
public sealed class MicrosoftCalendarProviderTransport(
    CalendarProviderConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IPlannerRepository repository,
    ICalendarSyncStore store,
    ICalendarTokenStore tokenStore,
    IOAuthBrowserLauncher browserLauncher)
    : OAuthCalendarTransportBase(configuration, httpClientFactory, repository, store, tokenStore, browserLauncher)
{
    /// <summary>
    /// Gets or updates authorization endpoint, the bindable or domain state represented by this property.
    /// </summary>
    protected override Uri AuthorizationEndpoint { get; } = new("https://login.microsoftonline.com/common/oauth2/v2.0/authorize");
    /// <summary>
    /// Gets or updates token endpoint, the bindable or domain state represented by this property.
    /// </summary>
    protected override Uri TokenEndpoint { get; } = new("https://login.microsoftonline.com/common/oauth2/v2.0/token");

    protected override async Task<(string Identifier, string DisplayName)> GetIdentityAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(new Uri("https://graph.microsoft.com/v1.0/me?$select=displayName,mail,userPrincipalName,id"), accessToken, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var identifier = root.TryGetProperty("mail", out var mail) && !string.IsNullOrWhiteSpace(mail.GetString())
            ? mail.GetString()
            : root.TryGetProperty("userPrincipalName", out var principal) ? principal.GetString() : root.GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(identifier)) throw new JsonException("Microsoft identity response omitted the account identifier.");
        var displayName = root.TryGetProperty("displayName", out var name) ? name.GetString() : null;
        return (identifier, string.IsNullOrWhiteSpace(displayName) ? identifier : displayName);
    }

    /// <summary>
    /// Performs sync core asynchronously so I/O does not block the caller's thread.
    /// </summary>
    protected override async Task<CalendarSyncResult> SyncCoreAsync(CalendarAccount account, CalendarTokenEnvelope token, CalendarSyncRequest request, CancellationToken cancellationToken)
    {
        await DrainOutboxAsync(account, token, ApplyOutboxAsync, cancellationToken).ConfigureAwait(false);
        var calendars = await GetCalendarsAsync(account, token.AccessToken, cancellationToken).ConfigureAwait(false);
        var counts = new SyncCounts();
        foreach (var calendar in calendars)
            await SyncCalendarAsync(account, calendar, token.AccessToken, request, counts, cancellationToken).ConfigureAwait(false);
        var message = $"Microsoft Calendar synced {counts.Added + counts.Updated} event{(counts.Added + counts.Updated == 1 ? string.Empty : "s")}; {counts.Deleted} removed"
                      + (counts.Conflicts == 0 ? "." : $"; {counts.Conflicts} conflict{(counts.Conflicts == 1 ? string.Empty : "s")} need review.");
        return new(counts.Conflicts == 0, CalendarSyncStatus.Ready, counts.Added, counts.Updated, counts.Deleted, counts.Conflicts, message);
    }

    /// <summary>
    /// Retrieves calendars async for the current operation.
    /// </summary>
    private async Task<IReadOnlyList<PlannerCalendar>> GetCalendarsAsync(CalendarAccount account, string accessToken, CancellationToken cancellationToken)
    {
        var result = new List<PlannerCalendar>();
        Uri? uri = new("https://graph.microsoft.com/v1.0/me/calendars?$select=id,name,color,canEdit,owner");
        while (uri is not null)
        {
            using var document = await GetJsonAsync(uri, accessToken, cancellationToken).ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var item in values.EnumerateArray())
                {
                    var remoteId = RequiredString(item, "id");
                    var existing = await Store.GetCalendarByProviderIdAsync(account.Id, remoteId, cancellationToken).ConfigureAwait(false);
                    var canEdit = item.TryGetProperty("canEdit", out var edit) && edit.GetBoolean();
                    var ownerAddress = item.TryGetProperty("owner", out var owner) && owner.TryGetProperty("address", out var address) ? address.GetString() : null;
                    var permission = !canEdit ? CalendarPermission.Reader
                        : ownerAddress?.Equals(account.AccountIdentifier, StringComparison.OrdinalIgnoreCase) == true ? CalendarPermission.Owner : CalendarPermission.Writer;
                    var calendar = new PlannerCalendar(existing?.Id ?? Guid.NewGuid(), account.Id, CalendarProviderKind.Microsoft, remoteId,
                        item.TryGetProperty("name", out var name) ? name.GetString() ?? "Microsoft Calendar" : "Microsoft Calendar",
                        GraphColor(item.TryGetProperty("color", out var color) ? color.GetString() : null), permission, existing?.IsVisible ?? true, DateTimeOffset.UtcNow);
                    await Repository.UpsertCalendarAsync(calendar, cancellationToken).ConfigureAwait(false);
                    result.Add(calendar);
                }
            }
            uri = document.RootElement.TryGetProperty("@odata.nextLink", out var next) && Uri.TryCreate(next.GetString(), UriKind.Absolute, out var nextUri) ? nextUri : null;
        }
        return result;
    }

    /// <summary>
    /// Performs sync calendar asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SyncCalendarAsync(CalendarAccount account, PlannerCalendar calendar, string accessToken, CalendarSyncRequest request, SyncCounts counts, CancellationToken cancellationToken)
    {
        var cursor = request.FullSync ? null : await Store.GetSyncCursorAsync(account.Id, calendar.Id, cancellationToken).ConfigureAwait(false);
        var initial = new Uri($"https://graph.microsoft.com/v1.0/me/calendars/{Uri.EscapeDataString(calendar.ProviderCalendarId)}/calendarView/delta?startDateTime={Uri.EscapeDataString(request.WindowStart.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}&endDateTime={Uri.EscapeDataString(request.WindowEnd.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}");
        var uri = !string.IsNullOrWhiteSpace(cursor?.DeltaLink) && Uri.TryCreate(cursor.DeltaLink, UriKind.Absolute, out var delta) ? delta : initial;
        string? deltaLink = null;
        try
        {
            while (uri is not null)
            {
                using var document = await GetJsonAsync(uri, accessToken, cancellationToken).ConfigureAwait(false);
                if (document.RootElement.TryGetProperty("value", out var values))
                    foreach (var remote in values.EnumerateArray()) await ApplyRemoteEventAsync(account, calendar, cursor, remote, counts, cancellationToken).ConfigureAwait(false);
                deltaLink = document.RootElement.TryGetProperty("@odata.deltaLink", out var final) ? final.GetString() : deltaLink;
                uri = document.RootElement.TryGetProperty("@odata.nextLink", out var next) && Uri.TryCreate(next.GetString(), UriKind.Absolute, out var nextUri) ? nextUri : null;
            }
        }
        catch (CalendarHttpException ex) when (ex.CalendarStatusCode is HttpStatusCode.Gone or HttpStatusCode.BadRequest && cursor?.DeltaLink is not null)
        {
            await Store.UpsertSyncCursorAsync(new CalendarSyncCursor(account.Id, calendar.Id, null, null, request.WindowStart, request.WindowEnd, null), cancellationToken).ConfigureAwait(false);
            await SyncCalendarAsync(account, calendar, accessToken, request with { FullSync = true }, counts, cancellationToken).ConfigureAwait(false);
            return;
        }
        await Store.UpsertSyncCursorAsync(new CalendarSyncCursor(account.Id, calendar.Id, null, deltaLink, request.WindowStart, request.WindowEnd, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs apply remote event asynchronously so I/O does not block the caller's thread.
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
        var etag = remote.TryGetProperty("changeKey", out var changeKey) ? changeKey.GetString()
            : remote.TryGetProperty("@odata.etag", out var odataEtag) ? odataEtag.GetString() : null;
        if (remote.TryGetProperty("@removed", out _))
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

        var startsAt = ParseGraphDate(remote.GetProperty("start"));
        var endsAt = ParseGraphDate(remote.GetProperty("end"));
        var hasAttendees = remote.TryGetProperty("attendees", out var attendees) && attendees.ValueKind == JsonValueKind.Array && attendees.GetArrayLength() > 0;
        var isOrganizer = !remote.TryGetProperty("isOrganizer", out var organizer) || organizer.GetBoolean();
        var isAllDay = remote.TryGetProperty("isAllDay", out var allDay) && allDay.GetBoolean();
        DateTimeOffset? reminderAt = null;
        if (remote.TryGetProperty("isReminderOn", out var reminderOn) && reminderOn.GetBoolean()
            && remote.TryGetProperty("reminderMinutesBeforeStart", out var minutes)) reminderAt = startsAt.AddMinutes(-minutes.GetInt32());
        var item = new PlannerEvent(existing?.Id ?? Guid.NewGuid(), calendar.Id,
            remote.TryGetProperty("subject", out var subject) ? subject.GetString() ?? "Untitled event" : "Untitled event",
            remote.TryGetProperty("bodyPreview", out var body) ? body.GetString() ?? string.Empty : string.Empty,
            remote.TryGetProperty("location", out var location) && location.TryGetProperty("displayName", out var display) ? display.GetString() ?? string.Empty : string.Empty,
            startsAt, endsAt, isAllDay, null, reminderAt, calendar.Permission == CalendarPermission.Reader || hasAttendees || !isOrganizer,
            remoteId, etag, existing?.CreatedAt ?? DateTimeOffset.UtcNow,
            remote.TryGetProperty("lastModifiedDateTime", out var modified) && DateTimeOffset.TryParse(modified.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var modifiedAt) ? modifiedAt : DateTimeOffset.UtcNow,
            null, remote.TryGetProperty("start", out var start) && start.TryGetProperty("timeZone", out var zone) ? zone.GetString() ?? "UTC" : "UTC");
        if (existing is not null && cursor?.LastSyncedAt is not null && existing.UpdatedAt > cursor.LastSyncedAt && existing.ProviderETag != etag)
        {
            await Store.AddConflictAsync(new CalendarConflict(Guid.NewGuid(), existing.Id, account.Id,
                JsonSerializer.Serialize(existing), JsonSerializer.Serialize(item), DateTimeOffset.UtcNow, null, null), cancellationToken).ConfigureAwait(false);
            counts.Conflicts++;
            return;
        }
        await Store.UpsertProviderEventAsync(item, cancellationToken).ConfigureAwait(false);
        if (existing is null) counts.Added++; else counts.Updated++;
    }

    /// <summary>
    /// Performs apply outbox asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ApplyOutboxAsync(CalendarOutboxItem outbox, PlannerEvent item, string accessToken, CancellationToken cancellationToken)
    {
        var calendar = await Store.GetCalendarAsync(item.CalendarId, cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidOperationException("The event's provider calendar no longer exists.");
        if (outbox.Operation == "delete")
        {
            if (string.IsNullOrWhiteSpace(item.ProviderEventId)) return;
            using var delete = new HttpRequestMessage(HttpMethod.Delete, new Uri($"https://graph.microsoft.com/v1.0/me/events/{Uri.EscapeDataString(item.ProviderEventId)}"));
            delete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await Client.SendAsync(delete, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                throw new CalendarHttpException(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return;
        }

        var creating = string.IsNullOrWhiteSpace(item.ProviderEventId);
        var uri = creating
            ? new Uri($"https://graph.microsoft.com/v1.0/me/calendars/{Uri.EscapeDataString(calendar.ProviderCalendarId)}/events")
            : new Uri($"https://graph.microsoft.com/v1.0/me/events/{Uri.EscapeDataString(item.ProviderEventId!)}");
        using var request = new HttpRequestMessage(creating ? HttpMethod.Post : HttpMethod.Patch, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(GraphEventBody(item)), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var document = await SendJsonAsync(request, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        await Store.UpsertProviderEventAsync(item with
        {
            ProviderEventId = RequiredString(root, "id"),
            ProviderETag = root.TryGetProperty("changeKey", out var key) ? key.GetString() : item.ProviderETag,
            DeletedAt = null,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the graph event body step owned by this component.
    /// </summary>
    private static object GraphEventBody(PlannerEvent item) => new
    {
        subject = item.Title,
        body = new { contentType = "text", content = item.Notes },
        location = new { displayName = item.Location },
        start = new { dateTime = item.StartsAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), timeZone = "UTC" },
        end = new { dateTime = item.EndsAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), timeZone = "UTC" },
        isAllDay = item.IsAllDay,
        isReminderOn = item.ReminderAt is not null,
        reminderMinutesBeforeStart = item.ReminderAt is null ? 15 : Math.Max(0, (int)(item.StartsAt - item.ReminderAt.Value).TotalMinutes)
    };

    /// <summary>
    /// Performs the parse graph date step owned by this component.
    /// </summary>
    private static DateTimeOffset ParseGraphDate(JsonElement value)
    {
        var text = RequiredString(value, "dateTime");
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)) return result;
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local)) throw new JsonException("Microsoft event contained an invalid date.");
        var zoneId = value.TryGetProperty("timeZone", out var zoneElement) ? zoneElement.GetString() : "UTC";
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(zoneId) ? "UTC" : zoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { zone = TimeZoneInfo.Utc; }
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>
    /// Performs the required string step owned by this component.
    /// </summary>
    private static string RequiredString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && !string.IsNullOrWhiteSpace(property.GetString()) ? property.GetString()! : throw new JsonException($"Microsoft response omitted {name}.");

    /// <summary>
    /// Performs the graph color step owned by this component.
    /// </summary>
    private static string GraphColor(string? color) => color?.ToLowerInvariant() switch
    {
        "lightblue" or "blue" => "#4285F4", "lightgreen" or "green" => "#55B685", "lightorange" or "orange" => "#F4A261",
        "lightred" or "red" => "#E76F51", "lightpurple" => "#9B72CF", _ => "#5B8DEF"
    };

    /// <summary>
    /// Represents sync counts and keeps its related state and behavior together.
    /// </summary>
    private sealed class SyncCounts { public int Added; public int Updated; public int Deleted; public int Conflicts; }
}
