using Avalonia.Media;
using Avalonia.Styling;
using Haven.Core;

namespace Haven.Desktop.Controls;

/// <summary>
/// The single editable colour catalogue for Haven surfaces. Product-specific
/// hues live in <see cref="Hues"/>; shared light/dark neutral colours are
/// assembled in <see cref="For"/>. Adding or restyling a surface should only
/// require changing this file.
/// </summary>
internal static class SurfacePaletteCatalog
{
    private sealed record SurfaceHue(
        string Tide,
        string Accent,
        string Secondary,
        string Strong,
        string Soft,
        string Ink = "#FFFFFFFF");

    internal sealed record Palette(
        Color TideBase,
        Color TideColour,
        Color Accent,
        Color AccentSecondary,
        Color AccentStrong,
        Color AccentSoft,
        Color AccentInk,
        Color Text,
        Color TextSoft,
        Color Muted,
        Color Muted2,
        Color Panel,
        Color Panel2,
        Color Panel3,
        Color PanelHover,
        Color Line,
        Color LineStrong,
        Color Button,
        Color ButtonHover,
        Color ButtonPressed,
        Color Focus,
        Color AccentBorder,
        Color Attention,
        Color AttentionBorder);

    // Edit mode hues here. All page controls and the tidal background consume
    // these same values through dynamic Haven resources.
    private static readonly IReadOnlyDictionary<HavenSurface, SurfaceHue> Hues =
        new Dictionary<HavenSurface, SurfaceHue>
        {
            [HavenSurface.Home] = Hue("#FFC8F0E8", "#FF239B78", "#FF54B99B", "#FF12684E", "#FFDDF3EC"),
            [HavenSurface.Chat] = Hue("#FFC8F0E8", "#FF239B78", "#FF54B99B", "#FF12684E", "#FFDDF3EC"),
            [HavenSurface.Study] = Hue("#FFD5DCF8", "#FF526DD0", "#FF8195E2", "#FF30458F", "#FFE6EAFB"),
            [HavenSurface.Tasks] = Hue("#FFC9F3A7", "#FF5AAE2B", "#FF86CD4D", "#FF327A16", "#FFE5F7D4"),
            [HavenSurface.Studio] = Hue("#FFC8F8FA", "#FF00A7B3", "#FF55CAD2", "#FF006A73", "#FFDCF7F8"),
            [HavenSurface.Browse] = Hue("#FFC9E8F8", "#FF268AC1", "#FF62B4DC", "#FF145D87", "#FFDDEFF9"),
            [HavenSurface.Plan] = Hue("#FFFFD8A8", "#FFE9771B", "#FFF0A052", "#FF9E450B", "#FFFFEAD2"),
            [HavenSurface.Training] = Hue("#FFDCCBFA", "#FF8254CB", "#FFA27BDD", "#FF56308F", "#FFEDE4FC"),
            [HavenSurface.Imagine] = Hue("#FFE6C9F8", "#FFA34EC4", "#FFC37BDD", "#FF702B8C", "#FFF2E1FA"),
            [HavenSurface.Present] = Hue("#FFFFCAB7", "#FFE65F42", "#FFF08D74", "#FF9E3824", "#FFFFE5DC"),
            [HavenSurface.Data] = Hue("#FFC7E2DD", "#FF268B7B", "#FF62B4A6", "#FF155F53", "#FFDCF0EC"),
            [HavenSurface.Vision] = Hue("#FFD2CDF0", "#FF6554B3", "#FF8E80CC", "#FF423383", "#FFE7E4F7"),
            [HavenSurface.Play] = Hue("#FFCFEACB", "#FF3E9A55", "#FF72BC81", "#FF236A35", "#FFE1F2DE"),
            [HavenSurface.Translate] = Hue("#FFCDDEF5", "#FF3D70BE", "#FF7198D3", "#FF274F8A", "#FFE2EAF8"),
            [HavenSurface.Launcher] = Hue("#FFDCCEF0", "#FF8055B4", "#FFA17AC8", "#FF56377F", "#FFEDE5F6"),
            [HavenSurface.Go] = Hue("#FFBEFAF8", "#FF00A99F", "#FF56CEC7", "#FF006D68", "#FFD9F7F5"),
            [HavenSurface.Dashboard] = Hue("#FFBEFAF8", "#FF00A99F", "#FF56CEC7", "#FF006D68", "#FFD9F7F5")
        };

    internal static Palette For(HavenSurface surface, ThemeVariant? theme)
    {
        var hue = Hues.TryGetValue(surface, out var configured) ? configured : Hues[HavenSurface.Home];
        var dark = theme == ThemeVariant.Dark;
        var accent = Parse(hue.Accent);
        var secondary = Parse(hue.Secondary);
        var strong = Parse(hue.Strong);
        var soft = Parse(hue.Soft);

        if (!dark)
        {
            return new Palette(
                Colors.White, Parse(hue.Tide), accent, secondary, strong, soft, Parse(hue.Ink),
                Parse("#FF111111"), Parse("#FF4F565A"), Parse("#FF73797D"), Parse("#FF60676B"),
                Parse("#F5FFFFFF"), Parse("#EBFFFFFF"), Parse("#FFF5F7F5"), Parse("#FFF0F4F1"),
                Parse("#FFDDE4DE"), Parse("#FFBFCAC1"), soft, Blend(soft, secondary, 0.24),
                Blend(soft, secondary, 0.42), WithAlpha(accent, 0x99), WithAlpha(accent, 0x80),
                Parse("#FFFFF9A8"), Parse("#FFE4DF52"));
        }

        var darkTide = Blend(Parse("#FF101318"), accent, 0.22);
        var darkSoft = Blend(Parse("#FF1A1F25"), accent, 0.20);
        return new Palette(
            Parse("#FF101318"), darkTide, secondary, accent, strong, darkSoft, Parse("#FF07100D"),
            Parse("#FFF5F7F8"), Parse("#FFCDD3D7"), Parse("#FFA8B0B5"), Parse("#FF879197"),
            Parse("#F51B2026"), Parse("#F022282F"), Parse("#FF292F37"), Parse("#FF313942"),
            Parse("#FF3C454F"), Parse("#FF58636E"), darkSoft, Blend(darkSoft, accent, 0.25),
            Blend(darkSoft, accent, 0.45), WithAlpha(secondary, 0xCC), WithAlpha(secondary, 0xA0),
            Parse("#FF45451E"), Parse("#FFB9B54C"));
    }

    private static SurfaceHue Hue(string tide, string accent, string secondary, string strong, string soft) =>
        new(tide, accent, secondary, strong, soft);

    private static Color Parse(string value) => Color.Parse(value);

    private static Color WithAlpha(Color value, byte alpha) =>
        Color.FromArgb(alpha, value.R, value.G, value.B);

    private static Color Blend(Color first, Color second, double secondWeight)
    {
        var weight = Math.Clamp(secondWeight, 0, 1);
        return Color.FromArgb(
            255,
            (byte)Math.Round(first.R + (second.R - first.R) * weight),
            (byte)Math.Round(first.G + (second.G - first.G) * weight),
            (byte)Math.Round(first.B + (second.B - first.B) * weight));
    }
}
