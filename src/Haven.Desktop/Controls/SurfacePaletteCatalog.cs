using Avalonia.Media;
using Avalonia.Styling;
using Haven.Core;
using Haven.Desktop.HavenUI.Tokens;

namespace Haven.Desktop.Controls;

/// <summary>
/// The single editable colour catalogue for Haven surfaces. Product-specific
/// hues live in <see cref="Hues"/>; the four HavenUI brightness appearances are
/// assembled in <see cref="For(HavenSurface, HavenUiAppearance)"/>. Adding or restyling a surface should only
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
        Color AttentionBorder)
    {
        /// <summary>The live three-tier gradient palette for the active page.</summary>
        internal HavenAccentPalette AccentPalette => HavenAccentPalette.FromAnchors(
            Accent,
            AccentSecondary,
            AccentStrong,
            AccentInk,
            AccentSoft,
            Panel);
    }

    // Edit mode hues here. All page controls and the tidal background consume
    // these same values through dynamic Haven resources.
    private static readonly IReadOnlyDictionary<HavenSurface, SurfaceHue> Hues =
        new Dictionary<HavenSurface, SurfaceHue>
        {
            [HavenSurface.Home] = Hue("#FF171D4A", "#FF3527FF", "#FF5868FF", "#FF2115C7", "#FF202750"),
            [HavenSurface.Chat] = Hue("#FF171D4A", "#FF3527FF", "#FF5868FF", "#FF2115C7", "#FF202750"),
            [HavenSurface.Study] = Hue("#FF17194A", "#FF3927FF", "#FF695CFF", "#FF2416C7", "#FF22244F"),
            [HavenSurface.Tasks] = Hue("#FF4A1D0E", "#FFFF5B19", "#FFFF7C43", "#FFC83A00", "#FF482518"),
            [HavenSurface.Studio] = Hue("#FF102B3A", "#FF19B8FF", "#FF62CEFF", "#FF007CB7", "#FF173342"),
            [HavenSurface.Browse] = Hue("#FF10273A", "#FF168FEA", "#FF59B5FF", "#FF075C9C", "#FF193246"),
            [HavenSurface.Plan] = Hue("#FF3C270F", "#FFFFA11A", "#FFFFBE5C", "#FFB96B00", "#FF42331B"),
            [HavenSurface.Training] = Hue("#FFDCCBFA", "#FF8254CB", "#FFA27BDD", "#FF56308F", "#FFEDE4FC"),
            [HavenSurface.Imagine] = Hue("#FFE6C9F8", "#FFA34EC4", "#FFC37BDD", "#FF702B8C", "#FFF2E1FA"),
            [HavenSurface.Present] = Hue("#FFFFCAB7", "#FFE65F42", "#FFF08D74", "#FF9E3824", "#FFFFE5DC"),
            [HavenSurface.Data] = Hue("#FFC7E2DD", "#FF268B7B", "#FF62B4A6", "#FF155F53", "#FFDCF0EC"),
            [HavenSurface.Vision] = Hue("#FFD2CDF0", "#FF6554B3", "#FF8E80CC", "#FF423383", "#FFE7E4F7"),
            [HavenSurface.Play] = Hue("#FFCFEACB", "#FF3E9A55", "#FF72BC81", "#FF236A35", "#FFE1F2DE"),
            [HavenSurface.Translate] = Hue("#FFCDDEF5", "#FF3D70BE", "#FF7198D3", "#FF274F8A", "#FFE2EAF8"),
            [HavenSurface.Launcher] = Hue("#FFDCCEF0", "#FF8055B4", "#FFA17AC8", "#FF56377F", "#FFEDE5F6"),
            [HavenSurface.Go] = Hue("#FF171D4A", "#FF3527FF", "#FF5868FF", "#FF2115C7", "#FF202750"),
            [HavenSurface.Dashboard] = Hue("#FF171D4A", "#FF3527FF", "#FF5868FF", "#FF2115C7", "#FF202750")
        };

    internal static Palette For(HavenSurface surface, ThemeVariant? theme) =>
        For(surface, theme == ThemeVariant.Dark ? HavenUiAppearance.Dark : HavenUiAppearance.Bright);

    internal static Palette For(HavenSurface surface, HavenUiAppearance appearance)
    {
        var hue = Hues.TryGetValue(surface, out var configured) ? configured : Hues[HavenSurface.Home];
        var accent = Parse(hue.Accent);
        var secondary = Parse(hue.Secondary);
        var strong = Parse(hue.Strong);
        var soft = Parse(hue.Soft);

        if (appearance == HavenUiAppearance.SuperBright)
        {
            var superBrightTide = Blend(Colors.White, Parse(hue.Tide), 0.62);
            var superBrightSoft = Blend(Colors.White, soft, 0.70);
            return new Palette(
                Parse("#FFFCFEFC"), superBrightTide, accent, secondary, strong, superBrightSoft, Parse(hue.Ink),
                Parse("#FF050607"), Parse("#FF353A3E"), Parse("#FF5D6469"), Parse("#FF4B5257"),
                Parse("#FFFFFFFF"), Parse("#FFF8FAF8"), Parse("#FFF1F5F2"), Parse("#FFE9F0EB"),
                Parse("#FFC8D2CA"), Parse("#FFA9B7AC"), superBrightSoft, Blend(superBrightSoft, secondary, 0.28),
                Blend(superBrightSoft, secondary, 0.48), WithAlpha(accent, 0xB8), WithAlpha(accent, 0x94),
                Parse("#FFFFF59B"), Parse("#FFD7C92B"));
        }

        if (appearance == HavenUiAppearance.Bright)
        {
            return new Palette(
                Colors.White, Parse(hue.Tide), accent, secondary, strong, soft, Parse(hue.Ink),
                Parse("#FF111111"), Parse("#FF4F565A"), Parse("#FF73797D"), Parse("#FF60676B"),
                Parse("#F5FFFFFF"), Parse("#EBFFFFFF"), Parse("#FFF5F7F5"), Parse("#FFF0F4F1"),
                Parse("#FFDDE4DE"), Parse("#FFBFCAC1"), soft, Blend(soft, secondary, 0.24),
                Blend(soft, secondary, 0.42), WithAlpha(accent, 0x99), WithAlpha(accent, 0x80),
                Parse("#FFFFF9A8"), Parse("#FFE4DF52"));
        }

        if (appearance == HavenUiAppearance.Dark)
        {
            var darkTide = Blend(Parse("#FF0B0E17"), accent, 0.27);
            var darkSoft = Blend(Parse("#FF171A2A"), accent, 0.22);
            var darkPanel = Blend(Parse("#FF161A2A"), accent, 0.09);
            var darkPanel2 = Blend(Parse("#FF1C2238"), accent, 0.14);
            var darkPanel3 = Blend(Parse("#FF232A45"), accent, 0.18);
            var darkHover = Blend(Parse("#FF2A3354"), accent, 0.22);
            return new Palette(
                Parse("#FF0B0E17"), darkTide, secondary, accent, strong, darkSoft, Parse("#FFFFFFFF"),
                Parse("#FFF8F8FC"), Parse("#FFD5D7E4"), Parse("#FFA7ABC0"), Parse("#FF858BA4"),
                WithAlpha(darkPanel, 0xF5), WithAlpha(darkPanel2, 0xF0), darkPanel3, darkHover,
                Parse("#FF323B5E"), Parse("#FF505B82"), darkSoft, Blend(darkSoft, accent, 0.25),
                Blend(darkSoft, accent, 0.45), WithAlpha(secondary, 0xCC), WithAlpha(secondary, 0xA0),
                Parse("#FF45451E"), Parse("#FFB9B54C"));
        }

        var superDarkBase = Parse("#FF06090D");
        var superDarkTide = Blend(Parse("#FF0B0E18"), accent, 0.25);
        var superDarkSoft = Blend(Parse("#FF121526"), accent, 0.20);
        var superDarkPanel = Blend(Parse("#FF0C0F1A"), accent, 0.10);
        var superDarkPanel2 = Blend(Parse("#FF121628"), accent, 0.15);
        var superDarkPanel3 = Blend(Parse("#FF191E34"), accent, 0.20);
        var superDarkHover = Blend(Parse("#FF222941"), accent, 0.24);
        return new Palette(
            superDarkBase, superDarkTide, secondary, accent, strong, superDarkSoft, Parse("#FF020705"),
            Parse("#FFF9F9FD"), Parse("#FFD6D8E6"), Parse("#FFA9AEC4"), Parse("#FF8990AA"),
            WithAlpha(superDarkPanel, 0xF5), WithAlpha(superDarkPanel2, 0xF5), superDarkPanel3, superDarkHover,
            Parse("#FF2D3551"), Parse("#FF4A5577"), superDarkSoft, Blend(superDarkSoft, accent, 0.22),
            Blend(superDarkSoft, accent, 0.40), WithAlpha(secondary, 0xD8), WithAlpha(secondary, 0xA8),
            Parse("#FF363611"), Parse("#FFC9C343"));
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
