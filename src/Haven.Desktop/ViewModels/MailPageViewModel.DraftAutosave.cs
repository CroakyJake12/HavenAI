using Haven.Application;

namespace Haven.Desktop.ViewModels;

public sealed partial class MailPageViewModel
{
    private readonly SemaphoreSlim _draftAutosaveGate = new(1, 1);
    private CancellationTokenSource? _draftAutosaveCancellation;
    private AsyncRelayCommand? _closeComposeSafeCommand;
    private AsyncRelayCommand? _discardDraftCommand;

    public AsyncRelayCommand CloseComposeSafeCommand => _closeComposeSafeCommand ??= new(CloseComposeAfterAutosaveAsync);
    public AsyncRelayCommand DiscardDraftCommand => _discardDraftCommand ??= new(DiscardDraftAsync);

    public void NotifyComposeChanged()
    {
        if (!IsComposeOpen || !CanSaveDraft) return;
        CancelPendingDraftAutosave();
        var source = new CancellationTokenSource();
        _draftAutosaveCancellation = source;
        _ = AutosaveDraftAfterDelayAsync(source);
    }

    public void CancelDraftAutosave() => CancelPendingDraftAutosave();

    private async Task AutosaveDraftAfterDelayAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1200), source.Token);
            if (!ReferenceEquals(source, _draftAutosaveCancellation) || source.IsCancellationRequested || !IsComposeOpen) return;
            await SavePartialDraftAsync(source.Token);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
    }

    private async Task CloseComposeAfterAutosaveAsync()
    {
        IsSendConfirmationOpen = false;
        CancelPendingDraftAutosave();
        if (HasMeaningfulComposeContent() && CanSaveDraft && !await SavePartialDraftAsync(CancellationToken.None)) return;
        IsComposeOpen = false;
    }

    private async Task DiscardDraftAsync()
    {
        CancelPendingDraftAutosave();
        if (_composeLocalDraftId is { } localDraftId)
        {
            try
            {
                var result = await _mail.DiscardDraftAsync(localDraftId, CancellationToken.None);
                if (!result.Succeeded)
                {
                    ApplyComposeFailure(result);
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ApplyComposeFailure(ex);
                return;
            }
        }
        else if (SelectedAccount is not null && !string.IsNullOrWhiteSpace(_composeDraftId))
        {
            try
            {
                var result = await _mail.DeleteDraftAsync(SelectedAccount.AccountId, _composeDraftId, CancellationToken.None);
                if (!result.Succeeded)
                {
                    ApplyComposeFailure(result);
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ApplyComposeFailure(ex);
                return;
            }
        }
        IsComposeOpen = false;
        ResetCompose();
        ComposeStatus = "Draft discarded.";
    }

    private async Task<bool> SavePartialDraftAsync(CancellationToken cancellationToken)
    {
        if (!HasMeaningfulComposeContent()) return true;
        var draft = BuildAutosaveDraft();
        if (draft is null) return false;

        await _draftAutosaveGate.WaitAsync(cancellationToken);
        try
        {
            ComposeStatus = "Saving…";
            var result = await _mail.SaveDraftAsync(draft, cancellationToken);
            if (!result.Succeeded)
            {
                ApplyComposeFailure(result);
                return false;
            }
            _composeLocalDraftId = result.LocalDraftId ?? _composeLocalDraftId;
            _composeDraftId = result.ProviderId ?? _composeDraftId;
            ComposeStatus = "Draft saved";
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApplyComposeFailure(ex);
            return false;
        }
        finally
        {
            _draftAutosaveGate.Release();
        }
    }

    private MailDraft? BuildAutosaveDraft()
    {
        if (SelectedAccount is null) return null;
        var invalidAddress = FirstInvalidAddress(ComposeTo, ComposeCc, ComposeBcc);
        if (invalidAddress is not null)
        {
            ComposeStatus = $"Draft will save when this address is complete: {invalidAddress}";
            return null;
        }

        var hasHtml = !string.IsNullOrWhiteSpace(ComposeHtmlBody);
        _composeLocalDraftId ??= Guid.NewGuid();
        return new MailDraft(
            SelectedAccount.AccountId, _composeDraftId, _composeResponseKind, _composeSourceMessageId, _composeThreadId,
            ParseAddresses(ComposeTo), ParseAddresses(ComposeCc), ParseAddresses(ComposeBcc), ComposeSubject.Trim(), hasHtml ? ComposeHtmlBody : ComposeBody, hasHtml,
            ComposeAttachments.Select(item => new MailDraftAttachment(item.FileName, item.ContentType, item.Content)).ToArray(),
            LocalId: _composeLocalDraftId.Value, Provider: SelectedAccount.Provider);
    }

    private bool HasMeaningfulComposeContent() =>
        !string.IsNullOrWhiteSpace(ComposeTo)
        || !string.IsNullOrWhiteSpace(ComposeCc)
        || !string.IsNullOrWhiteSpace(ComposeBcc)
        || !string.IsNullOrWhiteSpace(ComposeSubject)
        || !string.IsNullOrWhiteSpace(ComposeBody)
        || ComposeAttachments.Count > 0;

    private void CancelPendingDraftAutosave()
    {
        var pending = _draftAutosaveCancellation;
        _draftAutosaveCancellation = null;
        if (pending is null) return;
        pending.Cancel();
        pending.Dispose();
    }
}
