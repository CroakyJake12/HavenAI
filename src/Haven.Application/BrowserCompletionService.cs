using Haven.Core;

namespace Haven.Application;

public sealed class BrowserCompletionService
{
    private readonly IBrowserToolService _browser;
    private readonly IBrowserTabHostManager _tabHost;
    private readonly IActivityLogRepository _activityLog;

    public BrowserCompletionService(IBrowserToolService browser, IBrowserTabHostManager tabHost, IActivityLogRepository activityLog)
    {
        _browser = browser;
        _tabHost = tabHost;
        _activityLog = activityLog;
    }

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

public sealed record BrowserCompletionResult(bool HasContent, string Message, string? PreviewText);
