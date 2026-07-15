using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

public sealed class BrowserAutomationService : IBrowserAutomationService
{
    private readonly BrowserSessionService _browser;
    private readonly IBrowserNavigationPolicy _policy;
    private readonly IBrowserAutomationStore _store;
    private readonly BrowserDownloadTransport _downloads;

    public BrowserAutomationService(
        BrowserSessionService browser,
        IBrowserNavigationPolicy policy,
        IBrowserAutomationStore store,
        IAppPaths paths)
        : this(browser, policy, store, new BrowserDownloadTransport(policy, paths))
    {
    }

    public BrowserAutomationService(
        BrowserSessionService browser,
        IBrowserNavigationPolicy policy,
        IBrowserAutomationStore store,
        BrowserDownloadTransport downloads)
    {
        _browser = browser;
        _policy = policy;
        _store = store;
        _downloads = downloads;
    }

    public async Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _browser.CaptureStructuredPageAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync(null, "capture", Origin(snapshot.Address), $"Captured {snapshot.Elements.Count} bounded page elements.", true, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public async Task<string> NavigateAsync(string address, CancellationToken cancellationToken)
    {
        var uri = NormalizeAddress(address);
        var assessment = await _policy.AssessAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!assessment.IsAllowed)
        {
            await AuditAsync(null, "navigate", Origin(uri), assessment.Reason, false, cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedAccessException("Navigation blocked: " + assessment.Reason);
        }
        var result = await _browser.NavigateAsync(uri.ToString(), cancellationToken).ConfigureAwait(false);
        await AuditAsync(null, "navigate", Origin(uri), $"Navigated to {uri}.", true, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<string> ClickReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var snapshot = await _browser.CaptureStructuredPageAsync(cancellationToken).ConfigureAwait(false);
        var element = FindElement(snapshot, reference);
        if (element.IsSensitive) throw new UnauthorizedAccessException("Sensitive page controls cannot be clicked by browser automation.");
        if (element.Address is { Length: > 0 } target && Uri.TryCreate(snapshot.Address, target, out var targetUri))
        {
            var assessment = await _policy.AssessAsync(targetUri, cancellationToken).ConfigureAwait(false);
            if (!assessment.IsAllowed) throw new UnauthorizedAccessException("The element points to a blocked destination: " + assessment.Reason);
        }
        if (element.SubmitsForm)
        {
            var action = NewAction(
                BrowserActionKind.SubmitElement,
                Origin(snapshot.Address),
                $"Submit the form control '{Bounded(element.Text, 120)}'",
                element.Reference,
                null);
            await _store.AddPendingAsync(action, cancellationToken).ConfigureAwait(false);
            await AuditAsync(action.Kind, "approval-requested", action.Origin, action.Summary, true, cancellationToken).ConfigureAwait(false);
            return $"Approval required before form submission. Pending browser action: {action.Id}. Open Browser safety to approve or reject it.";
        }

        var result = await _browser.ClickReferenceAsync(element.Reference, cancellationToken).ConfigureAwait(false);
        EnsureBrowserResult(result, "clicked", "The page changed before the referenced element could be clicked.");
        await AuditAsync(null, "click", Origin(snapshot.Address), $"Clicked {element.Kind} reference {element.Reference}.", true, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<string> FillReferenceAsync(string reference, string value, CancellationToken cancellationToken)
    {
        var snapshot = await _browser.CaptureStructuredPageAsync(cancellationToken).ConfigureAwait(false);
        var element = FindElement(snapshot, reference);
        if (element.Kind != "input" && element.Kind != "textarea" && element.Kind != "select")
            throw new InvalidOperationException("The reference is not an editable field.");
        if (element.IsSensitive || element.InputType is "password" or "file" or "hidden")
            throw new UnauthorizedAccessException("Password, file, hidden, payment, and one-time-code fields cannot be filled by model-facing browser tools.");

        var result = await _browser.FillReferenceAsync(element.Reference, value, cancellationToken).ConfigureAwait(false);
        EnsureBrowserResult(result, "filled", "The page changed or rejected the referenced field before it could be filled.");
        await AuditAsync(null, "fill", Origin(snapshot.Address), $"Filled non-sensitive field {element.Reference}; the value was not logged.", true, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<BrowserPendingAction> RequestDownloadAsync(string address, string? suggestedFileName, CancellationToken cancellationToken)
    {
        var uri = NormalizeAddress(address);
        var assessment = await _policy.AssessAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!assessment.IsAllowed) throw new UnauthorizedAccessException("Download blocked: " + assessment.Reason);
        var action = NewAction(
            BrowserActionKind.Download,
            Origin(uri),
            $"Download {uri}",
            uri.ToString(),
            string.IsNullOrWhiteSpace(suggestedFileName) ? null : suggestedFileName.Trim());
        await _store.AddPendingAsync(action, cancellationToken).ConfigureAwait(false);
        await AuditAsync(action.Kind, "approval-requested", action.Origin, action.Summary, true, cancellationToken).ConfigureAwait(false);
        return action;
    }

    public async Task<BrowserActionExecutionResult> ApproveAsync(Guid actionId, CancellationToken cancellationToken)
    {
        var action = await _store.GetActionAsync(actionId, cancellationToken).ConfigureAwait(false)
                     ?? throw new KeyNotFoundException("The browser action no longer exists.");
        if (action.State != BrowserActionState.Pending)
            return new BrowserActionExecutionResult(action.Id, action.State, $"The action is already {action.State.ToString().ToLowerInvariant()}.");
        if (action.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            var expired = action with { State = BrowserActionState.Expired, UpdatedAt = DateTimeOffset.UtcNow, Failure = "The approval expired." };
            await _store.UpdateActionAsync(expired, cancellationToken).ConfigureAwait(false);
            return new BrowserActionExecutionResult(expired.Id, expired.State, "The approval expired; request the action again.");
        }

        action = await _store.UpdateActionAsync(action with { State = BrowserActionState.Approved, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
        try
        {
            BrowserDownloadRecord? download = null;
            string message;
            switch (action.Kind)
            {
                case BrowserActionKind.SubmitElement:
                    if (!Origin(_browser.State.Address).Equals(action.Origin, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The active page origin changed after approval was requested.");
                    var click = await _browser.ClickReferenceAsync(action.Target, cancellationToken).ConfigureAwait(false);
                    EnsureBrowserResult(click, "clicked", "The page changed before the approved form control could be submitted.");
                    message = click;
                    break;
                case BrowserActionKind.Download:
                    download = await _downloads.DownloadAsync(action, cancellationToken).ConfigureAwait(false);
                    await _store.AddDownloadAsync(download, cancellationToken).ConfigureAwait(false);
                    message = $"Downloaded {download.FileName} ({download.SizeBytes:N0} bytes) to Haven's Downloads folder.";
                    break;
                default:
                    throw new InvalidOperationException("Unsupported browser action kind.");
            }

            var executed = action with { State = BrowserActionState.Executed, UpdatedAt = DateTimeOffset.UtcNow, Failure = null };
            await _store.UpdateActionAsync(executed, cancellationToken).ConfigureAwait(false);
            await AuditAsync(action.Kind, "executed", action.Origin, message, true, cancellationToken).ConfigureAwait(false);
            return new BrowserActionExecutionResult(action.Id, executed.State, message, download);
        }
        catch (OperationCanceledException)
        {
            var cancelled = action with { State = BrowserActionState.Failed, UpdatedAt = DateTimeOffset.UtcNow, Failure = "Execution was cancelled and will not resume automatically." };
            await _store.UpdateActionAsync(cancelled, CancellationToken.None).ConfigureAwait(false);
            await AuditAsync(action.Kind, "execution-cancelled", action.Origin, cancelled.Failure, false, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            var failed = action with { State = BrowserActionState.Failed, UpdatedAt = DateTimeOffset.UtcNow, Failure = Bounded(exception.Message, 1_000) };
            await _store.UpdateActionAsync(failed, cancellationToken).ConfigureAwait(false);
            await AuditAsync(action.Kind, "execution-failed", action.Origin, failed.Failure, false, cancellationToken).ConfigureAwait(false);
            return new BrowserActionExecutionResult(action.Id, failed.State, "Browser action failed: " + exception.Message);
        }
    }

    public async Task<BrowserActionExecutionResult> RejectAsync(Guid actionId, CancellationToken cancellationToken)
    {
        var action = await _store.GetActionAsync(actionId, cancellationToken).ConfigureAwait(false)
                     ?? throw new KeyNotFoundException("The browser action no longer exists.");
        if (action.State != BrowserActionState.Pending)
            return new BrowserActionExecutionResult(action.Id, action.State, $"The action is already {action.State.ToString().ToLowerInvariant()}.");
        var rejected = action with { State = BrowserActionState.Rejected, UpdatedAt = DateTimeOffset.UtcNow, Failure = "Rejected by the user." };
        await _store.UpdateActionAsync(rejected, cancellationToken).ConfigureAwait(false);
        await AuditAsync(action.Kind, "rejected", action.Origin, action.Summary, true, cancellationToken).ConfigureAwait(false);
        return new BrowserActionExecutionResult(action.Id, rejected.State, "Browser action rejected.");
    }

    public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken) => _store.GetPendingAsync(cancellationToken);
    public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) => _store.GetAuditAsync(limit, cancellationToken);
    public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken) => _store.GetDownloadsAsync(limit, cancellationToken);

    private static BrowserPageElement FindElement(BrowserPageSnapshot snapshot, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("An element reference is required.", nameof(reference));
        return snapshot.Elements.FirstOrDefault(item => item.Reference.Equals(reference.Trim(), StringComparison.Ordinal))
               ?? throw new KeyNotFoundException("The element reference is stale or was not present in the latest page snapshot.");
    }

    private static void EnsureBrowserResult(string result, string expectedPrefix, string failure)
    {
        if (!result.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(failure + " Browser response: " + Bounded(result, 300));
    }

    private static BrowserPendingAction NewAction(BrowserActionKind kind, string origin, string summary, string target, string? fileName)
    {
        var now = DateTimeOffset.UtcNow;
        return new BrowserPendingAction(Guid.NewGuid(), kind, origin, summary, target, fileName, BrowserActionState.Pending, now, now.AddMinutes(10), now, null);
    }

    private Task AuditAsync(BrowserActionKind? kind, string operation, string origin, string? detail, bool succeeded, CancellationToken cancellationToken) =>
        _store.AddAuditAsync(new BrowserAuditEntry(Guid.NewGuid(), kind, operation, origin, Bounded(detail ?? string.Empty, 2_000), succeeded, DateTimeOffset.UtcNow), cancellationToken);

    private static Uri NormalizeAddress(string value)
    {
        var candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var direct)) return direct;
        if (!candidate.Contains(' ') && candidate.Contains('.')) return new Uri("https://" + candidate, UriKind.Absolute);
        return new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(candidate), UriKind.Absolute);
    }

    private static string Origin(Uri? address) => address is null ? string.Empty : address.GetLeftPart(UriPartial.Authority);
    private static string Bounded(string value, int maximum) => value.Length <= maximum ? value : value[..maximum] + "…";
}
