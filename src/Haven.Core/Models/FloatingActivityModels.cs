namespace Haven.Core;

public enum FloatingActivityState
{
    Created = 0,
    Presented = 1,
    Compact = 2,
    Expanded = 3,
    Dismissed = 4,
    Failed = 5
}

public enum FloatingActivityPresentation
{
    InApp = 0,
    DetachedWindow = 1,
    SystemOverlay = 2,
    PictureInPicture = 3,
    Bubble = 4
}

public sealed record FloatingActivityDefinition(
    Guid Id,
    Guid ThreadId,
    string AppKey,
    string Title,
    string AccentKey,
    FloatingActivityPresentation Presentation,
    bool AlwaysOnTop,
    bool IsDismissible,
    DateTimeOffset CreatedAt);

public sealed record FloatingActivitySnapshot(
    Guid Id,
    FloatingActivityState State,
    double Width,
    double Height,
    double X,
    double Y,
    string? Error = null);
