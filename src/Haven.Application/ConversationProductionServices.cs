using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed class ConversationVersioningService(
    IConversationRepository conversations,
    IConversationProductionRepository production) : IConversationVersioningService
{
    public async Task<ConversationBranch> EnsureCurrentBranchAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await production.GetCurrentBranchAsync(conversationId, cancellationToken).ConfigureAwait(false)
        ?? await production.EnsureRootBranchAsync(conversationId, cancellationToken).ConfigureAwait(false);

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

public sealed class ConversationExportService(IConversationProductionRepository production) : IConversationExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

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

    public async Task<string> ExportJsonAsync(Guid conversationId, CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await production.BuildExportAsync(conversationId, cancellationToken).ConfigureAwait(false), JsonOptions);
}
