/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Browser/BrowserAutomationCoordinator.cs, in the Browser layer, which isolates browser state, safety policy, transport, and automation.
 * What: This file owns BrowserAutomationService, SubmitActionTarget. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Browser capabilities are isolated behind explicit policy boundaries because navigation and automation process untrusted external content.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

/// <summary>
/// Represents browser automation service and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserAutomationService : IBrowserAutomationService, IDisposable
{
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    /// <summary>
    /// Stores browser locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly BrowserSessionService _browser;
    /// <summary>
    /// Stores policy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IBrowserNavigationPolicy _policy;
    /// <summary>
    /// Stores store locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IBrowserAutomationStore _store;
    /// <summary>
    /// Stores downloads locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly BrowserDownloadTransport _downloads;
    /// <summary>
    /// Stores background pages locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly BrowserBackgroundPageLoader _backgroundPages;
    /// <summary>
    /// Stores action gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _actionGate = new(1, 1);
    /// <summary>
    /// Stores ephemeral targets locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, string> _ephemeralTargets = new();
    /// <summary>
    /// Stores background snapshot locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private BrowserPageSnapshot? _backgroundSnapshot;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _disposed;

    public BrowserAutomationService(
        BrowserSessionService browser,
        IBrowserNavigationPolicy policy,
        IBrowserAutomationStore store,
        IAppPaths paths)
        : this(
            browser,
            policy,
            store,
            new BrowserDownloadTransport(policy, paths),
            new BrowserBackgroundPageLoader(policy))
    {
    }

    public BrowserAutomationService(
        BrowserSessionService browser,
        IBrowserNavigationPolicy policy,
        IBrowserAutomationStore store,
        BrowserDownloadTransport downloads,
        BrowserBackgroundPageLoader backgroundPages)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
        _backgroundPages = backgroundPages ?? throw new ArgumentNullException(nameof(backgroundPages));
    }

    /// <summary>
    /// Performs capture page asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var snapshot = _browser.IsInteractiveAvailable
            ? await _browser.CaptureStructuredPageAsync(cancellationToken).ConfigureAwait(false)
            : _backgroundSnapshot ?? await _browser.CaptureStructuredPageAsync(cancellationToken).ConfigureAwait(false);
        await AuditAsync(null, "capture", Origin(snapshot.Address), $"Captured {snapshot.Elements.Count} bounded page elements.", true, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    /// <summary>
    /// Performs navigate asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<string> NavigateAsync(string address, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var uri = NormalizeAddress(address);
        var assessment = await _policy.AssessAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!assessment.IsAllowed)
        {
            await AuditAsync(null, "navigate", Origin(uri), assessment.Reason, false, cancellationToken).ConfigureAwait(false);
            throw new UnauthorizedAccessException("Navigation blocked: " + assessment.Reason);
        }

        string result;
        Uri finalAddress;
        if (_browser.IsInteractiveAvailable)
        {
            result = await _browser.NavigateAsync(uri.ToString(), cancellationToken).ConfigureAwait(false);
            finalAddress = uri;
            _backgroundSnapshot = null;
        }
        else
        {
            _backgroundSnapshot = await _backgroundPages.LoadAsync(uri, cancellationToken).ConfigureAwait(false);
            finalAddress = _backgroundSnapshot.Address ?? uri;
            result = $"Loaded {RedactedAddress(finalAddress)} in Haven's isolated background browser. Use browser_snapshot to inspect the bounded page text.";
        }
        await AuditAsync(null, "navigate", Origin(finalAddress), $"Navigated to {RedactedAddress(finalAddress)}.", true, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Performs click reference asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<string> ClickReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var snapshot = await RequireInteractiveSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var element = FindElement(snapshot, reference);
        if (element.IsSensitive) throw new UnauthorizedAccessException("Sensitive page controls cannot be clicked by browser automation.");
        if (element.Address is { Length: > 0 } elementTarget && Uri.TryCreate(snapshot.Address, elementTarget, out var targetUri))
        {
            var assessment = await _policy.AssessAsync(targetUri, cancellationToken).ConfigureAwait(false);
            if (!assessment.IsAllowed) throw new UnauthorizedAccessException("The element points to a blocked destination: " + assessment.Reason);
        }
        if (element.SubmitsForm)
        {
            var target = new SubmitActionTarget(
                element.Reference,
                element.Kind,
                element.Text,
                element.Address,
                element.Name,
                element.InputType);
            var action = NewAction(
                BrowserActionKind.SubmitElement,
                Origin(snapshot.Address),
                $"Submit the form control '{Bounded(element.Text, 120)}'",
                JsonSerializer.Serialize(target, JsonOptions),
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

    /// <summary>
    /// Performs fill reference asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<string> FillReferenceAsync(string reference, string value, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var snapshot = await RequireInteractiveSnapshotAsync(cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Performs request download asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<BrowserPendingAction> RequestDownloadAsync(string address, string? suggestedFileName, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var uri = NormalizeAddress(address);
        var assessment = await _policy.AssessAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!assessment.IsAllowed) throw new UnauthorizedAccessException("Download blocked: " + assessment.Reason);
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var containsSensitiveComponents = !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment);
        var persistedTarget = containsSensitiveComponents ? "ephemeral:" + id.ToString("N") : uri.ToString();
        if (containsSensitiveComponents) _ephemeralTargets[id] = uri.ToString();
        var action = new BrowserPendingAction(
            id,
            BrowserActionKind.Download,
            Origin(uri),
            $"Download {RedactedAddress(uri)}",
            persistedTarget,
            string.IsNullOrWhiteSpace(suggestedFileName) ? null : suggestedFileName.Trim(),
            BrowserActionState.Pending,
            now,
            now.AddMinutes(10),
            now,
            null);
        try
        {
            await _store.AddPendingAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _ephemeralTargets.TryRemove(id, out _);
            throw;
        }
        await AuditAsync(action.Kind, "approval-requested", action.Origin, action.Summary, true, cancellationToken).ConfigureAwait(false);
        return action;
    }

    /// <summary>
    /// Performs approve asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<BrowserActionExecutionResult> ApproveAsync(Guid actionId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _actionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await ApproveCoreAsync(actionId, cancellationToken).ConfigureAwait(false);
        }
        finally { _actionGate.Release(); }
    }

    /// <summary>
    /// Performs reject asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<BrowserActionExecutionResult> RejectAsync(Guid actionId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _actionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var action = await _store.GetActionAsync(actionId, cancellationToken).ConfigureAwait(false)
                         ?? throw new KeyNotFoundException("The browser action no longer exists.");
            if (action.State != BrowserActionState.Pending)
                return new BrowserActionExecutionResult(action.Id, action.State, $"The action is already {action.State.ToString().ToLowerInvariant()}.");
            _ephemeralTargets.TryRemove(action.Id, out _);
            var rejected = action with { State = BrowserActionState.Rejected, UpdatedAt = DateTimeOffset.UtcNow, Failure = "Rejected by the user." };
            await _store.UpdateActionAsync(rejected, cancellationToken).ConfigureAwait(false);
            await AuditAsync(action.Kind, "rejected", action.Origin, action.Summary, true, cancellationToken).ConfigureAwait(false);
            return new BrowserActionExecutionResult(action.Id, rejected.State, "Browser action rejected.");
        }
        finally { _actionGate.Release(); }
    }

    /// <summary>
    /// Retrieves pending async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var pending = await _store.GetPendingAsync(cancellationToken).ConfigureAwait(false);
        var activeIds = pending.Select(item => item.Id).ToHashSet();
        foreach (var id in _ephemeralTargets.Keys)
            if (!activeIds.Contains(id)) _ephemeralTargets.TryRemove(id, out _);
        return pending;
    }

    /// <summary>
    /// Retrieves audit async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _store.GetAuditAsync(limit, cancellationToken);
    }

    /// <summary>
    /// Retrieves downloads async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return _store.GetDownloadsAsync(limit, cancellationToken);
    }

    /// <summary>
    /// Performs approve core asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<BrowserActionExecutionResult> ApproveCoreAsync(Guid actionId, CancellationToken cancellationToken)
    {
        var action = await _store.GetActionAsync(actionId, cancellationToken).ConfigureAwait(false)
                     ?? throw new KeyNotFoundException("The browser action no longer exists.");
        if (action.State != BrowserActionState.Pending)
            return new BrowserActionExecutionResult(action.Id, action.State, $"The action is already {action.State.ToString().ToLowerInvariant()}.");
        if (action.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _ephemeralTargets.TryRemove(action.Id, out _);
            var expired = action with { State = BrowserActionState.Expired, UpdatedAt = DateTimeOffset.UtcNow, Failure = "The approval expired." };
            await _store.UpdateActionAsync(expired, cancellationToken).ConfigureAwait(false);
            return new BrowserActionExecutionResult(expired.Id, expired.State, "The approval expired; request the action again.");
        }

        action = await _store.UpdateActionAsync(action with { State = BrowserActionState.Approved, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
        BrowserDownloadRecord? download = null;
        var sideEffectCompleted = false;
        try
        {
            string message;
            switch (action.Kind)
            {
                case BrowserActionKind.SubmitElement:
                    var submitTarget = JsonSerializer.Deserialize<SubmitActionTarget>(action.Target, JsonOptions)
                                       ?? throw new InvalidOperationException("The saved form-submission target is invalid.");
                    var snapshot = await RequireInteractiveSnapshotAsync(cancellationToken).ConfigureAwait(false);
                    if (!Origin(snapshot.Address).Equals(action.Origin, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The active page origin changed after approval was requested.");
                    var current = FindElement(snapshot, submitTarget.Reference);
                    if (!Matches(current, submitTarget) || !current.SubmitsForm)
                        throw new InvalidOperationException("The approved form control changed after the request. Capture a new page snapshot and request approval again.");
                    var click = await _browser.ClickReferenceAsync(current.Reference, cancellationToken).ConfigureAwait(false);
                    EnsureBrowserResult(click, "clicked", "The page changed before the approved form control could be submitted.");
                    sideEffectCompleted = true;
                    message = click;
                    break;
                case BrowserActionKind.Download:
                    var target = ResolveDownloadTarget(action);
                    download = await _downloads.DownloadAsync(action with { Target = target }, cancellationToken).ConfigureAwait(false);
                    sideEffectCompleted = true;
                    _ephemeralTargets.TryRemove(action.Id, out _);
                    try
                    {
                        await _store.AddDownloadAsync(download, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        if (TryDeleteDownloadedFile(download.StoredPath)) sideEffectCompleted = false;
                        throw;
                    }
                    message = $"Downloaded {download.FileName} ({download.SizeBytes:N0} bytes) to Haven's Downloads folder.";
                    break;
                default:
                    throw new InvalidOperationException("Unsupported browser action kind.");
            }

            var executed = action with { State = BrowserActionState.Executed, UpdatedAt = DateTimeOffset.UtcNow, Failure = null };
            await _store.UpdateActionAsync(executed, CancellationToken.None).ConfigureAwait(false);
            await AuditAsync(action.Kind, "executed", action.Origin, message, true, CancellationToken.None).ConfigureAwait(false);
            return new BrowserActionExecutionResult(action.Id, executed.State, message, download);
        }
        catch (OperationCanceledException) when (!sideEffectCompleted)
        {
            _ephemeralTargets.TryRemove(action.Id, out _);
            var cancelled = action with
            {
                State = BrowserActionState.Failed,
                UpdatedAt = DateTimeOffset.UtcNow,
                Failure = "Execution was cancelled before the browser action completed and will not resume automatically."
            };
            await TryUpdateActionAsync(cancelled).ConfigureAwait(false);
            await AuditAsync(action.Kind, "execution-cancelled", action.Origin, cancelled.Failure, false, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            _ephemeralTargets.TryRemove(action.Id, out _);
            var failure = sideEffectCompleted
                ? "The browser side effect completed, but Haven could not finish recording its final state: " + Bounded(exception.Message, 700)
                : Bounded(exception.Message, 1_000);
            var failed = action with
            {
                State = BrowserActionState.Failed,
                UpdatedAt = DateTimeOffset.UtcNow,
                Failure = failure
            };
            await TryUpdateActionAsync(failed).ConfigureAwait(false);
            await AuditAsync(
                action.Kind,
                sideEffectCompleted ? "execution-state-uncertain" : "execution-failed",
                action.Origin,
                failure,
                false,
                CancellationToken.None).ConfigureAwait(false);
            var message = sideEffectCompleted
                ? "The browser action may have completed, but Haven could not persist its final state. Do not repeat it without checking the page or Downloads folder first."
                : "Browser action failed: " + exception.Message;
            return new BrowserActionExecutionResult(action.Id, failed.State, message, download);
        }
    }

    /// <summary>
    /// Performs the resolve download target step owned by this component.
    /// </summary>
    private string ResolveDownloadTarget(BrowserPendingAction action)
    {
        if (!action.Target.StartsWith("ephemeral:", StringComparison.Ordinal)) return action.Target;
        return _ephemeralTargets.TryGetValue(action.Id, out var target)
            ? target
            : throw new InvalidOperationException("This signed download request cannot be resumed after restart. Request it again so the token remains session-only.");
    }

    /// <summary>
    /// Performs require interactive snapshot asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<BrowserPageSnapshot> RequireInteractiveSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!_browser.IsInteractiveAvailable)
            throw new InvalidOperationException("Interactive page actions require Haven Browse to be open. Background browsing supports navigation, reading, and approval-gated downloads only.");
        return await _browser.CaptureStructuredPageAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the find element step owned by this component.
    /// </summary>
    private static BrowserPageElement FindElement(BrowserPageSnapshot snapshot, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("An element reference is required.", nameof(reference));
        return snapshot.Elements.FirstOrDefault(item => item.Reference.Equals(reference.Trim(), StringComparison.Ordinal))
               ?? throw new KeyNotFoundException("The element reference is stale or was not present in the latest page snapshot.");
    }

    /// <summary>
    /// Performs the matches step owned by this component.
    /// </summary>
    private static bool Matches(BrowserPageElement element, SubmitActionTarget target) =>
        element.Reference.Equals(target.Reference, StringComparison.Ordinal)
        && element.Kind.Equals(target.Kind, StringComparison.Ordinal)
        && element.Text.Equals(target.Text, StringComparison.Ordinal)
        && string.Equals(element.Address, target.Address, StringComparison.Ordinal)
        && string.Equals(element.Name, target.Name, StringComparison.Ordinal)
        && string.Equals(element.InputType, target.InputType, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Performs the ensure browser result step owned by this component.
    /// </summary>
    private static void EnsureBrowserResult(string result, string expectedPrefix, string failure)
    {
        if (!result.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(failure + " Browser response: " + Bounded(result, 300));
    }

    /// <summary>
    /// Performs the new action step owned by this component.
    /// </summary>
    private static BrowserPendingAction NewAction(BrowserActionKind kind, string origin, string summary, string target, string? fileName)
    {
        var now = DateTimeOffset.UtcNow;
        return new BrowserPendingAction(Guid.NewGuid(), kind, origin, summary, target, fileName, BrowserActionState.Pending, now, now.AddMinutes(10), now, null);
    }

    /// <summary>
    /// Performs audit asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task AuditAsync(
        BrowserActionKind? kind,
        string operation,
        string origin,
        string? detail,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        try
        {
            await _store.AddAuditAsync(
                new BrowserAuditEntry(
                    Guid.NewGuid(),
                    kind,
                    operation,
                    origin,
                    Bounded(detail ?? string.Empty, 2_000),
                    succeeded,
                    DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("Browser audit persistence failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Attempts to update action async and reports the result without using failure for normal control flow.
    /// </summary>
    private async Task TryUpdateActionAsync(BrowserPendingAction action)
    {
        try { await _store.UpdateActionAsync(action, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine("Browser action state persistence failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Attempts to delete downloaded file and reports the result without using failure for normal control flow.
    /// </summary>
    private static bool TryDeleteDownloadedFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return !File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Performs the normalize address step owned by this component.
    /// </summary>
    private static Uri NormalizeAddress(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var direct)) return direct;
        if (!candidate.Contains(' ') && candidate.Contains('.')) return new Uri("https://" + candidate, UriKind.Absolute);
        return new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(candidate), UriKind.Absolute);
    }

    /// <summary>
    /// Performs the origin step owned by this component.
    /// </summary>
    private static string Origin(Uri? address) => address is null ? string.Empty : address.GetLeftPart(UriPartial.Authority);
    /// <summary>
    /// Performs the redacted address step owned by this component.
    /// </summary>
    private static string RedactedAddress(Uri address) => address.GetLeftPart(UriPartial.Path);
    /// <summary>
    /// Performs the bounded step owned by this component.
    /// </summary>
    private static string Bounded(string value, int maximum) => value.Length <= maximum ? value : value[..maximum] + "…";

    /// <summary>
    /// Performs the throw if disposed step owned by this component.
    /// </summary>
    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _ephemeralTargets.Clear();
        // The service is app-lifetime. Do not dispose the semaphore while an
        // in-flight approval may still release it during coordinated shutdown.
    }

    /// <summary>
    /// Represents submit action target and keeps its related state and behavior together.
    /// </summary>
    private sealed record SubmitActionTarget(
        string Reference,
        string Kind,
        string Text,
        string? Address,
        string? Name,
        string? InputType);
}
