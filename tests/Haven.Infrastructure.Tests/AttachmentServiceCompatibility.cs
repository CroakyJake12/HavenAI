using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

// Existing attachment extraction tests intentionally exercise the processor in
// isolation. This test-only adapter preserves that focused constructor while the
// production SafeMessageAttachmentService additionally owns retrieval cleanup.
internal sealed class SafeMessageAttachmentService(
    MessageAttachmentService inner,
    IAppPaths paths) : IMessageAttachmentService
{
    public Task<MessageAttachment> ImportAsync(
        Guid conversationId,
        Guid? messageId,
        Guid? branchId,
        string path,
        AttachmentProcessingOptions? options,
        CancellationToken cancellationToken) =>
        inner.ImportAsync(conversationId, messageId, branchId, path, options, cancellationToken);

    public Task<AttachmentPromptContext> BuildPromptContextAsync(
        Guid conversationId,
        IReadOnlyCollection<Guid>? attachmentIds,
        AttachmentProcessingOptions? options,
        CancellationToken cancellationToken) =>
        inner.BuildPromptContextAsync(conversationId, attachmentIds, options, cancellationToken);

    public Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken) =>
        inner.DeleteAsync(attachmentId, cancellationToken);
}
