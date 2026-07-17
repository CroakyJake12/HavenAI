using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

// Focused extraction tests exercise the processor in isolation. This adapter
// deliberately exposes the stable IMessageAttachmentService contract without
// pulling retrieval cleanup into those tests.
internal sealed class SafeMessageAttachmentService(
    MessageAttachmentService inner) : IMessageAttachmentService
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
