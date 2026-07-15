using Haven.Core;

namespace Haven.Application;

public sealed record AttachmentProcessingOptions(
    long MaxDocumentBytes = 50L * 1024 * 1024,
    long MaxImageBytes = 20L * 1024 * 1024,
    long MaxAudioBytes = 250L * 1024 * 1024,
    long MaxVideoBytes = 750L * 1024 * 1024,
    int MaxExtractedCharacters = 500_000,
    int MaxVideoFrames = 12);

public sealed record AttachmentPromptContext(
    IReadOnlyList<string> ImageBase64,
    string ExtractedText,
    IReadOnlyList<string> Notices,
    IReadOnlyList<MessageAttachment> Attachments);

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

public interface ILocalMediaToolLocator
{
    string? FindExecutable(string name);
}
