using Haven.Core;

namespace Haven.Application;

public interface IBrowserNavigationPolicy
{
    Task<BrowserNavigationAssessment> AssessAsync(Uri address, CancellationToken cancellationToken);
}

public interface IBrowserAutomationStore
{
    Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken);
    Task<BrowserPendingAction> AddPendingAsync(BrowserPendingAction action, CancellationToken cancellationToken);
    Task<BrowserPendingAction?> GetActionAsync(Guid actionId, CancellationToken cancellationToken);
    Task<BrowserPendingAction> UpdateActionAsync(BrowserPendingAction action, CancellationToken cancellationToken);
    Task AddAuditAsync(BrowserAuditEntry entry, CancellationToken cancellationToken);
    Task AddDownloadAsync(BrowserDownloadRecord download, CancellationToken cancellationToken);
}

public interface IBrowserAutomationService
{
    Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken cancellationToken);
    Task<string> NavigateAsync(string address, CancellationToken cancellationToken);
    Task<string> ClickReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<string> FillReferenceAsync(string reference, string value, CancellationToken cancellationToken);
    Task<BrowserPendingAction> RequestDownloadAsync(string address, string? suggestedFileName, CancellationToken cancellationToken);
    Task<BrowserActionExecutionResult> ApproveAsync(Guid actionId, CancellationToken cancellationToken);
    Task<BrowserActionExecutionResult> RejectAsync(Guid actionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken);
}

public interface IBrowserAutomationProvider
{
    IBrowserAutomationService Automation { get; }
}
