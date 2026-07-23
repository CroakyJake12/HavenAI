using System.Collections.Concurrent;
using Avalonia.Controls;

namespace Haven.Desktop.Events;

/// <summary>
/// Central event bus that registers named UI elements and exposes pointer events
/// with the naming convention Page.Section.Name.Event().
/// </summary>
public sealed class HavenEventBus : IDisposable
{
    private readonly ConcurrentDictionary<string, WeakReference<Control>> _elements = new();
    private readonly ConcurrentDictionary<string, List<Subscription>> _subscriptions = new();
    private readonly object _lock = new();

    // Application state queries
    public string CurrentDashboardType { get; set; } = "default";
    public bool IsCallActive { get; set; }
    public string CurrentMode { get; set; } = "chat";
    public bool IsSidebarOpen { get; set; } = true;
    public bool IsCommandPaletteOpen { get; set; }

    /// <summary>
    /// Registers a named element in the event tree.
    /// </summary>
    public void RegisterElement(string qualifiedName, Control control)
    {
        _elements[qualifiedName] = new WeakReference<Control>(control);
    }

    /// <summary>
    /// Removes a named element from the event tree.
    /// </summary>
    public void UnregisterElement(string qualifiedName)
    {
        _elements.TryRemove(qualifiedName, out _);
    }

    /// <summary>
    /// Wires all pointer events for a control under the given qualified name.
    /// </summary>
    public void WirePointerEvents(string qualifiedName, Control control)
    {
        control.PointerEntered += (_, _) => Fire($"{qualifiedName}.Hover");
        control.PointerExited += (_, _) => Fire($"{qualifiedName}.Leave");
        control.PointerPressed += (_, _) => Fire($"{qualifiedName}.Press");
        control.PointerReleased += (_, _) => Fire($"{qualifiedName}.Release");
        control.PointerMoved += (_, _) => Fire($"{qualifiedName}.Move");
        control.PointerWheelChanged += (_, _) => Fire($"{qualifiedName}.Wheel");
    }

    /// <summary>
    /// Subscribes to an event. The handler fires every time the event occurs.
    /// </summary>
    public IDisposable Subscribe(string eventName, Action handler, double cooldownSeconds = 0)
    {
        var subscription = new Subscription(handler, cooldownSeconds);
        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(eventName, out var list))
            {
                list = [];
                _subscriptions[eventName] = list;
            }
            list.Add(subscription);
        }

        return new Unsubscriber(() =>
        {
            lock (_lock)
            {
                if (_subscriptions.TryGetValue(eventName, out var current))
                    current.Remove(subscription);
            }
        });
    }

    /// <summary>
    /// Fires an event by qualified name.
    /// </summary>
    public void Fire(string eventName)
    {
        if (!_subscriptions.TryGetValue(eventName, out var list)) return;

        List<Subscription> snapshot;
        lock (_lock)
        {
            snapshot = [.. list];
        }

        foreach (var subscription in snapshot)
        {
            subscription.Execute();
        }
    }

    /// <summary>
    /// Returns true if the given qualified element name is registered and alive.
    /// </summary>
    public bool IsElementRegistered(string qualifiedName)
    {
        return _elements.TryGetValue(qualifiedName, out var weakRef) && weakRef.TryGetTarget(out _);
    }

    /// <summary>
    /// Returns all registered element names matching an optional prefix.
    /// </summary>
    public IReadOnlyList<string> GetRegisteredElements(string? prefix = null)
    {
        var keys = _elements.Keys.ToList();
        if (prefix is not null)
            keys = keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        return keys;
    }

    public void Dispose()
    {
        _elements.Clear();
        lock (_lock)
        {
            _subscriptions.Clear();
        }
    }

    /// <summary>
    /// Represents a single event subscription with optional cooldown.
    /// </summary>
    internal sealed class Subscription
    {
        private readonly Action _handler;
        private readonly double _cooldownSeconds;
        private DateTime _lastFired = DateTime.MinValue;
        private readonly object _timerLock = new();

        public Subscription(Action handler, double cooldownSeconds)
        {
            _handler = handler;
            _cooldownSeconds = cooldownSeconds;
        }

        public void Execute()
        {
            lock (_timerLock)
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - _lastFired).TotalSeconds;
                if (elapsed < _cooldownSeconds) return;
                _lastFired = now;
            }

            _handler();
        }
    }

    /// <summary>
    /// Disposable handle that removes a subscription when disposed.
    /// </summary>
    private sealed class Unsubscriber(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}
