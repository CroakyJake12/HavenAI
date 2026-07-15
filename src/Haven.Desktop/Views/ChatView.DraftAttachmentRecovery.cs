using System.Text.Json;
using Haven.Core;

namespace Haven.Desktop.Views;

public sealed partial class ChatView
{
    private Guid _recoveredDraftAttachmentConversationId;

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
