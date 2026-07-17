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
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;

        var lifetime = new CancellationTokenSource();
        _cancellation = lifetime;
        try
        {
            using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetime.Token);
            await DrainAsync(startupCancellation.Token).ConfigureAwait(false);
            ObjectDisposedException.ThrowIf(_disposed, this);
            _loop = Task.Run(() => RunLoopAsync(lifetime.Token), CancellationToken.None);
        }
        catch
        {
            if (ReferenceEquals(_cancellation, lifetime))
                _cancellation = null;
            lifetime.Cancel();
            lifetime.Dispose();
            _loop = null;
            Interlocked.Exchange(ref _started, 0);
            throw;
        }
    }

    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
        try
        {
            var deliveries = await outbox.DrainAsync(cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < deliveries.Count; index++)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var delivery = deliveries[index];
                    notifications.Show(
                        delivery.Title,
                        delivery.Message,
                        delivery.Kind == AutomationDeliveryKind.ConditionMet
                            ? ToastKind.Success
                            : ToastKind.Error,
                        TimeSpan.FromSeconds(delivery.Kind == AutomationDeliveryKind.ConditionMet ? 18 : 24));
                }
                catch
                {
                    await RequeueAsync(deliveries, index).ConfigureAwait(false);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The current and remaining deliveries are re-enqueued when UI delivery
            // fails. Storage-level failures remain eligible for the next polling pass.
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RequeueAsync(
        IReadOnlyList<AutomationDelivery> deliveries,
        int startIndex)
    {
        Exception? firstFailure = null;
        for (var index = startIndex; index < deliveries.Count; index++)
        {
            try
            {
                await outbox.EnqueueAsync(deliveries[index], CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
            }
        }

        if (firstFailure is not null)
            throw new IOException("One or more automation deliveries could not be returned to the durable outbox.", firstFailure);
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
        catch (ObjectDisposedException) when (_disposed)
        {
            // The service provider can dispose between a timer tick and DrainAsync.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var cancellation = Interlocked.Exchange(ref _cancellation, null);
        cancellation?.Cancel();
        var loop = _loop;
        _loop = null;
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        _gate.Release();
        cancellation?.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
