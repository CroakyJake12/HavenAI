namespace Haven.UI;

/// <summary>Backend-neutral pointer gesture delivered by HavenInputRouter.</summary>
public readonly record struct HavenPointerInput(
    HavenPoint Position,
    HavenPoint LocalPosition,
    HavenPointerKind PointerKind);

/// <summary>Consumes raw pointer press/move/release without exposing a platform input type to Haven.UI.</summary>
public interface IHavenPointerInputTarget
{
    bool PointerPressed(HavenPointerInput input);
    bool PointerMoved(HavenPointerInput input);
    bool PointerReleased(HavenPointerInput input);
}
/// <summary>Consumes a backend-neutral pointer-wheel gesture before ordinary scroll containers.</summary>
public interface IHavenScrollInputTarget
{
    bool PointerWheel(HavenPoint localPosition, double deltaX, double deltaY);
}
