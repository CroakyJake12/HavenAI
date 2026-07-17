namespace Haven.Browser;

public sealed class BrowserRecoveryLimiter
{
    private readonly int _maximumAttempts;
    private readonly TimeSpan _window;
    private readonly Queue<DateTimeOffset> _attempts = new();
    private readonly object _sync = new();

    public BrowserRecoveryLimiter(int maximumAttempts, TimeSpan window)
    {
        if (maximumAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        _maximumAttempts = maximumAttempts;
        _window = window;
    }

    public bool TryAcquire(DateTimeOffset now)
    {
        lock (_sync)
        {
            while (_attempts.Count > 0 && now - _attempts.Peek() >= _window) _attempts.Dequeue();
            if (_attempts.Count >= _maximumAttempts) return false;
            _attempts.Enqueue(now);
            return true;
        }
    }

    public int ActiveAttempts(DateTimeOffset now)
    {
        lock (_sync)
        {
            while (_attempts.Count > 0 && now - _attempts.Peek() >= _window) _attempts.Dequeue();
            return _attempts.Count;
        }
    }
}
