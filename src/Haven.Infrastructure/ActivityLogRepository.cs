/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/ActivityLogRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ActivityLogRepository. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents activity log repository and keeps its related state and behavior together.
/// </summary>
public sealed class ActivityLogRepository(ISqliteConnectionFactory factory) : IActivityLogRepository
{
    /// <summary>
    /// Retrieves recent async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ActivityEvent>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM activity_events ORDER BY timestamp DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        var results = new List<ActivityEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(new ActivityEvent(
                Guid.Parse(reader.String("id")),
                (ActivityEventKind)reader.Int32("kind"),
                reader.NullableGuid("conversation_id"),
                reader.NullableGuid("mode_id"),
                reader.String("summary"),
                reader.String("detail_json"),
                reader.DateTimeOffset("timestamp")));
        return results;
    }

    /// <summary>
    /// Performs add event async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task AddEventAsync(ActivityEvent activityEvent, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO activity_events(id, kind, conversation_id, mode_id, summary, detail_json, timestamp)
            VALUES($id, $kind, $conversationId, $modeId, $summary, $detailJson, $timestamp);
            """;
        command.Parameters.AddWithValue("$id", activityEvent.Id.ToString());
        command.Parameters.AddWithValue("$kind", (int)activityEvent.Kind);
        command.Parameters.AddWithValue("$conversationId", (object?)activityEvent.ConversationId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$modeId", (object?)activityEvent.ModeId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$summary", activityEvent.Summary);
        command.Parameters.AddWithValue("$detailJson", activityEvent.DetailJson);
        command.Parameters.AddWithValue("$timestamp", activityEvent.Timestamp.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
