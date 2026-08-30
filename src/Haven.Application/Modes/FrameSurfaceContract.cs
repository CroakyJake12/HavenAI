namespace Haven.Application.Modes;

/// <summary>
/// Boot-safe registration contract for the Frame workspace surface.
/// Keep Frame disabled and launcher-hidden until a tested shell route is integrated.
/// </summary>
public static class FrameSurfaceContract
{
    public const string AppKey = "frame";
    public const string SurfaceKey = "frame";
    public const string RouteKey = "frame";
    public const string WorkspaceRendererKey = "hui";
    public const int WorkspaceContractVersion = 1;

    public const bool IsEnabledByDefault = false;
    public const bool IsLauncherVisibleByDefault = false;
}