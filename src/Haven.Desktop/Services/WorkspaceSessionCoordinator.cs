using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.Services;

/// <summary>Debounces one authoritative snapshot across every Haven window.</summary>
public sealed class WorkspaceSessionCoordinator(IWorkspaceSessionRepository repository)
{
    private readonly object _gate = new();
    private readonly Dictionary<MainView, WorkspaceWindowKind> _shells = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Guid, PopUpRegistration> _popUps = [];
    private CancellationTokenSource? _pending;

    private sealed record PopUpRegistration(
        Func<TabSessionSnapshot> CreateTabSnapshot,
        Func<WorkspaceWindowSnapshot> CreateWindowSnapshot);

    public void Register(MainView shell, WorkspaceWindowKind kind, bool queueSave = true)
    {
        lock (_gate) _shells[shell] = kind;
        if (queueSave) QueueSave();
    }

    public void Unregister(MainView shell)
    {
        lock (_gate) _shells.Remove(shell);
        QueueSave();
    }

    public void RegisterPopUp(
        Guid windowId,
        Func<TabSessionSnapshot> createTabSnapshot,
        Func<WorkspaceWindowSnapshot> createWindowSnapshot)
    {
        ArgumentNullException.ThrowIfNull(createTabSnapshot);
        ArgumentNullException.ThrowIfNull(createWindowSnapshot);
        lock (_gate) _popUps[windowId] = new PopUpRegistration(createTabSnapshot, createWindowSnapshot);
        QueueSave();
    }

    public void UnregisterPopUp(Guid windowId)
    {
        lock (_gate) _popUps.Remove(windowId);
        QueueSave();
    }

    public void QueueSave()
    {
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = cancellation = new CancellationTokenSource();
        }
        _ = SaveAfterDelayAsync(cancellation.Token);
    }

    public Task<WorkspaceSessionSnapshot?> LoadAsync(CancellationToken cancellationToken) => repository.LoadAsync(cancellationToken);

    public async Task SaveNowAndCancelPendingAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? pending;
        lock (_gate)
        {
            pending = _pending;
            _pending = null;
        }
        pending?.Cancel();
        pending?.Dispose();
        await SaveNowAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveNowAsync(CancellationToken cancellationToken)
    {
        MainView[] shells;
        Dictionary<MainView, WorkspaceWindowKind> kinds;
        PopUpRegistration[] popUps;
        lock (_gate)
        {
            shells = _shells.Keys.Where(shell => !shell.IsDisposed).ToArray();
            kinds = new Dictionary<MainView, WorkspaceWindowKind>(_shells, ReferenceEqualityComparer.Instance);
            popUps = _popUps.Values.ToArray();
        }
        WorkspaceSessionSnapshot CreateSnapshot()
        {
            var tabs = shells.SelectMany(shell => shell.CreateTabSnapshots())
                .Concat(popUps.Select(popUp => popUp.CreateTabSnapshot()))
                .DistinctBy(tab => tab.Id)
                .ToArray();
            var groups = shells.SelectMany(shell => shell.CreateGroupSnapshots()).DistinctBy(group => group.Id).ToArray();
            var windows = shells.Select(shell => shell.CreateWindowSnapshot(kinds[shell]))
                .Concat(popUps.Select(popUp => popUp.CreateWindowSnapshot()))
                .ToArray();
            return new WorkspaceSessionSnapshot(WorkspaceSessionSnapshot.CurrentSchemaVersion, tabs, groups, windows, DateTimeOffset.UtcNow);
        }

        var snapshot = Dispatcher.UIThread.CheckAccess()
            ? CreateSnapshot()
            : await Dispatcher.UIThread.InvokeAsync(CreateSnapshot);
        await repository.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
            await SaveNowAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch
        {
            // Session persistence must not crash the active shell; startup retains the last complete snapshot.
        }
    }
}
