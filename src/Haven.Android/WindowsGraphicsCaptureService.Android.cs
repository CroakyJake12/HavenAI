using Haven.Application;

namespace Haven.Desktop.Services;

/// <summary>
/// Android compatibility adapter for the Windows screen-share registration.
/// Android capture requires MediaProjection consent and is intentionally reported
/// as unavailable until that platform flow is implemented.
/// </summary>
public sealed class WindowsGraphicsCaptureService : IScreenShareService, IAsyncDisposable
{
    public bool IsSupported => false;
    public bool IsSharing => false;
    public string? UnavailableReason =>
        "Screen sharing is not available in this Android preview.";
    public ScreenShareSource? CurrentSource => null;

    public event EventHandler? SourceClosed
    {
        add { }
        remove { }
    }

    public event EventHandler<ScreenShareSnapshotEventArgs>? SnapshotAvailable
    {
        add { }
        remove { }
    }

    public Task<ScreenShareSource> StartWithSystemPickerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException(UnavailableReason);
    }

    public Task<ScreenShareSnapshot?> GetLatestSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ScreenShareSnapshot?>(null);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
