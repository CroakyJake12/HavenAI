using Avalonia.Controls;
using Haven.UI;

namespace Haven.Desktop.HavenUI.Backend;

/// <summary>
/// Opt-in bridge for the small set of Haven elements that require an actual
/// platform control. Normal Haven elements are never offered to this bridge.
/// </summary>
public interface IHavenAvaloniaNativeControlResolver
{
    bool TryCreate(HavenElement element, out Control? control);
}

/// <summary>
/// Safe default that creates no native controls. Platform composition roots
/// must explicitly provide capabilities such as a WebView or video player.
/// </summary>
public sealed class HavenAvaloniaNativeControlResolver : IHavenAvaloniaNativeControlResolver
{
    public bool TryCreate(HavenElement element, out Control? control)
    {
        ArgumentNullException.ThrowIfNull(element);
        control = null;
        return false;
    }
}
