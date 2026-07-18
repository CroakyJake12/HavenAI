/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/BrowserAutomationAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns IBrowserNavigationPolicy, IBrowserAutomationStore, IBrowserAutomationService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Defines the i browser navigation policy contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IBrowserNavigationPolicy
{
    Task<BrowserNavigationAssessment> AssessAsync(Uri address, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the i browser automation store contract so callers depend on a capability rather than one implementation.
/// </summary>
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

/// <summary>
/// Defines the i browser automation service contract so callers depend on a capability rather than one implementation.
/// </summary>
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
