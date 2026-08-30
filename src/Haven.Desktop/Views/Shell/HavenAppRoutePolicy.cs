using Haven.Core;

namespace Haven.Desktop.Views.Shell;

/// <summary>
/// Identifies the shell operation that owns an App launch. The policy is kept
/// free of Avalonia controls so every registered App route can be protected by
/// automated tests before the legacy launcher and presentation paths are removed.
/// </summary>
public enum HavenAppRouteKind
{
    BaseMode = 0,
    Go = 1,
    Dashboard = 2,
    Browse = 3,
    Plan = 4,
    Training = 5,
    ModeWorkspace = 6,
    Write = 7,
    Imagine = 8,
    Vision = 9,
    Automations = 10,
    Translate = 11,
    Terminal = 12,
    Play = 13,
    Mesh = 14,
    Spaces = 15,
    Maps = 16
}

/// <summary>Describes the concrete route and visible surface for an App.</summary>
public readonly record struct HavenAppRoute(HavenAppRouteKind Kind, HavenSurface Surface);

/// <summary>Provides one exhaustive routing policy for built-in and user Apps.</summary>
public static class HavenAppRoutePolicy
{
    public static HavenAppRoute Resolve(ModeDefinition app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var key = app.Key.Trim().ToLowerInvariant();

        return key switch
        {
            "go" => new(HavenAppRouteKind.Go, HavenSurface.Go),
            "dashboard" => new(HavenAppRouteKind.Dashboard, HavenSurface.Dashboard),
            "browse" or "browser" => new(HavenAppRouteKind.Browse, HavenSurface.Browse),
            "plan" => new(HavenAppRouteKind.Plan, HavenSurface.Plan),
            "training" => new(HavenAppRouteKind.Training, HavenSurface.Training),
            "automations" => new(HavenAppRouteKind.Automations, HavenSurface.Automations),
            "terminal" => new(HavenAppRouteKind.Terminal, HavenSurface.Terminal),
            "imagine" => new(HavenAppRouteKind.Imagine, HavenSurface.Imagine),
            "write" => new(HavenAppRouteKind.ModeWorkspace, HavenSurface.Write),
            "canvas" => new(HavenAppRouteKind.ModeWorkspace, HavenSurface.Canvas),
            "present" => new(HavenAppRouteKind.ModeWorkspace, HavenSurface.Present),
            "data" => new(HavenAppRouteKind.ModeWorkspace, HavenSurface.Data),
            "vision" => new(HavenAppRouteKind.Vision, HavenSurface.Vision),
            "play" => new(HavenAppRouteKind.Play, HavenSurface.Play),
            "translate" => new(HavenAppRouteKind.Translate, HavenSurface.Translate),
            "mesh" => new(HavenAppRouteKind.Mesh, HavenSurface.Mesh),
            "spaces" => new(HavenAppRouteKind.Spaces, HavenSurface.Spaces),
            "boards" => new(HavenAppRouteKind.ModeWorkspace, HavenSurface.Boards),
            "maps" => new(HavenAppRouteKind.Maps, HavenSurface.Maps),
            "motion" => new(HavenAppRouteKind.ModeWorkspace, HavenSurface.Motion),
            "launcher" => new(HavenAppRouteKind.ModeWorkspace, HavenSurface.Launcher),
            _ => new(HavenAppRouteKind.BaseMode, SurfaceFor(app.BaseMode))
        };
    }

    private static HavenSurface SurfaceFor(HavenMode mode) => mode switch
    {
        HavenMode.Study => HavenSurface.Study,
        HavenMode.Tasks => HavenSurface.Tasks,
        HavenMode.Studio => HavenSurface.Studio,
        _ => HavenSurface.Chat
    };
}
