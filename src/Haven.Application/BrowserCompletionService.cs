/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/BrowserCompletionService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns BrowserCompletionService, BrowserCompletionResult. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents browser completion service and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserCompletionService
{
    /// <summary>
    /// Stores browser locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IBrowserToolService _browser;
    /// <summary>
    /// Stores tab host locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IBrowserTabHostManager _tabHost;
    /// <summary>
    /// Stores activity log locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IActivityLogRepository _activityLog;

    public BrowserCompletionService(IBrowserToolService browser, IBrowserTabHostManager tabHost, IActivityLogRepository activityLog)
    {
        _browser = browser;
        _tabHost = tabHost;
        _activityLog = activityLog;
    }

    /// <summary>
    /// Performs check completion asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<BrowserCompletionResult> CheckCompletionAsync(CancellationToken cancellationToken)
    {
        var tabCount = await _tabHost.GetActiveTabCountAsync(cancellationToken).ConfigureAwait(false);
        if (tabCount == 0)
            return new BrowserCompletionResult(false, "No active browser tabs.", null);

        try
        {
            var text = await _browser.ReadVisibleTextAsync(cancellationToken).ConfigureAwait(false);
            var hasContent = !string.IsNullOrWhiteSpace(text);
            return new BrowserCompletionResult(
                hasContent,
                hasContent ? $"Page has {text.Length} characters of visible text." : "Page appears empty.",
                hasContent ? text[..Math.Min(500, text.Length)] : null);
        }
        catch (Exception ex)
        {
            return new BrowserCompletionResult(false, $"Could not read page: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Performs wait for navigation asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<BrowserCompletionResult> WaitForNavigationAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - start < timeout)
        {
            var result = await CheckCompletionAsync(cancellationToken).ConfigureAwait(false);
            if (result.HasContent) return result;
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
        return new BrowserCompletionResult(false, "Navigation timed out.", null);
    }
}

/// <summary>
/// Represents browser completion result and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserCompletionResult(bool HasContent, string Message, string? PreviewText);
