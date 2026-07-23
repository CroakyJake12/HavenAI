namespace Haven.Desktop.Events;

/// <summary>
/// Represents a reference to a specific event on a named element.
/// Created by element proxy classes (e.g., TopRail.Actions.Hover()).
/// </summary>
public sealed class EventToken
{
    /// <summary>
    /// Gets the fully qualified event name (e.g., "TopRail.Actions.Hover").
    /// </summary>
    public string EventName { get; }

    public EventToken(string eventName)
    {
        EventName = eventName;
    }

    public override string ToString() => EventName;
}
