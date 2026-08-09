using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.HavenUI.Tokens;

/// <summary>
/// Opt-in accent scope for the small set of mockup-authorised cross-App
/// components. It changes only semantic accent resources below this host.
/// </summary>
public sealed class HavenAccentScope : ContentControl
{
    public static readonly StyledProperty<HavenSurface> AccentSurfaceProperty =
        AvaloniaProperty.Register<HavenAccentScope, HavenSurface>(nameof(AccentSurface), HavenSurface.Home);

    public HavenAccentScope()
    {
        Classes.Add("havenAccentScope");
        AttachedToVisualTree += (_, _) =>
        {
            HavenUiResourceApplier.PaletteChanged += OnGlobalPaletteChanged;
            ApplyScope();
        };
        DetachedFromVisualTree += (_, _) => HavenUiResourceApplier.PaletteChanged -= OnGlobalPaletteChanged;
    }

    public HavenSurface AccentSurface
    {
        get => GetValue(AccentSurfaceProperty);
        set => SetValue(AccentSurfaceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AccentSurfaceProperty) ApplyScope();
    }

    private void OnGlobalPaletteChanged(object? sender, EventArgs e) => ApplyScope();

    private void ApplyScope()
    {
        var appearance = Avalonia.Application.Current?.Resources["HavenUiAppearance"] is HavenUiAppearance configured
            ? configured
            : HavenUiAppearance.SuperDark;
        var palette = SurfacePaletteCatalog.For(AccentSurface, appearance).AccentPalette;

        SetGradient("HavenAccentBrush", palette.Primary);
        SetGradient("HavenAccentPrimaryBrush", palette.Primary);
        SetGradient("HavenAccentSecondaryBrush", palette.Secondary);
        SetGradient("HavenAccentTertiaryBrush", palette.Tertiary);
        SetGradient("HavenAccentPrimaryHoverBrush", palette.Primary);
        SetGradient("HavenAccentSecondaryHoverBrush", palette.Secondary);
        SetGradient("HavenAccentTertiaryHoverBrush", palette.Secondary);
        SetGradient("HavenAccentPressedBrush", palette.Tertiary);
        Resources["HavenAccentPrimaryColor"] = palette.Primary.Middle;
        Resources["HavenAccentSecondaryColor"] = palette.Secondary.Middle;
        Resources["HavenAccentTertiaryColor"] = palette.Tertiary.Middle;
        Resources["HavenAccentPrimaryGlowColor"] = WithAlpha(palette.Primary.Start, 0xC8);
        Resources["HavenAccentSecondaryGlowColor"] = WithAlpha(palette.Secondary.Start, 0xA8);
        Resources["HavenAccentTertiaryGlowColor"] = WithAlpha(palette.Tertiary.End, 0x92);
        SetSolid("HavenAccentForegroundBrush", palette.Foreground);
        SetSolid("HavenAccentInkBrush", palette.Foreground);
        SetSolid("HavenAccentSoftBrush", palette.SoftSurface);
    }

    private void SetGradient(string key, HavenAccentGradient gradient)
    {
        if (Resources[key] is not LinearGradientBrush brush)
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
            Resources[key] = brush;
        }

        brush.StartPoint = gradient.StartPoint;
        brush.EndPoint = gradient.EndPoint;
        brush.GradientStops[0].Color = gradient.Start;
        brush.GradientStops[1].Color = gradient.Middle;
        brush.GradientStops[2].Color = gradient.End;
    }

    private void SetSolid(string key, Color colour)
    {
        if (Resources[key] is SolidColorBrush brush)
            brush.Color = colour;
        else
            Resources[key] = new SolidColorBrush(colour);
    }

    private static Color WithAlpha(Color colour, byte alpha) =>
        Color.FromArgb(alpha, colour.R, colour.G, colour.B);
}
