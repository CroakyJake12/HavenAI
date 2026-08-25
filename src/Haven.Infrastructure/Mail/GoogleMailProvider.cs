using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class GoogleMailProvider(
    IHttpClientFactory httpClientFactory,
    IConnectedAccountAccessTokenProvider tokenProvider)
    : MailHttpProviderBase(httpClientFactory, tokenProvider), IMailProvider
{
    private static readonly Uri BaseUri = new("https://gmail.googleapis.com/gmail/v1/users/me/");
    private static readonly string[] MailScopes = ["https://www.googleapis.com/auth/gmail.modify"];

    protected override CalendarProviderKind ProviderKind => CalendarProviderKind.Google;
    protected override IReadOnlyCollection<string> Scopes => MailScopes;
    public CalendarProviderKind Kind => CalendarProviderKind.Google;
    public IReadOnlyCollection<string> RequiredScopes => MailScopes;
    public MailProviderCapabilities Capabilities => MailProviderCapabilities.Search | MailProviderCapabilities.Threads |
        MailProviderCapabilities.Drafts | MailProviderCapabilities.Send | MailProviderCapabilities.Attachments |
        MailProviderCapabilities.Archive | MailProviderCapabilities.Delete | MailProviderCapabilities.ReadState |
        MailProviderCapabilities.Flag | MailProviderCapabilities.Folders | MailProviderCapabilities.FolderManagement |
        MailProviderCapabilities.Move | MailProviderCapabilities.Spam | MailProviderCapabilities.Important | MailProviderCapabilities.Bulk;

    public async Task<MailOperationResult> CheckAccessAsync(Guid accountId, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendJsonAsync(accountId, HttpMethod.Get, new Uri(BaseUri, "profile"), null, cancellationToken).ConfigureAwait(false);
            return new(true, "Gmail is ready.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public async Task<IReadOnlyList<MailFolder>> GetFoldersAsync(Guid accountId, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(accountId, HttpMethod.Get, new Uri(BaseUri, "labels"), null, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("labels", out var labels)) return [];
        return labels.EnumerateArray()
            .Where(label => label.TryGetProperty("id", out _))
            .Select(label =>
            {
                var id = Text(label, "id");
                var name = Text(label, "name");
                return new MailFolder(id, name, FolderKind(id, name), Number(label, "messagesUnread"), Number(label, "messagesTotal"), Text(label, "type").Equals("system", StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(folder => FolderOrder(folder.Kind)).ThenBy(folder => folder.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<MailPage> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken)
    {
        var pairs = new List<KeyValuePair<string, string>> { new("maxResults", Math.Clamp(query.PageSize, 1, 100).ToString()) };
        if (!string.IsNullOrWhiteSpace(query.ContinuationToken)) pairs.Add(new("pageToken", query.ContinuationToken));
        if (!string.IsNullOrWhiteSpace(query.FolderId)) pairs.Add(new("labelIds", query.FolderId));
        var search = BuildSearch(query);
        if (!string.IsNullOrWhiteSpace(search)) pairs.Add(new("q", search));
        var listUri = Query(new Uri(BaseUri, "messages"), pairs);
        using var list = await SendJsonAsync(query.AccountId, HttpMethod.Get, listUri, null, cancellationToken).ConfigureAwait(false);
        var result = new List<MailMessageSummary>();
        if (list.RootElement.TryGetProperty("messages", out var messages))
        {
            foreach (var item in messages.EnumerateArray())
            {
                var id = Text(item, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                var message = await GetMessageAsync(query.AccountId, id, cancellationToken).ConfigureAwait(false);
                result.Add(ToSummary(message));
            }
        }
        var next = list.RootElement.TryGetProperty("nextPageToken", out var token) ? token.GetString() : null;
        return new MailPage(result, next, DateTimeOffset.UtcNow);
    }

    public async Task<MailMessage> GetMessageAsync(Guid accountId, string messageId, CancellationToken cancellationToken)
    {
        var uri = Query(new Uri(BaseUri, $"messages/{Uri.EscapeDataString(messageId)}"), [new("format", "full")]);
        using var document = await SendJsonAsync(accountId, HttpMethod.Get, uri, null, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var payload = root.GetProperty("payload");
        var headers = ReadHeaders(payload);
        var parts = new GmailBodyParts();
        ReadParts(payload, parts);
        var labels = root.TryGetProperty("labelIds", out var labelArray)
            ? labelArray.EnumerateArray().Select(value => value.GetString() ?? string.Empty).Where(value => value.Length > 0).ToArray()
            : [];
        var received = root.TryGetProperty("internalDate", out var internalDate) && long.TryParse(internalDate.GetString(), out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : ParseDate(Header(headers, "Date"));
        return new MailMessage(
            Text(root, "id"), Text(root, "threadId"), Header(headers, "Message-ID"),
            ParseSingleAddress(Header(headers, "From")), ParseAddresses(Header(headers, "To")),
            ParseAddresses(Header(headers, "Cc")), ParseAddresses(Header(headers, "Bcc")),
            Header(headers, "Subject"), parts.PlainText, parts.Html, received, !labels.Contains("UNREAD"),
            labels.Contains("STARRED"), labels.Contains("IMPORTANT"), labels, parts.Attachments);
    }

    public async Task<IReadOnlyList<MailMessage>> GetThreadAsync(Guid accountId, string threadId, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(accountId, HttpMethod.Get, new Uri(BaseUri, $"threads/{Uri.EscapeDataString(threadId)}"), null, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("messages", out var messages)) return [];
        var result = new List<MailMessage>();
        foreach (var item in messages.EnumerateArray())
        {
            var id = Text(item, "id");
            if (!string.IsNullOrWhiteSpace(id)) result.Add(await GetMessageAsync(accountId, id, cancellationToken).ConfigureAwait(false));
        }
        return result.OrderBy(message => message.ReceivedAt).ToArray();
    }

    public async Task<byte[]> DownloadAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken cancellationToken)
    {
        using var document = await SendJsonAsync(accountId, HttpMethod.Get, new Uri(BaseUri, $"messages/{Uri.EscapeDataString(messageId)}/attachments/{Uri.EscapeDataString(attachmentId)}"), null, cancellationToken).ConfigureAwait(false);
        return DecodeBase64Url(Text(document.RootElement, "data"));
    }

    public async Task<MailOperationResult> SaveDraftAsync(MailDraft draft, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await BuildRawAsync(draft, cancellationToken).ConfigureAwait(false);
            var body = JsonContent.Create(new { message = new { raw, threadId = draft.ThreadId } });
            var method = string.IsNullOrWhiteSpace(draft.DraftId) ? HttpMethod.Post : HttpMethod.Put;
            var uri = string.IsNullOrWhiteSpace(draft.DraftId) ? new Uri(BaseUri, "drafts") : new Uri(BaseUri, $"drafts/{Uri.EscapeDataString(draft.DraftId)}");
            using var document = await SendJsonAsync(draft.AccountId, method, uri, body, cancellationToken).ConfigureAwait(false);
            return new(true, "Draft saved to Gmail.", ProviderId: Text(document.RootElement, "id"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public async Task<MailOperationResult> DeleteDraftAsync(Guid accountId, string draftId, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(accountId, HttpMethod.Delete, new Uri(BaseUri, $"drafts/{Uri.EscapeDataString(draftId)}"), null, cancellationToken).ConfigureAwait(false);
            return new(true, "Draft deleted.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public async Task<MailOperationResult> SendAsync(MailDraft draft, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await BuildRawAsync(draft, cancellationToken).ConfigureAwait(false);
            HttpContent content;
            Uri uri;
            if (!string.IsNullOrWhiteSpace(draft.DraftId))
            {
                uri = new Uri(BaseUri, "drafts/send");
                content = JsonContent.Create(new { id = draft.DraftId, message = new { raw, threadId = draft.ThreadId } });
            }
            else
            {
                uri = new Uri(BaseUri, "messages/send");
                content = JsonContent.Create(new { raw, threadId = draft.ThreadId });
            }
            using var document = await SendJsonAsync(draft.AccountId, HttpMethod.Post, uri, content, cancellationToken).ConfigureAwait(false);
            return new(true, "Message sent.", ProviderId: Text(document.RootElement, "id"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public Task<MailOperationResult> ArchiveAsync(Guid accountId, string messageId, CancellationToken cancellationToken)
        => ModifyLabelsAsync(accountId, messageId, [], ["INBOX"], "Message archived.", cancellationToken);

    public async Task<MailOperationResult> DeleteAsync(Guid accountId, string messageId, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendJsonAsync(accountId, HttpMethod.Post, new Uri(BaseUri, $"messages/{Uri.EscapeDataString(messageId)}/trash"), JsonContent.Create(new { }), cancellationToken).ConfigureAwait(false);
            return new(true, "Message moved to Bin.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public Task<MailOperationResult> SetReadAsync(Guid accountId, string messageId, bool isRead, CancellationToken cancellationToken)
        => ModifyLabelsAsync(accountId, messageId, isRead ? [] : ["UNREAD"], isRead ? ["UNREAD"] : [], isRead ? "Marked read." : "Marked unread.", cancellationToken);

    public Task<MailOperationResult> SetFlaggedAsync(Guid accountId, string messageId, bool isFlagged, CancellationToken cancellationToken)
        => ModifyLabelsAsync(accountId, messageId, isFlagged ? ["STARRED"] : [], isFlagged ? [] : ["STARRED"], isFlagged ? "Starred." : "Star removed.", cancellationToken);

    public async Task<MailOperationResult> CreateFolderAsync(Guid accountId, string displayName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return new(false, "Enter a label name.", MailFailureKind.InvalidRequest);
        try
        {
            using var document = await SendJsonAsync(accountId, HttpMethod.Post, new Uri(BaseUri, "labels"),
                JsonContent.Create(new { name = displayName.Trim(), labelListVisibility = "labelShow", messageListVisibility = "show" }), cancellationToken).ConfigureAwait(false);
            return new(true, "Label created.", ProviderId: Text(document.RootElement, "id"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public async Task<MailOperationResult> RenameFolderAsync(Guid accountId, string folderId, string displayName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return new(false, "Enter a label name.", MailFailureKind.InvalidRequest);
        try
        {
            using var document = await SendJsonAsync(accountId, HttpMethod.Patch, new Uri(BaseUri, $"labels/{Uri.EscapeDataString(folderId)}"),
                JsonContent.Create(new { name = displayName.Trim() }), cancellationToken).ConfigureAwait(false);
            return new(true, "Label renamed.", ProviderId: Text(document.RootElement, "id"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public async Task<MailOperationResult> DeleteFolderAsync(Guid accountId, string folderId, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(accountId, HttpMethod.Delete, new Uri(BaseUri, $"labels/{Uri.EscapeDataString(folderId)}"), null, cancellationToken).ConfigureAwait(false);
            return new(true, "Label deleted.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public Task<MailOperationResult> MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken cancellationToken)
    {
        var destination = folderId.Trim();
        var remove = destination.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ? Array.Empty<string>() : ["INBOX"];
        return ModifyLabelsAsync(accountId, messageId, [destination], remove, "Message moved.", cancellationToken);
    }

    public Task<MailOperationResult> SetSpamAsync(Guid accountId, string messageId, bool isSpam, CancellationToken cancellationToken)
        => ModifyLabelsAsync(accountId, messageId, isSpam ? ["SPAM"] : ["INBOX"], isSpam ? ["INBOX"] : ["SPAM"], isSpam ? "Moved to Spam." : "Removed from Spam.", cancellationToken);

    public Task<MailOperationResult> SetImportantAsync(Guid accountId, string messageId, bool isImportant, CancellationToken cancellationToken)
        => ModifyLabelsAsync(accountId, messageId, isImportant ? ["IMPORTANT"] : [], isImportant ? [] : ["IMPORTANT"], isImportant ? "Marked important." : "Importance removed.", cancellationToken);

    private async Task<MailOperationResult> ModifyLabelsAsync(Guid accountId, string messageId, string[] add, string[] remove, string success, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendJsonAsync(accountId, HttpMethod.Post, new Uri(BaseUri, $"messages/{Uri.EscapeDataString(messageId)}/modify"), JsonContent.Create(new { addLabelIds = add, removeLabelIds = remove }), cancellationToken).ConfigureAwait(false);
            return new(true, success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    private async Task<string> BuildRawAsync(MailDraft draft, CancellationToken cancellationToken)
    {
        string? replyMessageId = null;
        if (draft.ResponseKind is MailResponseKind.Reply or MailResponseKind.ReplyAll && !string.IsNullOrWhiteSpace(draft.SourceMessageId))
            replyMessageId = (await GetMessageAsync(draft.AccountId, draft.SourceMessageId, cancellationToken).ConfigureAwait(false)).InternetMessageId;
        return EncodeBase64Url(BuildMime(draft, replyMessageId));
    }

    private static byte[] BuildMime(MailDraft draft, string? inReplyTo)
    {
        var builder = new StringBuilder();
        if (draft.To.Count > 0) builder.Append("To: " + string.Join(", ", draft.To.Select(FormatAddress)) + "\r\n");
        if (draft.Cc.Count > 0) builder.Append("Cc: " + string.Join(", ", draft.Cc.Select(FormatAddress)) + "\r\n");
        if (draft.Bcc.Count > 0) builder.Append("Bcc: " + string.Join(", ", draft.Bcc.Select(FormatAddress)) + "\r\n");
        builder.Append("Subject: " + EncodeHeader(draft.Subject) + "\r\n");
        builder.Append("MIME-Version: 1.0\r\n");
        if (!string.IsNullOrWhiteSpace(inReplyTo))
        {
            builder.Append("In-Reply-To: " + inReplyTo + "\r\n");
            builder.Append("References: " + inReplyTo + "\r\n");
        }

        if (draft.Attachments.Count == 0)
        {
            builder.Append($"Content-Type: {(draft.IsHtml ? "text/html" : "text/plain")}; charset=utf-8\r\n");
            builder.Append("Content-Transfer-Encoding: base64\r\n\r\n");
            builder.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(draft.Body)));
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        var boundary = "haven_" + Guid.NewGuid().ToString("N");
        builder.Append($"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n\r\n");
        builder.Append($"--{boundary}\r\nContent-Type: {(draft.IsHtml ? "text/html" : "text/plain")}; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n");
        builder.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(draft.Body)) + "\r\n");
        foreach (var attachment in draft.Attachments)
        {
            var name = attachment.FileName.Replace("\"", string.Empty, StringComparison.Ordinal);
            builder.Append($"--{boundary}\r\nContent-Type: {attachment.ContentType}; name=\"{name}\"\r\nContent-Disposition: attachment; filename=\"{name}\"\r\nContent-Transfer-Encoding: base64\r\n\r\n");
            builder.Append(Convert.ToBase64String(attachment.Content, Base64FormattingOptions.InsertLineBreaks) + "\r\n");
        }
        builder.Append($"--{boundary}--\r\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static MailMessageSummary ToSummary(MailMessage message) => new(
        message.Id, message.ThreadId, message.From, message.To, message.Subject,
        StripHtml(string.IsNullOrWhiteSpace(message.PlainTextBody) ? message.HtmlBody ?? string.Empty : message.PlainTextBody),
        message.ReceivedAt, message.IsRead, message.IsFlagged, message.IsImportant, message.Attachments.Count > 0, message.Labels);

    private static string BuildSearch(MailQuery query)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Text)) parts.Add(query.Text.Trim());
        if (query.UnreadOnly == true) parts.Add("is:unread");
        if (query.FlaggedOnly == true) parts.Add("is:starred");
        if (query.After is { } after) parts.Add($"after:{after.UtcDateTime:yyyy/MM/dd}");
        if (query.Before is { } before) parts.Add($"before:{before.UtcDateTime:yyyy/MM/dd}");
        return string.Join(' ', parts);
    }

    private static Dictionary<string, string> ReadHeaders(JsonElement payload)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!payload.TryGetProperty("headers", out var headers)) return result;
        foreach (var item in headers.EnumerateArray())
        {
            var name = Text(item, "name");
            if (name.Length > 0) result[name] = Text(item, "value");
        }
        return result;
    }

    private static void ReadParts(JsonElement part, GmailBodyParts result)
    {
        var mime = Text(part, "mimeType");
        var fileName = Text(part, "filename");
        if (part.TryGetProperty("body", out var body))
        {
            var data = Text(body, "data");
            if (!string.IsNullOrWhiteSpace(data))
            {
                var text = Encoding.UTF8.GetString(DecodeBase64Url(data));
                if (mime.Equals("text/plain", StringComparison.OrdinalIgnoreCase)) result.PlainText += text;
                else if (mime.Equals("text/html", StringComparison.OrdinalIgnoreCase)) result.Html = (result.Html ?? string.Empty) + text;
            }
            var attachmentId = Text(body, "attachmentId");
            if (!string.IsNullOrWhiteSpace(attachmentId))
                result.Attachments.Add(new MailAttachmentDescriptor(attachmentId, string.IsNullOrWhiteSpace(fileName) ? "attachment" : fileName, string.IsNullOrWhiteSpace(mime) ? "application/octet-stream" : mime, LongNumber(body, "size")));
        }
        if (part.TryGetProperty("parts", out var children))
            foreach (var child in children.EnumerateArray()) ReadParts(child, result);
    }

    private static MailAddress ParseSingleAddress(string value) => ParseAddresses(value).FirstOrDefault() ?? new MailAddress(string.Empty, value);
    private static IReadOnlyList<MailAddress> ParseAddresses(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        try
        {
            var message = new System.Net.Mail.MailMessage();
            message.To.Add(value);
            return message.To.Select(address => new MailAddress(address.DisplayName ?? string.Empty, address.Address)).ToArray();
        }
        catch (FormatException)
        {
            return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(address => new MailAddress(string.Empty, address)).ToArray();
        }
    }

    private static string FormatAddress(MailAddress address) => string.IsNullOrWhiteSpace(address.DisplayName) ? address.Address : $"\"{address.DisplayName.Replace("\"", string.Empty, StringComparison.Ordinal)}\" <{address.Address}>";
    private static string EncodeHeader(string value) => value.All(character => character <= 127) ? value : $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";
    private static string Header(IReadOnlyDictionary<string, string> headers, string name) => headers.TryGetValue(name, out var value) ? value : string.Empty;
    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;
    private static string StripHtml(string value) => System.Text.RegularExpressions.Regex.Replace(value, "<[^>]+>", " " ).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
    private static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() ?? value.ToString() : string.Empty;
    private static int? Number(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
    private static long LongNumber(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;
    private static int FolderOrder(MailFolderKind kind) => kind switch { MailFolderKind.Inbox => 0, MailFolderKind.Drafts => 1, MailFolderKind.Sent => 2, MailFolderKind.Archive => 3, MailFolderKind.Trash => 4, MailFolderKind.Spam => 5, _ => 10 };
    private static MailFolderKind FolderKind(string id, string name) => id.ToUpperInvariant() switch { "INBOX" => MailFolderKind.Inbox, "SENT" => MailFolderKind.Sent, "DRAFT" => MailFolderKind.Drafts, "TRASH" => MailFolderKind.Trash, "SPAM" => MailFolderKind.Spam, "IMPORTANT" => MailFolderKind.Important, _ when name.Equals("Archive", StringComparison.OrdinalIgnoreCase) => MailFolderKind.Archive, _ => MailFolderKind.Custom };
    private static Uri Query(Uri uri, IEnumerable<KeyValuePair<string, string>> pairs) => new(uri + "?" + string.Join('&', pairs.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")));
    private static string EncodeBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);
        return Convert.FromBase64String(normalized);
    }

    private sealed class GmailBodyParts
    {
        public string PlainText { get; set; } = string.Empty;
        public string? Html { get; set; }
        public List<MailAttachmentDescriptor> Attachments { get; } = [];
    }
}
