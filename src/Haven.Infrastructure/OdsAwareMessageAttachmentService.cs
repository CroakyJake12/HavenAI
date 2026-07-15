using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class OdsAwareMessageAttachmentService(
    SafeMessageAttachmentService inner,
    IAppPaths paths,
    ISqliteConnectionFactory factory,
    IRetrievalIndexService retrieval) : IMessageAttachmentService
{
    public async Task<MessageAttachment> ImportAsync(
        Guid conversationId,
        Guid? messageId,
        Guid? branchId,
        string path,
        AttachmentProcessingOptions? options,
        CancellationToken cancellationToken)
    {
        var attachment = await inner.ImportAsync(conversationId, messageId, branchId, path, options, cancellationToken).ConfigureAwait(false);
        if (!Path.GetExtension(attachment.OriginalName).Equals(".ods", StringComparison.OrdinalIgnoreCase)) return attachment;

        try
        {
            var storedPath = Path.GetFullPath(Path.Combine(paths.AttachmentsDirectory, attachment.StoredRelativePath));
            var attachmentRoot = Path.GetFullPath(paths.AttachmentsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!storedPath.StartsWith(attachmentRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The stored attachment path escaped Haven's attachment directory.");
            var text = await ExtractOdsTextAsync(storedPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException("The OpenDocument spreadsheet contained no readable cell text.");
            var metadata = MergeMetadata(attachment.MetadataJson, new Dictionary<string, object?>
            {
                ["format"] = "OpenDocument Spreadsheet",
                ["entry"] = "content.xml",
                ["extractedCharacters"] = text.Length
            });
            var updated = attachment with
            {
                ExtractedText = text,
                ProcessingState = AttachmentProcessingState.Ready,
                AnalysisMethod = "OpenDocument XML cell text extraction",
                MetadataJson = metadata
            };
            await UpdateRecordAsync(updated, cancellationToken).ConfigureAwait(false);
            await retrieval.IndexTextAsync(
                new RetrievalScope(RetrievalScopeKind.Attachment, updated.Id),
                "attachment", updated.Id.ToString("N"), updated.OriginalName, text, cancellationToken).ConfigureAwait(false);
            await retrieval.IndexTextAsync(
                new RetrievalScope(RetrievalScopeKind.Conversation, updated.ConversationId),
                "attachment", updated.Id.ToString("N"), updated.OriginalName, text, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or System.Xml.XmlException)
        {
            var metadata = MergeMetadata(attachment.MetadataJson, new Dictionary<string, object?>
            {
                ["format"] = "OpenDocument Spreadsheet",
                ["error"] = ex.Message
            });
            var failed = attachment with
            {
                ExtractedText = string.Empty,
                ProcessingState = AttachmentProcessingState.Failed,
                AnalysisMethod = "OpenDocument extraction failed: " + ex.Message,
                MetadataJson = metadata
            };
            await UpdateRecordAsync(failed, cancellationToken).ConfigureAwait(false);
            return failed;
        }
    }

    public Task<AttachmentPromptContext> BuildPromptContextAsync(
        Guid conversationId,
        IReadOnlyCollection<Guid>? attachmentIds,
        AttachmentProcessingOptions? options,
        CancellationToken cancellationToken) =>
        inner.BuildPromptContextAsync(conversationId, attachmentIds, options, cancellationToken);

    public Task DeleteAsync(Guid attachmentId, CancellationToken cancellationToken) => inner.DeleteAsync(attachmentId, cancellationToken);

    private static async Task<string> ExtractOdsTextAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("content.xml") ?? throw new InvalidDataException("The ODS archive does not contain content.xml.");
        if (entry.Length > 64L * 1024 * 1024) throw new InvalidDataException("The ODS content.xml entry exceeds the 64 MiB extraction limit.");
        await using var entryStream = entry.Open();
        var document = await XDocument.LoadAsync(entryStream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        var builder = new System.Text.StringBuilder();
        foreach (var element in document.Descendants().Where(element => element.Name.LocalName is "p" or "h" or "table-cell"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = string.Join(' ', element.DescendantNodes().OfType<XText>().Select(node => node.Value.Trim()).Where(value => value.Length > 0));
            if (value.Length == 0) continue;
            builder.AppendLine(value);
            if (builder.Length > 2_000_000)
            {
                builder.Length = 2_000_000;
                builder.AppendLine("\n[ODS extraction truncated at 2,000,000 characters]");
                break;
            }
        }
        return builder.ToString().Trim();
    }

    private async Task UpdateRecordAsync(MessageAttachment attachment, CancellationToken cancellationToken)
    {
        await ConversationProductionSchema.EnsureAsync(factory, cancellationToken).ConfigureAwait(false);
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE message_attachments
               SET extracted_text=$text,processing_state=$state,analysis_method=$method,metadata_json=$metadata
             WHERE id=$id;
            """;
        command.Parameters.AddWithValue("$text", attachment.ExtractedText);
        command.Parameters.AddWithValue("$state", (int)attachment.ProcessingState);
        command.Parameters.AddWithValue("$method", attachment.AnalysisMethod);
        command.Parameters.AddWithValue("$metadata", attachment.MetadataJson);
        command.Parameters.AddWithValue("$id", attachment.Id.ToString());
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("The imported ODS attachment record was not found.");
    }

    private static string MergeMetadata(string existingJson, IReadOnlyDictionary<string, object?> additions)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try
            {
                using var document = JsonDocument.Parse(existingJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var property in document.RootElement.EnumerateObject()) values[property.Name] = property.Value.Clone();
            }
            catch (JsonException) { }
        }
        foreach (var pair in additions) values[pair.Key] = pair.Value;
        return JsonSerializer.Serialize(values, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
