// Where:    src/Haven.Android/KeyboardTheme.cs
// What:     Colour palette definitions and resolution for the Haven Keyboard IME.
// How:      Two static palettes (light/dark) defined as literal colours; callers
//           resolve a palette from the saved KeyboardThemeMode plus the current
//           system night mode, then pass the palette into keyboard views.
// Why:      The IME renders pure programmatic Android views outside Avalonia, so it
//           cannot reuse Haven's AXAML styles. Centralised fallback literals keep
//           the keyboard visually consistent with Haven's purple brand without a
//           resource dependency that could miss on some OEM themes.
// Maintenance: Adjust palette colours here only. Never sample colours from the
//           focused app's field; the keyboard must look identical everywhere.

using Android.Graphics;

namespace Haven.Android;

/// <summary>Selects how the Haven Keyboard picks its colour palette.</summary>
internal enum KeyboardThemeMode
{
    /// <summary>Follow the system light/dark setting.</summary>
    FollowSystem = 0,

    /// <summary>Always use the light palette.</summary>
    Light = 1,

    /// <summary>Always use the dark palette.</summary>
    Dark = 2,
}

/// <summary>
/// A resolved set of colours used to draw the keyboard and suggestion strip.
/// </summary>
/// <param name="Background">Overall keyboard surface.</param>
/// <param name="StripBackground">Suggestion strip surface.</param>
/// <param name="KeyBackground">Standard letter/symbol key surface.</param>
/// <param name="ModifierBackground">Modifier keys (shift, backspace, layer, enter).</param>
/// <param name="KeyForeground">Primary key label colour.</param>
/// <param name="KeyForegroundDim">Secondary/hint label colour.</param>
/// <param name="Accent">Haven accent used for active states and the AI chip.</param>
/// <param name="OnAccent">Readable foreground drawn on top of <paramref name="Accent"/>.</param>
internal sealed record KeyboardPalette(
    Color Background,
    Color StripBackground,
    Color KeyBackground,
    Color ModifierBackground,
    Color KeyForeground,
    Color KeyForegroundDim,
    Color Accent,
    Color OnAccent)
{
    /// <summary>Semi-transparent overlay colour applied while a key is touched.</summary>
    internal Color PressedOverlay => Color.Argb(78, Accent.R, Accent.G, Accent.B);
}

/// <summary>
/// Resolves <see cref="KeyboardThemeMode"/> values into concrete palettes using
/// literal fallback colours (never app-supplied resources, so the keyboard cannot
/// inherit an untrusted host theme).
/// </summary>
internal static class KeyboardTheme
{
    private static readonly KeyboardPalette Light = new(
        Background: Color.Rgb(245, 243, 251),
        StripBackground: Color.Rgb(255, 255, 255),
        KeyBackground: Color.Rgb(255, 255, 255),
        ModifierBackground: Color.Rgb(226, 222, 240),
        KeyForeground: Color.Rgb(30, 26, 48),
        KeyForegroundDim: Color.Rgb(122, 116, 148),
        Accent: Color.Rgb(112, 72, 232),
        OnAccent: Color.Rgb(255, 255, 255));

    private static readonly KeyboardPalette Dark = new(
        Background: Color.Rgb(18, 14, 28),
        StripBackground: Color.Rgb(24, 18, 38),
        KeyBackground: Color.Rgb(37, 30, 56),
        ModifierBackground: Color.Rgb(49, 41, 74),
        KeyForeground: Color.Rgb(238, 234, 250),
        KeyForegroundDim: Color.Rgb(146, 139, 176),
        Accent: Color.Rgb(154, 124, 255),
        OnAccent: Color.Rgb(20, 15, 34));

    /// <summary>
    /// Resolves the palette for the given preference and system night mode.
    /// </summary>
    internal static KeyboardPalette Resolve(KeyboardThemeMode mode, bool systemNightMode) => mode switch
    {
        KeyboardThemeMode.Light => Light,
        KeyboardThemeMode.Dark => Dark,
        _ => systemNightMode ? Dark : Light,
    };
}
