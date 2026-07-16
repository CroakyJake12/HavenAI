using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>
/// Delivers worker notifications when the desktop is open and drains notifications that
/// were queued while it was closed. The durable outbox remains the source of truth.
/// </summary>
public sealed class AutomationDeliveryController(
    IAutomationDeliveryOutbox outbox,
    NotificationService notifications) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private int _started;
    private bool _disposed;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _cancellation = new CancellationTokenSource();
        await DrainAsync(cancellationToken).ConfigureAwait(false);
        _loop = Task.Run(() => RunLoopAsync(_cancellation.Token), CancellationToken.None);
    }

    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
        try
        {
            var deliveries = await outbox.DrainAsync(cancellationToken).ConfigureAwait(false);
            foreach (var delivery in deliveries)
            {
                notifications.Show(
                    delivery.Title,
                    delivery.Message,
                    delivery.Kind == AutomationDeliveryKind.ConditionMet
                        ? ToastKind.Success
                        : ToastKind.Error,
                    TimeSpan.FromSeconds(delivery.Kind == AutomationDeliveryKind.ConditionMet ? 18 : 24));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Keep the durable outbox for a later drain. Notification delivery should
            // never prevent the desktop from starting or remaining usable.
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await DrainAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var cancellation = _cancellation;
        _cancellation = null;
        cancellation?.Cancel();
        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        cancellation?.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
