using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class SafeMessageAttachmentService(
    MessageAttachmentService inner,
    IAppPaths paths) : IMessageAttachmentService
{
    public Task<MessageAttachment> ImportAsync(
        Guid conversationId,
        Guid? messageId,
        Guid? branchId,
        string path,
        AttachmentProcessingOptions? options,
        CancellationToken cancellationToken) => inner.ImportAsync(conversationId, messageId, branchId, path, options, cancellationToken);

    public Task<AttachmentPromptContext> BuildPromptContextAsync(
        Guid conversationId,
        IReadOnlyCollection<Guid>? attachmentIds,
        AttachmentProcessingOptions? options,
        CancellationToken cancellationToken) => inner.BuildPromptContextAsync(conversationId, attachmentIds, options, cancellationToken);

    public async Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        await inner.DeleteAsync(attachmentId, cancellationToken).ConfigureAwait(false);
        if (!Directory.Exists(paths.AttachmentsDirectory)) return;

        var id = attachmentId.ToString("N");
        foreach (var conversationDirectory in Directory.EnumerateDirectories(paths.AttachmentsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsDirectChild(conversationDirectory, paths.AttachmentsDirectory)) continue;
            foreach (var file in Directory.EnumerateFiles(conversationDirectory, id + ".*", SearchOption.TopDirectoryOnly))
                TryDeleteFile(file);
            var frames = Path.Combine(conversationDirectory, id + "-frames");
            if (Directory.Exists(frames) && IsDirectChild(frames, conversationDirectory)) TryDeleteDirectory(frames);
        }
    }

    private static bool IsDirectChild(string path, string parent)
    {
        var fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (!fullPath.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase)) return false;
        return !fullPath[fullParent.Length..].Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
