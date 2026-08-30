using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class MicrosoftMailProvider(
    IHttpClientFactory httpClientFactory,
    IConnectedAccountAccessTokenProvider tokenProvider)
    : MailHttpProviderBase(httpClientFactory, tokenProvider), IMailProvider
{
    private static readonly Uri BaseUri = new("https://graph.microsoft.com/v1.0/me/");
    private static readonly string[] MailScopes = ["Mail.ReadWrite", "Mail.Send"];

    protected override CalendarProviderKind ProviderKind => CalendarProviderKind.Microsoft;
    protected override IReadOnlyCollection<string> Scopes => MailScopes;
    public CalendarProviderKind Kind => CalendarProviderKind.Microsoft;
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
            using var _ = await SendJsonAsync(accountId, HttpMethod.Get, Query(new Uri(BaseUri, "messages"), [new("$top", "1"), new("$select", "id")]), null, cancellationToken).ConfigureAwait(false);
            return new(true, "Outlook Mail is ready.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public async Task<IReadOnlyList<MailFolder>> GetFoldersAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var uri = Query(new Uri(BaseUri, "mailFolders"), [
            new("$top", "100"),
            new("includeHiddenFolders", "true"),
            new("$select", "id,displayName,unreadItemCount,totalItemCount")]);
        using var document = await SendJsonAsync(accountId, HttpMethod.Get, uri, null, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("value", out var value)) return [];
        return value.EnumerateArray().Select(folder =>
        {
            var display = Text(folder, "displayName");
            var kind = FolderKind(display);
            return new MailFolder(Text(folder, "id"), display, kind, Number(folder, "unreadItemCount"), Number(folder, "totalItemCount"), kind != MailFolderKind.Custom);
        }).OrderBy(folder => FolderOrder(folder.Kind)).ThenBy(folder => folder.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<MailPage> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken)
    {
        Uri uri;
        if (!string.IsNullOrWhiteSpace(query.ContinuationToken) && Uri.TryCreate(query.ContinuationToken, UriKind.Absolute, out var continuation))
        {
            uri = continuation;
        }
        else
        {
            var path = string.IsNullOrWhiteSpace(query.FolderId) ? "messages" : $"mailFolders/{Uri.EscapeDataString(query.FolderId)}/messages";
            var pairs = new List<KeyValuePair<string, string>>
            {
                new("$top", Math.Clamp(query.PageSize, 1, 100).ToString()),
                new("$select", "id,conversationId,from,toRecipients,subject,bodyPreview,receivedDateTime,isRead,flag,importance,hasAttachments,categories"),
                new("$orderby", "receivedDateTime desc")
            };
            var filters = new List<string>();
            if (query.UnreadOnly == true) filters.Add("isRead eq false");
            if (query.FlaggedOnly == true) filters.Add("flag/flagStatus eq 'flagged'");
            if (query.After is { } after) filters.Add($"receivedDateTime ge {after.UtcDateTime:O}");
            if (query.Before is { } before) filters.Add($"receivedDateTime lt {before.UtcDateTime:O}");
            if (filters.Count > 0) pairs.Add(new("$filter", string.Join(" and ", filters)));
            if (!string.IsNullOrWhiteSpace(query.Text)) pairs.Add(new("$search", $"\"{query.Text.Trim().Replace("\"", "\\\"", StringComparison.Ordinal)}\""));
            uri = Query(new Uri(BaseUri, path), pairs);
        }

        var headers = new Dictionary<string, string> { ["ConsistencyLevel"] = "eventual" };
        using var document = await SendJsonAsync(query.AccountId, HttpMethod.Get, uri, null, cancellationToken, headers).ConfigureAwait(false);
        var messages = document.RootElement.TryGetProperty("value", out var value)
            ? value.EnumerateArray().Select(ToSummary).ToArray()
            : [];
        var next = document.RootElement.TryGetProperty("@odata.nextLink", out var link) ? link.GetString() : null;
        return new MailPage(messages, next, DateTimeOffset.UtcNow);
    }

    public async Task<MailMessage> GetMessageAsync(Guid accountId, string messageId, CancellationToken cancellationToken)
    {
        var uri = Query(new Uri(BaseUri, $"messages/{Uri.EscapeDataString(messageId)}"), [
            new("$select", "id,conversationId,internetMessageId,from,toRecipients,ccRecipients,bccRecipients,subject,body,bodyPreview,receivedDateTime,isRead,flag,importance,categories"),
            new("$expand", "attachments($select=id,name,contentType,size,isInline)")]);
        using var document = await SendJsonAsync(accountId, HttpMethod.Get, uri, null, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var body = root.TryGetProperty("body", out var bodyElement) ? Text(bodyElement, "content") : Text(root, "bodyPreview");
        var contentType = root.TryGetProperty("body", out var bodyObject) ? Text(bodyObject, "contentType") : "text";
        var attachments = root.TryGetProperty("attachments", out var attachmentsArray)
            ? attachmentsArray.EnumerateArray().Select(item => new MailAttachmentDescriptor(Text(item, "id"), Text(item, "name"), Default(Text(item, "contentType"), "application/octet-stream"), LongNumber(item, "size"), Bool(item, "isInline"))).ToArray()
            : [];
        return new MailMessage(
            Text(root, "id"), Default(Text(root, "conversationId"), Text(root, "id")), Text(root, "internetMessageId"),
            Address(root, "from"), Addresses(root, "toRecipients"), Addresses(root, "ccRecipients"), Addresses(root, "bccRecipients"),
            Text(root, "subject"), contentType.Equals("html", StringComparison.OrdinalIgnoreCase) ? StripHtml(body) : body,
            contentType.Equals("html", StringComparison.OrdinalIgnoreCase) ? body : null, ParseDate(Text(root, "receivedDateTime")), Bool(root, "isRead"),
            Flagged(root), Important(root), Categories(root), attachments);
    }

    public async Task<IReadOnlyList<MailMessage>> GetThreadAsync(Guid accountId, string threadId, CancellationToken cancellationToken)
    {
        var escaped = threadId.Replace("'", "''", StringComparison.Ordinal);
        var uri = Query(new Uri(BaseUri, "messages"), [
            new("$filter", $"conversationId eq '{escaped}'"),
            new("$orderby", "receivedDateTime asc"),
            new("$top", "100"),
            new("$select", "id")]);
        using var document = await SendJsonAsync(accountId, HttpMethod.Get, uri, null, cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("value", out var messages)) return [];
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
        var uri = Query(new Uri(BaseUri, $"messages/{Uri.EscapeDataString(messageId)}/attachments/{Uri.EscapeDataString(attachmentId)}"), [new("$select", "contentBytes")]);
        using var document = await SendJsonAsync(accountId, HttpMethod.Get, uri, null, cancellationToken).ConfigureAwait(false);
        var content = Text(document.RootElement, "contentBytes");
        if (string.IsNullOrWhiteSpace(content)) throw new MailProviderException(MailFailureKind.ProviderError, "The provider did not return attachment content.");
        return Convert.FromBase64String(content);
    }

    public async Task<MailOperationResult> SaveDraftAsync(MailDraft draft, CancellationToken cancellationToken)
    {
        try
        {
            var id = await CreateOrUpdateDraftAsync(draft, cancellationToken).ConfigureAwait(false);
            return new(true, "Draft saved to Outlook.", ProviderId: id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public async Task<MailOperationResult> DeleteDraftAsync(Guid accountId, string draftId, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(accountId, HttpMethod.Delete, new Uri(BaseUri, $"messages/{Uri.EscapeDataString(draftId)}"), null, cancellationToken).ConfigureAwait(false);
            return new(true, "Draft deleted.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public async Task<MailOperationResult> SendAsync(MailDraft draft, CancellationToken cancellationToken)
    {
        try
        {
            var id = await CreateOrUpdateDraftAsync(draft, cancellationToken).ConfigureAwait(false);
            await SendAsync(draft.AccountId, HttpMethod.Post, new Uri(BaseUri, $"messages/{Uri.EscapeDataString(id)}/send"), null, cancellationToken).ConfigureAwait(false);
            return new(true, "Message sent.", ProviderId: id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public async Task<MailOperationResult> ArchiveAsync(Guid accountId, string messageId, CancellationToken cancellationToken)
    {
        try
        {
            using var archive = await SendJsonAsync(accountId, HttpMethod.Get, Query(new Uri(BaseUri, "mailFolders/archive"), [new("$select", "id")]), null, cancellationToken).ConfigureAwait(false);
            var destinationId = Text(archive.RootElement, "id");
            using var _ = await SendJsonAsync(accountId, HttpMethod.Post, new Uri(BaseUri, $"messages/{Uri.EscapeDataString(messageId)}/move"), JsonContent.Create(new { destinationId }), cancellationToken).ConfigureAwait(false);
            return new(true, "Message archived.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public async Task<MailOperationResult> DeleteAsync(Guid accountId, string messageId, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(accountId, HttpMethod.Delete, new Uri(BaseUri, $"messages/{Uri.EscapeDataString(messageId)}"), null, cancellationToken).ConfigureAwait(false);
            return new(true, "Message deleted.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public Task<MailOperationResult> SetReadAsync(Guid accountId, string messageId, bool isRead, CancellationToken cancellationToken)
        => PatchStateAsync(accountId, messageId, JsonContent.Create(new { isRead }), isRead ? "Marked read." : "Marked unread.", cancellationToken);

    public Task<MailOperationResult> SetFlaggedAsync(Guid accountId, string messageId, bool isFlagged, CancellationToken cancellationToken)
        => PatchStateAsync(accountId, messageId, JsonContent.Create(new { flag = new { flagStatus = isFlagged ? "flagged" : "notFlagged" } }), isFlagged ? "Flagged." : "Flag removed.", cancellationToken);

    public async Task<MailOperationResult> CreateFolderAsync(Guid accountId, string displayName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return new(false, "Enter a folder name.", MailFailureKind.InvalidRequest);
        try
        {
            using var document = await SendJsonAsync(accountId, HttpMethod.Post, new Uri(BaseUri, "mailFolders"), JsonContent.Create(new { displayName = displayName.Trim() }), cancellationToken).ConfigureAwait(false);
            return new(true, "Folder created.", ProviderId: Text(document.RootElement, "id"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public async Task<MailOperationResult> RenameFolderAsync(Guid accountId, string folderId, string displayName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return new(false, "Enter a folder name.", MailFailureKind.InvalidRequest);
        try
        {
            using var document = await SendJsonAsync(accountId, HttpMethod.Patch, new Uri(BaseUri, $"mailFolders/{Uri.EscapeDataString(folderId)}"), JsonContent.Create(new { displayName = displayName.Trim() }), cancellationToken).ConfigureAwait(false);
            return new(true, "Folder renamed.", ProviderId: Text(document.RootElement, "id"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public async Task<MailOperationResult> DeleteFolderAsync(Guid accountId, string folderId, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(accountId, HttpMethod.Delete, new Uri(BaseUri, $"mailFolders/{Uri.EscapeDataString(folderId)}"), null, cancellationToken).ConfigureAwait(false);
            return new(true, "Folder deleted.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public async Task<MailOperationResult> MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await SendJsonAsync(accountId, HttpMethod.Post, new Uri(BaseUri, $"messages/{Uri.EscapeDataString(messageId)}/move"), JsonContent.Create(new { destinationId = folderId }), cancellationToken).ConfigureAwait(false);
            return new(true, "Message moved.", ProviderId: Text(document.RootElement, "id"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    public Task<MailOperationResult> SetSpamAsync(Guid accountId, string messageId, bool isSpam, CancellationToken cancellationToken)
        => MoveToFolderAsync(accountId, messageId, isSpam ? "junkemail" : "inbox", cancellationToken);

    public Task<MailOperationResult> SetImportantAsync(Guid accountId, string messageId, bool isImportant, CancellationToken cancellationToken)
        => PatchStateAsync(accountId, messageId, JsonContent.Create(new { importance = isImportant ? "high" : "normal" }), isImportant ? "Marked important." : "Importance removed.", cancellationToken);

    private async Task<MailOperationResult> PatchStateAsync(Guid accountId, string messageId, HttpContent content, string success, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendJsonAsync(accountId, HttpMethod.Patch, new Uri(BaseUri, $"messages/{Uri.EscapeDataString(messageId)}"), content, cancellationToken).ConfigureAwait(false);
            return new(true, success);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Failure(ex); }
    }

    private async Task<string> CreateOrUpdateDraftAsync(MailDraft draft, CancellationToken cancellationToken)
    {
        string? id = draft.DraftId;
        if (string.IsNullOrWhiteSpace(id))
        {
            if (draft.ResponseKind is not MailResponseKind.New && !string.IsNullOrWhiteSpace(draft.SourceMessageId))
            {
                var action = draft.ResponseKind switch
                {
                    MailResponseKind.Reply => "createReply",
                    MailResponseKind.ReplyAll => "createReplyAll",
                    MailResponseKind.Forward => "createForward",
                    _ => throw new InvalidOperationException()
                };
                using var response = await SendJsonAsync(draft.AccountId, HttpMethod.Post, new Uri(BaseUri, $"messages/{Uri.EscapeDataString(draft.SourceMessageId)}/{action}"), null, cancellationToken).ConfigureAwait(false);
                id = Text(response.RootElement, "id");
            }
            else
            {
                using var response = await SendJsonAsync(draft.AccountId, HttpMethod.Post, new Uri(BaseUri, "messages"), CreateMessageContent(draft), cancellationToken).ConfigureAwait(false);
                id = Text(response.RootElement, "id");
            }
        }

        if (string.IsNullOrWhiteSpace(id)) throw new MailProviderException(MailFailureKind.ProviderError, "The provider did not return a draft identifier.");
        using (var _ = await SendJsonAsync(draft.AccountId, HttpMethod.Patch, new Uri(BaseUri, $"messages/{Uri.EscapeDataString(id)}"), CreateMessageContent(draft), cancellationToken).ConfigureAwait(false)) { }
        await ReplaceAttachmentsAsync(draft.AccountId, id, draft.Attachments, cancellationToken).ConfigureAwait(false);
        return id;
    }

    private async Task ReplaceAttachmentsAsync(Guid accountId, string messageId, IReadOnlyList<MailDraftAttachment> attachments, CancellationToken cancellationToken)
    {
        using var existing = await SendJsonAsync(accountId, HttpMethod.Get, Query(new Uri(BaseUri, $"messages/{Uri.EscapeDataString(messageId)}/attachments"), [new("$select", "id")]), null, cancellationToken).ConfigureAwait(false);
        if (existing.RootElement.TryGetProperty("value", out var value))
        {
            foreach (var attachment in value.EnumerateArray())
            {
                var id = Text(attachment, "id");
                if (!string.IsNullOrWhiteSpace(id)) await SendAsync(accountId, HttpMethod.Delete, new Uri(BaseUri, $"messages/{Uri.EscapeDataString(messageId)}/attachments/{Uri.EscapeDataString(id)}"), null, cancellationToken).ConfigureAwait(false);
            }
        }
        foreach (var attachment in attachments)
        {
            var content = JsonContent.Create(new Dictionary<string, object?>
            {
                ["@odata.type"] = "#microsoft.graph.fileAttachment",
                ["name"] = attachment.FileName,
                ["contentType"] = attachment.ContentType,
                ["contentBytes"] = Convert.ToBase64String(attachment.Content)
            });
            using var _ = await SendJsonAsync(accountId, HttpMethod.Post, new Uri(BaseUri, $"messages/{Uri.EscapeDataString(messageId)}/attachments"), content, cancellationToken).ConfigureAwait(false);
        }
    }

    private static HttpContent CreateMessageContent(MailDraft draft) => JsonContent.Create(new
    {
        subject = draft.Subject,
        body = new { contentType = draft.IsHtml ? "HTML" : "Text", content = draft.Body },
        toRecipients = draft.To.Select(Recipient).ToArray(),
        ccRecipients = draft.Cc.Select(Recipient).ToArray(),
        bccRecipients = draft.Bcc.Select(Recipient).ToArray()
    });

    private static object Recipient(MailAddress address) => new { emailAddress = new { name = address.DisplayName, address = address.Address } };

    private static MailMessageSummary ToSummary(JsonElement root)
    {
        var categories = Categories(root);
        return new MailMessageSummary(
            Text(root, "id"), Default(Text(root, "conversationId"), Text(root, "id")), Address(root, "from"), Addresses(root, "toRecipients"),
            Text(root, "subject"), Text(root, "bodyPreview"), ParseDate(Text(root, "receivedDateTime")), Bool(root, "isRead"), Flagged(root),
            Important(root), Bool(root, "hasAttachments"), categories);
    }

    private static MailAddress Address(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var recipient) || !recipient.TryGetProperty("emailAddress", out var email)) return new MailAddress(string.Empty, string.Empty);
        return new MailAddress(Text(email, "name"), Text(email, "address"));
    }

    private static IReadOnlyList<MailAddress> Addresses(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var recipients) || recipients.ValueKind != JsonValueKind.Array) return [];
        return recipients.EnumerateArray().Select(item =>
        {
            var email = item.TryGetProperty("emailAddress", out var value) ? value : default;
            return new MailAddress(email.ValueKind == JsonValueKind.Object ? Text(email, "name") : string.Empty, email.ValueKind == JsonValueKind.Object ? Text(email, "address") : string.Empty);
        }).Where(item => !string.IsNullOrWhiteSpace(item.Address)).ToArray();
    }

    private static IReadOnlyList<string> Categories(JsonElement root) => root.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array
        ? categories.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(item => item.Length > 0).ToArray()
        : [];
    private static bool Flagged(JsonElement root) => root.TryGetProperty("flag", out var flag) && Text(flag, "flagStatus").Equals("flagged", StringComparison.OrdinalIgnoreCase);
    private static bool Important(JsonElement root) => Text(root, "importance").Equals("high", StringComparison.OrdinalIgnoreCase);
    private static bool Bool(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() ?? value.ToString() : string.Empty;
    private static int? Number(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
    private static long LongNumber(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;
    private static string Default(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private static DateTimeOffset ParseDate(string value) => DateTimeOffset.TryParse(value, out var date) ? date : DateTimeOffset.MinValue;
    private static string StripHtml(string value) => Regex.Replace(value, "<[^>]+>", " " ).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
    private static MailFolderKind FolderKind(string display) => display.Trim().ToLowerInvariant() switch { "inbox" => MailFolderKind.Inbox, "drafts" => MailFolderKind.Drafts, "sent items" => MailFolderKind.Sent, "archive" => MailFolderKind.Archive, "deleted items" => MailFolderKind.Trash, "junk email" => MailFolderKind.Spam, _ => MailFolderKind.Custom };
    private static int FolderOrder(MailFolderKind kind) => kind switch { MailFolderKind.Inbox => 0, MailFolderKind.Drafts => 1, MailFolderKind.Sent => 2, MailFolderKind.Archive => 3, MailFolderKind.Trash => 4, MailFolderKind.Spam => 5, _ => 10 };
    private static Uri Query(Uri uri, IEnumerable<KeyValuePair<string, string>> pairs) => new(uri + "?" + string.Join('&', pairs.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")));
}
