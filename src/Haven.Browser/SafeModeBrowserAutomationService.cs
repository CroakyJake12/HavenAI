using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

public sealed class SafeModeBrowserAutomationService(
    BrowserAutomationService inner,
    IProductionDiagnostics diagnostics) : IBrowserAutomationService
{
    public Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken cancellationToken) =>
        ExecuteAsync("capture", token => inner.CapturePageAsync(token), cancellationToken);

    public Task<string> NavigateAsync(string address, CancellationToken cancellationToken) =>
        ExecuteAsync("navigate", token => inner.NavigateAsync(address, token), cancellationToken);

    public Task<string> ClickReferenceAsync(string reference, CancellationToken cancellationToken) =>
        ExecuteAsync("click", token => inner.ClickReferenceAsync(reference, token), cancellationToken);

    public Task<string> FillReferenceAsync(string reference, string value, CancellationToken cancellationToken) =>
        ExecuteAsync("fill", token => inner.FillReferenceAsync(reference, value, token), cancellationToken);

    public Task<BrowserPendingAction> RequestDownloadAsync(
        string address,
        string? suggestedFileName,
        CancellationToken cancellationToken) =>
        ExecuteAsync("download-request", token => inner.RequestDownloadAsync(address, suggestedFileName, token), cancellationToken);

    public Task<BrowserActionExecutionResult> ApproveAsync(Guid actionId, CancellationToken cancellationToken) =>
        ExecuteAsync("approve", token => inner.ApproveAsync(actionId, token), cancellationToken);

    // Rejecting an old pending action is deliberately allowed in safe mode. It cannot
    // create a browser/network side effect and lets the user clean up interrupted work.
    public Task<BrowserActionExecutionResult> RejectAsync(Guid actionId, CancellationToken cancellationToken) =>
        inner.RejectAsync(actionId, cancellationToken);

    public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken) =>
        inner.GetPendingAsync(cancellationToken);

    public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) =>
        inner.GetAuditAsync(limit, cancellationToken);

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
