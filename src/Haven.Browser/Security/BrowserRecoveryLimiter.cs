/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Browser/BrowserRecoveryLimiter.cs, in the Browser layer, which isolates browser state, safety policy, transport, and automation.
 * What: This file owns BrowserRecoveryLimiter. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Browser capabilities are isolated behind explicit policy boundaries because navigation and automation process untrusted external content.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Browser;

/// <summary>
/// Represents browser recovery limiter and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserRecoveryLimiter
{
    /// <summary>
    /// Stores maximum attempts locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly int _maximumAttempts;
    /// <summary>
    /// Stores window locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TimeSpan _window;
    /// <summary>
    /// Stores attempts locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Queue<DateTimeOffset> _attempts = new();
    /// <summary>
    /// Stores sync locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly object _sync = new();

    public BrowserRecoveryLimiter(int maximumAttempts, TimeSpan window)
    {
        if (maximumAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        _maximumAttempts = maximumAttempts;
        _window = window;
    }

    /// <summary>
    /// Attempts to acquire and reports the result without using failure for normal control flow.
    /// </summary>
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

    /// <summary>
    /// Performs the active attempts step owned by this component.
    /// </summary>
    public int ActiveAttempts(DateTimeOffset now)
    {
        lock (_sync)
        {
            while (_attempts.Count > 0 && now - _attempts.Peek() >= _window) _attempts.Dequeue();
            return _attempts.Count;
        }
    }
}
