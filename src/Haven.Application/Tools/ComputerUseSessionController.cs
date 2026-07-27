namespace Haven.Application;

/// <summary>
/// Process-wide, privacy-safe state for an explicitly approved Computer Use pass.
/// The desktop uses it for the safety banner while the tool runtime uses the same
/// instance for real pause and cancellation gates.
/// </summary>
public sealed record ComputerUseSessionState(
    bool IsActive,
    bool IsPaused,
    string Action,
    int? CursorX = null,
    int? CursorY = null);

public interface IComputerUseSessionController
{
    ComputerUseSessionState State { get; }
    CancellationToken StopToken { get; }
    event EventHandler<ComputerUseSessionState>? StateChanged;
    IDisposable BeginSession();
    Task WaitIfPausedAsync(CancellationToken cancellationToken);
    void UpdateAction(string action, int? cursorX = null, int? cursorY = null);
    void TogglePause();
    void Stop();
}

public sealed class ComputerUseSessionController : IComputerUseSessionController, IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource _stop = new();
    private TaskCompletionSource _resume = CompletedSignal();
    private ComputerUseSessionState _state = new(false, false, "Waiting");
    private int _sessionDepth;

    public ComputerUseSessionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public CancellationToken StopToken
    {
        get
        {
            lock (_gate)
            {
                return _stop.Token;
            }
        }
    }

    public event EventHandler<ComputerUseSessionState>? StateChanged;

    public IDisposable BeginSession()
    {
        ComputerUseSessionState next;
        lock (_gate)
        {
            if (_sessionDepth == 0)
            {
                _stop.Dispose();
                _stop = new CancellationTokenSource();
                _resume = CompletedSignal();
                _state = new ComputerUseSessionState(true, false, "Preparing computer use");
            }

            _sessionDepth++;
            next = _state;
        }

        StateChanged?.Invoke(this, next);
        return new SessionScope(this);
    }

    public async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task wait;
            lock (_gate)
            {
                if (!_state.IsPaused)
                {
                    return;
                }

                wait = _resume.Task;
            }

            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void UpdateAction(string action, int? cursorX = null, int? cursorY = null)
    {
        ComputerUseSessionState next;
        lock (_gate)
        {
            if (!_state.IsActive)
            {
                return;
            }

            _state = _state with
            {
                Action = action,
                CursorX = cursorX,
                CursorY = cursorY
            };
            next = _state;
        }

        StateChanged?.Invoke(this, next);
    }

    public void TogglePause()
    {
        ComputerUseSessionState next;
        lock (_gate)
        {
            if (!_state.IsActive)
            {
                return;
            }

            var paused = !_state.IsPaused;
            if (paused)
            {
                _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            else
            {
                _resume.TrySetResult();
            }

            _state = _state with
            {
                IsPaused = paused,
                Action = paused ? "Paused after the current action" : "Resuming computer use"
            };
            next = _state;
        }

        StateChanged?.Invoke(this, next);
    }

    public void Stop()
    {
        ComputerUseSessionState next;
        lock (_gate)
        {
            if (!_state.IsActive)
            {
                return;
            }

            _stop.Cancel();
            _resume.TrySetResult();
            _sessionDepth = 0;
            _state = new ComputerUseSessionState(false, false, "Stopped");
            next = _state;
        }

        StateChanged?.Invoke(this, next);
    }

    private void EndSession()
    {
        ComputerUseSessionState? next = null;
        lock (_gate)
        {
            if (_sessionDepth > 0)
            {
                _sessionDepth--;
            }

            if (_sessionDepth == 0 && _state.IsActive)
            {
                _resume.TrySetResult();
                _state = new ComputerUseSessionState(false, false, "Finished");
                next = _state;
            }
        }

        if (next is not null)
        {
            StateChanged?.Invoke(this, next);
        }
    }

    private static TaskCompletionSource CompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.TrySetResult();
        return signal;
    }

    public void Dispose()
    {
        Stop();
        _stop.Dispose();
    }

    private sealed class SessionScope(ComputerUseSessionController owner) : IDisposable
    {
        private ComputerUseSessionController? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndSession();
    }
}