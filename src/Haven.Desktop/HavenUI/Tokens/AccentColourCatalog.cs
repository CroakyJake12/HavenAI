using Haven.Core;

namespace Haven.Desktop.HavenUI.Tokens;

/// <summary>Accent anchors for one semantic colour family in one appearance family.</summary>
internal sealed record AccentAnchorSet(string Primary, string Secondary, string Strong, string Soft);

/// <summary>
/// The thirteen semantic accent palettes offered by personalisation. Values are
/// hue anchors: the active appearance branch blends them into surfaces and the
/// active theme decides how they behave (gradients, glows, illumination), so a
/// palette is a colour family rather than a fixed RGB constant.
/// </summary>
internal static class AccentColourCatalog
{
    private sealed record PaletteDefinition(
        HavenAccentColour Colour,
        string Name,
        AccentAnchorSet Light,
        AccentAnchorSet Dark);

    // Dark-appearance anchors are lifted toward brighter primaries so accents
    // keep contrast on near-black panels; Yellow/Lime use deliberately deep
    // strong/soft anchors to survive contrast guards.
    private static readonly IReadOnlyList<PaletteDefinition> Palettes =
    [
        new(HavenAccentColour.Red, "Red",
            new("#FFD13438", "#FFE0575B", "#FFA31E22", "#FFF6DADA"),
            new("#FFFF5C60", "#FFFF8487", "#FFC22F33", "#FF3A1D20")),
        new(HavenAccentColour.Orange, "Orange",
            new("#FFEF7B1A", "#FFFF9540", "#FFC25F04", "#FFFBE8D8"),
            new("#FFFF9433", "#FFFFAE5E", "#FFCC6A10", "#FF3A2718")),
        new(HavenAccentColour.Yellow, "Yellow",
            new("#FFD9A400", "#FFE9BC2E", "#FFA87A00", "#FFFAF0CE"),
            new("#FFFFC83D", "#FFFFD75E", "#FFC79406", "#FF383115")),
        new(HavenAccentColour.Lime, "Lime",
            new("#FF93B500", "#FFAAC21F", "#FF6E8800", "#FFEEF5CF"),
            new("#FFB4D414", "#FFC6E23C", "#FF8AA504", "#FF2B3413")),
        new(HavenAccentColour.Green, "Green",
            new("#FF1E9E58", "#FF42B877", "#FF12713E", "#FFD8F0E2"),
            new("#FF37C97B", "#FF5FD998", "#FF1E8A52", "#FF16301F")),
        new(HavenAccentColour.Teal, "Teal",
            new("#FF0F9494", "#FF31B0B0", "#FF086B6B", "#FFD5EFEE"),
            new("#FF26B8B8", "#FF4BD0D0", "#FF128282", "#FF12302F")),
        new(HavenAccentColour.Cyan, "Cyan",
            new("#FF0FA3D1", "#FF35BCE6", "#FF07799C", "#FFD6EFF8"),
            new("#FF2FC0EC", "#FF57D2F5", "#FF0E93BF", "#FF10303B")),
        new(HavenAccentColour.Blue, "Blue",
            new("#FF2563EB", "#FF4D82F5", "#FF1643AF", "#FFDAE4FB"),
            new("#FF4E86FF", "#FF74A1FF", "#FF2F62CC", "#FF16233F")),
        new(HavenAccentColour.Purple, "Purple",
            new("#FF8B44D8", "#FFA463E8", "#FF662BA6", "#FFEDE0FA"),
            new("#FFA25FE8", "#FFB87FF5", "#FF7C33BD", "#FF241736")),
        new(HavenAccentColour.Pink, "Pink",
            new("#FFE24C8B", "#FFED6EA3", "#FFB92E67", "#FFFBDDE8"),
            new("#FFF76AA6", "#FFFA8CBC", "#FFCC4583", "#FF391A28")),
        new(HavenAccentColour.Strawberry, "Strawberry",
            new("#FFE8554F", "#FFF2736E", "#FFB93030", "#FFFBDAD8"),
            new("#FFFF7069", "#FFFF8C86", "#FFD63E38", "#FF3A1B19")),
        new(HavenAccentColour.Brown, "Brown",
            new("#FF96601F", "#FFB17A38", "#FF6E4412", "#FFF3E6D5"),
            new("#FFC08A47", "#FFD4A263", "#FF96702E", "#FF31251A")),
        new(HavenAccentColour.Monotone, "Monotone",
            new("#FF171717", "#FF3B3B3B", "#FF000000", "#FFE9E9E9"),
            new("#FFF2F2F2", "#FFFFFFFF", "#FFBDBDBD", "#FF262626"))
    ];

    /// <summary>Ordered semantic colours for the settings palette picker.</summary>
    internal static IReadOnlyList<HavenAccentColour> Colours =>
        Palettes.Select(palette => palette.Colour).ToArray();

    /// <summary>Ordered display names for the settings palette picker.</summary>
    internal static IReadOnlyList<string> Names =>
        Palettes.Select(palette => palette.Name).ToArray();

    /// <summary>Parses a persisted palette name safely; unknown values return null (no override).</summary>
    internal static HavenAccentColour? Parse(string? value)
    {
        foreach (var palette in Palettes)
            if (palette.Name.Equals(value, StringComparison.OrdinalIgnoreCase))
                return palette.Colour;
        return null;
    }

    /// <summary>Returns the canonical persisted name for a palette.</summary>
    internal static string Name(HavenAccentColour colour) =>
        Palettes.FirstOrDefault(palette => palette.Colour == colour)?.Name ?? string.Empty;

    /// <summary>
    /// Resolves accent anchors for a palette under the current appearance
    /// family. Bright appearances use the light set, dark appearances the dark set.
    /// </summary>
    internal static AccentAnchorSet Resolve(HavenAccentColour colour, HavenUiAppearance appearance)
    {
        var palette = Palettes.FirstOrDefault(candidate => candidate.Colour == colour) ?? Palettes[7];
        var dark = appearance is HavenUiAppearance.Dark or HavenUiAppearance.SuperDark;
        return dark ? palette.Dark : palette.Light;
    }
}
