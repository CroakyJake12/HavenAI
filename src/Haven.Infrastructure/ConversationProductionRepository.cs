/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/ConversationProductionRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ConversationProductionRepository. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Represents conversation production repository and keeps its related state and behavior together.
/// </summary>
public sealed class ConversationProductionRepository(
    ISqliteConnectionFactory factory,
    IConversationRepository conversations) : IConversationProductionRepository
{
    /// <summary>
    /// Performs ensure root branch async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<ConversationBranch> EnsureRootBranchAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var existing = await ReadCurrentBranchAsync(connection, transaction, conversationId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return existing;
        }

        await using (var conversationCheck = connection.CreateCommand())
        {
            conversationCheck.Transaction = transaction;
            conversationCheck.CommandText = "SELECT COUNT(*) FROM conversations WHERE id=$id;";
            conversationCheck.Parameters.AddWithValue("$id", conversationId.ToString());
            if (Convert.ToInt32(await conversationCheck.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 0)
                throw new InvalidOperationException("The conversation must be saved before a branch can be created.");
        }

        var now = DateTimeOffset.UtcNow;
        var branch = new ConversationBranch(Guid.NewGuid(), conversationId, null, null, "Main", ConversationBranchReason.Root, true, now, now);
        await SetAllBranchesNotCurrentAsync(connection, transaction, conversationId, cancellationToken).ConfigureAwait(false);
        await InsertBranchAsync(connection, transaction, branch, cancellationToken).ConfigureAwait(false);

        var sequence = 0;
        Guid? openTurnId = null;
        var turnSequence = 0;
        await using (var messages = connection.CreateCommand())
        {
            messages.Transaction = transaction;
            messages.CommandText = "SELECT id, role, content, metadata_json, created_at FROM messages WHERE conversation_id=$conversationId ORDER BY created_at, rowid;";
            messages.Parameters.AddWithValue("$conversationId", conversationId.ToString());
            await using var reader = await messages.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var messageId = Guid.Parse(reader.GetString(0));
                var role = (MessageRole)reader.GetInt32(1);
                var content = reader.GetString(2);
                var metadata = reader.IsDBNull(3) ? null : reader.GetString(3);
                var createdAt = DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture);
                sequence++;
                await InsertBranchMessageAsync(connection, transaction, branch.Id, messageId, sequence, cancellationToken).ConfigureAwait(false);
                await InsertInitialVersionIfMissingAsync(connection, transaction, messageId, branch.Id, content, metadata, createdAt, cancellationToken).ConfigureAwait(false);

                if (role == MessageRole.User)
                {
                    openTurnId = Guid.NewGuid();
                    turnSequence++;
                    await InsertTurnAsync(connection, transaction,
                        new ConversationTurn(openTurnId.Value, conversationId, branch.Id, turnSequence, messageId, null, createdAt),
                        cancellationToken).ConfigureAwait(false);
                }
                else if (role == MessageRole.Assistant && openTurnId is { } turnId)
                {
                    await using var updateTurn = connection.CreateCommand();
                    updateTurn.Transaction = transaction;
                    updateTurn.CommandText = "UPDATE conversation_turns SET assistant_message_id=$messageId WHERE id=$id;";
                    updateTurn.Parameters.AddWithValue("$messageId", messageId.ToString());
                    updateTurn.Parameters.AddWithValue("$id", turnId.ToString());
                    await updateTurn.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    openTurnId = null;
                }
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return branch;
    }

    /// <summary>
    /// Retrieves branches async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ConversationBranch>> GetBranchesAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM conversation_branches WHERE conversation_id=$conversationId ORDER BY created_at;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        return await ReadBranchesAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves current branch async for the current operation.
    /// </summary>
    public async Task<ConversationBranch?> GetCurrentBranchAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadCurrentBranchAsync(connection, null, conversationId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates branch async with the invariants required by its callers.
    /// </summary>
    public async Task<ConversationBranch> CreateBranchAsync(
        Guid conversationId,
        Guid parentBranchId,
        Guid? forkedFromMessageId,
        string name,
        ConversationBranchReason reason,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var parentCheck = connection.CreateCommand())
        {
            parentCheck.Transaction = transaction;
            parentCheck.CommandText = "SELECT COUNT(*) FROM conversation_branches WHERE id=$id AND conversation_id=$conversationId;";
            parentCheck.Parameters.AddWithValue("$id", parentBranchId.ToString());
            parentCheck.Parameters.AddWithValue("$conversationId", conversationId.ToString());
            if (Convert.ToInt32(await parentCheck.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 0)
                throw new InvalidOperationException("The parent branch does not belong to this conversation.");
        }

        int? forkSequence = null;
        if (forkedFromMessageId is { } forkMessageId)
        {
            await using var fork = connection.CreateCommand();
            fork.Transaction = transaction;
            fork.CommandText = "SELECT sequence FROM conversation_branch_messages WHERE branch_id=$branchId AND message_id=$messageId;";
            fork.Parameters.AddWithValue("$branchId", parentBranchId.ToString());
            fork.Parameters.AddWithValue("$messageId", forkMessageId.ToString());
            var value = await fork.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is null)
                throw new InvalidOperationException("The fork message is not part of the parent branch.");
            forkSequence = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        var now = DateTimeOffset.UtcNow;
        var branch = new ConversationBranch(
            Guid.NewGuid(), conversationId, parentBranchId, forkedFromMessageId,
            string.IsNullOrWhiteSpace(name) ? "Branch" : name.Trim(), reason, true, now, now);
        await SetAllBranchesNotCurrentAsync(connection, transaction, conversationId, cancellationToken).ConfigureAwait(false);
        await InsertBranchAsync(connection, transaction, branch, cancellationToken).ConfigureAwait(false);

        await using (var copyMessages = connection.CreateCommand())
        {
            copyMessages.Transaction = transaction;
            copyMessages.CommandText = forkSequence is null
                ? "INSERT INTO conversation_branch_messages(branch_id,message_id,sequence) SELECT $newBranch,message_id,sequence FROM conversation_branch_messages WHERE branch_id=$parentBranch ORDER BY sequence;"
                : "INSERT INTO conversation_branch_messages(branch_id,message_id,sequence) SELECT $newBranch,message_id,sequence FROM conversation_branch_messages WHERE branch_id=$parentBranch AND sequence<=$forkSequence ORDER BY sequence;";
            copyMessages.Parameters.AddWithValue("$newBranch", branch.Id.ToString());
            copyMessages.Parameters.AddWithValue("$parentBranch", parentBranchId.ToString());
            if (forkSequence is not null) copyMessages.Parameters.AddWithValue("$forkSequence", forkSequence.Value);
            await copyMessages.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var turns = connection.CreateCommand())
        {
            turns.Transaction = transaction;
            turns.CommandText = "SELECT sequence,user_message_id,assistant_message_id,created_at FROM conversation_turns WHERE branch_id=$branchId ORDER BY sequence;";
            turns.Parameters.AddWithValue("$branchId", parentBranchId.ToString());
            await using var reader = await turns.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid? userId = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1));
                Guid? assistantId = reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2));
                if (forkedFromMessageId is not null && userId != forkedFromMessageId && assistantId != forkedFromMessageId)
                {
                    var included = await AreTurnMessagesMappedAsync(connection, transaction, branch.Id, userId, assistantId, cancellationToken).ConfigureAwait(false);
                    if (!included) continue;
                }
                await InsertTurnAsync(connection, transaction,
                    new ConversationTurn(Guid.NewGuid(), conversationId, branch.Id, reader.GetInt32(0), userId, assistantId,
                        DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture)), cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return branch;
    }

    /// <summary>
    /// Performs set current branch async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task SetCurrentBranchAsync(Guid conversationId, Guid branchId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await SetAllBranchesNotCurrentAsync(connection, transaction, conversationId, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE conversation_branches SET is_current=1,updated_at=$updatedAt WHERE id=$branchId AND conversation_id=$conversationId;";
        command.Parameters.AddWithValue("$branchId", branchId.ToString());
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("The branch does not belong to this conversation.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves turns async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ConversationTurn>> GetTurnsAsync(Guid conversationId, Guid branchId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM conversation_turns WHERE conversation_id=$conversationId AND branch_id=$branchId ORDER BY sequence;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        command.Parameters.AddWithValue("$branchId", branchId.ToString());
        var result = new List<ConversationTurn>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ConversationTurn(
                Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                Guid.Parse(reader.GetString(reader.GetOrdinal("conversation_id"))),
                Guid.Parse(reader.GetString(reader.GetOrdinal("branch_id"))),
                reader.GetInt32(reader.GetOrdinal("sequence")),
                ReadNullableGuid(reader, "user_message_id"),
                ReadNullableGuid(reader, "assistant_message_id"),
                DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at")), System.Globalization.CultureInfo.InvariantCulture)));
        return result;
    }

    /// <summary>
    /// Retrieves versions async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<MessageVersion>> GetVersionsAsync(Guid messageId, Guid branchId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM message_versions WHERE message_id=$messageId AND branch_id=$branchId ORDER BY version_number;";
        command.Parameters.AddWithValue("$messageId", messageId.ToString());
        command.Parameters.AddWithValue("$branchId", branchId.ToString());
        return await ReadVersionsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves current version async for the current operation.
    /// </summary>
    public async Task<MessageVersion?> GetCurrentVersionAsync(Guid messageId, Guid branchId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var visited = new HashSet<Guid>();
        Guid? current = branchId;
        while (current is { } currentId && visited.Add(currentId))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM message_versions WHERE message_id=$messageId AND branch_id=$branchId AND is_current=1 LIMIT 1;";
            command.Parameters.AddWithValue("$messageId", messageId.ToString());
            command.Parameters.AddWithValue("$branchId", currentId.ToString());
            var versions = await ReadVersionsAsync(command, cancellationToken).ConfigureAwait(false);
            if (versions.FirstOrDefault() is { } version) return version;

            await using var parent = connection.CreateCommand();
            parent.CommandText = "SELECT parent_branch_id FROM conversation_branches WHERE id=$branchId;";
            parent.Parameters.AddWithValue("$branchId", currentId.ToString());
            var value = await parent.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            current = value is null or DBNull ? null : Guid.Parse((string)value);
        }
        return null;
    }

    /// <summary>
    /// Performs add version async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<MessageVersion> AddVersionAsync(
        Guid messageId,
        Guid branchId,
        MessageVersionKind kind,
        string content,
        string? metadataJson,
        bool makeCurrent,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var next = 1;
        await using (var number = connection.CreateCommand())
        {
            number.Transaction = transaction;
            number.CommandText = "SELECT COALESCE(MAX(version_number),0)+1 FROM message_versions WHERE message_id=$messageId AND branch_id=$branchId;";
            number.Parameters.AddWithValue("$messageId", messageId.ToString());
            number.Parameters.AddWithValue("$branchId", branchId.ToString());
            next = Convert.ToInt32(await number.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        }
        if (makeCurrent)
        {
            await using var clear = connection.CreateCommand();
            clear.Transaction = transaction;
            clear.CommandText = "UPDATE message_versions SET is_current=0 WHERE message_id=$messageId AND branch_id=$branchId;";
            clear.Parameters.AddWithValue("$messageId", messageId.ToString());
            clear.Parameters.AddWithValue("$branchId", branchId.ToString());
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        var version = new MessageVersion(Guid.NewGuid(), messageId, branchId, next, kind, content, metadataJson, makeCurrent, DateTimeOffset.UtcNow);
        await InsertVersionAsync(connection, transaction, version, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return version;
    }

    /// <summary>
    /// Performs replace message content async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task ReplaceMessageContentAsync(Guid messageId, string content, string? metadataJson, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE messages SET content=$content,metadata_json=$metadataJson WHERE id=$id;";
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$metadataJson", (object?)metadataJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", messageId.ToString());
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("The message no longer exists.");
    }

    /// <summary>
    /// Performs remove branch messages after async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task RemoveBranchMessagesAfterAsync(Guid branchId, Guid messageId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        int sequence;
        await using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = "SELECT sequence FROM conversation_branch_messages WHERE branch_id=$branchId AND message_id=$messageId;";
            find.Parameters.AddWithValue("$branchId", branchId.ToString());
            find.Parameters.AddWithValue("$messageId", messageId.ToString());
            var value = await find.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("The message is not part of this branch.");
            sequence = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM conversation_branch_messages WHERE branch_id=$branchId AND sequence>$sequence;";
            delete.Parameters.AddWithValue("$branchId", branchId.ToString());
            delete.Parameters.AddWithValue("$sequence", sequence);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var cleanTurns = connection.CreateCommand())
        {
            cleanTurns.Transaction = transaction;
            cleanTurns.CommandText = """
                DELETE FROM conversation_turns
                 WHERE branch_id=$branchId
                   AND ((user_message_id IS NOT NULL AND user_message_id NOT IN (SELECT message_id FROM conversation_branch_messages WHERE branch_id=$branchId))
                     OR (assistant_message_id IS NOT NULL AND assistant_message_id NOT IN (SELECT message_id FROM conversation_branch_messages WHERE branch_id=$branchId)));
                """;
            cleanTurns.Parameters.AddWithValue("$branchId", branchId.ToString());
            await cleanTurns.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves attachments async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<MessageAttachment>> GetAttachmentsAsync(Guid conversationId, Guid? messageId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = messageId is null
            ? "SELECT * FROM message_attachments WHERE conversation_id=$conversationId ORDER BY created_at;"
            : "SELECT * FROM message_attachments WHERE conversation_id=$conversationId AND message_id=$messageId ORDER BY created_at;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        if (messageId is not null) command.Parameters.AddWithValue("$messageId", messageId.Value.ToString());
        return await ReadAttachmentsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs upsert attachment async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<MessageAttachment> UpsertAttachmentAsync(MessageAttachment attachment, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO message_attachments(id,conversation_id,message_id,branch_id,original_name,stored_name,media_type,kind,size_bytes,sha256,processing_state,analysis_method,extracted_text,metadata_json,created_at,updated_at)
            VALUES($id,$conversationId,$messageId,$branchId,$originalName,$storedName,$mediaType,$kind,$sizeBytes,$sha256,$processingState,$analysisMethod,$extractedText,$metadataJson,$createdAt,$updatedAt)
            ON CONFLICT(id) DO UPDATE SET message_id=excluded.message_id,branch_id=excluded.branch_id,original_name=excluded.original_name,
              stored_name=excluded.stored_name,media_type=excluded.media_type,kind=excluded.kind,size_bytes=excluded.size_bytes,sha256=excluded.sha256,
              processing_state=excluded.processing_state,analysis_method=excluded.analysis_method,extracted_text=excluded.extracted_text,
              metadata_json=excluded.metadata_json,updated_at=excluded.updated_at;
            """;
        AddAttachmentParameters(command, attachment);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return attachment;
    }

    /// <summary>
    /// Performs delete attachment async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM message_attachments WHERE id=$id;";
        command.Parameters.AddWithValue("$id", attachmentId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves draft async for the current operation.
    /// </summary>
    public async Task<ConversationDraft?> GetDraftAsync(Guid conversationId, Guid? branchId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = branchId is null
            ? "SELECT * FROM conversation_drafts WHERE conversation_id=$conversationId AND branch_id IS NULL ORDER BY updated_at DESC LIMIT 1;"
            : "SELECT * FROM conversation_drafts WHERE conversation_id=$conversationId AND branch_id=$branchId LIMIT 1;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        if (branchId is not null) command.Parameters.AddWithValue("$branchId", branchId.Value.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new ConversationDraft(
                Guid.Parse(reader.GetString(reader.GetOrdinal("conversation_id"))),
                ReadNullableGuid(reader, "branch_id"),
                reader.GetString(reader.GetOrdinal("content")),
                reader.GetString(reader.GetOrdinal("attachment_ids_json")),
                DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at")), System.Globalization.CultureInfo.InvariantCulture))
            : null;
    }

    /// <summary>
    /// Performs save draft async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task SaveDraftAsync(ConversationDraft draft, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await DeleteDraftCoreAsync(connection, transaction, draft.ConversationId, draft.BranchId, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO conversation_drafts(conversation_id,branch_id,content,attachment_ids_json,updated_at) VALUES($conversationId,$branchId,$content,$attachments,$updatedAt);";
        command.Parameters.AddWithValue("$conversationId", draft.ConversationId.ToString());
        command.Parameters.AddWithValue("$branchId", (object?)draft.BranchId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$content", draft.Content);
        command.Parameters.AddWithValue("$attachments", draft.AttachmentIdsJson);
        command.Parameters.AddWithValue("$updatedAt", draft.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs delete draft async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteDraftAsync(Guid conversationId, Guid? branchId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await DeleteDraftCoreAsync(connection, null, conversationId, branchId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves bookmarks async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<MessageBookmark>> GetBookmarksAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM message_bookmarks WHERE conversation_id=$conversationId ORDER BY created_at;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        var result = new List<MessageBookmark>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new MessageBookmark(
                Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                Guid.Parse(reader.GetString(reader.GetOrdinal("conversation_id"))),
                Guid.Parse(reader.GetString(reader.GetOrdinal("message_id"))),
                reader.GetString(reader.GetOrdinal("label")),
                reader.GetString(reader.GetOrdinal("note")),
                DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at")), System.Globalization.CultureInfo.InvariantCulture)));
        return result;
    }

    /// <summary>
    /// Performs upsert bookmark async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertBookmarkAsync(MessageBookmark bookmark, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO message_bookmarks(id,conversation_id,message_id,label,note,created_at)
            VALUES($id,$conversationId,$messageId,$label,$note,$createdAt)
            ON CONFLICT(message_id) DO UPDATE SET label=excluded.label,note=excluded.note;
            """;
        command.Parameters.AddWithValue("$id", bookmark.Id.ToString());
        command.Parameters.AddWithValue("$conversationId", bookmark.ConversationId.ToString());
        command.Parameters.AddWithValue("$messageId", bookmark.MessageId.ToString());
        command.Parameters.AddWithValue("$label", bookmark.Label);
        command.Parameters.AddWithValue("$note", bookmark.Note);
        command.Parameters.AddWithValue("$createdAt", bookmark.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs delete bookmark async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DeleteBookmarkAsync(Guid bookmarkId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM message_bookmarks WHERE id=$id;";
        command.Parameters.AddWithValue("$id", bookmarkId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs search async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<IReadOnlyList<ConversationSearchResult>> SearchAsync(string query, Guid? conversationId, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = conversationId is null
            ? """
                SELECT c.id,m.id,c.title,m.content,m.created_at
                  FROM messages m JOIN conversations c ON c.id=m.conversation_id
                 WHERE c.is_temporary=0 AND (m.content LIKE $query ESCAPE '\\' OR c.title LIKE $query ESCAPE '\\')
                 ORDER BY m.created_at DESC LIMIT $limit;
                """
            : """
                SELECT c.id,m.id,c.title,m.content,m.created_at
                  FROM messages m JOIN conversations c ON c.id=m.conversation_id
                 WHERE c.id=$conversationId AND m.content LIKE $query ESCAPE '\\'
                 ORDER BY m.created_at DESC LIMIT $limit;
                """;
        command.Parameters.AddWithValue("$query", "%" + EscapeLike(query.Trim()) + "%");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        if (conversationId is not null) command.Parameters.AddWithValue("$conversationId", conversationId.Value.ToString());
        var result = new List<ConversationSearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var content = reader.GetString(3);
            result.Add(new ConversationSearchResult(
                Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2),
                BuildSnippet(content, query.Trim()),
                DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture), 1));
        }
        return result;
    }

    /// <summary>
    /// Retrieves active share async for the current operation.
    /// </summary>
    public async Task<SharedSession?> GetActiveShareAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM shared_sessions WHERE conversation_id=$conversationId AND state=0 AND expires_at>$now ORDER BY created_at DESC LIMIT 1;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadShare(reader) : null;
    }

    /// <summary>
    /// Performs upsert share async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task UpsertShareAsync(SharedSession session, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO shared_sessions(id,conversation_id,token_hash,bind_address,port,state,created_at,expires_at,stopped_at)
            VALUES($id,$conversationId,$tokenHash,$bindAddress,$port,$state,$createdAt,$expiresAt,$stoppedAt)
            ON CONFLICT(id) DO UPDATE SET state=excluded.state,expires_at=excluded.expires_at,stopped_at=excluded.stopped_at;
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString());
        command.Parameters.AddWithValue("$conversationId", session.ConversationId.ToString());
        command.Parameters.AddWithValue("$tokenHash", session.TokenHash);
        command.Parameters.AddWithValue("$bindAddress", session.BindAddress);
        command.Parameters.AddWithValue("$port", session.Port);
        command.Parameters.AddWithValue("$state", (int)session.State);
        command.Parameters.AddWithValue("$createdAt", session.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$expiresAt", session.ExpiresAt.ToString("O"));
        command.Parameters.AddWithValue("$stoppedAt", (object?)session.StoppedAt?.ToString("O") ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs stop share async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task StopShareAsync(Guid shareId, DateTimeOffset stoppedAt, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE shared_sessions SET state=1,stopped_at=$stoppedAt WHERE id=$id;";
        command.Parameters.AddWithValue("$id", shareId.ToString());
        command.Parameters.AddWithValue("$stoppedAt", stoppedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds export async from the currently available inputs.
    /// </summary>
    public async Task<ConversationExportDocument> BuildExportAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetAsync(conversationId, cancellationToken).ConfigureAwait(false)
                           ?? throw new InvalidOperationException("Conversation not found.");
        var branches = await GetBranchesAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var messages = await conversations.GetMessagesAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var attachments = await GetAttachmentsAsync(conversationId, null, cancellationToken).ConfigureAwait(false);
        var bookmarks = await GetBookmarksAsync(conversationId, cancellationToken).ConfigureAwait(false);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var versionsCommand = connection.CreateCommand();
        versionsCommand.CommandText = """
            SELECT v.* FROM message_versions v
            JOIN messages m ON m.id=v.message_id
            WHERE m.conversation_id=$conversationId
            ORDER BY v.created_at;
            """;
        versionsCommand.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        var versions = await ReadVersionsAsync(versionsCommand, cancellationToken).ConfigureAwait(false);
        return new ConversationExportDocument(conversation, branches, messages, versions, attachments, bookmarks, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Performs ensure schema async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task EnsureSchemaAsync(CancellationToken cancellationToken) =>
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Performs read current branch async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<ConversationBranch?> ReadCurrentBranchAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM conversation_branches WHERE conversation_id=$conversationId AND is_current=1 LIMIT 1;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        var rows = await ReadBranchesAsync(command, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    /// <summary>
    /// Performs read branches async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<IReadOnlyList<ConversationBranch>> ReadBranchesAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var result = new List<ConversationBranch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ConversationBranch(
                Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                Guid.Parse(reader.GetString(reader.GetOrdinal("conversation_id"))),
                ReadNullableGuid(reader, "parent_branch_id"),
                ReadNullableGuid(reader, "forked_from_message_id"),
                reader.GetString(reader.GetOrdinal("name")),
                (ConversationBranchReason)reader.GetInt32(reader.GetOrdinal("reason")),
                reader.GetInt32(reader.GetOrdinal("is_current")) != 0,
                DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at")), System.Globalization.CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at")), System.Globalization.CultureInfo.InvariantCulture)));
        return result;
    }

    /// <summary>
    /// Performs read versions async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<IReadOnlyList<MessageVersion>> ReadVersionsAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var result = new List<MessageVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new MessageVersion(
                Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                Guid.Parse(reader.GetString(reader.GetOrdinal("message_id"))),
                Guid.Parse(reader.GetString(reader.GetOrdinal("branch_id"))),
                reader.GetInt32(reader.GetOrdinal("version_number")),
                (MessageVersionKind)reader.GetInt32(reader.GetOrdinal("kind")),
                reader.GetString(reader.GetOrdinal("content")),
                reader.IsDBNull(reader.GetOrdinal("metadata_json")) ? null : reader.GetString(reader.GetOrdinal("metadata_json")),
                reader.GetInt32(reader.GetOrdinal("is_current")) != 0,
                DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at")), System.Globalization.CultureInfo.InvariantCulture)));
        return result;
    }

    /// <summary>
    /// Performs read attachments async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<IReadOnlyList<MessageAttachment>> ReadAttachmentsAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var result = new List<MessageAttachment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new MessageAttachment(
                Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                Guid.Parse(reader.GetString(reader.GetOrdinal("conversation_id"))),
                ReadNullableGuid(reader, "message_id"),
                ReadNullableGuid(reader, "branch_id"),
                reader.GetString(reader.GetOrdinal("original_name")),
                reader.GetString(reader.GetOrdinal("stored_name")),
                reader.GetString(reader.GetOrdinal("media_type")),
                (MessageAttachmentKind)reader.GetInt32(reader.GetOrdinal("kind")),
                reader.GetInt64(reader.GetOrdinal("size_bytes")),
                reader.GetString(reader.GetOrdinal("sha256")),
                (AttachmentProcessingState)reader.GetInt32(reader.GetOrdinal("processing_state")),
                (AttachmentAnalysisMethod)reader.GetInt32(reader.GetOrdinal("analysis_method")),
                reader.GetString(reader.GetOrdinal("extracted_text")),
                reader.GetString(reader.GetOrdinal("metadata_json")),
                DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at")), System.Globalization.CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at")), System.Globalization.CultureInfo.InvariantCulture)));
        return result;
    }

    /// <summary>
    /// Performs insert branch async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task InsertBranchAsync(SqliteConnection connection, SqliteTransaction transaction, ConversationBranch branch, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO conversation_branches(id,conversation_id,parent_branch_id,forked_from_message_id,name,reason,is_current,created_at,updated_at)
            VALUES($id,$conversationId,$parentBranchId,$forkedFromMessageId,$name,$reason,$isCurrent,$createdAt,$updatedAt);
            """;
        command.Parameters.AddWithValue("$id", branch.Id.ToString());
        command.Parameters.AddWithValue("$conversationId", branch.ConversationId.ToString());
        command.Parameters.AddWithValue("$parentBranchId", (object?)branch.ParentBranchId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$forkedFromMessageId", (object?)branch.ForkedFromMessageId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", branch.Name);
        command.Parameters.AddWithValue("$reason", (int)branch.Reason);
        command.Parameters.AddWithValue("$isCurrent", branch.IsCurrent ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", branch.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", branch.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs set all branches not current async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task SetAllBranchesNotCurrentAsync(SqliteConnection connection, SqliteTransaction transaction, Guid conversationId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE conversation_branches SET is_current=0 WHERE conversation_id=$conversationId;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs insert branch message async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task InsertBranchMessageAsync(SqliteConnection connection, SqliteTransaction transaction, Guid branchId, Guid messageId, int sequence, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO conversation_branch_messages(branch_id,message_id,sequence) VALUES($branchId,$messageId,$sequence);";
        command.Parameters.AddWithValue("$branchId", branchId.ToString());
        command.Parameters.AddWithValue("$messageId", messageId.ToString());
        command.Parameters.AddWithValue("$sequence", sequence);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs insert initial version if missing async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task InsertInitialVersionIfMissingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid messageId,
        Guid branchId,
        string content,
        string? metadata,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO message_versions(id,message_id,branch_id,version_number,kind,content,metadata_json,is_current,created_at)
            VALUES($id,$messageId,$branchId,1,0,$content,$metadataJson,1,$createdAt);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$messageId", messageId.ToString());
        command.Parameters.AddWithValue("$branchId", branchId.ToString());
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$metadataJson", (object?)metadata ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs insert version async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task InsertVersionAsync(SqliteConnection connection, SqliteTransaction transaction, MessageVersion version, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO message_versions(id,message_id,branch_id,version_number,kind,content,metadata_json,is_current,created_at)
            VALUES($id,$messageId,$branchId,$versionNumber,$kind,$content,$metadataJson,$isCurrent,$createdAt);
            """;
        command.Parameters.AddWithValue("$id", version.Id.ToString());
        command.Parameters.AddWithValue("$messageId", version.MessageId.ToString());
        command.Parameters.AddWithValue("$branchId", version.BranchId.ToString());
        command.Parameters.AddWithValue("$versionNumber", version.VersionNumber);
        command.Parameters.AddWithValue("$kind", (int)version.Kind);
        command.Parameters.AddWithValue("$content", version.Content);
        command.Parameters.AddWithValue("$metadataJson", (object?)version.MetadataJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$isCurrent", version.IsCurrent ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", version.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs insert turn async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task InsertTurnAsync(SqliteConnection connection, SqliteTransaction transaction, ConversationTurn turn, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO conversation_turns(id,conversation_id,branch_id,sequence,user_message_id,assistant_message_id,created_at)
            VALUES($id,$conversationId,$branchId,$sequence,$userMessageId,$assistantMessageId,$createdAt);
            """;
        command.Parameters.AddWithValue("$id", turn.Id.ToString());
        command.Parameters.AddWithValue("$conversationId", turn.ConversationId.ToString());
        command.Parameters.AddWithValue("$branchId", turn.BranchId.ToString());
        command.Parameters.AddWithValue("$sequence", turn.Sequence);
        command.Parameters.AddWithValue("$userMessageId", (object?)turn.UserMessageId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$assistantMessageId", (object?)turn.AssistantMessageId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", turn.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs are turn messages mapped async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<bool> AreTurnMessagesMappedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid branchId,
        Guid? userId,
        Guid? assistantId,
        CancellationToken cancellationToken)
    {
        foreach (var id in new[] { userId, assistantId }.Where(value => value is not null).Cast<Guid>())
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM conversation_branch_messages WHERE branch_id=$branchId AND message_id=$messageId;";
            command.Parameters.AddWithValue("$branchId", branchId.ToString());
            command.Parameters.AddWithValue("$messageId", id.ToString());
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Performs the add attachment parameters step owned by this component.
    /// </summary>
    private static void AddAttachmentParameters(SqliteCommand command, MessageAttachment attachment)
    {
        command.Parameters.AddWithValue("$id", attachment.Id.ToString());
        command.Parameters.AddWithValue("$conversationId", attachment.ConversationId.ToString());
        command.Parameters.AddWithValue("$messageId", (object?)attachment.MessageId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$branchId", (object?)attachment.BranchId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$originalName", attachment.OriginalName);
        command.Parameters.AddWithValue("$storedName", attachment.StoredName);
        command.Parameters.AddWithValue("$mediaType", attachment.MediaType);
        command.Parameters.AddWithValue("$kind", (int)attachment.Kind);
        command.Parameters.AddWithValue("$sizeBytes", attachment.SizeBytes);
        command.Parameters.AddWithValue("$sha256", attachment.Sha256);
        command.Parameters.AddWithValue("$processingState", (int)attachment.ProcessingState);
        command.Parameters.AddWithValue("$analysisMethod", (int)attachment.AnalysisMethod);
        command.Parameters.AddWithValue("$extractedText", attachment.ExtractedText);
        command.Parameters.AddWithValue("$metadataJson", attachment.MetadataJson);
        command.Parameters.AddWithValue("$createdAt", attachment.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", attachment.UpdatedAt.ToString("O"));
    }

    /// <summary>
    /// Performs delete draft core async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task DeleteDraftCoreAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid conversationId,
        Guid? branchId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = branchId is null
            ? "DELETE FROM conversation_drafts WHERE conversation_id=$conversationId AND branch_id IS NULL;"
            : "DELETE FROM conversation_drafts WHERE conversation_id=$conversationId AND branch_id=$branchId;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        if (branchId is not null) command.Parameters.AddWithValue("$branchId", branchId.Value.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the read share step owned by this component.
    /// </summary>
    private static SharedSession ReadShare(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
        Guid.Parse(reader.GetString(reader.GetOrdinal("conversation_id"))),
        reader.GetString(reader.GetOrdinal("token_hash")),
        reader.GetString(reader.GetOrdinal("bind_address")),
        reader.GetInt32(reader.GetOrdinal("port")),
        (SharedSessionState)reader.GetInt32(reader.GetOrdinal("state")),
        DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at")), System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("expires_at")), System.Globalization.CultureInfo.InvariantCulture),
        reader.IsDBNull(reader.GetOrdinal("stopped_at")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("stopped_at")), System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// Performs the read nullable guid step owned by this component.
    /// </summary>
    private static Guid? ReadNullableGuid(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));
    }

    /// <summary>
    /// Performs the escape like step owned by this component.
    /// </summary>
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>
    /// Builds snippet from the currently available inputs.
    /// </summary>
    private static string BuildSnippet(string content, string query)
    {
        const int radius = 90;
        var index = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return content.Length <= radius * 2 ? content : content[..(radius * 2)] + "…";
        var start = Math.Max(0, index - radius);
        var length = Math.Min(content.Length - start, query.Length + radius * 2);
        return (start > 0 ? "…" : string.Empty) + content.Substring(start, length) + (start + length < content.Length ? "…" : string.Empty);
    }
}
