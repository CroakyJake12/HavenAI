using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.HavenUI.Tokens;

/// <summary>
/// Applies one canonical HavenUI semantic palette to application resources.
/// Existing resource names remain aliases while screens migrate to the clearer
/// semantic names; both sets always resolve to the same colour values.
/// </summary>
internal static class HavenUiResourceApplier
{
    internal static event EventHandler? PaletteChanged;

    internal static void Apply(SurfacePaletteCatalog.Palette palette)
    {
        // Palette changes can originate from navigation, generated Apps or a
        // settings preview. Marshal them to the UI thread and mutate the live
        // brushes there; no I/O or expensive theme reconstruction occurs.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Apply(palette), DispatcherPriority.Render);
            return;
        }

        var isDark = Luminance(palette.Text) > Luminance(palette.Panel);
        var disabledText = WithAlpha(palette.Muted, 0x88);
        var overlay = WithAlpha(palette.Panel, isDark ? (byte)0xF2 : (byte)0xF7);
        var input = palette.Button;
        var success = Color.Parse(isDark ? "#FF76D7A0" : "#FF147A48");
        var information = palette.AccentSecondary;
        var expression = HavenThemeCatalog.Resolve(palette.Theme);
        var shadowBase = Color.Parse(isDark ? "#B8000000" : "#52000000");
        var shadow = WithAlpha(shadowBase, (byte)Math.Clamp(Math.Round(shadowBase.A * expression.ShadowOpacityScale), 0, 255));

        SetBrush("HavenBackgroundBrush", palette.TideBase);
        SetBrush("HavenTextBrush", palette.Text);
        SetBrush("HavenTextSoftBrush", palette.TextSoft);
        SetBrush("HavenMutedBrush", palette.Muted);
        SetBrush("HavenMuted2Brush", palette.Muted2);
        SetBrush("HavenPanelBrush", palette.Panel);
        SetBrush("HavenElevatedBrush", palette.Panel);
        SetBrush("HavenPanel2Brush", palette.Panel2);
        SetBrush("HavenPanel3Brush", palette.Panel3);
        SetBrush("HavenPanelHoverBrush", palette.PanelHover);
        SetBrush("HavenLineBrush", palette.Line);
        SetBrush("HavenLineStrongBrush", palette.LineStrong);
        SetBrush("HavenButtonBrush", palette.Button);
        SetBrush("HavenButtonHoverBrush", palette.ButtonHover);
        SetBrush("HavenButtonPressedBrush", palette.ButtonPressed);
        SetBrush("HavenFocusBrush", palette.Focus);
        var accents = palette.AccentPalette;
        ApplyAccentPaletteCore(accents);
        SetBrush("HavenAccentInkBrush", palette.AccentInk);
        SetBrush("HavenAccentSoftBrush", palette.AccentSoft);
        SetBrush("HavenBlueSoftBrush", palette.AccentSoft);
        SetBrush("HavenNubBrush", palette.AccentSecondary);
        SetBrush("HavenAccentBorderBrush", palette.AccentBorder);
        SetBrush("HavenAttentionBrush", palette.Attention);
        SetBrush("HavenAttentionBorderBrush", palette.AttentionBorder);

        SetBrush("HavenBackgroundPrimaryBrush", palette.TideBase);
        SetBrush("HavenBackgroundSecondaryBrush", palette.Panel2);
        SetBrush("HavenBackgroundElevatedBrush", palette.Panel);
        SetBrush("HavenCardSurfaceBrush", palette.Panel);
        SetBrush("HavenOverlaySurfaceBrush", overlay);
        SetBrush("HavenInputSurfaceBrush", input);
        SetBrush("HavenTextPrimaryBrush", palette.Text);
        SetBrush("HavenTextSecondaryBrush", palette.TextSoft);
        SetBrush("HavenTextMutedBrush", palette.Muted);
        SetBrush("HavenTextDisabledBrush", disabledText);
        SetBrush("HavenBorderSubtleBrush", palette.Line);
        SetBrush("HavenSeparatorBrush", palette.Line);
        SetBrush("HavenStateHoverBrush", palette.ButtonHover);
        SetBrush("HavenStatePressedBrush", palette.ButtonPressed);
        SetBrush("HavenStateSelectedBrush", palette.AccentSoft);
        SetBrush("HavenStateDisabledBrush", WithAlpha(palette.Button, 0x70));
        SetBrush("HavenSuccessBrush", success);
        SetBrush("HavenInformationBrush", information);
        SetBrush("HavenLinkBrush", palette.Accent);
        SetBrush("HavenShadowBrush", shadow);
        SetBrush("HavenAccentForegroundBrush", palette.AccentInk);

        SetBrush("StrokeBrush", palette.LineStrong);
        SetBrush("SurfaceCardBrush", palette.Panel);
        SetBrush("TextPrimaryBrush", palette.Text);

        // Theme personality: shared structural tokens stay semantic while each
        // theme scales how geometry and motion express themselves. Glow writes
        // the baseline values exactly.
        SetCornerRadius("HavenControlRadius", HavenThemeExpression.BaseControlRadius * expression.ControlRadiusScale);
        SetCornerRadius("HavenCardRadius", HavenThemeExpression.BaseCardRadius * expression.CardRadiusScale);
        SetCornerRadius("HavenPopupRadius", HavenThemeExpression.BasePopupRadius * expression.PopupRadiusScale);
        var resources = Avalonia.Application.Current?.Resources;
        if (resources is not null) resources["HavenMotionDurationScale"] = expression.MotionDurationScale;
        PaletteChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void SetCornerRadius(string key, double radius)
    {
        var resources = Avalonia.Application.Current?.Resources;
        if (resources is null) return;
        resources[key] = new CornerRadius(Math.Max(0d, Math.Round(radius)));
    }

    /// <summary>
    /// Applies a generated/App-scoped three-tier accent without replacing the
    /// rest of the current HavenUI appearance.
    /// </summary>
    internal static void ApplyAccentPalette(HavenAccentPalette accents)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyAccentPalette(accents), DispatcherPriority.Render);
            return;
        }

        ApplyAccentPaletteCore(accents);
    }

    private static void ApplyAccentPaletteCore(HavenAccentPalette accents)
    {
        SetGradient("HavenAccentBrush", accents.Primary);
        SetGradient("HavenAccentPrimaryBrush", accents.Primary);
        SetGradient("HavenAccentSecondaryBrush", accents.Secondary);
        SetGradient("HavenAccentTertiaryBrush", accents.Tertiary);
        SetGradient("HavenAccentPrimaryHoverBrush", Shift(accents.Primary, accents.Secondary.Start, 0.16));
        SetGradient("HavenAccentSecondaryHoverBrush", Shift(accents.Secondary, accents.Primary.Start, 0.18));
        SetGradient("HavenAccentTertiaryHoverBrush", Shift(accents.Tertiary, accents.Secondary.Start, 0.70));
        SetGradient("HavenAccentPressedBrush", Shift(accents.Primary, accents.Tertiary.Middle, 0.22));
        SetColour("HavenAccentPrimaryColor", accents.Primary.Middle);
        SetColour("HavenAccentSecondaryColor", accents.Secondary.Middle);
        SetColour("HavenAccentTertiaryColor", accents.Tertiary.Middle);
        SetColour("HavenAccentPrimaryGlowColor", WithAlpha(accents.Primary.Start, 0xC8));
        SetColour("HavenAccentSecondaryGlowColor", WithAlpha(accents.Secondary.Start, 0xA8));
        SetColour("HavenAccentTertiaryGlowColor", WithAlpha(accents.Tertiary.End, 0x92));
        SetGradient("PrimaryBrush", accents.Primary);
        SetBrush("HavenAccentInkBrush", accents.Foreground);
        SetBrush("HavenAccentForegroundBrush", accents.Foreground);
        SetBrush("HavenAccentSoftBrush", accents.SoftSurface);
    }

    private static void SetBrush(string key, Color colour)
    {
        var resources = Avalonia.Application.Current?.Resources;
        if (resources is null) return;
        if (resources[key] is SolidColorBrush existing)
        {
            existing.Color = colour;
            return;
        }

        resources[key] = new SolidColorBrush(colour);
    }

    private static void SetGradient(string key, HavenAccentGradient gradient)
    {
        var resources = Avalonia.Application.Current?.Resources;
        if (resources is null) return;

        if (resources[key] is not LinearGradientBrush brush)
        {
            brush = new LinearGradientBrush
            {
                GradientStops =
                [
                    new GradientStop(gradient.Start, 0d),
                    new GradientStop(gradient.Middle, 0.52d),
                    new GradientStop(gradient.End, 1d)
                ]
            };
            resources[key] = brush;
        }
        else
        {
            while (brush.GradientStops.Count < 3)
                brush.GradientStops.Add(new GradientStop());
            while (brush.GradientStops.Count > 3)
                brush.GradientStops.RemoveAt(brush.GradientStops.Count - 1);

            brush.GradientStops[0].Color = gradient.Start;
            brush.GradientStops[0].Offset = 0d;
            brush.GradientStops[1].Color = gradient.Middle;
            brush.GradientStops[1].Offset = 0.52d;
            brush.GradientStops[2].Color = gradient.End;
            brush.GradientStops[2].Offset = 1d;
        }

        brush.StartPoint = gradient.StartPoint;
        brush.EndPoint = gradient.EndPoint;
    }

    private static void SetColour(string key, Color colour)
    {
        var resources = Avalonia.Application.Current?.Resources;
        if (resources is not null) resources[key] = colour;
    }

    private static HavenAccentGradient Shift(HavenAccentGradient source, Color toward, double amount) =>
        new(
            Blend(source.Start, toward, amount),
            Blend(source.Middle, toward, amount),
            Blend(source.End, toward, amount),
            source.StartPoint,
            source.EndPoint);

    private static Color Blend(Color first, Color second, double secondWeight)
    {
        var weight = Math.Clamp(secondWeight, 0d, 1d);
        return Color.FromArgb(
            (byte)Math.Round(first.A + ((second.A - first.A) * weight)),
            (byte)Math.Round(first.R + ((second.R - first.R) * weight)),
            (byte)Math.Round(first.G + ((second.G - first.G) * weight)),
            (byte)Math.Round(first.B + ((second.B - first.B) * weight)));
    }

    private static Color WithAlpha(Color value, byte alpha) =>
        Color.FromArgb(alpha, value.R, value.G, value.B);

    private static double Luminance(Color colour) =>
        (0.2126d * colour.R) + (0.7152d * colour.G) + (0.0722d * colour.B);
}
