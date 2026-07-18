/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Browser/SafeModeBrowserAutomationService.cs, in the Browser layer, which isolates browser state, safety policy, transport, and automation.
 * What: This file owns SafeModeBrowserAutomationService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Browser capabilities are isolated behind explicit policy boundaries because navigation and automation process untrusted external content.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

/// <summary>
/// Represents safe mode browser automation service and keeps its related state and behavior together.
/// </summary>
public sealed class SafeModeBrowserAutomationService(
    BrowserAutomationService inner,
    IProductionDiagnostics diagnostics) : IBrowserAutomationService
{
    /// <summary>
    /// Performs capture page asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken cancellationToken) =>
        ExecuteAsync("capture", token => inner.CapturePageAsync(token), cancellationToken);

    /// <summary>
    /// Performs navigate asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> NavigateAsync(string address, CancellationToken cancellationToken) =>
        ExecuteAsync("navigate", token => inner.NavigateAsync(address, token), cancellationToken);

    /// <summary>
    /// Performs click reference asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> ClickReferenceAsync(string reference, CancellationToken cancellationToken) =>
        ExecuteAsync("click", token => inner.ClickReferenceAsync(reference, token), cancellationToken);

    /// <summary>
    /// Performs fill reference asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> FillReferenceAsync(string reference, string value, CancellationToken cancellationToken) =>
        ExecuteAsync("fill", token => inner.FillReferenceAsync(reference, value, token), cancellationToken);

    /// <summary>
    /// Performs request download asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<BrowserPendingAction> RequestDownloadAsync(
        string address,
        string? suggestedFileName,
        CancellationToken cancellationToken) =>
        ExecuteAsync("download-request", token => inner.RequestDownloadAsync(address, suggestedFileName, token), cancellationToken);

    /// <summary>
    /// Performs approve asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<BrowserActionExecutionResult> ApproveAsync(Guid actionId, CancellationToken cancellationToken) =>
        ExecuteAsync("approve", token => inner.ApproveAsync(actionId, token), cancellationToken);

    // Rejecting an old pending action is deliberately allowed in safe mode. It cannot
    // create a browser/network side effect and lets the user clean up interrupted work.
    /// <summary>
    /// Performs reject asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<BrowserActionExecutionResult> RejectAsync(Guid actionId, CancellationToken cancellationToken) =>
        inner.RejectAsync(actionId, cancellationToken);

    /// <summary>
    /// Retrieves pending async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken) =>
        inner.GetPendingAsync(cancellationToken);

    /// <summary>
    /// Retrieves audit async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) =>
        inner.GetAuditAsync(limit, cancellationToken);

    /// <summary>
    /// Retrieves downloads async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken) =>
        inner.GetDownloadsAsync(limit, cancellationToken);

    private async Task<T> ExecuteAsync<T>(
        string operation,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!RuntimeSafetyState.IsSafeMode) return await action(cancellationToken).ConfigureAwait(false);
        await diagnostics.WriteAsync(
            ReliabilitySeverity.Warning,
            "safe-mode",
            "browser-automation-blocked",
            $"Browser automation operation '{operation}' was blocked by crash-loop recovery safe mode.",
            new Dictionary<string, string> { ["operation"] = operation },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException($"Browser automation is disabled in crash-loop recovery safe mode. {RuntimeSafetyState.Reason}");
    }
}
