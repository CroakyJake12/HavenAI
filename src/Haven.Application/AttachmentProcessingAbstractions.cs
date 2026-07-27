/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/AttachmentProcessingAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns AttachmentProcessingOptions, AttachmentPromptContext, IMessageAttachmentService, ILocalMediaToolLocator. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents attachment processing options and keeps its related state and behavior together.
/// </summary>
public sealed record AttachmentProcessingOptions(
    long MaxDocumentBytes = 50L * 1024 * 1024,
    long MaxImageBytes = 20L * 1024 * 1024,
    long MaxAudioBytes = 250L * 1024 * 1024,
    long MaxVideoBytes = 750L * 1024 * 1024,
    int MaxExtractedCharacters = 500_000,
    int MaxVideoFrames = 12);

/// <summary>
/// Represents attachment prompt context and keeps its related state and behavior together.
/// </summary>
public sealed record AttachmentPromptContext(
    IReadOnlyList<string> ImageBase64,
    string ExtractedText,
    IReadOnlyList<string> Notices,
    IReadOnlyList<MessageAttachment> Attachments);

/// <summary>
/// Defines the message attachment service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IMessageAttachmentService
{
    Task<MessageAttachment> ImportAsync(
        Guid conversationId,
        Guid? messageId,
        Guid? branchId,
        string path,
        AttachmentProcessingOptions? options,
        CancellationToken cancellationToken);

    Task<AttachmentPromptContext> BuildPromptContextAsync(
        Guid conversationId,
        IReadOnlyCollection<Guid>? attachmentIds,
        AttachmentProcessingOptions? options,
        CancellationToken cancellationToken);

    Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the local media tool locator contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface ILocalMediaToolLocator
{
    string? FindExecutable(string name);
}
