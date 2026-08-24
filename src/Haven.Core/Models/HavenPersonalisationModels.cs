namespace Haven.Core;

/// <summary>
/// Canonical Haven visual themes. A theme decides how components look and react
/// (colour treatment, geometry personality, interaction feedback and motion);
/// it never changes navigation structure or information architecture.
/// Glow is the default and migration fallback and must remain visually
/// identical to the pre-theme Haven appearance.
/// </summary>
public enum HavenUiTheme
{
    Glow = 0,
    Bubble = 1,
    Retro = 2,
    Playful = 3,
    Cinematic = 4
}

/// <summary>
/// Semantic accent colour families offered by personalisation. Each id expands
/// to a full anchor set (primary/secondary/strong/soft) resolved through the
/// active theme and appearance rather than acting as a single RGB constant.
/// Numeric values are persisted; never renumber.
/// </summary>
public enum HavenAccentColour
{
    Red = 0,
    Orange = 1,
    Yellow = 2,
    Lime = 3,
    Green = 4,
    Teal = 5,
    Cyan = 6,
    Blue = 7,
    Purple = 8,
    Pink = 9,
    Strawberry = 10,
    Brown = 11,
    Monotone = 12
}
