using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class ConversationMoveRepository(ISqliteConnectionFactory factory) : IConversationMoveRepository
{
    public async Task RecordMoveAsync(ConversationMove move, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO conversation_moves(id, conversation_id, from_mode_id, to_mode_id,
                from_placement, to_placement, reason, moved_at)
            VALUES($id, $conversationId, $fromModeId, $toModeId,
                $fromPlacement, $toPlacement, $reason, $movedAt);
            """;
        command.Parameters.AddWithValue("$id", move.Id.ToString());
        command.Parameters.AddWithValue("$conversationId", move.ConversationId.ToString());
        command.Parameters.AddWithValue("$fromModeId", (object?)move.FromModeId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$toModeId", (object?)move.ToModeId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$fromPlacement", (int)move.FromPlacement);
        command.Parameters.AddWithValue("$toPlacement", (int)move.ToPlacement);
        command.Parameters.AddWithValue("$reason", move.Reason);
        command.Parameters.AddWithValue("$movedAt", move.MovedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConversationMove>> GetMovesAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM conversation_moves WHERE conversation_id=$conversationId ORDER BY moved_at DESC;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        var results = new List<ConversationMove>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(new ConversationMove(
                Guid.Parse(reader.String("id")),
                Guid.Parse(reader.String("conversation_id")),
                reader.NullableGuid("from_mode_id"),
                reader.NullableGuid("to_mode_id"),
                (ConversationPlacement)reader.Int32("from_placement"),
                (ConversationPlacement)reader.Int32("to_placement"),
                reader.String("reason"),
                reader.DateTimeOffset("moved_at")));
        return results;
    }
}
