/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/ConversationRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ConversationRepository. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Represents conversation repository and keeps its related state and behavior together.
/// </summary>
public sealed class ConversationRepository(ISqliteConnectionFactory factory) : IConversationRepository
{
    /// <summary>
    /// Retrieves recent async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = mode is null
            ? "SELECT * FROM conversations WHERE is_temporary=0 AND is_archived=0 ORDER BY updated_at DESC LIMIT $limit;"
            : "SELECT * FROM conversations WHERE is_temporary=0 AND is_archived=0 AND mode=$mode ORDER BY updated_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        if (mode is not null) command.Parameters.AddWithValue("$mode", (int)mode.Value);
        return await ReadConversationsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves recent in scope async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<Conversation>> GetRecentInScopeAsync(ConversationScope scope, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var scopePredicate = scope.Kind switch
        {
            ConversationScopeKind.GeneralChat => "mode=$mode AND kind=$kind AND container_id IS NULL AND lesson_id IS NULL",
            ConversationScopeKind.ChatGroup => "mode=$mode AND kind=$kind AND container_id=$containerId AND lesson_id IS NULL",
            ConversationScopeKind.StudyQuickChat => "mode=$mode AND kind=$kind AND container_id IS NULL AND lesson_id IS NULL",
            ConversationScopeKind.StudyLesson => "mode=$mode AND kind=$kind AND container_id=$containerId AND lesson_id=$lessonId",
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
        command.CommandText = $"SELECT * FROM conversations WHERE is_temporary=0 AND is_archived=0 AND {scopePredicate} ORDER BY updated_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$mode", (int)scope.Mode);
        command.Parameters.AddWithValue("$kind", (int)KindForScope(scope.Kind));
        command.Parameters.AddWithValue("$limit", Math.Max(0, limit));
        if (scope.ContainerId is { } containerId) command.Parameters.AddWithValue("$containerId", containerId.ToString());
        if (scope.LessonId is { } lessonId) command.Parameters.AddWithValue("$lessonId", lessonId.ToString());
        return await ReadConversationsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the kind for scope step owned by this component.
    /// </summary>
    private static ConversationKind KindForScope(ConversationScopeKind kind) => kind switch
    {
        ConversationScopeKind.GeneralChat or ConversationScopeKind.ChatGroup => ConversationKind.Chat,
        ConversationScopeKind.StudyQuickChat => ConversationKind.QuickChat,
        ConversationScopeKind.StudyLesson => ConversationKind.LessonChat,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>
    /// Retrieves archived async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<Conversation>> GetArchivedAsync(HavenMode? mode, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = mode is null
            ? "SELECT * FROM conversations WHERE is_temporary=0 AND is_archived=1 ORDER BY updated_at DESC LIMIT $limit;"
            : "SELECT * FROM conversations WHERE is_temporary=0 AND is_archived=1 AND mode=$mode ORDER BY updated_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        if (mode is not null) command.Parameters.AddWithValue("$mode", (int)mode.Value);
        return await ReadConversationsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves async for the current operation.
    /// </summary>
    public async Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM conversations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        var rows = await ReadConversationsAsync(command, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    /// <summary>
    /// Retrieves messages async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken)
        => await ReadMessagesAsync(conversationId, includeCompacted: true, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Retrieves context messages async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ChatMessage>> GetContextMessagesAsync(Guid conversationId, CancellationToken cancellationToken)
        => await ReadMessagesAsync(conversationId, includeCompacted: false, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Performs read messages asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<IReadOnlyList<ChatMessage>> ReadMessagesAsync(Guid conversationId, bool includeCompacted, CancellationToken cancellationToken)
    {
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var branchId = await GetCurrentBranchIdAsync(connection, null, conversationId, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        if (branchId is null)
        {
            command.CommandText = includeCompacted
                ? "SELECT * FROM messages WHERE conversation_id=$conversationId ORDER BY created_at;"
                : "SELECT * FROM messages WHERE conversation_id=$conversationId AND is_compacted=0 ORDER BY created_at;";
            command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
            return await ReadMessagesFromReaderAsync(command, cancellationToken).ConfigureAwait(false);
        }

        command.CommandText = $"""
            WITH RECURSIVE ancestry(id, depth) AS (
                SELECT $branchId, 0
                UNION ALL
                SELECT b.parent_branch_id, ancestry.depth + 1
                  FROM conversation_branches b
                  JOIN ancestry ON b.id=ancestry.id
                 WHERE b.parent_branch_id IS NOT NULL
            )
            SELECT m.id,m.conversation_id,m.role,
                   COALESCE((SELECT v.content FROM message_versions v JOIN ancestry a ON a.id=v.branch_id
                              WHERE v.message_id=m.id AND v.is_current=1 ORDER BY a.depth LIMIT 1),m.content) AS content,
                   m.agent_name,m.model_name,
                   COALESCE((SELECT v.metadata_json FROM message_versions v JOIN ancestry a ON a.id=v.branch_id
                              WHERE v.message_id=m.id AND v.is_current=1 AND v.metadata_json IS NOT NULL ORDER BY a.depth LIMIT 1),m.metadata_json) AS metadata_json,
                   m.created_at,m.is_compacted
              FROM conversation_branch_messages bm
              JOIN messages m ON m.id=bm.message_id
             WHERE bm.branch_id=$branchId {(includeCompacted ? string.Empty : "AND m.is_compacted=0")}
             ORDER BY bm.sequence;
            """;
        command.Parameters.AddWithValue("$branchId", branchId.Value.ToString());
        return await ReadMessagesFromReaderAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs read messages from reader asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<IReadOnlyList<ChatMessage>> ReadMessagesFromReaderAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var result = new List<ChatMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new ChatMessage(
                reader.Guid("id"), reader.Guid("conversation_id"), (MessageRole)reader.Int32("role"), reader.String("content"),
                reader.NullableString("agent_name"), reader.NullableString("model_name"), reader.NullableString("metadata_json"), reader.DateTimeOffset("created_at"),
                reader.Boolean("is_compacted")));
        }
        return result;
    }

    /// <summary>
    /// Performs upsert conversation asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO conversations(id, mode, kind, title, container_id, lesson_id, is_pinned, is_temporary, created_at, updated_at,is_archived,parent_conversation_id,compacted_at)
            VALUES($id,$mode,$kind,$title,$containerId,$lessonId,$isPinned,$isTemporary,$createdAt,$updatedAt,$isArchived,$parentConversationId,$compactedAt)
            ON CONFLICT(id) DO UPDATE SET mode=excluded.mode, kind=excluded.kind, title=excluded.title,
              container_id=excluded.container_id, lesson_id=excluded.lesson_id, is_pinned=excluded.is_pinned,
              is_temporary=excluded.is_temporary, updated_at=excluded.updated_at,is_archived=excluded.is_archived,
              parent_conversation_id=excluded.parent_conversation_id,compacted_at=excluded.compacted_at;
            """;
        command.Parameters.AddWithValue("$id", conversation.Id.ToString());
        command.Parameters.AddWithValue("$mode", (int)conversation.Mode);
        command.Parameters.AddWithValue("$kind", (int)conversation.Kind);
        command.Parameters.AddWithValue("$title", conversation.Title);
        command.Parameters.AddWithValue("$containerId", (object?)conversation.ContainerId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$lessonId", (object?)conversation.LessonId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$isPinned", conversation.IsPinned ? 1 : 0);
        command.Parameters.AddWithValue("$isTemporary", conversation.IsTemporary ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", conversation.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", conversation.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$isArchived", conversation.IsArchived ? 1 : 0);
        command.Parameters.AddWithValue("$parentConversationId", (object?)conversation.ParentConversationId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$compactedAt", (object?)conversation.CompactedAt?.ToString("O") ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs add message asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR REPLACE INTO messages(id, conversation_id, role, content, agent_name, model_name, metadata_json, created_at,is_compacted)
                VALUES($id,$conversationId,$role,$content,$agentName,$modelName,$metadataJson,$createdAt,$isCompacted);
                """;
            command.Parameters.AddWithValue("$id", message.Id.ToString());
            command.Parameters.AddWithValue("$conversationId", message.ConversationId.ToString());
            command.Parameters.AddWithValue("$role", (int)message.Role);
            command.Parameters.AddWithValue("$content", message.Content);
            command.Parameters.AddWithValue("$agentName", (object?)message.AgentName ?? DBNull.Value);
            command.Parameters.AddWithValue("$modelName", (object?)message.ModelName ?? DBNull.Value);
            command.Parameters.AddWithValue("$metadataJson", (object?)message.MetadataJson ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$isCompacted", message.IsCompacted ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var branchId = await EnsureCurrentBranchCoreAsync(connection, transaction, message.ConversationId, cancellationToken).ConfigureAwait(false);
        var sequence = await GetNextMessageSequenceAsync(connection, transaction, branchId, cancellationToken).ConfigureAwait(false);
        await using (var mapping = connection.CreateCommand())
        {
            mapping.Transaction = transaction;
            mapping.CommandText = "INSERT OR IGNORE INTO conversation_branch_messages(branch_id,message_id,sequence) VALUES($branchId,$messageId,$sequence);";
            mapping.Parameters.AddWithValue("$branchId", branchId.ToString());
            mapping.Parameters.AddWithValue("$messageId", message.Id.ToString());
            mapping.Parameters.AddWithValue("$sequence", sequence);
            await mapping.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var version = connection.CreateCommand())
        {
            version.Transaction = transaction;
            version.CommandText = """
                INSERT OR IGNORE INTO message_versions(id,message_id,branch_id,version_number,kind,content,metadata_json,is_current,created_at)
                VALUES($id,$messageId,$branchId,1,0,$content,$metadataJson,1,$createdAt);
                """;
            version.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            version.Parameters.AddWithValue("$messageId", message.Id.ToString());
            version.Parameters.AddWithValue("$branchId", branchId.ToString());
            version.Parameters.AddWithValue("$content", message.Content);
            version.Parameters.AddWithValue("$metadataJson", (object?)message.MetadataJson ?? DBNull.Value);
            version.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("O"));
            await version.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await UpdateTurnsForMessageAsync(connection, transaction, branchId, message, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Permanently removes one message and the branch/version records that depend on it.
    /// </summary>
    public async Task DeleteMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM messages WHERE id=$id AND conversation_id=$conversationId;";
            command.Parameters.AddWithValue("$id", messageId.ToString());
            command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The message no longer exists in this conversation.");
        }

        await using (var cleanTurns = connection.CreateCommand())
        {
            cleanTurns.Transaction = transaction;
            cleanTurns.CommandText = "DELETE FROM conversation_turns WHERE conversation_id=$conversationId AND user_message_id IS NULL AND assistant_message_id IS NULL;";
            cleanTurns.Parameters.AddWithValue("$conversationId", conversationId.ToString());
            await cleanTurns.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs mark messages compacted asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task MarkMessagesCompactedAsync(Guid conversationId, IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken)
    {
        if (messageIds.Count == 0) return;
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var id in messageIds)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE messages SET is_compacted=1 WHERE id=$id AND conversation_id=$conversationId;";
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves context entries async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ConversationContextEntry>> GetContextEntriesAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM conversation_context WHERE conversation_id=$conversationId ORDER BY created_at;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        var result = new List<ConversationContextEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ConversationContextEntry(reader.Guid("id"), reader.Guid("conversation_id"), (ContextEntryKind)reader.Int32("kind"),
                reader.String("title"), reader.String("content"), reader.String("evidence"), reader.DateTimeOffset("created_at")));
        return result;
    }

    /// <summary>
    /// Performs add context entry asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task AddContextEntryAsync(ConversationContextEntry entry, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO conversation_context(id,conversation_id,kind,title,content,evidence,created_at)
            VALUES($id,$conversationId,$kind,$title,$content,$evidence,$createdAt);
            """;
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$conversationId", entry.ConversationId.ToString());
        command.Parameters.AddWithValue("$kind", (int)entry.Kind);
        command.Parameters.AddWithValue("$title", entry.Title);
        command.Parameters.AddWithValue("$content", entry.Content);
        command.Parameters.AddWithValue("$evidence", entry.Evidence);
        command.Parameters.AddWithValue("$createdAt", entry.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs delete conversation asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM conversations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs ensure current branch core asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<Guid> EnsureCurrentBranchCoreAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        if (await GetCurrentBranchIdAsync(connection, transaction, conversationId, cancellationToken).ConfigureAwait(false) is { } existing)
            return existing;

        var branchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToString("O");
        await using (var branch = connection.CreateCommand())
        {
            branch.Transaction = transaction;
            branch.CommandText = """
                INSERT INTO conversation_branches(id,conversation_id,parent_branch_id,forked_from_message_id,name,reason,is_current,created_at,updated_at)
                VALUES($id,$conversationId,NULL,NULL,'Main',0,1,$now,$now);
                """;
            branch.Parameters.AddWithValue("$id", branchId.ToString());
            branch.Parameters.AddWithValue("$conversationId", conversationId.ToString());
            branch.Parameters.AddWithValue("$now", now);
            await branch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var mapExisting = connection.CreateCommand())
        {
            mapExisting.Transaction = transaction;
            mapExisting.CommandText = """
                INSERT OR IGNORE INTO conversation_branch_messages(branch_id,message_id,sequence)
                SELECT $branchId,id,ROW_NUMBER() OVER(ORDER BY created_at,rowid)
                  FROM messages WHERE conversation_id=$conversationId;
                """;
            mapExisting.Parameters.AddWithValue("$branchId", branchId.ToString());
            mapExisting.Parameters.AddWithValue("$conversationId", conversationId.ToString());
            await mapExisting.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return branchId;
    }

    /// <summary>
    /// Retrieves current branch id async for the current operation.
    /// </summary>
    private static async Task<Guid?> GetCurrentBranchIdAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM conversation_branches WHERE conversation_id=$conversationId AND is_current=1 LIMIT 1;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : Guid.Parse((string)value);
    }

    /// <summary>
    /// Retrieves next message sequence async for the current operation.
    /// </summary>
    private static async Task<int> GetNextMessageSequenceAsync(SqliteConnection connection, SqliteTransaction transaction, Guid branchId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(sequence),0)+1 FROM conversation_branch_messages WHERE branch_id=$branchId;";
        command.Parameters.AddWithValue("$branchId", branchId.ToString());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Performs update turns for message asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task UpdateTurnsForMessageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid branchId,
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        if (message.Role == MessageRole.User)
        {
            var next = 1;
            await using (var sequence = connection.CreateCommand())
            {
                sequence.Transaction = transaction;
                sequence.CommandText = "SELECT COALESCE(MAX(sequence),0)+1 FROM conversation_turns WHERE branch_id=$branchId;";
                sequence.Parameters.AddWithValue("$branchId", branchId.ToString());
                next = Convert.ToInt32(await sequence.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            }
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO conversation_turns(id,conversation_id,branch_id,sequence,user_message_id,assistant_message_id,created_at)
                VALUES($id,$conversationId,$branchId,$sequence,$messageId,NULL,$createdAt);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("$conversationId", message.ConversationId.ToString());
            insert.Parameters.AddWithValue("$branchId", branchId.ToString());
            insert.Parameters.AddWithValue("$sequence", next);
            insert.Parameters.AddWithValue("$messageId", message.Id.ToString());
            insert.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (message.Role == MessageRole.Assistant)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE conversation_turns SET assistant_message_id=$messageId
                 WHERE id=(SELECT id FROM conversation_turns WHERE branch_id=$branchId AND assistant_message_id IS NULL ORDER BY sequence DESC LIMIT 1);
                """;
            update.Parameters.AddWithValue("$messageId", message.Id.ToString());
            update.Parameters.AddWithValue("$branchId", branchId.ToString());
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Performs read conversations asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<IReadOnlyList<Conversation>> ReadConversationsAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var result = new List<Conversation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new Conversation(
                reader.Guid("id"), (HavenMode)reader.Int32("mode"), (ConversationKind)reader.Int32("kind"), reader.String("title"),
                reader.NullableGuid("container_id"), reader.NullableGuid("lesson_id"), reader.Boolean("is_pinned"), reader.Boolean("is_temporary"),
                reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at"), reader.Boolean("is_archived"),
                reader.NullableGuid("parent_conversation_id"), reader.NullableString("compacted_at") is { } compacted ? DateTimeOffset.Parse(compacted, System.Globalization.CultureInfo.InvariantCulture) : null));
        }
        return result;
    }
}
