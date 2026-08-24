using Haven.Core;

namespace Haven.Desktop.HavenUI.Tokens;

/// <summary>
/// The visual personality of one canonical Haven theme: how the shared
/// component system expresses radius, borders, shadows, motion and interaction
/// feedback on top of a resolved surface palette. Layout, spacing rhythm,
/// navigation and control identity are intentionally absent — themes never
/// change information architecture.
/// </summary>
internal sealed record HavenThemeExpression(
    HavenUiTheme Theme,
    string DisplayName,
    string Description,
    double ControlRadiusScale,
    double CardRadiusScale,
    double PopupRadiusScale,
    double MotionDurationScale,
    double ShadowOpacityScale,
    double BorderIntensity)
{
    /// <summary>Baseline radii matching the pre-theme Haven geometry.</summary>
    internal const double BaseControlRadius = 10d;
    internal const double BaseCardRadius = 16d;
    internal const double BasePopupRadius = 20d;
}

/// <summary>
/// Catalogue of the five canonical themes. Glow is deliberately the identity
/// transform: when the user has not personalised Haven, every value below
/// reproduces the pre-theme appearance exactly.
/// </summary>
internal static class HavenThemeCatalog
{
    internal static IReadOnlyList<HavenThemeExpression> All { get; } =
    [
        new(
            HavenUiTheme.Glow, "Glow",
            "The default Haven look: tidal gradients, soft glow accents.",
            1.0, 1.0, 1.0, 1.0, 1.0, 1.0),
        new(
            HavenUiTheme.Bubble, "Bubble",
            "Soft glassy surfaces, atmospheric tint and gentle bloom.",
            1.35, 1.3, 1.25, 1.15, 1.35, 0.8),
        new(
            HavenUiTheme.Retro, "Retro",
            "Engineered technical surfaces with fast edge illumination.",
            0.45, 0.55, 0.6, 0.7, 0.75, 1.25),
        new(
            HavenUiTheme.Playful, "Playful",
            "Tactile tonal shapes with springy, friendly feedback.",
            1.5, 1.35, 1.3, 0.9, 0.9, 1.1),
        new(
            HavenUiTheme.Cinematic, "Cinematic",
            "Immersive layered depth with contextual light and smooth fades.",
            1.0, 1.05, 1.1, 1.25, 1.7, 0.95)
    ];

    /// <summary>
    /// Resolves a theme expression; unknown values fall back to Glow so a
    /// malformed preference can never prevent Haven from launching.
    /// </summary>
    internal static HavenThemeExpression Resolve(HavenUiTheme theme)
    {
        foreach (var candidate in All)
            if (candidate.Theme == theme)
                return candidate;
        return All[0];
    }

    /// <summary>Parses a persisted theme name safely, falling back to Glow.</summary>
    internal static HavenUiTheme Parse(string? value) =>
        Enum.TryParse<HavenUiTheme>(value, ignoreCase: true, out var parsed) ? parsed : HavenUiTheme.Glow;

    /// <summary>Returns the canonical persisted name for a theme.</summary>
    internal static string Name(HavenUiTheme theme) =>
        All.FirstOrDefault(expression => expression.Theme == theme)?.DisplayName ?? All[0].DisplayName;
}
