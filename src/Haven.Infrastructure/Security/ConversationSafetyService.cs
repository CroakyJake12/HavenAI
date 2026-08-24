using Haven.Application;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Persists confirmed safety events and performs the irreversible three-event
/// transition in one SQLite write transaction. Event IDs are idempotency keys.
/// </summary>
public sealed class ConversationSafetyService(ISqliteConnectionFactory factory) : IConversationSafetyService
{
    public async Task<ConversationSafetySnapshot> GetSnapshotAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadSnapshotAsync(connection, null, conversationId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(
        Guid conversationId,
        ConfirmedSafetyFlag flag,
        CancellationToken cancellationToken)
    {
        if (conversationId == Guid.Empty) throw new ArgumentException("A conversation ID is required.", nameof(conversationId));
        if (flag.EventId == Guid.Empty) throw new ArgumentException("A safety event ID is required.", nameof(flag));
        ArgumentException.ThrowIfNullOrWhiteSpace(flag.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(flag.Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(flag.EvidenceHash);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT OR IGNORE INTO conversation_safety_flags(
                conversation_id,event_id,source,category,evidence_hash,confirmed_at)
            VALUES($conversationId,$eventId,$source,$category,$evidenceHash,$confirmedAt);
            """;
        insert.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        insert.Parameters.AddWithValue("$eventId", flag.EventId.ToString());
        insert.Parameters.AddWithValue("$source", flag.Source.Trim());
        insert.Parameters.AddWithValue("$category", flag.Category.Trim());
        insert.Parameters.AddWithValue("$evidenceHash", flag.EvidenceHash.Trim().ToLowerInvariant());
        insert.Parameters.AddWithValue("$confirmedAt", flag.ConfirmedAt.ToUniversalTime().ToString("O"));
        var added = await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;

        if (added)
        {
            var updatedAt = DateTimeOffset.UtcNow;
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                INSERT INTO conversation_safety_state(
                    conversation_id,confirmed_count,state,locked_at,version,updated_at)
                VALUES($conversationId,1,0,NULL,1,$updatedAt)
                ON CONFLICT(conversation_id) DO UPDATE SET
                    confirmed_count=conversation_safety_state.confirmed_count+1,
                    state=CASE
                        WHEN conversation_safety_state.state=1 OR conversation_safety_state.confirmed_count+1>=3 THEN 1
                        ELSE 0
                    END,
                    locked_at=CASE
                        WHEN conversation_safety_state.locked_at IS NULL
                         AND conversation_safety_state.confirmed_count+1>=3 THEN $updatedAt
                        ELSE conversation_safety_state.locked_at
                    END,
                    version=conversation_safety_state.version+1,
                    updated_at=$updatedAt;
                """;
            update.Parameters.AddWithValue("$conversationId", conversationId.ToString());
            update.Parameters.AddWithValue("$updatedAt", updatedAt.ToString("O"));
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var snapshot = await ReadSnapshotAsync(connection, transaction, conversationId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ConversationSafetyFlagResult(
            added,
            added && snapshot.State == ConversationSafetyState.Locked && snapshot.ConfirmedCount == 3,
            snapshot);
    }

    public async Task EnsureMayActAsync(Guid conversationId, string operation, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        var snapshot = await GetSnapshotAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (snapshot.State == ConversationSafetyState.Locked)
            throw new ConversationSafetyLockException(conversationId, operation);
    }

    private static async Task<ConversationSafetySnapshot> ReadSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT confirmed_count,state,locked_at,version FROM conversation_safety_state WHERE conversation_id=$conversationId;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return new ConversationSafetySnapshot(conversationId, 0, ConversationSafetyState.Active, null, 0);
        return new ConversationSafetySnapshot(
            conversationId,
            reader.GetInt32(0),
            (ConversationSafetyState)reader.GetInt32(1),
            reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture),
            reader.GetInt64(3));
    }
}
