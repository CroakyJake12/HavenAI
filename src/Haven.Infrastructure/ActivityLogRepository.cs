using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class ActivityLogRepository(ISqliteConnectionFactory factory) : IActivityLogRepository
{
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
