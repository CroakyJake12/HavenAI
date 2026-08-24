using Avalonia.Controls;

namespace Haven.Desktop.HavenUI.Floating;

/// <summary>
/// Canonical visible surface for detached HavenUI content. The host window
/// remains transparent; this control owns the visible HavenUI material.
/// </summary>
public sealed class HavenFloatingSurface : Border
{
    public HavenFloatingSurface()
    {
        Classes.Add("havenFloatingSurface");
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
    }
}
