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
        Color AttentionBorder,
        HavenUiTheme Theme = HavenUiTheme.Glow)
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
            [HavenSurface.Automations] = Hue("#FF3C270F", "#FFFFA11A", "#FFFFBE5C", "#FFB96B00", "#FF42331B"),
            [HavenSurface.Terminal] = Hue("#FF102B3A", "#FF19B8FF", "#FF62CEFF", "#FF007CB7", "#FF173342"),
            [HavenSurface.Training] = Hue("#FFDCCBFA", "#FF8254CB", "#FFA27BDD", "#FF56308F", "#FFEDE4FC"),
            [HavenSurface.Imagine] = Hue("#FFE6C9F8", "#FFA34EC4", "#FFC37BDD", "#FF702B8C", "#FFF2E1FA"),
            [HavenSurface.Present] = Hue("#FFFFCAB7", "#FFE65F42", "#FFF08D74", "#FF9E3824", "#FFFFE5DC"),
            [HavenSurface.Data] = Hue("#FFC7E2DD", "#FF268B7B", "#FF62B4A6", "#FF155F53", "#FFDCF0EC"),
            [HavenSurface.Vision] = Hue("#FFD2CDF0", "#FF6554B3", "#FF8E80CC", "#FF423383", "#FFE7E4F7"),
            [HavenSurface.Play] = Hue("#FFCFEACB", "#FF3E9A55", "#FF72BC81", "#FF236A35", "#FFE1F2DE"),
            [HavenSurface.Translate] = Hue("#FFCDDEF5", "#FF3D70BE", "#FF7198D3", "#FF274F8A", "#FFE2EAF8"),
            [HavenSurface.Launcher] = Hue("#FFDCCEF0", "#FF8055B4", "#FFA17AC8", "#FF56377F", "#FFEDE5F6"),
            [HavenSurface.Go] = Hue("#FF171D4A", "#FF3527FF", "#FF4658FF", "#FF2115C7", "#FF202750"),
            [HavenSurface.Spaces] = Hue("#FF221A4A", "#FF9D5CFF", "#FFB87EFF", "#FF6E2BC7", "#FF29224F"),
            [HavenSurface.Boards] = Hue("#FF12332A", "#FF1FA37A", "#FF5FC2A0", "#FF0E6E52", "#FF1A3A31"),
            [HavenSurface.Dashboard] = Hue("#FF171D4A", "#FF3527FF", "#FF5868FF", "#FF2115C7", "#FF202750")
        };

    internal static Palette For(HavenSurface surface, ThemeVariant? theme)
    {
        var appearance = Avalonia.Application.Current?.Resources["HavenUiAppearance"] is HavenUiAppearance configured
            ? configured
            : theme == ThemeVariant.Dark ? HavenUiAppearance.Dark : HavenUiAppearance.Bright;
        return For(surface, appearance);
    }

    internal static Palette For(HavenSurface surface, HavenUiAppearance appearance)
    {
        var hue = Hues.TryGetValue(surface, out var configured) ? configured : Hues[HavenSurface.Home];
        var theme = HavenPersonalisation.Theme;
        var accent = Parse(hue.Accent);
        var secondary = Parse(hue.Secondary);
        var strong = Parse(hue.Strong);
        var soft = Parse(hue.Soft);

        // Accent precedence: an explicit personalisation palette replaces the
        // surface hue anchors; the appearance branch and theme interpretation
        // still adapt them, so apps only ever consume semantic accent values.
        if (HavenPersonalisation.OverrideAccent && HavenPersonalisation.Accent is { } overrideColour)
        {
            var anchors = AccentColourCatalog.Resolve(overrideColour, appearance);
            accent = Parse(anchors.Primary);
            secondary = Parse(anchors.Secondary);
            strong = Parse(anchors.Strong);
            soft = Parse(anchors.Soft);
        }

        var palette = Assemble(surface, hue, appearance, theme, accent, secondary, strong, soft);
        return theme == HavenUiTheme.Glow ? palette : Express(palette, appearance);
    }

    private static Palette Assemble(
        HavenSurface surface,
        SurfaceHue hue,
        HavenUiAppearance appearance,
        HavenUiTheme theme,
        Color accent,
        Color secondary,
        Color strong,
        Color soft)
    {        if (appearance == HavenUiAppearance.SuperBright)
        {
            var superBrightTide = Blend(Colors.White, Parse(hue.Tide), 0.62);
            var superBrightSoft = Blend(Colors.White, soft, 0.70);
            return new Palette(
                Parse("#FFFCFEFC"), superBrightTide, accent, secondary, strong, superBrightSoft, Parse(hue.Ink),
                Parse("#FF050607"), Parse("#FF353A3E"), Parse("#FF5D6469"), Parse("#FF4B5257"),
                Parse("#FFFFFFFF"), Parse("#FFF8FAF8"), Parse("#FFF1F5F2"), Parse("#FFE9F0EB"),
                Parse("#FFC8D2CA"), Parse("#FFA9B7AC"), superBrightSoft, Blend(superBrightSoft, secondary, 0.28),
                Blend(superBrightSoft, secondary, 0.48), WithAlpha(accent, 0xB8), WithAlpha(accent, 0x94),
                Parse("#FFFFF59B"), Parse("#FFD7C92B"), theme);
        }

        if (appearance == HavenUiAppearance.Bright)
        {
            return new Palette(
                Colors.White, Parse(hue.Tide), accent, secondary, strong, soft, Parse(hue.Ink),
                Parse("#FF111111"), Parse("#FF4F565A"), Parse("#FF73797D"), Parse("#FF60676B"),
                Parse("#F5FFFFFF"), Parse("#EBFFFFFF"), Parse("#FFF5F7F5"), Parse("#FFF0F4F1"),
                Parse("#FFDDE4DE"), Parse("#FFBFCAC1"), soft, Blend(soft, secondary, 0.24),
                Blend(soft, secondary, 0.42), WithAlpha(accent, 0x99), WithAlpha(accent, 0x80),
                Parse("#FFFFF9A8"), Parse("#FFE4DF52"), theme);
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
                Parse("#FF45451E"), Parse("#FFB9B54C"), theme);
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
            Parse("#FF363611"), Parse("#FFC9C343"), theme);
    }

    /// <summary>
    /// Applies one non-Glow theme's interaction language to an assembled
    /// palette: how hover, press and selection react, how glassy surfaces are,
    /// and how strongly borders read. Glow never passes through here so the
    /// default appearance stays byte-identical to its baseline.
    /// </summary>
    private static Palette Express(Palette palette, HavenUiAppearance appearance)
    {
        var isDark = appearance is HavenUiAppearance.Dark or HavenUiAppearance.SuperDark;
        return palette.Theme switch
        {
            HavenUiTheme.Bubble => palette with
            {
                Panel = WithAlpha(palette.Panel, isDark ? (byte)0xE8 : (byte)0xE4),
                Panel2 = WithAlpha(palette.Panel2, isDark ? (byte)0xDE : (byte)0xDA),
                Line = Blend(palette.Line, palette.Panel2, 0.30),
                ButtonHover = Blend(palette.ButtonHover, palette.AccentSoft, 0.45),
                ButtonPressed = Blend(palette.ButtonPressed, palette.AccentStrong, 0.25),
                Focus = WithAlpha(palette.Focus, 0xD8)
            },
            HavenUiTheme.Retro => palette with
            {
                Line = Blend(palette.LineStrong, palette.Accent, 0.38),
                LineStrong = Blend(palette.LineStrong, palette.Accent, 0.55),
                Button = Blend(palette.Button, palette.TideBase, isDark ? 0.42 : 0.30),
                ButtonHover = WithAlpha(palette.Accent, isDark ? (byte)0x30 : (byte)0x24),
                ButtonPressed = WithAlpha(palette.AccentStrong, (byte)0x40),
                Focus = WithAlpha(palette.AccentSecondary, 0xEE)
            },
            HavenUiTheme.Playful => palette with
            {
                Panel = WithAlpha(palette.Panel, 0xFF),
                Panel2 = WithAlpha(palette.Panel2, 0xFF),
                Line = Blend(palette.Line, palette.Panel3, 0.35),
                ButtonHover = WithAlpha(Blend(palette.AccentSoft, palette.Accent, 0.40), 0xFF),
                ButtonPressed = WithAlpha(Blend(palette.AccentSoft, palette.AccentStrong, 0.55), 0xFF),
                Focus = WithAlpha(palette.AccentSecondary, 0xE6)
            },
            HavenUiTheme.Cinematic => palette with
            {
                Panel = WithAlpha(Blend(palette.Panel, palette.TideColour, isDark ? 0.18 : 0.10), isDark ? (byte)0xEC : (byte)0xF0),
                Panel2 = WithAlpha(Blend(palette.Panel2, palette.TideColour, 0.14), isDark ? (byte)0xE6 : (byte)0xEA),
                Panel3 = Blend(palette.Panel3, palette.TideBase, 0.12),
                Line = Blend(palette.Line, palette.TideColour, 0.22),
                ButtonHover = Blend(palette.ButtonHover, palette.Accent, 0.20),
                ButtonPressed = Blend(palette.ButtonPressed, palette.Panel3, 0.30),
                Focus = WithAlpha(palette.Accent, 0xCC)
            },
            _ => palette
        };
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
