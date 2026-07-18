/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/SafeConversationProductionRepository.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns SafeConversationProductionRepository, BranchMessageRow. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Delegates the straightforward production-storage operations to the primary repository,
/// while materialising branch source rows before opening a write transaction. This avoids
/// interleaving writes with an active SQLite reader on the same connection.
/// </summary>
public sealed class SafeConversationProductionRepository(
    ISqliteConnectionFactory factory,
    IConversationRepository conversations,
    ConversationProductionRepository inner) : IConversationProductionRepository
{
    /// <summary>
    /// Performs ensure root branch async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<ConversationBranch> EnsureRootBranchAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        if (await inner.GetCurrentBranchAsync(conversationId, cancellationToken).ConfigureAwait(false) is { } existing)
            return existing;

        var conversation = await conversations.GetAsync(conversationId, cancellationToken).ConfigureAwait(false)
                           ?? throw new InvalidOperationException("The conversation must be saved before a branch can be created.");
        var messages = await conversations.GetMessagesAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var branch = new ConversationBranch(Guid.NewGuid(), conversation.Id, null, null, "Main", ConversationBranchReason.Root, true, now, now);

        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await ClearCurrentAsync(connection, transaction, conversationId, cancellationToken).ConfigureAwait(false);
        await InsertBranchAsync(connection, transaction, branch, cancellationToken).ConfigureAwait(false);

        var sequence = 0;
        var turnSequence = 0;
        Guid? openTurnId = null;
        foreach (var message in messages.OrderBy(item => item.CreatedAt))
        {
            sequence++;
            await InsertBranchMessageAsync(connection, transaction, branch.Id, message.Id, sequence, cancellationToken).ConfigureAwait(false);
            await InsertInitialVersionAsync(connection, transaction, branch.Id, message, cancellationToken).ConfigureAwait(false);

            if (message.Role == MessageRole.User)
            {
                openTurnId = Guid.NewGuid();
                turnSequence++;
                await InsertTurnAsync(connection, transaction,
                    new ConversationTurn(openTurnId.Value, conversationId, branch.Id, turnSequence, message.Id, null, message.CreatedAt),
                    cancellationToken).ConfigureAwait(false);
            }
            else if (message.Role == MessageRole.Assistant && openTurnId is { } turnId)
            {
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE conversation_turns SET assistant_message_id=$messageId WHERE id=$id;";
                update.Parameters.AddWithValue("$messageId", message.Id.ToString());
                update.Parameters.AddWithValue("$id", turnId.ToString());
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                openTurnId = null;
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return branch;
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
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        var parent = (await inner.GetBranchesAsync(conversationId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.Id == parentBranchId)
            ?? throw new InvalidOperationException("The parent branch does not belong to this conversation.");

        var sourceMessages = await LoadBranchMessagesAsync(parentBranchId, cancellationToken).ConfigureAwait(false);
        var forkSequence = forkedFromMessageId is null
            ? (int?)null
            : sourceMessages.FirstOrDefault(item => item.MessageId == forkedFromMessageId.Value)?.Sequence
              ?? throw new InvalidOperationException("The fork message is not part of the parent branch.");
        var includedMessages = forkSequence is null
            ? sourceMessages
            : sourceMessages.Where(item => item.Sequence <= forkSequence.Value).ToArray();
        var includedIds = includedMessages.Select(item => item.MessageId).ToHashSet();
        var sourceTurns = (await inner.GetTurnsAsync(conversationId, parentBranchId, cancellationToken).ConfigureAwait(false))
            .Where(item => (item.UserMessageId is null || includedIds.Contains(item.UserMessageId.Value))
                           && (item.AssistantMessageId is null || includedIds.Contains(item.AssistantMessageId.Value)))
            .ToArray();

        var now = DateTimeOffset.UtcNow;
        var branch = new ConversationBranch(
            Guid.NewGuid(), conversationId, parent.Id, forkedFromMessageId,
            string.IsNullOrWhiteSpace(name) ? "Branch" : name.Trim(), reason, true, now, now);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ClearCurrentAsync(connection, transaction, conversationId, cancellationToken).ConfigureAwait(false);
        await InsertBranchAsync(connection, transaction, branch, cancellationToken).ConfigureAwait(false);
        foreach (var item in includedMessages)
            await InsertBranchMessageAsync(connection, transaction, branch.Id, item.MessageId, item.Sequence, cancellationToken).ConfigureAwait(false);
        foreach (var turn in sourceTurns)
            await InsertTurnAsync(connection, transaction, turn with { Id = Guid.NewGuid(), BranchId = branch.Id }, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return branch;
    }

    /// <summary>
    /// Retrieves branches async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<ConversationBranch>> GetBranchesAsync(Guid conversationId, CancellationToken cancellationToken) => inner.GetBranchesAsync(conversationId, cancellationToken);
    /// <summary>
    /// Retrieves current branch async for the current operation.
    /// </summary>
    public Task<ConversationBranch?> GetCurrentBranchAsync(Guid conversationId, CancellationToken cancellationToken) => inner.GetCurrentBranchAsync(conversationId, cancellationToken);
    /// <summary>
    /// Performs set current branch async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task SetCurrentBranchAsync(Guid conversationId, Guid branchId, CancellationToken cancellationToken) => inner.SetCurrentBranchAsync(conversationId, branchId, cancellationToken);
    /// <summary>
    /// Retrieves turns async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<ConversationTurn>> GetTurnsAsync(Guid conversationId, Guid branchId, CancellationToken cancellationToken) => inner.GetTurnsAsync(conversationId, branchId, cancellationToken);
    /// <summary>
    /// Retrieves versions async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<MessageVersion>> GetVersionsAsync(Guid messageId, Guid branchId, CancellationToken cancellationToken) => inner.GetVersionsAsync(messageId, branchId, cancellationToken);
    /// <summary>
    /// Retrieves current version async for the current operation.
    /// </summary>
    public Task<MessageVersion?> GetCurrentVersionAsync(Guid messageId, Guid branchId, CancellationToken cancellationToken) => inner.GetCurrentVersionAsync(messageId, branchId, cancellationToken);
    /// <summary>
    /// Performs add version async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<MessageVersion> AddVersionAsync(Guid messageId, Guid branchId, MessageVersionKind kind, string content, string? metadataJson, bool makeCurrent, CancellationToken cancellationToken) => inner.AddVersionAsync(messageId, branchId, kind, content, metadataJson, makeCurrent, cancellationToken);
    /// <summary>
    /// Performs replace message content async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task ReplaceMessageContentAsync(Guid messageId, string content, string? metadataJson, CancellationToken cancellationToken) => inner.ReplaceMessageContentAsync(messageId, content, metadataJson, cancellationToken);
    /// <summary>
    /// Performs remove branch messages after async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task RemoveBranchMessagesAfterAsync(Guid branchId, Guid messageId, CancellationToken cancellationToken) => inner.RemoveBranchMessagesAfterAsync(branchId, messageId, cancellationToken);
    /// <summary>
    /// Retrieves attachments async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<MessageAttachment>> GetAttachmentsAsync(Guid conversationId, Guid? messageId, CancellationToken cancellationToken) => inner.GetAttachmentsAsync(conversationId, messageId, cancellationToken);
    /// <summary>
    /// Performs upsert attachment async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<MessageAttachment> UpsertAttachmentAsync(MessageAttachment attachment, CancellationToken cancellationToken) => inner.UpsertAttachmentAsync(attachment, cancellationToken);
    /// <summary>
    /// Performs delete attachment async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken) => inner.DeleteAttachmentAsync(attachmentId, cancellationToken);
    /// <summary>
    /// Retrieves draft async for the current operation.
    /// </summary>
    public Task<ConversationDraft?> GetDraftAsync(Guid conversationId, Guid? branchId, CancellationToken cancellationToken) => inner.GetDraftAsync(conversationId, branchId, cancellationToken);
    /// <summary>
    /// Performs save draft async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task SaveDraftAsync(ConversationDraft draft, CancellationToken cancellationToken) => inner.SaveDraftAsync(draft, cancellationToken);
    /// <summary>
    /// Performs delete draft async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteDraftAsync(Guid conversationId, Guid? branchId, CancellationToken cancellationToken) => inner.DeleteDraftAsync(conversationId, branchId, cancellationToken);
    /// <summary>
    /// Retrieves bookmarks async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<MessageBookmark>> GetBookmarksAsync(Guid conversationId, CancellationToken cancellationToken) => inner.GetBookmarksAsync(conversationId, cancellationToken);
    /// <summary>
    /// Performs upsert bookmark async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task UpsertBookmarkAsync(MessageBookmark bookmark, CancellationToken cancellationToken) => inner.UpsertBookmarkAsync(bookmark, cancellationToken);
    /// <summary>
    /// Performs delete bookmark async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteBookmarkAsync(Guid bookmarkId, CancellationToken cancellationToken) => inner.DeleteBookmarkAsync(bookmarkId, cancellationToken);
    /// <summary>
    /// Performs search async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<IReadOnlyList<ConversationSearchResult>> SearchAsync(string query, Guid? conversationId, int limit, CancellationToken cancellationToken) => inner.SearchAsync(query, conversationId, limit, cancellationToken);
    /// <summary>
    /// Retrieves active share async for the current operation.
    /// </summary>
    public Task<SharedSession?> GetActiveShareAsync(Guid conversationId, CancellationToken cancellationToken) => inner.GetActiveShareAsync(conversationId, cancellationToken);
    /// <summary>
    /// Performs upsert share async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task UpsertShareAsync(SharedSession session, CancellationToken cancellationToken) => inner.UpsertShareAsync(session, cancellationToken);
    /// <summary>
    /// Performs stop share async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task StopShareAsync(Guid shareId, DateTimeOffset stoppedAt, CancellationToken cancellationToken) => inner.StopShareAsync(shareId, stoppedAt, cancellationToken);
    /// <summary>
    /// Builds export async from the currently available inputs.
    /// </summary>
    public Task<ConversationExportDocument> BuildExportAsync(Guid conversationId, CancellationToken cancellationToken) => inner.BuildExportAsync(conversationId, cancellationToken);

    /// <summary>
    /// Performs load branch messages async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<IReadOnlyList<BranchMessageRow>> LoadBranchMessagesAsync(Guid branchId, CancellationToken cancellationToken)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT message_id,sequence FROM conversation_branch_messages WHERE branch_id=$branchId ORDER BY sequence;";
        command.Parameters.AddWithValue("$branchId", branchId.ToString());
        var result = new List<BranchMessageRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new BranchMessageRow(Guid.Parse(reader.GetString(0)), reader.GetInt32(1)));
        return result;
    }

    /// <summary>
    /// Performs clear current async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task ClearCurrentAsync(SqliteConnection connection, SqliteTransaction transaction, Guid conversationId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE conversation_branches SET is_current=0 WHERE conversation_id=$conversationId;";
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
    /// Performs insert branch message async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task InsertBranchMessageAsync(SqliteConnection connection, SqliteTransaction transaction, Guid branchId, Guid messageId, int sequence, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO conversation_branch_messages(branch_id,message_id,sequence) VALUES($branchId,$messageId,$sequence);";
        command.Parameters.AddWithValue("$branchId", branchId.ToString());
        command.Parameters.AddWithValue("$messageId", messageId.ToString());
        command.Parameters.AddWithValue("$sequence", sequence);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs insert initial version async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task InsertInitialVersionAsync(SqliteConnection connection, SqliteTransaction transaction, Guid branchId, ChatMessage message, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO message_versions(id,message_id,branch_id,version_number,kind,content,metadata_json,is_current,created_at)
            VALUES($id,$messageId,$branchId,1,0,$content,$metadataJson,1,$createdAt);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$messageId", message.Id.ToString());
        command.Parameters.AddWithValue("$branchId", branchId.ToString());
        command.Parameters.AddWithValue("$content", message.Content);
        command.Parameters.AddWithValue("$metadataJson", (object?)message.MetadataJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", message.CreatedAt.ToString("O"));
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
            INSERT INTO conversation_turns(id,conversation_id,branch_id,sequence,user_message_id,assistant_message_id,created_at)
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
    /// Represents branch message row and keeps its related state and behavior together.
    /// </summary>
    private sealed record BranchMessageRow(Guid MessageId, int Sequence);
}
