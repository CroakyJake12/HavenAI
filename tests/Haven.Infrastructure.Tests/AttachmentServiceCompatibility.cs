/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/AttachmentServiceCompatibility.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns SafeMessageAttachmentService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

// Focused extraction tests exercise the processor in isolation. This adapter
// deliberately exposes the stable IMessageAttachmentService contract without
// pulling retrieval cleanup into those tests.
/// <summary>
/// Represents safe message attachment service and keeps its related state and behavior together.
/// </summary>
internal sealed class SafeMessageAttachmentService(
    MessageAttachmentService inner) : IMessageAttachmentService
{
    /// <summary>
    /// Performs import asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<MessageAttachment> ImportAsync(
        Guid conversationId,
        Guid? messageId,
        Guid? branchId,
        string path,
        AttachmentProcessingOptions? options,
        CancellationToken cancellationToken) =>
        inner.ImportAsync(conversationId, messageId, branchId, path, options, cancellationToken);

    /// <summary>
    /// Builds prompt context async from the currently available inputs.
    /// </summary>
    public Task<AttachmentPromptContext> BuildPromptContextAsync(
        Guid conversationId,
        IReadOnlyCollection<Guid>? attachmentIds,
        AttachmentProcessingOptions? options,
        CancellationToken cancellationToken) =>
        inner.BuildPromptContextAsync(conversationId, attachmentIds, options, cancellationToken);

    /// <summary>
    /// Performs delete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken) =>
        inner.DeleteAsync(attachmentId, cancellationToken);
}
