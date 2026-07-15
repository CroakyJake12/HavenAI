using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed class ConversationRepository(ISqliteConnectionFactory factory) : IConversationRepository
{
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

    public async Task<IReadOnlyList<Conversation>> GetRecentInScopeAsync(ConversationScope scope, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        var scopePredicate = scope.Kind switch
        {
            ConversationScopeKind.GeneralChat => "mode=$mode AND kind=$kind AND container_id IS NULL AND lesson_id IS NULL",
            ConversationScopeKind.ChatGroup => "mode=$mode AND kind=$kind AND container_id=$containerId AND lesson_id IS NULL",
            ConversationScopeKind.TeachQuickChat => "mode=$mode AND kind=$kind AND container_id IS NULL AND lesson_id IS NULL",
            ConversationScopeKind.TeachLesson => "mode=$mode AND kind=$kind AND container_id=$containerId AND lesson_id=$lessonId",
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

    private static ConversationKind KindForScope(ConversationScopeKind kind) => kind switch
    {
        ConversationScopeKind.GeneralChat or ConversationScopeKind.ChatGroup => ConversationKind.Chat,
        ConversationScopeKind.TeachQuickChat => ConversationKind.QuickChat,
        ConversationScopeKind.TeachLesson => ConversationKind.LessonChat,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

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

    public async Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM conversations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        var rows = await ReadConversationsAsync(command, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken)
        => await ReadMessagesAsync(conversationId, includeCompacted: true, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ChatMessage>> GetContextMessagesAsync(Guid conversationId, CancellationToken cancellationToken)
        => await ReadMessagesAsync(conversationId, includeCompacted: false, cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<ChatMessage>> ReadMessagesAsync(Guid conversationId, bool includeCompacted, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = includeCompacted
            ? "SELECT * FROM messages WHERE conversation_id=$conversationId ORDER BY created_at;"
            : "SELECT * FROM messages WHERE conversation_id=$conversationId AND is_compacted=0 ORDER BY created_at;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
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

    public async Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
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

    public async Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM conversations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

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
