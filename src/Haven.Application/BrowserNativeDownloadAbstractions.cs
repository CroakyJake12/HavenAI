using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Describes a page-initiated native browser download before it enters Haven's approval queue.
/// </summary>
public sealed record BrowserNativeDownloadRequest(
    Guid ActionId,
    Uri ApprovalAddress,
    string? SuggestedFileName,
    bool IsPrivate);

/// <summary>
/// Represents one live native browser transfer. Implementations remain platform-owned and are never persisted.
/// </summary>
public interface IBrowserNativeDownloadExecution
{
    Task<BrowserDownloadRecord> ExecuteAsync(CancellationToken cancellationToken);
    Task CancelAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Registers page-initiated native downloads with Haven's existing browser approval flow.
/// </summary>
public interface IBrowserNativeDownloadService
{
    Task<BrowserPendingAction> RequestNativeDownloadAsync(
        BrowserNativeDownloadRequest request,
        IBrowserNativeDownloadExecution execution,
        CancellationToken cancellationToken);
}
