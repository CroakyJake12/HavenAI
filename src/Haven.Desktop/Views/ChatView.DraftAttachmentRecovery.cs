/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/ChatView.DraftAttachmentRecovery.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns ChatView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Core;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents chat view and keeps its related state and behavior together.
/// </summary>
public sealed partial class ChatView
{
    /// <summary>
    /// Stores recovered draft attachment conversation id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Guid _recoveredDraftAttachmentConversationId;

    /// <summary>
    /// Performs recover draft attachments asynchronously so I/O does not block the caller's thread.
    /// </summary>
    internal async Task RecoverDraftAttachmentsAsync(CancellationToken cancellationToken)
    {
        if (_viewModel is null || _production is null || _paths is null || _viewModel.IsTemporary) return;
        if (_recoveredDraftAttachmentConversationId == _viewModel.ConversationId) return;
        var branch = await _production.GetCurrentBranchAsync(_viewModel.ConversationId, cancellationToken);
        var draft = await _production.GetDraftAsync(_viewModel.ConversationId, branch?.Id, cancellationToken);
        _recoveredDraftAttachmentConversationId = _viewModel.ConversationId;
        if (draft is null || string.IsNullOrWhiteSpace(draft.AttachmentIdsJson)) return;

        Guid[] ids;
        try { ids = JsonSerializer.Deserialize<Guid[]>(draft.AttachmentIdsJson) ?? []; }
        catch (JsonException) { return; }
        if (ids.Length == 0) return;
        var attachments = await _production.GetAttachmentsAsync(_viewModel.ConversationId, null, cancellationToken);
        _attachmentConversationId = _viewModel.ConversationId;
        foreach (var attachment in attachments.Where(item => ids.Contains(item.Id) && item.MessageId is null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _pendingAttachmentIds.Add(attachment.Id);
            await AddAttachmentRepresentationsAsync(_viewModel, attachment, cancellationToken);
        }
    }
}
