using Avalonia.Threading;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private DispatcherTimer? _automationTimer;
    private CancellationTokenSource? _automationCancellation;
    private int _isRunningDueAutomations;

    private void StartAutomationScheduler()
    {
        if (_automationTimer is not null) return;
        _automationCancellation = new CancellationTokenSource();
        _automationTimer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background,
            async (_, _) => await RunDueAutomationsTickAsync());
        _automationTimer.Start();
    }

    private void StopAutomationScheduler()
    {
        _automationTimer?.Stop();
        _automationTimer = null;
        try
        {
            _automationCancellation?.Cancel();
            _automationCancellation?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _automationCancellation = null;
        }
    }

    private async Task RunDueAutomationsTickAsync()
    {
        if (Interlocked.Exchange(ref _isRunningDueAutomations, 1) != 0) return;
        var cancellationSource = _automationCancellation;
        var cancellationToken = cancellationSource is { IsCancellationRequested: false } ? cancellationSource.Token : CancellationToken.None;
        try
        {
            var result = await _automationRunner.RunDueAsync(DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(true);
            if (result.Started > 0)
                _bus.Fire("Shell.Automations.DueRan");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"[Scheduled automations] Due run failed: {exception.Message}");
            _bus.Fire("Shell.Automations.DueRunFailed");
        }
        finally
        {
            Interlocked.Exchange(ref _isRunningDueAutomations, 0);
        }
    }
}
