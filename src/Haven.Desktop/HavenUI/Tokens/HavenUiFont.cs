using Avalonia.Media;

namespace Haven.Desktop.HavenUI.Tokens;

/// <summary>
/// Resolves the globally selected UI font family for every text surface.
/// Montserrat remains the bundled default and final fallback; a user-selected
/// installed family is expressed as an Avalonia fallback chain so a missing
/// font degrades safely instead of breaking layout.
/// </summary>
internal static class HavenUiFont
{
    internal const string DefaultFamily = "Montserrat";
    private const string BundledMontserrat = "avares://Haven/Assets/Fonts/MontserratStatic#Montserrat";

    private static string? _userFamily;

    /// <summary>The selected family name, or null when using bundled Montserrat.</summary>
    internal static string? UserFamily => _userFamily;

    /// <summary>
    /// Sets the global selection; null or empty restores Montserrat. Names are
    /// validated against installed system fonts at selection time.
    /// </summary>
    internal static void SetUserFamily(string? family) =>
        _userFamily = string.IsNullOrWhiteSpace(family) || family.Trim().Equals(DefaultFamily, StringComparison.OrdinalIgnoreCase)
            ? null
            : family.Trim();

    /// <summary>
    /// Maps a HUI/AXAML requested family to the concrete Avalonia family,
    /// routing the canonical Montserrat request through the user's selection
    /// with the bundled face as guaranteed fallback.
    /// </summary>
    internal static FontFamily Resolve(string requested)
    {
        if (!requested.Equals(DefaultFamily, StringComparison.OrdinalIgnoreCase))
            return new FontFamily(requested);
        return _userFamily is null
            ? new FontFamily(BundledMontserrat)
            : new FontFamily($"{_userFamily}, {BundledMontserrat}");
    }

    /// <summary>Reports whether a family is available through the OS/application font collection.</summary>
    internal static bool IsInstalled(string name)
    {
        try
        {
            return FontManager.Current.SystemFonts.Any(font => font.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
