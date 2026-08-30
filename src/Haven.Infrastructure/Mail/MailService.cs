using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class MailProviderRegistry : IMailProviderRegistry
{
    private readonly IReadOnlyDictionary<CalendarProviderKind, IMailProvider> _byKind;

    public MailProviderRegistry(IEnumerable<IMailProvider> providers)
    {
        _byKind = providers.ToDictionary(provider => provider.Kind);
        Providers = _byKind.Values.OrderBy(provider => provider.Kind).ToArray();
    }

    public IReadOnlyList<IMailProvider> Providers { get; }

    public IMailProvider Get(CalendarProviderKind kind) =>
        _byKind.TryGetValue(kind, out var provider)
            ? provider
            : throw new MailProviderException(MailFailureKind.InvalidRequest, $"No Mail provider is registered for {kind}.");
}

public sealed class MailService(IPlannerRepository planner, IMailProviderRegistry providers, IMailDraftStore drafts) : IMailService
{
    public async Task<IReadOnlyList<MailAccount>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        var accounts = await planner.GetCalendarAccountsAsync(cancellationToken).ConfigureAwait(false);
        return accounts
            .Where(account => account.Status is not (CalendarSyncStatus.NotConfigured or CalendarSyncStatus.Disconnected))
            .Where(account => providers.Providers.Any(provider => provider.Kind == account.Provider))
            .OrderBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(account => new MailAccount(account.Id, account.Provider, account.DisplayName, account.AccountIdentifier, providers.Get(account.Provider).Capabilities))
            .ToArray();
    }

    public async Task<MailOperationResult> CheckAccessAsync(Guid accountId, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.CheckAccessAsync(accountId, ct), cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<MailFolder>> GetFoldersAsync(Guid accountId, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.GetFoldersAsync(accountId, ct), cancellationToken).ConfigureAwait(false);

    public async Task<MailPage> GetMessagesAsync(MailQuery query, CancellationToken cancellationToken) =>
        await WithProviderAsync(query.AccountId, (provider, ct) => provider.GetMessagesAsync(query, ct), cancellationToken).ConfigureAwait(false);

    public async Task<MailMessage> GetMessageAsync(Guid accountId, string messageId, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.GetMessageAsync(accountId, messageId, ct), cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<MailMessage>> GetThreadAsync(Guid accountId, string threadId, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.GetThreadAsync(accountId, threadId, ct), cancellationToken).ConfigureAwait(false);

    public async Task<byte[]> DownloadAttachmentAsync(Guid accountId, string messageId, string attachmentId, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.DownloadAttachmentAsync(accountId, messageId, attachmentId, ct), cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<MailDraft>> GetDraftsAsync(CancellationToken cancellationToken) =>
        (await drafts.GetAllAsync(cancellationToken).ConfigureAwait(false))
        .Where(item => item.PersistenceState != MailDraftPersistenceState.Sent)
        .OrderByDescending(item => item.UpdatedAt ?? DateTimeOffset.MinValue)
        .ToArray();

    public Task<MailDraft?> GetDraftAsync(Guid localDraftId, CancellationToken cancellationToken) => drafts.GetAsync(localDraftId, cancellationToken);

    public async Task<MailOperationResult> SaveDraftAsync(MailDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var localId = draft.LocalId == Guid.Empty ? Guid.NewGuid() : draft.LocalId;
        CalendarAccount? account = null;
        MailDraft local = draft with { LocalId = localId, UpdatedAt = DateTimeOffset.UtcNow, PersistenceState = MailDraftPersistenceState.Saving, LastSafeError = null };
        try
        {
            account = await ResolveAccountAsync(draft.AccountId, cancellationToken).ConfigureAwait(false);
            local = NormalizeDraft(local, account.Provider, MailDraftPersistenceState.Saving);
            await drafts.UpsertAsync(local, cancellationToken).ConfigureAwait(false);
            var result = await providers.Get(account.Provider).SaveDraftAsync(local, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                var failed = local with { UpdatedAt = DateTimeOffset.UtcNow, PersistenceState = MailDraftPersistenceState.SaveFailed, LastSafeError = result.Message };
                await drafts.UpsertAsync(failed, cancellationToken).ConfigureAwait(false);
                return result with { LocalDraftId = localId, SuggestedRemediation = RemediationFor(result.FailureKind) };
            }
            var saved = local with
            {
                DraftId = result.ProviderId ?? local.DraftId,
                UpdatedAt = DateTimeOffset.UtcNow,
                PersistenceState = MailDraftPersistenceState.Saved,
                LastSafeError = null
            };
            await drafts.UpsertAsync(saved, cancellationToken).ConfigureAwait(false);
            return result with { LocalDraftId = localId, ProviderId = saved.DraftId, SuggestedRemediation = null };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var result = SafeFailure(ex, localId);
            var failed = local with
            {
                Provider = account?.Provider ?? local.Provider,
                UpdatedAt = DateTimeOffset.UtcNow,
                PersistenceState = MailDraftPersistenceState.SaveFailed,
                LastSafeError = result.Message
            };
            await drafts.UpsertAsync(failed, CancellationToken.None).ConfigureAwait(false);
            return result;
        }
    }

    public async Task<MailOperationResult> DeleteDraftAsync(Guid accountId, string draftId, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.DeleteDraftAsync(accountId, draftId, ct), cancellationToken).ConfigureAwait(false);

    public async Task<MailOperationResult> DiscardDraftAsync(Guid localDraftId, CancellationToken cancellationToken)
    {
        var draft = await drafts.GetAsync(localDraftId, cancellationToken).ConfigureAwait(false);
        if (draft is null) return new(true, "Draft discarded.", LocalDraftId: localDraftId);
        if (!string.IsNullOrWhiteSpace(draft.DraftId))
        {
            var providerResult = await DeleteDraftAsync(draft.AccountId, draft.DraftId, cancellationToken).ConfigureAwait(false);
            if (!providerResult.Succeeded) return providerResult with { LocalDraftId = localDraftId, SuggestedRemediation = RemediationFor(providerResult.FailureKind) };
        }
        await drafts.DeleteAsync(localDraftId, cancellationToken).ConfigureAwait(false);
        return new(true, "Draft discarded.", LocalDraftId: localDraftId);
    }

    public async Task<MailOperationResult> SendAsync(MailDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var localId = draft.LocalId == Guid.Empty ? Guid.NewGuid() : draft.LocalId;
        var existing = draft.LocalId == Guid.Empty ? null : await drafts.GetAsync(localId, cancellationToken).ConfigureAwait(false);
        if (existing?.PersistenceState == MailDraftPersistenceState.Sent)
            return new(true, "Message was already sent.", ProviderId: existing.DraftId, LocalDraftId: localId);

        CalendarAccount? account = null;
        MailDraft sending = draft with { LocalId = localId };
        try
        {
            account = await ResolveAccountAsync(draft.AccountId, cancellationToken).ConfigureAwait(false);
            sending = NormalizeDraft(sending, account.Provider, MailDraftPersistenceState.Sending);
            await drafts.UpsertAsync(sending, cancellationToken).ConfigureAwait(false);
            var provider = providers.Get(account.Provider);

            if (string.IsNullOrWhiteSpace(sending.DraftId))
            {
                var save = await provider.SaveDraftAsync(sending, cancellationToken).ConfigureAwait(false);
                if (!save.Succeeded)
                {
                    var saveFailed = sending with { UpdatedAt = DateTimeOffset.UtcNow, PersistenceState = MailDraftPersistenceState.SaveFailed, LastSafeError = save.Message };
                    await drafts.UpsertAsync(saveFailed, cancellationToken).ConfigureAwait(false);
                    return save with { LocalDraftId = localId, SuggestedRemediation = RemediationFor(save.FailureKind) };
                }
                sending = sending with { DraftId = save.ProviderId ?? sending.DraftId, UpdatedAt = DateTimeOffset.UtcNow };
                await drafts.UpsertAsync(sending, cancellationToken).ConfigureAwait(false);
            }

            var result = await provider.SendAsync(sending, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                var failed = sending with { UpdatedAt = DateTimeOffset.UtcNow, PersistenceState = MailDraftPersistenceState.SendFailed, LastSafeError = result.Message };
                await drafts.UpsertAsync(failed, cancellationToken).ConfigureAwait(false);
                return result with { LocalDraftId = localId, SuggestedRemediation = RemediationFor(result.FailureKind) };
            }

            var sent = sending with { UpdatedAt = DateTimeOffset.UtcNow, PersistenceState = MailDraftPersistenceState.Sent, LastSafeError = null };
            await drafts.UpsertAsync(sent, cancellationToken).ConfigureAwait(false);
            return result with { LocalDraftId = localId, SuggestedRemediation = null };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var result = SafeFailure(ex, localId);
            var failed = sending with
            {
                Provider = account?.Provider ?? sending.Provider,
                UpdatedAt = DateTimeOffset.UtcNow,
                PersistenceState = MailDraftPersistenceState.SendFailed,
                LastSafeError = result.Message
            };
            await drafts.UpsertAsync(failed, CancellationToken.None).ConfigureAwait(false);
            return result;
        }
    }

    public async Task<MailOperationResult> ArchiveAsync(Guid accountId, string messageId, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.ArchiveAsync(accountId, messageId, ct), cancellationToken).ConfigureAwait(false);
    public async Task<MailOperationResult> DeleteAsync(Guid accountId, string messageId, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.DeleteAsync(accountId, messageId, ct), cancellationToken).ConfigureAwait(false);
    public async Task<MailOperationResult> SetReadAsync(Guid accountId, string messageId, bool isRead, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.SetReadAsync(accountId, messageId, isRead, ct), cancellationToken).ConfigureAwait(false);
    public async Task<MailOperationResult> SetFlaggedAsync(Guid accountId, string messageId, bool isFlagged, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.SetFlaggedAsync(accountId, messageId, isFlagged, ct), cancellationToken).ConfigureAwait(false);
    public async Task<MailOperationResult> CreateFolderAsync(Guid accountId, string displayName, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.CreateFolderAsync(accountId, displayName, ct), cancellationToken).ConfigureAwait(false);
    public async Task<MailOperationResult> RenameFolderAsync(Guid accountId, string folderId, string displayName, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.RenameFolderAsync(accountId, folderId, displayName, ct), cancellationToken).ConfigureAwait(false);
    public async Task<MailOperationResult> DeleteFolderAsync(Guid accountId, string folderId, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.DeleteFolderAsync(accountId, folderId, ct), cancellationToken).ConfigureAwait(false);
    public async Task<MailOperationResult> MoveToFolderAsync(Guid accountId, string messageId, string folderId, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.MoveToFolderAsync(accountId, messageId, folderId, ct), cancellationToken).ConfigureAwait(false);
    public async Task<MailOperationResult> SetSpamAsync(Guid accountId, string messageId, bool isSpam, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.SetSpamAsync(accountId, messageId, isSpam, ct), cancellationToken).ConfigureAwait(false);
    public async Task<MailOperationResult> SetImportantAsync(Guid accountId, string messageId, bool isImportant, CancellationToken cancellationToken) =>
        await WithProviderAsync(accountId, (provider, ct) => provider.SetImportantAsync(accountId, messageId, isImportant, ct), cancellationToken).ConfigureAwait(false);

    public async Task<MailBulkOperationResult> ExecuteBulkAsync(Guid accountId, IReadOnlyCollection<string> messageIds, MailBulkActionKind action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messageIds);
        var ids = messageIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        var succeeded = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = action switch
            {
                MailBulkActionKind.Archive => await ArchiveAsync(accountId, id, cancellationToken).ConfigureAwait(false),
                MailBulkActionKind.Delete => await DeleteAsync(accountId, id, cancellationToken).ConfigureAwait(false),
                MailBulkActionKind.MarkRead => await SetReadAsync(accountId, id, true, cancellationToken).ConfigureAwait(false),
                MailBulkActionKind.MarkUnread => await SetReadAsync(accountId, id, false, cancellationToken).ConfigureAwait(false),
                MailBulkActionKind.Flag => await SetFlaggedAsync(accountId, id, true, cancellationToken).ConfigureAwait(false),
                MailBulkActionKind.Unflag => await SetFlaggedAsync(accountId, id, false, cancellationToken).ConfigureAwait(false),
                MailBulkActionKind.Spam => await SetSpamAsync(accountId, id, true, cancellationToken).ConfigureAwait(false),
                MailBulkActionKind.NotSpam => await SetSpamAsync(accountId, id, false, cancellationToken).ConfigureAwait(false),
                MailBulkActionKind.Important => await SetImportantAsync(accountId, id, true, cancellationToken).ConfigureAwait(false),
                MailBulkActionKind.NotImportant => await SetImportantAsync(accountId, id, false, cancellationToken).ConfigureAwait(false),
                _ => new MailOperationResult(false, "Unsupported bulk Mail action.", MailFailureKind.InvalidRequest)
            };
            if (result.Succeeded) succeeded++;
        }
        var failed = ids.Length - succeeded;
        return new(ids.Length, succeeded, failed, failed == 0 ? $"Updated {succeeded} message{(succeeded == 1 ? string.Empty : "s")}." : $"Updated {succeeded} of {ids.Length} messages; {failed} failed.");
    }

    public async Task<MailBulkOperationResult> MoveMessagesToFolderAsync(Guid accountId, IReadOnlyCollection<string> messageIds, string folderId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messageIds);
        var ids = messageIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        var succeeded = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((await MoveToFolderAsync(accountId, id, folderId, cancellationToken).ConfigureAwait(false)).Succeeded) succeeded++;
        }
        var failed = ids.Length - succeeded;
        return new(ids.Length, succeeded, failed, failed == 0 ? $"Moved {succeeded} message{(succeeded == 1 ? string.Empty : "s")}." : $"Moved {succeeded} of {ids.Length} messages; {failed} failed.");
    }

    private async Task<T> WithProviderAsync<T>(Guid accountId, Func<IMailProvider, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        var account = await ResolveAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        return await action(providers.Get(account.Provider), cancellationToken).ConfigureAwait(false);
    }

    private async Task<CalendarAccount> ResolveAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = (await planner.GetCalendarAccountsAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == accountId);
        if (account is null || account.Status is CalendarSyncStatus.NotConfigured or CalendarSyncStatus.Disconnected)
            throw new MailProviderException(MailFailureKind.NotConnected, "This account is not connected. Connect it in Settings first.");
        _ = providers.Get(account.Provider);
        return account;
    }

    private static MailDraft NormalizeDraft(MailDraft draft, CalendarProviderKind provider, MailDraftPersistenceState state) => draft with
    {
        LocalId = draft.LocalId == Guid.Empty ? Guid.NewGuid() : draft.LocalId,
        Provider = provider,
        UpdatedAt = DateTimeOffset.UtcNow,
        PersistenceState = state,
        Attachments = draft.Attachments.Select(item => item.LocalId == Guid.Empty ? item with { LocalId = Guid.NewGuid() } : item).ToArray()
    };

    private static MailOperationResult SafeFailure(Exception exception, Guid? localDraftId = null)
    {
        if (exception is MailProviderException provider)
            return new(false, provider.Message, provider.FailureKind, LocalDraftId: localDraftId, SuggestedRemediation: RemediationFor(provider.FailureKind));
        return new(false, "Mail could not complete this operation. Your draft has been kept.", MailFailureKind.ProviderError, LocalDraftId: localDraftId);
    }

    private static RemediationType? RemediationFor(MailFailureKind kind) => kind is MailFailureKind.NotConnected or MailFailureKind.ReconnectRequired or MailFailureKind.PermissionDenied
        ? RemediationType.OAuthReconnect
        : null;
}
