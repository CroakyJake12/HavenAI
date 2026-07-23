using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Haven.Desktop;

/// <summary>
/// Helper for batching UI updates to reduce layout thrashing.
/// Collects changes and applies them in a single dispatcher pass.
/// </summary>
public static class UiBatcher
{
    [ThreadStatic]
    private static int _batchDepth;

    [ThreadStatic]
    private static Action? _pendingAction;

    /// <summary>
    /// Begins a batch of UI updates. All changes within the batch are applied together.
    /// Use with using statement for automatic disposal.
    /// </summary>
    public static IDisposable BeginBatch()
    {
        _batchDepth++;
        return new BatchDisposable();
    }

    /// <summary>
    /// Schedules an action to run at the end of the current batch.
    /// If no batch is active, runs immediately on the dispatcher.
    /// </summary>
    public static void Schedule(Action action)
    {
        if (_batchDepth > 0)
        {
            _pendingAction += action;
        }
        else
        {
            Dispatcher.UIThread.Post(action, DispatcherPriority.Normal);
        }
    }

    /// <summary>
    /// Clears and rebuilds a panel's children in a single layout pass.
    /// </summary>
    public static void RebuildChildren(Panel panel, Action<Panel> rebuild)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            panel.Children.Clear();
            rebuild(panel);
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                panel.Children.Clear();
                rebuild(panel);
            });
        }
    }

    /// <summary>
    /// Defers heavy UI work to the next dispatcher priority level.
    /// Allows the current layout pass to complete first.
    /// </summary>
    public static void Defer(Action action)
    {
        Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
    }

    private sealed class BatchDisposable : IDisposable
    {
        public void Dispose()
        {
            _batchDepth--;
            if (_batchDepth <= 0)
            {
                _batchDepth = 0;
                var action = Interlocked.Exchange(ref _pendingAction, null);
                action?.Invoke();
            }
        }
    }
}
