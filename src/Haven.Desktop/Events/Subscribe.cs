namespace Haven.Desktop.Events;

/// <summary>
/// Provides the Subscribe function for listening to pointer events with optional cooldown.
/// Usage:
///   Subscribe(TopRail.Actions.Hover(), () => {
///       Console.WriteLine("Hovered!");
///       Subscribe.Cooldown(3);
///   });
/// </summary>
public static class Subscribe
{
    [ThreadStatic]
    private static double _pendingCooldown;

    [ThreadStatic]
    private static bool _isSubscribing;

    /// <summary>
    /// Gets or sets the global event bus used by all Subscribe calls.
    /// Must be set once at application startup.
    /// </summary>
    public static HavenEventBus? EventBus { get; set; }

    /// <summary>
    /// Subscribes to the given event token. The handler fires every time the event occurs.
    /// Call Subscribe.Cooldown(seconds) inside the handler to set a cooldown period.
    /// </summary>
    public static IDisposable To(EventToken token, Action handler)
    {
        if (EventBus is null)
            throw new InvalidOperationException(
                "Subscribe.EventBus has not been set. Assign it during application startup.");

        _pendingCooldown = 0;
        _isSubscribing = true;

        try
        {
            handler();
        }
        finally
        {
            _isSubscribing = false;
        }

        var cooldown = _pendingCooldown;
        _pendingCooldown = 0;

        return EventBus.Subscribe(token.EventName, handler, cooldown);
    }

    /// <summary>
    /// Sets the cooldown period in seconds for the current subscription.
    /// Call this inside a Subscribe handler to prevent re-firing for the given duration.
    /// </summary>
    public static void Cooldown(double seconds)
    {
        if (!_isSubscribing)
            throw new InvalidOperationException(
                "Subscribe.Cooldown() can only be called inside a Subscribe handler.");

        _pendingCooldown = seconds;
    }
}
