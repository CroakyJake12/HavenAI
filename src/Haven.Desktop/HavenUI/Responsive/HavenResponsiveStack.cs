using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Haven.Desktop.HavenUI.Responsive;

/// <summary>
/// A shared responsive primitive that preserves child identity while changing
/// composition. At compact widths it stacks vertically; at wider widths it
/// flows horizontally. Screens configure the breakpoint rather than duplicating
/// resize handlers.
/// </summary>
public sealed class HavenResponsiveStack : StackPanel
{
    public static readonly StyledProperty<double> CompactBreakpointProperty =
        AvaloniaProperty.Register<HavenResponsiveStack, double>(nameof(CompactBreakpoint), 720d);

    public HavenResponsiveStack()
    {
        Classes.Add("havenResponsiveStack");
        SizeChanged += (_, e) => ApplyWidth(e.NewSize.Width);
    }

    public double CompactBreakpoint
    {
        get => GetValue(CompactBreakpointProperty);
        set => SetValue(CompactBreakpointProperty, Math.Max(240d, value));
    }

    private void ApplyWidth(double width)
    {
        var compact = width > 0 && width < CompactBreakpoint;
        Orientation = compact ? Orientation.Vertical : Orientation.Horizontal;
        Classes.Set("compact", compact);
    }
}
