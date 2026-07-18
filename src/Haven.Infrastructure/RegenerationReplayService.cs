/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/RegenerationReplayService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns RegenerationReplayService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents regeneration replay service and keeps its related state and behavior together.
/// </summary>
public sealed class RegenerationReplayService(
    IConversationProductionRepository production,
    ISqliteConnectionFactory factory)
{
    /// <summary>
    /// Performs prepare user replay asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task PrepareUserReplayAsync(Guid conversationId, string userContent, CancellationToken cancellationToken)
    {
        if (conversationId == Guid.Empty) throw new ArgumentException("Conversation identifier is required.", nameof(conversationId));
        if (string.IsNullOrWhiteSpace(userContent)) throw new ArgumentException("The user turn to replay is required.", nameof(userContent));
        var branch = await production.GetCurrentBranchAsync(conversationId, cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("The conversation has no active branch.");
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        int? position = null;
        await using (var find = connection.CreateCommand())
        {
            find.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            find.CommandText = """
                SELECT bm.position
                  FROM branch_messages bm
                  JOIN messages m ON m.id=bm.message_id
                 WHERE bm.branch_id=$branchId AND m.role=$role AND m.content=$content
                 ORDER BY bm.position DESC LIMIT 1;
                """;
            find.Parameters.AddWithValue("$branchId", branch.Id.ToString());
            find.Parameters.AddWithValue("$role", (int)MessageRole.User);
            find.Parameters.AddWithValue("$content", userContent);
            var value = await find.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is long number) position = checked((int)number);
            else if (value is int integer) position = integer;
        }
        if (position is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("The preceding user turn was not found in the active branch.");
        }
        await using (var remove = connection.CreateCommand())
        {
            remove.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            remove.CommandText = "DELETE FROM branch_messages WHERE branch_id=$branchId AND position >= $position;";
            remove.Parameters.AddWithValue("$branchId", branch.Id.ToString());
            remove.Parameters.AddWithValue("$position", position.Value);
            await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
