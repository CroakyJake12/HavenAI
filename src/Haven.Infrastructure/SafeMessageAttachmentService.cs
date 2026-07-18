/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/SafeMessageAttachmentService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns SafeMessageAttachmentService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents safe message attachment service and keeps its related state and behavior together.
/// </summary>
public sealed class SafeMessageAttachmentService(
    MessageAttachmentService inner,
    IAppPaths paths) : IMessageAttachmentService
{
    /// <summary>
    /// Performs import async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<MessageAttachment> ImportAsync(
        Guid conversationId,
        Guid? messageId,
        Guid? branchId,
        string path,
        AttachmentProcessingOptions? options,
        CancellationToken cancellationToken) => inner.ImportAsync(conversationId, messageId, branchId, path, options, cancellationToken);

    /// <summary>
    /// Builds prompt context async from the currently available inputs.
    /// </summary>
    public Task<AttachmentPromptContext> BuildPromptContextAsync(
        Guid conversationId,
        IReadOnlyCollection<Guid>? attachmentIds,
        AttachmentProcessingOptions? options,
        CancellationToken cancellationToken) => inner.BuildPromptContextAsync(conversationId, attachmentIds, options, cancellationToken);

    /// <summary>
    /// Performs delete async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Reports whether is direct child is true for the current state.
    /// </summary>
    private static bool IsDirectChild(string path, string parent)
    {
        var fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (!fullPath.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase)) return false;
        return !fullPath[fullParent.Length..].Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>
    /// Attempts to delete file and reports the result without using failure for normal control flow.
    /// </summary>
    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Attempts to delete directory and reports the result without using failure for normal control flow.
    /// </summary>
    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
