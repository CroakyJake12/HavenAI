/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ConversationProductionServices.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ConversationVersioningService, ConversationExportService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents conversation versioning service and keeps its related state and behavior together.
/// </summary>
public sealed class ConversationVersioningService(
    IConversationRepository conversations,
    IConversationProductionRepository production) : IConversationVersioningService
{
    /// <summary>
    /// Performs ensure current branch async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<ConversationBranch> EnsureCurrentBranchAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await production.GetCurrentBranchAsync(conversationId, cancellationToken).ConfigureAwait(false)
        ?? await production.EnsureRootBranchAsync(conversationId, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Performs edit user message async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<ConversationBranch> EditUserMessageAsync(
        Guid conversationId,
        Guid messageId,
        string content,
        MessageEditMode mode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Edited message content is required.", nameof(content));

        var message = (await conversations.GetMessagesAsync(conversationId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.Id == messageId)
            ?? throw new InvalidOperationException("The message no longer exists in this conversation.");
        if (message.Role != MessageRole.User)
            throw new InvalidOperationException("Only user messages can be edited.");

        var current = await EnsureCurrentBranchAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var target = current;
        if (mode == MessageEditMode.NewBranch)
        {
            target = await production.CreateBranchAsync(
                conversationId,
                current.Id,
                messageId,
                $"Edit from {message.CreatedAt.LocalDateTime:g}",
                ConversationBranchReason.EditedUserMessage,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var existing = await production.GetCurrentVersionAsync(messageId, current.Id, cancellationToken).ConfigureAwait(false);
            await production.AddVersionAsync(
                messageId,
                current.Id,
                MessageVersionKind.RecoverySnapshot,
                existing?.Content ?? message.Content,
                existing?.MetadataJson ?? message.MetadataJson,
                false,
                cancellationToken).ConfigureAwait(false);
        }

        await production.AddVersionAsync(
            messageId,
            target.Id,
            MessageVersionKind.UserEdit,
            content.Trim(),
            message.MetadataJson,
            true,
            cancellationToken).ConfigureAwait(false);
        await production.ReplaceMessageContentAsync(messageId, content.Trim(), message.MetadataJson, cancellationToken).ConfigureAwait(false);
        await production.RemoveBranchMessagesAfterAsync(target.Id, messageId, cancellationToken).ConfigureAwait(false);
        await production.SetCurrentBranchAsync(conversationId, target.Id, cancellationToken).ConfigureAwait(false);
        return target;
    }

    /// <summary>
    /// Performs prepare regeneration async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<ConversationBranch> PrepareRegenerationAsync(
        Guid conversationId,
        Guid messageId,
        bool isLatestAssistantMessage,
        ResponseRegenerationMode mode,
        CancellationToken cancellationToken)
    {
        var messages = await conversations.GetMessagesAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var index = messages.Select((message, position) => (message, position))
            .FirstOrDefault(item => item.message.Id == messageId).position;
        var message = messages.FirstOrDefault(item => item.Id == messageId)
                      ?? throw new InvalidOperationException("The message no longer exists in this conversation.");
        if (message.Role != MessageRole.Assistant)
            throw new InvalidOperationException("Only assistant responses can be regenerated.");
        if (!isLatestAssistantMessage && mode == ResponseRegenerationMode.Here)
            throw new InvalidOperationException("An older response must be regenerated in a new branch.");

        var precedingUser = messages.Take(index).LastOrDefault(item => item.Role == MessageRole.User)
                            ?? throw new InvalidOperationException("The assistant response has no preceding user turn to regenerate.");
        var current = await EnsureCurrentBranchAsync(conversationId, cancellationToken).ConfigureAwait(false);
        ConversationBranch target;

        if (mode == ResponseRegenerationMode.NewBranch || !isLatestAssistantMessage)
        {
            target = await production.CreateBranchAsync(
                conversationId,
                current.Id,
                precedingUser.Id,
                $"Regeneration from {message.CreatedAt.LocalDateTime:g}",
                ConversationBranchReason.RegeneratedResponse,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var existing = await production.GetCurrentVersionAsync(messageId, current.Id, cancellationToken).ConfigureAwait(false);
            await production.AddVersionAsync(
                messageId,
                current.Id,
                MessageVersionKind.RecoverySnapshot,
                existing?.Content ?? message.Content,
                existing?.MetadataJson ?? message.MetadataJson,
                false,
                cancellationToken).ConfigureAwait(false);
            await production.RemoveBranchMessagesAfterAsync(current.Id, precedingUser.Id, cancellationToken).ConfigureAwait(false);
            target = current;
        }

        await production.SetCurrentBranchAsync(conversationId, target.Id, cancellationToken).ConfigureAwait(false);
        return target;
    }
}

/// <summary>
/// Represents conversation export service and keeps its related state and behavior together.
/// </summary>
public sealed class ConversationExportService(IConversationProductionRepository production) : IConversationExportService
{
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Performs export markdown async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<string> ExportMarkdownAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var document = await production.BuildExportAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(document.Conversation.Title).AppendLine();
        builder.Append("- Mode: ").AppendLine(document.Conversation.Mode.ToString());
        builder.Append("- Exported: ").AppendLine(document.ExportedAt.ToString("O"));
        builder.Append("- Branches: ").AppendLine(document.Branches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.AppendLine();

        foreach (var message in document.Messages.OrderBy(item => item.CreatedAt))
        {
            builder.Append("## ").Append(message.Role == MessageRole.User ? "You" : message.AgentName ?? "Haven").AppendLine();
            if (!string.IsNullOrWhiteSpace(message.ModelName))
                builder.Append("_Model: ").Append(message.ModelName).AppendLine("_");
            builder.AppendLine().AppendLine(message.Content).AppendLine();

            foreach (var attachment in document.Attachments.Where(item => item.MessageId == message.Id))
            {
                builder.Append("- Attachment: **").Append(attachment.OriginalName).Append("** — ")
                    .Append(attachment.AnalysisMethod).AppendLine();
            }
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// Performs export plain text async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<string> ExportPlainTextAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var document = await production.BuildExportAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var builder = new StringBuilder(document.Conversation.Title).AppendLine().AppendLine();
        foreach (var message in document.Messages.OrderBy(item => item.CreatedAt))
        {
            builder.Append(message.Role == MessageRole.User ? "You" : message.AgentName ?? "Haven")
                .Append(": ").AppendLine(message.Content).AppendLine();
        }
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// Performs export json async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<string> ExportJsonAsync(Guid conversationId, CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await production.BuildExportAsync(conversationId, cancellationToken).ConfigureAwait(false), JsonOptions);
}
