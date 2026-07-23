using Avalonia.Controls;

namespace Haven.Desktop.Events;

/// <summary>
/// Extension methods and helpers for wiring events from AXAML code-behind files.
/// </summary>
public static class EventRegistration
{
    /// <summary>
    /// Registers an element and wires all pointer events in one call.
    /// </summary>
    public static void RegisterWithEvents(this Control control, string qualifiedName, HavenEventBus bus)
    {
        bus.RegisterElement(qualifiedName, control);
        bus.WirePointerEvents(qualifiedName, control);
    }

    /// <summary>
    /// Generates a qualified name for an indexed element (e.g., "Home.Dashboard.Tile0").
    /// </summary>
    public static string ElementName(string section, string element, int index)
        => $"{section}.{element}{index}";

    /// <summary>
    /// Generates a qualified name for a named element (e.g., "TopRail.Actions").
    /// </summary>
    public static string ElementName(string section, string element)
        => $"{section}.{element}";
}
