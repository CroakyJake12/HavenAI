using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Shell.Overlays;

/// <summary>
/// Haven-owned drag gesture for the floating Voice header. It consumes only the
/// designated header pointer sequence and reports incremental backend-neutral deltas.
/// </summary>
internal sealed class VoiceDragHandle : Container, IHavenPointerInputTarget
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

        var delta = new HavenPoint(
            input.Position.X - previous.X,
            input.Position.Y - previous.Y);
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
