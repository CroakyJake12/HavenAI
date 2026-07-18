/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/PlannerRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns PlannerRepository. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Globalization;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Represents planner repository and keeps its related state and behavior together.
/// </summary>
public sealed class PlannerRepository(ISqliteConnectionFactory factory) : IPlannerRepository, ICalendarSyncStore
{
    /// <summary>
    /// Performs ensure defaults async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        await InsertDefaultCollectionAsync(connection, transaction, PlannerDefaults.PersonalCollectionId, "Personal", 0, now, cancellationToken).ConfigureAwait(false);
        await InsertDefaultCollectionAsync(connection, transaction, PlannerDefaults.CollegeCollectionId, "College", 1, now, cancellationToken).ConfigureAwait(false);
        await InsertDefaultCollectionAsync(connection, transaction, PlannerDefaults.WorkCollectionId, "Work", 2, now, cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO planner_calendars(id,account_id,provider,provider_calendar_id,name,color,permission,is_visible,updated_at)
                VALUES($id,NULL,$provider,'local','Haven','#74E5C1',$permission,1,$updatedAt);
                """;
            command.Parameters.AddWithValue("$id", PlannerDefaults.LocalCalendarId.ToString());
            command.Parameters.AddWithValue("$provider", (int)CalendarProviderKind.Local);
            command.Parameters.AddWithValue("$permission", (int)CalendarPermission.Owner);
            command.Parameters.AddWithValue("$updatedAt", Timestamp(now));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves collections async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<PlannerCollection>> GetCollectionsAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = includeArchived
            ? "SELECT * FROM planner_collections ORDER BY is_archived,sort_order,name COLLATE NOCASE;"
            : "SELECT * FROM planner_collections WHERE is_archived=0 ORDER BY sort_order,name COLLATE NOCASE;";
        var result = new List<PlannerCollection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadCollection(reader));
        return result;
    }

    /// <summary>
    /// Performs upsert collection async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertCollectionAsync(PlannerCollection collection, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(collection.Name)) throw new ArgumentException("Collection name is required.", nameof(collection));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO planner_collections(id,name,sort_order,is_archived,created_at,updated_at)
            VALUES($id,$name,$sortOrder,$isArchived,$createdAt,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET name=excluded.name,sort_order=excluded.sort_order,is_archived=excluded.is_archived,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", collection.Id.ToString());
        command.Parameters.AddWithValue("$name", collection.Name.Trim());
        command.Parameters.AddWithValue("$sortOrder", collection.SortOrder);
        command.Parameters.AddWithValue("$isArchived", collection.IsArchived ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", Timestamp(collection.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", Timestamp(collection.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs archive collection async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task ArchiveCollectionAsync(Guid id, bool archived, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE planner_collections SET is_archived=$archived,updated_at=$updatedAt WHERE id=$id;";
        command.Parameters.AddWithValue("$archived", archived ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", Timestamp(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves task async for the current operation.
    /// </summary>
    public async Task<PlannerTask?> GetTaskAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM planner_tasks WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadTask(reader) : null;
    }

    /// <summary>
    /// Retrieves tasks async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<PlannerTask>> GetTasksAsync(PlannerTaskQuery query, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var clauses = new List<string>();
        if (query.CollectionId is not null) { clauses.Add("collection_id=$collectionId"); command.Parameters.AddWithValue("$collectionId", query.CollectionId.Value.ToString()); }
        if (query.Status is not null) { clauses.Add("status=$status"); command.Parameters.AddWithValue("$status", (int)query.Status.Value); }
        else if (!query.IncludeCompleted) clauses.Add("status NOT IN ($completed,$cancelled)");
        if (!query.IncludeCompleted)
        {
            command.Parameters.AddWithValue("$completed", (int)PlannerTaskStatus.Completed);
            command.Parameters.AddWithValue("$cancelled", (int)PlannerTaskStatus.Cancelled);
        }
        if (query.RangeStart is not null) { clauses.Add("COALESCE(due_at,starts_at) >= $rangeStart"); command.Parameters.AddWithValue("$rangeStart", Timestamp(query.RangeStart.Value)); }
        if (query.RangeEnd is not null) { clauses.Add("COALESCE(starts_at,due_at) < $rangeEnd"); command.Parameters.AddWithValue("$rangeEnd", Timestamp(query.RangeEnd.Value)); }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            clauses.Add("(title LIKE $search ESCAPE '\\' OR notes LIKE $search ESCAPE '\\' OR tags_json LIKE $search ESCAPE '\\')");
            command.Parameters.AddWithValue("$search", $"%{EscapeLike(query.Search.Trim())}%");
        }
        command.CommandText = $"SELECT * FROM planner_tasks{(clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses))} ORDER BY CASE WHEN due_at IS NULL THEN 1 ELSE 0 END,due_at,sort_order,created_at;";
        var result = new List<PlannerTask>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadTask(reader));
        return result;
    }

    /// <summary>
    /// Performs upsert task async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertTaskAsync(PlannerTask task, CancellationToken cancellationToken)
    {
        ValidateTask(task);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ValidateTaskHierarchyAsync(connection, transaction, task, cancellationToken).ConfigureAwait(false);
        await UpsertTaskAsync(connection, transaction, task, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs complete task async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task CompleteTaskAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var task = await GetTaskAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("The task no longer exists.");
        await CompleteTaskAsync(connection, transaction, task, completedAt, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs delete task async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteTaskAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM planner_tasks WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves completion history async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<PlannerTaskCompletion>> GetCompletionHistoryAsync(Guid taskId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM planner_task_completions WHERE task_id=$taskId ORDER BY completed_at DESC;";
        command.Parameters.AddWithValue("$taskId", taskId.ToString());
        var result = new List<PlannerTaskCompletion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.Guid("id"), reader.Guid("task_id"), reader.DateTimeOffset("completed_at"), reader.NullableDateTimeOffset("occurrence_due_at")));
        return result;
    }

    /// <summary>
    /// Retrieves calendars async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<PlannerCalendar>> GetCalendarsAsync(bool visibleOnly, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = visibleOnly ? "SELECT * FROM planner_calendars WHERE is_visible=1 ORDER BY provider,name;" : "SELECT * FROM planner_calendars ORDER BY provider,name;";
        var result = new List<PlannerCalendar>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadCalendar(reader));
        return result;
    }

    /// <summary>
    /// Performs upsert calendar async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertCalendarAsync(PlannerCalendar calendar, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(calendar.Name)) throw new ArgumentException("Calendar name is required.", nameof(calendar));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO planner_calendars(id,account_id,provider,provider_calendar_id,name,color,permission,is_visible,updated_at)
            VALUES($id,$accountId,$provider,$providerCalendarId,$name,$color,$permission,$isVisible,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET account_id=excluded.account_id,provider=excluded.provider,provider_calendar_id=excluded.provider_calendar_id,
              name=excluded.name,color=excluded.color,permission=excluded.permission,is_visible=excluded.is_visible,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", calendar.Id.ToString());
        command.Parameters.AddWithValue("$accountId", Db(calendar.AccountId?.ToString()));
        command.Parameters.AddWithValue("$provider", (int)calendar.Provider);
        command.Parameters.AddWithValue("$providerCalendarId", calendar.ProviderCalendarId);
        command.Parameters.AddWithValue("$name", calendar.Name.Trim());
        command.Parameters.AddWithValue("$color", calendar.Color);
        command.Parameters.AddWithValue("$permission", (int)calendar.Permission);
        command.Parameters.AddWithValue("$isVisible", calendar.IsVisible ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", Timestamp(calendar.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves events async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<PlannerEvent>> GetEventsAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, Guid? calendarId, CancellationToken cancellationToken)
    {
        if (rangeEnd <= rangeStart) throw new ArgumentException("The calendar range must end after it starts.", nameof(rangeEnd));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = calendarId is null
            ? "SELECT * FROM planner_events WHERE deleted_at IS NULL AND starts_at < $rangeEnd AND (recurrence_rule IS NOT NULL OR ends_at > $rangeStart) ORDER BY starts_at,ends_at;"
            : "SELECT * FROM planner_events WHERE deleted_at IS NULL AND calendar_id=$calendarId AND starts_at < $rangeEnd AND (recurrence_rule IS NOT NULL OR ends_at > $rangeStart) ORDER BY starts_at,ends_at;";
        command.Parameters.AddWithValue("$rangeStart", Timestamp(rangeStart));
        command.Parameters.AddWithValue("$rangeEnd", Timestamp(rangeEnd));
        if (calendarId is not null) command.Parameters.AddWithValue("$calendarId", calendarId.Value.ToString());
        var result = new List<PlannerEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var item = ReadEvent(reader);
            if (string.IsNullOrWhiteSpace(item.RecurrenceRule)) result.Add(item);
            else result.AddRange(ExpandRecurringEvent(item, rangeStart, rangeEnd));
        }
        return result.OrderBy(item => item.StartsAt).ThenBy(item => item.EndsAt).ToArray();
    }

    /// <summary>
    /// Retrieves event async for the current operation.
    /// </summary>
    public async Task<PlannerEvent?> GetEventAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM planner_events WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadEvent(reader) : null;
    }

    /// <summary>
    /// Performs upsert event async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertEventAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken)
    {
        ValidateEvent(plannerEvent);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await EnsureCalendarWritableAsync(connection, transaction, plannerEvent.CalendarId, cancellationToken).ConfigureAwait(false);
        var existing = await GetEventAsync(connection, transaction, plannerEvent.Id, cancellationToken).ConfigureAwait(false);
        if (existing?.IsReadOnly == true) throw new InvalidOperationException("This provider event is read-only.");
        await UpsertEventAsync(connection, transaction, plannerEvent, cancellationToken).ConfigureAwait(false);
        await QueueOutboxIfRemoteAsync(connection, transaction, plannerEvent, plannerEvent.ProviderEventId is null ? "create" : "update", cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs delete event async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteEventAsync(Guid id, DateTimeOffset deletedAt, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var existing = await GetEventAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false);
        if (existing is null) return;
        if (existing.IsReadOnly) throw new InvalidOperationException("This provider event is read-only.");
        await QueueOutboxIfRemoteAsync(connection, transaction, existing, "delete", cancellationToken).ConfigureAwait(false);
        await SoftDeleteEventAsync(connection, transaction, id, deletedAt, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves calendar accounts async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<CalendarAccount>> GetCalendarAccountsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM calendar_accounts ORDER BY provider,display_name;";
        var result = new List<CalendarAccount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadAccount(reader));
        return result;
    }

    /// <summary>
    /// Performs upsert calendar account async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertCalendarAccountAsync(CalendarAccount account, CancellationToken cancellationToken)
    {
        if (account.Provider == CalendarProviderKind.Local) throw new ArgumentException("Local calendars do not use provider accounts.", nameof(account));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO calendar_accounts(id,provider,display_name,account_identifier,status,status_message,last_synced_at,created_at,updated_at)
            VALUES($id,$provider,$displayName,$accountIdentifier,$status,$statusMessage,$lastSyncedAt,$createdAt,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET display_name=excluded.display_name,account_identifier=excluded.account_identifier,status=excluded.status,
              status_message=excluded.status_message,last_synced_at=excluded.last_synced_at,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", account.Id.ToString());
        command.Parameters.AddWithValue("$provider", (int)account.Provider);
        command.Parameters.AddWithValue("$displayName", account.DisplayName);
        command.Parameters.AddWithValue("$accountIdentifier", account.AccountIdentifier);
        command.Parameters.AddWithValue("$status", (int)account.Status);
        command.Parameters.AddWithValue("$statusMessage", Db(account.StatusMessage));
        command.Parameters.AddWithValue("$lastSyncedAt", Db(account.LastSyncedAt));
        command.Parameters.AddWithValue("$createdAt", Timestamp(account.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", Timestamp(account.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves unresolved conflicts async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<CalendarConflict>> GetUnresolvedConflictsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM calendar_conflicts WHERE resolved_at IS NULL ORDER BY detected_at DESC;";
        var result = new List<CalendarConflict>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadConflict(reader));
        return result;
    }

    /// <summary>
    /// Performs resolve conflict async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task ResolveConflictAsync(Guid id, CalendarConflictResolution resolution, DateTimeOffset resolvedAt, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        CalendarConflict conflict;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT * FROM calendar_conflicts WHERE id=$id AND resolved_at IS NULL;";
            select.Parameters.AddWithValue("$id", id.ToString());
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("The calendar conflict no longer exists or has already been resolved.");
            conflict = ReadConflict(reader);
        }

        var haven = JsonSerializer.Deserialize<PlannerEvent>(conflict.HavenSnapshotJson)
                    ?? throw new InvalidOperationException("The saved Haven conflict snapshot is invalid.");
        var provider = JsonSerializer.Deserialize<PlannerEvent>(conflict.ProviderSnapshotJson)
                       ?? throw new InvalidOperationException("The saved provider conflict snapshot is invalid.");

        switch (resolution)
        {
            case CalendarConflictResolution.KeepHaven:
            {
                var retained = provider.DeletedAt is null
                    ? haven
                    : haven with { ProviderEventId = null, ProviderETag = null, DeletedAt = null, UpdatedAt = resolvedAt };
                await UpsertEventAsync(connection, transaction, retained, cancellationToken).ConfigureAwait(false);
                await QueueOutboxIfRemoteAsync(connection, transaction, retained,
                    retained.ProviderEventId is null ? "create" : "update", cancellationToken).ConfigureAwait(false);
                break;
            }
            case CalendarConflictResolution.KeepProvider:
                await ExecuteAsync(connection, transaction, "DELETE FROM calendar_outbox WHERE event_id=$eventId;",
                    cancellationToken, ("$eventId", haven.Id.ToString())).ConfigureAwait(false);
                if (provider.DeletedAt is null)
                    await UpsertEventAsync(connection, transaction, provider, cancellationToken).ConfigureAwait(false);
                else
                    await SoftDeleteEventAsync(connection, transaction, haven.Id, resolvedAt, cancellationToken).ConfigureAwait(false);
                break;
            case CalendarConflictResolution.Duplicate:
            {
                await ExecuteAsync(connection, transaction, "DELETE FROM calendar_outbox WHERE event_id=$eventId;",
                    cancellationToken, ("$eventId", haven.Id.ToString())).ConfigureAwait(false);
                if (provider.DeletedAt is null)
                    await UpsertEventAsync(connection, transaction, provider, cancellationToken).ConfigureAwait(false);
                else
                    await SoftDeleteEventAsync(connection, transaction, haven.Id, resolvedAt, cancellationToken).ConfigureAwait(false);

                // Keep the Haven edit as a private local copy. This deliberately avoids creating
                // duplicate meetings or sending provider invitations while preserving both versions.
                var localCopy = haven with
                {
                    Id = Guid.NewGuid(),
                    CalendarId = PlannerDefaults.LocalCalendarId,
                    Title = haven.Title + " (Haven copy)",
                    IsReadOnly = false,
                    ProviderEventId = null,
                    ProviderETag = null,
                    CreatedAt = resolvedAt,
                    UpdatedAt = resolvedAt,
                    DeletedAt = null
                };
                await UpsertEventAsync(connection, transaction, localCopy, cancellationToken).ConfigureAwait(false);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(resolution));
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE calendar_conflicts SET resolved_at=$resolvedAt,resolution=$resolution WHERE id=$id AND resolved_at IS NULL;";
            update.Parameters.AddWithValue("$resolvedAt", Timestamp(resolvedAt));
            update.Parameters.AddWithValue("$resolution", (int)resolution);
            update.Parameters.AddWithValue("$id", id.ToString());
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves due reminders async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<PlannerReminder>> GetDueRemindersAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT $taskKind AS entity_kind,id AS entity_id,title,reminder_at,COALESCE(due_at,starts_at,reminder_at) AS occurrence_at
              FROM planner_tasks task
             WHERE reminder_at IS NOT NULL AND reminder_at <= $now AND status NOT IN ($completed,$cancelled)
               AND NOT EXISTS(SELECT 1 FROM planner_reminder_deliveries delivery
                    WHERE delivery.entity_kind=$taskKind AND delivery.entity_id=task.id
                      AND delivery.occurrence_at=COALESCE(task.due_at,task.starts_at,task.reminder_at))
            UNION ALL
            SELECT $eventKind AS entity_kind,id AS entity_id,title,reminder_at,starts_at AS occurrence_at
              FROM planner_events event
             WHERE deleted_at IS NULL AND reminder_at IS NOT NULL AND reminder_at <= $now
               AND NOT EXISTS(SELECT 1 FROM planner_reminder_deliveries delivery
                    WHERE delivery.entity_kind=$eventKind AND delivery.entity_id=event.id AND delivery.occurrence_at=event.starts_at)
            ORDER BY reminder_at
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$taskKind", (int)PlannerReminderKind.Task);
        command.Parameters.AddWithValue("$eventKind", (int)PlannerReminderKind.Event);
        command.Parameters.AddWithValue("$now", Timestamp(now));
        command.Parameters.AddWithValue("$completed", (int)PlannerTaskStatus.Completed);
        command.Parameters.AddWithValue("$cancelled", (int)PlannerTaskStatus.Cancelled);
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<PlannerReminder>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new((PlannerReminderKind)reader.Int32("entity_kind"), reader.Guid("entity_id"), reader.String("title"), reader.DateTimeOffset("reminder_at"), reader.DateTimeOffset("occurrence_at")));
        return result;
    }

    /// <summary>
    /// Performs mark reminder delivered async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task MarkReminderDeliveredAsync(PlannerReminder reminder, DateTimeOffset deliveredAt, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO planner_reminder_deliveries(entity_kind,entity_id,occurrence_at,delivered_at) VALUES($kind,$entityId,$occurrenceAt,$deliveredAt);";
        command.Parameters.AddWithValue("$kind", (int)reminder.Kind);
        command.Parameters.AddWithValue("$entityId", reminder.EntityId.ToString());
        command.Parameters.AddWithValue("$occurrenceAt", Timestamp(reminder.OccurrenceAt));
        command.Parameters.AddWithValue("$deliveredAt", Timestamp(deliveredAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves calendar async for the current operation.
    /// </summary>
    public async Task<PlannerCalendar?> GetCalendarAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM planner_calendars WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadCalendar(reader) : null;
    }

    /// <summary>
    /// Retrieves calendar by provider id async for the current operation.
    /// </summary>
    public async Task<PlannerCalendar?> GetCalendarByProviderIdAsync(Guid accountId, string providerCalendarId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM planner_calendars WHERE account_id=$accountId AND provider_calendar_id=$providerCalendarId;";
        command.Parameters.AddWithValue("$accountId", accountId.ToString());
        command.Parameters.AddWithValue("$providerCalendarId", providerCalendarId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadCalendar(reader) : null;
    }

    /// <summary>
    /// Retrieves event by provider id async for the current operation.
    /// </summary>
    public async Task<PlannerEvent?> GetEventByProviderIdAsync(Guid calendarId, string providerEventId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM planner_events WHERE calendar_id=$calendarId AND provider_event_id=$providerEventId;";
        command.Parameters.AddWithValue("$calendarId", calendarId.ToString());
        command.Parameters.AddWithValue("$providerEventId", providerEventId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadEvent(reader) : null;
    }

    /// <summary>
    /// Performs upsert provider event async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertProviderEventAsync(PlannerEvent plannerEvent, CancellationToken cancellationToken)
    {
        ValidateEvent(plannerEvent);
        if (string.IsNullOrWhiteSpace(plannerEvent.ProviderEventId)) throw new ArgumentException("Provider event ID is required.", nameof(plannerEvent));
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await UpsertEventAsync(connection, transaction, plannerEvent, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs delete provider event async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteProviderEventAsync(Guid calendarId, string providerEventId, DateTimeOffset deletedAt, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE planner_events SET deleted_at=$deletedAt,updated_at=$deletedAt WHERE calendar_id=$calendarId AND provider_event_id=$providerEventId;";
        command.Parameters.AddWithValue("$deletedAt", Timestamp(deletedAt));
        command.Parameters.AddWithValue("$calendarId", calendarId.ToString());
        command.Parameters.AddWithValue("$providerEventId", providerEventId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves sync cursor async for the current operation.
    /// </summary>
    public async Task<CalendarSyncCursor?> GetSyncCursorAsync(Guid accountId, Guid calendarId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM calendar_sync_state WHERE account_id=$accountId AND calendar_id=$calendarId;";
        command.Parameters.AddWithValue("$accountId", accountId.ToString());
        command.Parameters.AddWithValue("$calendarId", calendarId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new(accountId, calendarId, reader.NullableString("sync_cursor"), reader.NullableString("delta_link"), reader.NullableDateTimeOffset("window_start"), reader.NullableDateTimeOffset("window_end"), reader.NullableDateTimeOffset("last_synced_at"))
            : null;
    }

    /// <summary>
    /// Performs upsert sync cursor async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertSyncCursorAsync(CalendarSyncCursor cursor, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO calendar_sync_state(account_id,calendar_id,sync_cursor,delta_link,window_start,window_end,last_synced_at)
            VALUES($accountId,$calendarId,$syncCursor,$deltaLink,$windowStart,$windowEnd,$lastSyncedAt)
            ON CONFLICT(account_id,calendar_id) DO UPDATE SET sync_cursor=excluded.sync_cursor,delta_link=excluded.delta_link,
              window_start=excluded.window_start,window_end=excluded.window_end,last_synced_at=excluded.last_synced_at;
            """;
        command.Parameters.AddWithValue("$accountId", cursor.AccountId.ToString());
        command.Parameters.AddWithValue("$calendarId", cursor.CalendarId.ToString());
        command.Parameters.AddWithValue("$syncCursor", Db(cursor.SyncCursor));
        command.Parameters.AddWithValue("$deltaLink", Db(cursor.DeltaLink));
        command.Parameters.AddWithValue("$windowStart", Db(cursor.WindowStart));
        command.Parameters.AddWithValue("$windowEnd", Db(cursor.WindowEnd));
        command.Parameters.AddWithValue("$lastSyncedAt", Db(cursor.LastSyncedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves due outbox async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<CalendarOutboxItem>> GetDueOutboxAsync(Guid accountId, DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM calendar_outbox WHERE account_id=$accountId AND next_attempt_at <= $now ORDER BY created_at LIMIT $limit;";
        command.Parameters.AddWithValue("$accountId", accountId.ToString());
        command.Parameters.AddWithValue("$now", Timestamp(now));
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        var result = new List<CalendarOutboxItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.Guid("id"), reader.Guid("account_id"), reader.NullableGuid("event_id"), reader.String("operation"), reader.String("payload_json"),
                reader.Int32("attempt_count"), reader.DateTimeOffset("next_attempt_at"), reader.NullableString("last_error"), reader.DateTimeOffset("created_at")));
        return result;
    }

    /// <summary>
    /// Performs complete outbox async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task CompleteOutboxAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM calendar_outbox WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs fail outbox async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task FailOutboxAsync(Guid id, string error, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE calendar_outbox SET attempt_count=attempt_count+1,last_error=$error,next_attempt_at=$nextAttemptAt WHERE id=$id;";
        command.Parameters.AddWithValue("$error", error.Length > 2000 ? error[..2000] : error);
        command.Parameters.AddWithValue("$nextAttemptAt", Timestamp(nextAttemptAt));
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs add conflict async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task AddConflictAsync(CalendarConflict conflict, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO calendar_conflicts(id,event_id,account_id,haven_snapshot_json,provider_snapshot_json,detected_at,resolved_at,resolution)
            VALUES($id,$eventId,$accountId,$haven,$provider,$detectedAt,$resolvedAt,$resolution);
            """;
        command.Parameters.AddWithValue("$id", conflict.Id.ToString());
        command.Parameters.AddWithValue("$eventId", conflict.EventId.ToString());
        command.Parameters.AddWithValue("$accountId", conflict.AccountId.ToString());
        command.Parameters.AddWithValue("$haven", conflict.HavenSnapshotJson);
        command.Parameters.AddWithValue("$provider", conflict.ProviderSnapshotJson);
        command.Parameters.AddWithValue("$detectedAt", Timestamp(conflict.DetectedAt));
        command.Parameters.AddWithValue("$resolvedAt", Db(conflict.ResolvedAt));
        command.Parameters.AddWithValue("$resolution", conflict.Resolution is null ? DBNull.Value : (int)conflict.Resolution.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports whether has unresolved conflict async is true for the current state.
    /// </summary>
    public async Task<bool> HasUnresolvedConflictAsync(Guid eventId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM calendar_conflicts WHERE event_id=$eventId AND resolved_at IS NULL);";
        command.Parameters.AddWithValue("$eventId", eventId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0;
    }

    /// <summary>
    /// Performs apply proposal async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task ApplyProposalAsync(PlannerChangeProposal proposal, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var change in proposal.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var payload = JsonDocument.Parse(change.PayloadJson);
            await ApplyChangeAsync(connection, transaction, change, payload.RootElement, cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs apply change async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task ApplyChangeAsync(SqliteConnection connection, SqliteTransaction transaction, PlannerProposedChange change, JsonElement payload, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        switch (change.Kind)
        {
            case PlannerChangeKind.CreateTask:
            {
                var task = NewTask(payload, now);
                ValidateTask(task);
                await ValidateTaskHierarchyAsync(connection, transaction, task, cancellationToken).ConfigureAwait(false);
                await UpsertTaskAsync(connection, transaction, task, cancellationToken).ConfigureAwait(false);
                break;
            }
            case PlannerChangeKind.UpdateTask:
            {
                var existing = await GetTaskAsync(connection, transaction, RequiredId(change), cancellationToken).ConfigureAwait(false)
                               ?? throw new InvalidOperationException("The task in this proposal no longer exists.");
                var task = MergeTask(existing, payload, now);
                ValidateTask(task);
                await ValidateTaskHierarchyAsync(connection, transaction, task, cancellationToken).ConfigureAwait(false);
                await UpsertTaskAsync(connection, transaction, task, cancellationToken).ConfigureAwait(false);
                break;
            }
            case PlannerChangeKind.CompleteTask:
            {
                var task = await GetTaskAsync(connection, transaction, RequiredId(change), cancellationToken).ConfigureAwait(false)
                           ?? throw new InvalidOperationException("The task in this proposal no longer exists.");
                await CompleteTaskAsync(connection, transaction, task, now, cancellationToken).ConfigureAwait(false);
                break;
            }
            case PlannerChangeKind.DeleteTask:
                await ExecuteAsync(connection, transaction, "DELETE FROM planner_tasks WHERE id=$id;", cancellationToken,
                    ("$id", RequiredId(change).ToString())).ConfigureAwait(false);
                break;
            case PlannerChangeKind.CreateEvent:
            {
                var plannerEvent = NewEvent(payload, now);
                ValidateEvent(plannerEvent);
                await EnsureCalendarWritableAsync(connection, transaction, plannerEvent.CalendarId, cancellationToken).ConfigureAwait(false);
                await UpsertEventAsync(connection, transaction, plannerEvent, cancellationToken).ConfigureAwait(false);
                await QueueOutboxIfRemoteAsync(connection, transaction, plannerEvent, "create", cancellationToken).ConfigureAwait(false);
                break;
            }
            case PlannerChangeKind.UpdateEvent:
            {
                var existing = await GetEventAsync(connection, transaction, RequiredId(change), cancellationToken).ConfigureAwait(false)
                               ?? throw new InvalidOperationException("The event in this proposal no longer exists.");
                if (existing.IsReadOnly) throw new InvalidOperationException("The proposal cannot change a read-only provider event.");
                var plannerEvent = MergeEvent(existing, payload, now);
                ValidateEvent(plannerEvent);
                await EnsureCalendarWritableAsync(connection, transaction, plannerEvent.CalendarId, cancellationToken).ConfigureAwait(false);
                await UpsertEventAsync(connection, transaction, plannerEvent, cancellationToken).ConfigureAwait(false);
                await QueueOutboxIfRemoteAsync(connection, transaction, plannerEvent, plannerEvent.ProviderEventId is null ? "create" : "update", cancellationToken).ConfigureAwait(false);
                break;
            }
            case PlannerChangeKind.DeleteEvent:
            {
                var existing = await GetEventAsync(connection, transaction, RequiredId(change), cancellationToken).ConfigureAwait(false);
                if (existing?.IsReadOnly == true) throw new InvalidOperationException("The proposal cannot delete a read-only provider event.");
                if (existing is not null)
                {
                    await QueueOutboxIfRemoteAsync(connection, transaction, existing, "delete", cancellationToken).ConfigureAwait(false);
                    await SoftDeleteEventAsync(connection, transaction, existing.Id, now, cancellationToken).ConfigureAwait(false);
                }
                break;
            }
            default: throw new ArgumentOutOfRangeException(nameof(change), change.Kind, "Unsupported planner operation.");
        }
    }

    /// <summary>
    /// Performs the new task step owned by this component.
    /// </summary>
    private static PlannerTask NewTask(JsonElement value, DateTimeOffset now) => new(
        Guid.NewGuid(), RequiredGuid(value, "collectionId"), OptionalGuid(value, "parentTaskId"), RequiredString(value, "title"),
        OptionalString(value, "notes") ?? string.Empty, OptionalEnum(value, "priority", PlannerPriority.None),
        OptionalEnum(value, "status", PlannerTaskStatus.Inbox), JsonValue(value, "tags", "[]"), OptionalInt(value, "estimatedMinutes"),
        OptionalDate(value, "startsAt"), OptionalDate(value, "dueAt"), OptionalString(value, "recurrenceRule"), OptionalDate(value, "reminderAt"),
        null, OptionalInt(value, "sortOrder") ?? 0, now, now, OptionalString(value, "timeZoneId") ?? "UTC");

    /// <summary>
    /// Performs the merge task step owned by this component.
    /// </summary>
    private static PlannerTask MergeTask(PlannerTask task, JsonElement value, DateTimeOffset now) => task with
    {
        CollectionId = OptionalGuid(value, "collectionId") ?? task.CollectionId,
        ParentTaskId = HasProperty(value, "parentTaskId") ? OptionalGuid(value, "parentTaskId") : task.ParentTaskId,
        Title = OptionalString(value, "title") ?? task.Title,
        Notes = OptionalString(value, "notes") ?? task.Notes,
        Priority = OptionalEnum(value, "priority", task.Priority),
        Status = OptionalEnum(value, "status", task.Status),
        TagsJson = HasProperty(value, "tags") ? JsonValue(value, "tags", "[]") : task.TagsJson,
        EstimatedMinutes = HasProperty(value, "estimatedMinutes") ? OptionalInt(value, "estimatedMinutes") : task.EstimatedMinutes,
        StartsAt = HasProperty(value, "startsAt") ? OptionalDate(value, "startsAt") : task.StartsAt,
        DueAt = HasProperty(value, "dueAt") ? OptionalDate(value, "dueAt") : task.DueAt,
        RecurrenceRule = HasProperty(value, "recurrenceRule") ? OptionalString(value, "recurrenceRule") : task.RecurrenceRule,
        ReminderAt = HasProperty(value, "reminderAt") ? OptionalDate(value, "reminderAt") : task.ReminderAt,
        SortOrder = OptionalInt(value, "sortOrder") ?? task.SortOrder,
        TimeZoneId = OptionalString(value, "timeZoneId") ?? task.TimeZoneId,
        UpdatedAt = now
    };

    /// <summary>
    /// Performs the new event step owned by this component.
    /// </summary>
    private static PlannerEvent NewEvent(JsonElement value, DateTimeOffset now) => new(
        Guid.NewGuid(), RequiredGuid(value, "calendarId"), RequiredString(value, "title"), OptionalString(value, "notes") ?? string.Empty,
        OptionalString(value, "location") ?? string.Empty, RequiredDate(value, "startsAt"), RequiredDate(value, "endsAt"),
        OptionalBool(value, "isAllDay"), OptionalString(value, "recurrenceRule"), OptionalDate(value, "reminderAt"), false, null, null,
        now, now, null, OptionalString(value, "timeZoneId") ?? "UTC");

    /// <summary>
    /// Performs the merge event step owned by this component.
    /// </summary>
    private static PlannerEvent MergeEvent(PlannerEvent item, JsonElement value, DateTimeOffset now) => item with
    {
        CalendarId = OptionalGuid(value, "calendarId") ?? item.CalendarId,
        Title = OptionalString(value, "title") ?? item.Title,
        Notes = OptionalString(value, "notes") ?? item.Notes,
        Location = OptionalString(value, "location") ?? item.Location,
        StartsAt = OptionalDate(value, "startsAt") ?? item.StartsAt,
        EndsAt = OptionalDate(value, "endsAt") ?? item.EndsAt,
        IsAllDay = HasProperty(value, "isAllDay") ? OptionalBool(value, "isAllDay") : item.IsAllDay,
        RecurrenceRule = HasProperty(value, "recurrenceRule") ? OptionalString(value, "recurrenceRule") : item.RecurrenceRule,
        ReminderAt = HasProperty(value, "reminderAt") ? OptionalDate(value, "reminderAt") : item.ReminderAt,
        TimeZoneId = OptionalString(value, "timeZoneId") ?? item.TimeZoneId,
        UpdatedAt = now
    };

    /// <summary>
    /// Validates task before it crosses the next trust or persistence boundary.
    /// </summary>
    private static void ValidateTask(PlannerTask task)
    {
        if (task.Id == Guid.Empty || task.CollectionId == Guid.Empty) throw new ArgumentException("Task and collection IDs are required.", nameof(task));
        if (string.IsNullOrWhiteSpace(task.Title)) throw new ArgumentException("Task title is required.", nameof(task));
        if (task.ParentTaskId == task.Id) throw new ArgumentException("A task cannot be its own parent.", nameof(task));
        if (task.EstimatedMinutes < 0) throw new ArgumentException("Estimated minutes cannot be negative.", nameof(task));
        if (task.StartsAt is not null && task.DueAt is not null && task.DueAt < task.StartsAt) throw new ArgumentException("Task due time cannot be before its start.", nameof(task));
        using var tags = JsonDocument.Parse(string.IsNullOrWhiteSpace(task.TagsJson) ? "[]" : task.TagsJson);
        if (tags.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException("Task tags must be a JSON array.", nameof(task));
        PlannerRecurrence.Validate(task.RecurrenceRule);
        _ = ResolveTimeZone(task.TimeZoneId);
    }

    /// <summary>
    /// Validates event before it crosses the next trust or persistence boundary.
    /// </summary>
    private static void ValidateEvent(PlannerEvent plannerEvent)
    {
        if (plannerEvent.Id == Guid.Empty || plannerEvent.CalendarId == Guid.Empty) throw new ArgumentException("Event and calendar IDs are required.", nameof(plannerEvent));
        if (string.IsNullOrWhiteSpace(plannerEvent.Title)) throw new ArgumentException("Event title is required.", nameof(plannerEvent));
        if (plannerEvent.EndsAt <= plannerEvent.StartsAt) throw new ArgumentException("An event must end after it starts.", nameof(plannerEvent));
        PlannerRecurrence.Validate(plannerEvent.RecurrenceRule);
        _ = ResolveTimeZone(plannerEvent.TimeZoneId);
    }

    /// <summary>
    /// Performs the expand recurring event step owned by this component.
    /// </summary>
    private static IEnumerable<PlannerEvent> ExpandRecurringEvent(PlannerEvent item, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        var duration = item.EndsAt - item.StartsAt;
        var occurrence = item.StartsAt;
        for (var iteration = 0; iteration < 10_000 && occurrence < rangeEnd; iteration++)
        {
            var occurrenceEnd = occurrence + duration;
            if (occurrenceEnd > rangeStart) yield return item with { StartsAt = occurrence, EndsAt = occurrenceEnd };
            occurrence = PlannerRecurrence.GetNextOccurrence(occurrence, item.RecurrenceRule, item.TimeZoneId)
                         ?? throw new InvalidOperationException("A recurring event did not produce its next occurrence.");
        }
    }

    /// <summary>
    /// Performs the resolve time zone step owned by this component.
    /// </summary>
    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? "UTC" : id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new ArgumentException($"Unknown time zone '{id}'.", nameof(id), ex); }
    }

    /// <summary>
    /// Validates task hierarchy async before it crosses the next trust or persistence boundary.
    /// </summary>
    private static async Task ValidateTaskHierarchyAsync(SqliteConnection connection, SqliteTransaction transaction, PlannerTask task, CancellationToken cancellationToken)
    {
        if (task.ParentTaskId is null) return;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH RECURSIVE ancestors(id,parent_task_id,collection_id) AS (
                SELECT id,parent_task_id,collection_id FROM planner_tasks WHERE id=$parentId
                UNION ALL
                SELECT task.id,task.parent_task_id,task.collection_id FROM planner_tasks task JOIN ancestors ON task.id=ancestors.parent_task_id
            )
            SELECT id,parent_task_id,collection_id FROM ancestors;
            """;
        command.Parameters.AddWithValue("$parentId", task.ParentTaskId.Value.ToString());
        var found = false;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            found = true;
            if (reader.String("collection_id") != task.CollectionId.ToString()) throw new InvalidOperationException("A subtask must be in the same collection as its parent.");
            if (reader.Guid("id") == task.Id) throw new InvalidOperationException("That parent would create a task cycle.");
        }
        if (!found) throw new InvalidOperationException("The selected parent task does not exist.");
    }

    /// <summary>
    /// Performs complete task async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task CompleteTaskAsync(SqliteConnection connection, SqliteTransaction transaction, PlannerTask task, DateTimeOffset completedAt, CancellationToken cancellationToken)
    {
        await using (var history = connection.CreateCommand())
        {
            history.Transaction = transaction;
            history.CommandText = "INSERT INTO planner_task_completions(id,task_id,completed_at,occurrence_due_at) VALUES($id,$taskId,$completedAt,$occurrenceDueAt);";
            history.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            history.Parameters.AddWithValue("$taskId", task.Id.ToString());
            history.Parameters.AddWithValue("$completedAt", Timestamp(completedAt));
            history.Parameters.AddWithValue("$occurrenceDueAt", Db(task.DueAt));
            await history.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var occurrence = task.DueAt ?? task.StartsAt ?? completedAt;
        var next = PlannerRecurrence.GetNextOccurrence(occurrence, task.RecurrenceRule, task.TimeZoneId);
        PlannerTask updated;
        if (next is null) updated = task with { Status = PlannerTaskStatus.Completed, CompletedAt = completedAt, UpdatedAt = completedAt };
        else
        {
            var startDelta = task.StartsAt is not null ? occurrence - task.StartsAt.Value : (TimeSpan?)null;
            var reminderDelta = task.ReminderAt is not null ? occurrence - task.ReminderAt.Value : (TimeSpan?)null;
            updated = task with
            {
                Status = PlannerTaskStatus.Planned,
                StartsAt = startDelta is null ? null : next.Value - startDelta.Value,
                DueAt = task.DueAt is null ? null : next,
                ReminderAt = reminderDelta is null ? null : next.Value - reminderDelta.Value,
                CompletedAt = null,
                UpdatedAt = completedAt
            };
        }
        await UpsertTaskAsync(connection, transaction, updated, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves task async for the current operation.
    /// </summary>
    private static async Task<PlannerTask?> GetTaskAsync(SqliteConnection connection, SqliteTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM planner_tasks WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadTask(reader) : null;
    }

    /// <summary>
    /// Retrieves event async for the current operation.
    /// </summary>
    private static async Task<PlannerEvent?> GetEventAsync(SqliteConnection connection, SqliteTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM planner_events WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadEvent(reader) : null;
    }

    /// <summary>
    /// Performs ensure calendar writable async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task EnsureCalendarWritableAsync(SqliteConnection connection, SqliteTransaction transaction, Guid calendarId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT permission FROM planner_calendars WHERE id=$id;";
        command.Parameters.AddWithValue("$id", calendarId.ToString());
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null) throw new InvalidOperationException("The selected calendar no longer exists.");
        if ((CalendarPermission)Convert.ToInt32(value, CultureInfo.InvariantCulture) == CalendarPermission.Reader)
            throw new InvalidOperationException("The selected provider calendar is read-only.");
    }

    /// <summary>
    /// Performs upsert task async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task UpsertTaskAsync(SqliteConnection connection, SqliteTransaction transaction, PlannerTask task, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO planner_tasks(id,collection_id,parent_task_id,title,notes,priority,status,tags_json,estimated_minutes,starts_at,due_at,recurrence_rule,reminder_at,completed_at,sort_order,time_zone_id,created_at,updated_at)
            VALUES($id,$collectionId,$parentTaskId,$title,$notes,$priority,$status,$tagsJson,$estimatedMinutes,$startsAt,$dueAt,$recurrenceRule,$reminderAt,$completedAt,$sortOrder,$timeZoneId,$createdAt,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET collection_id=excluded.collection_id,parent_task_id=excluded.parent_task_id,title=excluded.title,notes=excluded.notes,
              priority=excluded.priority,status=excluded.status,tags_json=excluded.tags_json,estimated_minutes=excluded.estimated_minutes,starts_at=excluded.starts_at,
              due_at=excluded.due_at,recurrence_rule=excluded.recurrence_rule,reminder_at=excluded.reminder_at,completed_at=excluded.completed_at,
              sort_order=excluded.sort_order,time_zone_id=excluded.time_zone_id,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", task.Id.ToString());
        command.Parameters.AddWithValue("$collectionId", task.CollectionId.ToString());
        command.Parameters.AddWithValue("$parentTaskId", Db(task.ParentTaskId?.ToString()));
        command.Parameters.AddWithValue("$title", task.Title.Trim());
        command.Parameters.AddWithValue("$notes", task.Notes);
        command.Parameters.AddWithValue("$priority", (int)task.Priority);
        command.Parameters.AddWithValue("$status", (int)task.Status);
        command.Parameters.AddWithValue("$tagsJson", string.IsNullOrWhiteSpace(task.TagsJson) ? "[]" : task.TagsJson);
        command.Parameters.AddWithValue("$estimatedMinutes", task.EstimatedMinutes is null ? DBNull.Value : task.EstimatedMinutes.Value);
        command.Parameters.AddWithValue("$startsAt", Db(task.StartsAt));
        command.Parameters.AddWithValue("$dueAt", Db(task.DueAt));
        command.Parameters.AddWithValue("$recurrenceRule", Db(task.RecurrenceRule));
        command.Parameters.AddWithValue("$reminderAt", Db(task.ReminderAt));
        command.Parameters.AddWithValue("$completedAt", Db(task.CompletedAt));
        command.Parameters.AddWithValue("$sortOrder", task.SortOrder);
        command.Parameters.AddWithValue("$timeZoneId", task.TimeZoneId);
        command.Parameters.AddWithValue("$createdAt", Timestamp(task.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", Timestamp(task.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs upsert event async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task UpsertEventAsync(SqliteConnection connection, SqliteTransaction transaction, PlannerEvent item, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO planner_events(id,calendar_id,title,notes,location,starts_at,ends_at,is_all_day,recurrence_rule,reminder_at,is_read_only,provider_event_id,provider_etag,time_zone_id,created_at,updated_at,deleted_at)
            VALUES($id,$calendarId,$title,$notes,$location,$startsAt,$endsAt,$isAllDay,$recurrenceRule,$reminderAt,$isReadOnly,$providerEventId,$providerETag,$timeZoneId,$createdAt,$updatedAt,$deletedAt)
            ON CONFLICT(id) DO UPDATE SET calendar_id=excluded.calendar_id,title=excluded.title,notes=excluded.notes,location=excluded.location,
              starts_at=excluded.starts_at,ends_at=excluded.ends_at,is_all_day=excluded.is_all_day,recurrence_rule=excluded.recurrence_rule,
              reminder_at=excluded.reminder_at,is_read_only=excluded.is_read_only,provider_event_id=excluded.provider_event_id,provider_etag=excluded.provider_etag,
              time_zone_id=excluded.time_zone_id,updated_at=excluded.updated_at,deleted_at=excluded.deleted_at;
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$calendarId", item.CalendarId.ToString());
        command.Parameters.AddWithValue("$title", item.Title.Trim());
        command.Parameters.AddWithValue("$notes", item.Notes);
        command.Parameters.AddWithValue("$location", item.Location);
        command.Parameters.AddWithValue("$startsAt", Timestamp(item.StartsAt));
        command.Parameters.AddWithValue("$endsAt", Timestamp(item.EndsAt));
        command.Parameters.AddWithValue("$isAllDay", item.IsAllDay ? 1 : 0);
        command.Parameters.AddWithValue("$recurrenceRule", Db(item.RecurrenceRule));
        command.Parameters.AddWithValue("$reminderAt", Db(item.ReminderAt));
        command.Parameters.AddWithValue("$isReadOnly", item.IsReadOnly ? 1 : 0);
        command.Parameters.AddWithValue("$providerEventId", Db(item.ProviderEventId));
        command.Parameters.AddWithValue("$providerETag", Db(item.ProviderETag));
        command.Parameters.AddWithValue("$timeZoneId", item.TimeZoneId);
        command.Parameters.AddWithValue("$createdAt", Timestamp(item.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", Timestamp(item.UpdatedAt));
        command.Parameters.AddWithValue("$deletedAt", Db(item.DeletedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs soft delete event async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task SoftDeleteEventAsync(SqliteConnection connection, SqliteTransaction transaction, Guid id, DateTimeOffset deletedAt, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction, "UPDATE planner_events SET deleted_at=$deletedAt,updated_at=$deletedAt WHERE id=$id;",
            cancellationToken, ("$deletedAt", Timestamp(deletedAt)), ("$id", id.ToString())).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs queue outbox if remote async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task QueueOutboxIfRemoteAsync(SqliteConnection connection, SqliteTransaction transaction, PlannerEvent item, string operation, CancellationToken cancellationToken)
    {
        Guid? accountId;
        await using (var calendar = connection.CreateCommand())
        {
            calendar.Transaction = transaction;
            calendar.CommandText = "SELECT account_id FROM planner_calendars WHERE id=$id AND provider<>$local;";
            calendar.Parameters.AddWithValue("$id", item.CalendarId.ToString());
            calendar.Parameters.AddWithValue("$local", (int)CalendarProviderKind.Local);
            var value = await calendar.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            accountId = value is string text && Guid.TryParse(text, out var parsed) ? parsed : null;
        }
        if (accountId is null) return;

        Guid? existingId = null;
        string? existingOperation = null;
        await using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = "SELECT id,operation FROM calendar_outbox WHERE account_id=$accountId AND event_id=$eventId ORDER BY created_at LIMIT 1;";
            existing.Parameters.AddWithValue("$accountId", accountId.Value.ToString());
            existing.Parameters.AddWithValue("$eventId", item.Id.ToString());
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                existingId = reader.Guid("id");
                existingOperation = reader.String("operation");
            }
        }

        if (existingId is not null && existingOperation == "create" && operation == "delete")
        {
            await ExecuteAsync(connection, transaction, "DELETE FROM calendar_outbox WHERE id=$id;", cancellationToken, ("$id", existingId.Value.ToString())).ConfigureAwait(false);
            return;
        }

        var effectiveOperation = existingOperation == "create" ? "create" : operation;
        var payload = JsonSerializer.Serialize(item);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (existingId is null)
        {
            command.CommandText = """
                INSERT INTO calendar_outbox(id,account_id,event_id,operation,payload_json,attempt_count,next_attempt_at,last_error,created_at)
                VALUES($id,$accountId,$eventId,$operation,$payload,0,$now,NULL,$now);
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$accountId", accountId.Value.ToString());
            command.Parameters.AddWithValue("$eventId", item.Id.ToString());
        }
        else
        {
            command.CommandText = "UPDATE calendar_outbox SET operation=$operation,payload_json=$payload,attempt_count=0,next_attempt_at=$now,last_error=NULL WHERE id=$id;";
            command.Parameters.AddWithValue("$id", existingId.Value.ToString());
        }
        command.Parameters.AddWithValue("$operation", effectiveOperation);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$now", Timestamp(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs insert default collection async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task InsertDefaultCollectionAsync(SqliteConnection connection, SqliteTransaction transaction, Guid id, string name, int sortOrder, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction,
            "INSERT OR IGNORE INTO planner_collections(id,name,sort_order,is_archived,created_at,updated_at) VALUES($id,$name,$sortOrder,0,$now,$now);",
            cancellationToken, ("$id", id.ToString()), ("$name", name), ("$sortOrder", sortOrder), ("$now", Timestamp(now))).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs execute async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] values)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var pair in values) command.Parameters.AddWithValue(pair.Name, pair.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the read collection step owned by this component.
    /// </summary>
    private static PlannerCollection ReadCollection(SqliteDataReader reader) => new(reader.Guid("id"), reader.String("name"), reader.Int32("sort_order"), reader.Boolean("is_archived"), reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at"));
    /// <summary>
    /// Performs the read task step owned by this component.
    /// </summary>
    private static PlannerTask ReadTask(SqliteDataReader reader) => new(reader.Guid("id"), reader.Guid("collection_id"), reader.NullableGuid("parent_task_id"), reader.String("title"), reader.String("notes"),
        (PlannerPriority)reader.Int32("priority"), (PlannerTaskStatus)reader.Int32("status"), reader.String("tags_json"), NullableInt32(reader, "estimated_minutes"),
        reader.NullableDateTimeOffset("starts_at"), reader.NullableDateTimeOffset("due_at"), reader.NullableString("recurrence_rule"), reader.NullableDateTimeOffset("reminder_at"),
        reader.NullableDateTimeOffset("completed_at"), reader.Int32("sort_order"), reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at"), reader.String("time_zone_id"));
    /// <summary>
    /// Performs the read calendar step owned by this component.
    /// </summary>
    private static PlannerCalendar ReadCalendar(SqliteDataReader reader) => new(reader.Guid("id"), reader.NullableGuid("account_id"), (CalendarProviderKind)reader.Int32("provider"), reader.String("provider_calendar_id"), reader.String("name"), reader.String("color"), (CalendarPermission)reader.Int32("permission"), reader.Boolean("is_visible"), reader.DateTimeOffset("updated_at"));
    /// <summary>
    /// Performs the read event step owned by this component.
    /// </summary>
    private static PlannerEvent ReadEvent(SqliteDataReader reader) => new(reader.Guid("id"), reader.Guid("calendar_id"), reader.String("title"), reader.String("notes"), reader.String("location"), reader.DateTimeOffset("starts_at"), reader.DateTimeOffset("ends_at"), reader.Boolean("is_all_day"), reader.NullableString("recurrence_rule"), reader.NullableDateTimeOffset("reminder_at"), reader.Boolean("is_read_only"), reader.NullableString("provider_event_id"), reader.NullableString("provider_etag"), reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at"), reader.NullableDateTimeOffset("deleted_at"), reader.String("time_zone_id"));
    /// <summary>
    /// Performs the read account step owned by this component.
    /// </summary>
    private static CalendarAccount ReadAccount(SqliteDataReader reader) => new(reader.Guid("id"), (CalendarProviderKind)reader.Int32("provider"), reader.String("display_name"), reader.String("account_identifier"), (CalendarSyncStatus)reader.Int32("status"), reader.NullableString("status_message"), reader.NullableDateTimeOffset("last_synced_at"), reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at"));
    /// <summary>
    /// Performs the read conflict step owned by this component.
    /// </summary>
    private static CalendarConflict ReadConflict(SqliteDataReader reader)
    {
        var ordinal = reader.GetOrdinal("resolution");
        var resolution = reader.IsDBNull(ordinal) ? (CalendarConflictResolution?)null : (CalendarConflictResolution)reader.GetInt32(ordinal);
        return new(reader.Guid("id"), reader.Guid("event_id"), reader.Guid("account_id"), reader.String("haven_snapshot_json"), reader.String("provider_snapshot_json"), reader.DateTimeOffset("detected_at"), reader.NullableDateTimeOffset("resolved_at"), resolution);
    }

    /// <summary>
    /// Performs the nullable int32 step owned by this component.
    /// </summary>
    private static int? NullableInt32(SqliteDataReader reader, string name) { var ordinal = reader.GetOrdinal(name); return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal); }
    /// <summary>
    /// Performs the timestamp step owned by this component.
    /// </summary>
    private static string Timestamp(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    /// <summary>
    /// Performs the db step owned by this component.
    /// </summary>
    private static object Db(string? value) => (object?)value ?? DBNull.Value;
    /// <summary>
    /// Performs the db step owned by this component.
    /// </summary>
    private static object Db(DateTimeOffset? value) => value is null ? DBNull.Value : Timestamp(value.Value);
    /// <summary>
    /// Performs the escape like step owned by this component.
    /// </summary>
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
    /// <summary>
    /// Performs the required id step owned by this component.
    /// </summary>
    private static Guid RequiredId(PlannerProposedChange change) => change.EntityId ?? throw new InvalidOperationException($"{change.Kind} requires an entity ID.");
    /// <summary>
    /// Reports whether has property is true for the current state.
    /// </summary>
    private static bool HasProperty(JsonElement value, string name) => value.TryGetProperty(name, out _);
    /// <summary>
    /// Performs the required string step owned by this component.
    /// </summary>
    private static string RequiredString(JsonElement value, string name) => OptionalString(value, name) ?? throw new InvalidOperationException($"{name} is required.");
    /// <summary>
    /// Performs the optional string step owned by this component.
    /// </summary>
    private static string? OptionalString(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind != JsonValueKind.Null ? item.GetString() : null;
    /// <summary>
    /// Performs the required guid step owned by this component.
    /// </summary>
    private static Guid RequiredGuid(JsonElement value, string name) => OptionalGuid(value, name) ?? throw new InvalidOperationException($"{name} is required.");
    /// <summary>
    /// Performs the optional guid step owned by this component.
    /// </summary>
    private static Guid? OptionalGuid(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind != JsonValueKind.Null ? Guid.Parse(item.GetString()!) : null;
    /// <summary>
    /// Performs the optional int step owned by this component.
    /// </summary>
    private static int? OptionalInt(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind != JsonValueKind.Null ? item.GetInt32() : null;
    /// <summary>
    /// Performs the optional bool step owned by this component.
    /// </summary>
    private static bool OptionalBool(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.True;
    /// <summary>
    /// Performs the required date step owned by this component.
    /// </summary>
    private static DateTimeOffset RequiredDate(JsonElement value, string name) => OptionalDate(value, name) ?? throw new InvalidOperationException($"{name} is required.");
    /// <summary>
    /// Performs the optional date step owned by this component.
    /// </summary>
    private static DateTimeOffset? OptionalDate(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind != JsonValueKind.Null ? DateTimeOffset.Parse(item.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : null;
    private static T OptionalEnum<T>(JsonElement value, string name, T fallback) where T : struct, Enum => value.TryGetProperty(name, out var item) && item.ValueKind != JsonValueKind.Null ? Enum.Parse<T>(item.GetString()!, true) : fallback;
    /// <summary>
    /// Performs the json value step owned by this component.
    /// </summary>
    private static string JsonValue(JsonElement value, string name, string fallback) => value.TryGetProperty(name, out var item) ? item.GetRawText() : fallback;
}
