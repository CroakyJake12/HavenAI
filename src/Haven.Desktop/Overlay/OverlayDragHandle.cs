#if !ANDROID
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Overlay;

/// <summary>
/// Haven-owned drag target for the system Overlay header. The native Window only moves
/// in response to deltas produced by this product-owned Haven.UI surface.
/// </summary>
internal sealed class OverlayDragHandle : Container, IHavenPointerInputTarget
{
    private HavenPoint? _lastPosition;

    public event Action<HavenPoint>? DragDelta;

    public bool PointerPressed(HavenPointerInput input)
    {
        _lastPosition = input.Position;
        return true;
    }

    public bool PointerMoved(HavenPointerInput input)
    {
        if (_lastPosition is not HavenPoint previous) return false;
        var delta = new HavenPoint(input.Position.X - previous.X, input.Position.Y - previous.Y);
        if (Math.Abs(delta.X) < 0.5 && Math.Abs(delta.Y) < 0.5) return true;
        _lastPosition = input.Position;
        DragDelta?.Invoke(delta);
        return true;
    }

    public bool PointerReleased(HavenPointerInput input)
    {
        _lastPosition = null;
        return true;
    }
}
#endif
