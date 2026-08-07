using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Haven.Core;

namespace Haven.Desktop.Controls;

/// <summary>
/// Manages the window background gradient that shifts colors based on the current mode.
/// Creates a gentle tidal animation effect where colors slowly move up and down.
/// </summary>
public sealed class TidalBackground : IDisposable
{
    private readonly Window _window;
    private readonly LinearGradientBrush _brush;
    private readonly DispatcherTimer _animationTimer;
    private readonly GradientStop _stop1;
    private readonly GradientStop _stop2;
    private readonly GradientStop _stop3;
    private readonly GradientStop _stop4;

    private Color _targetColor1 = Color.Parse("#FFFFFFFF");
    private Color _targetColor2 = Color.Parse("#FFFFFFFF");
    private Color _targetColor3 = Color.Parse("#FFBEFAF8");
    private Color _targetColor4 = Color.Parse("#FFBEFAF8");

    private Color _currentColor1;
    private Color _currentColor2;
    private Color _currentColor3;
    private Color _currentColor4;
    private HavenSurface _surface = HavenSurface.Home;

    private double _tidalPhase = 0;
    private const double TidalSpeed = 0.002; // About one slow rise-and-fall every 52 seconds.
    private const double ColorTransitionSpeed = 0.02; // Controls how fast colors change between modes

    public TidalBackground(Window window)
    {
        _window = window;
        _window.ActualThemeVariantChanged += OnActualThemeVariantChanged;

        // Create gradient stops
        _stop1 = new GradientStop(Color.Parse("#FFFFFFFF"), 0);
        _stop2 = new GradientStop(Color.Parse("#FFFFFFFF"), 0.46);
        _stop3 = new GradientStop(Color.Parse("#FFBEFAF8"), 0.72);
        _stop4 = new GradientStop(Color.Parse("#FFBEFAF8"), 1);

        // Initialize current colors
        _currentColor1 = _stop1.Color;
        _currentColor2 = _stop2.Color;
        _currentColor3 = _stop3.Color;
        _currentColor4 = _stop4.Color;

        // Create the gradient brush
        _brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops { _stop1, _stop2, _stop3, _stop4 }
        };

        _window.Background = _brush;

        // Setup animation timer (~60fps)
        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
    }

    /// <summary>
    /// Sets the target colors for the given mode. The background will smoothly transition.
    /// </summary>
    public void SetSurface(HavenSurface surface)
    {
        _surface = surface;
        var palette = SurfacePaletteCatalog.For(surface, _window.ActualThemeVariant);
        _targetColor1 = palette.TideBase;
        _targetColor2 = palette.TideBase;
        _targetColor3 = palette.TideColour;
        _targetColor4 = palette.TideColour;
        ApplyPaletteResources(palette);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e) => SetSurface(_surface);

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        // Smoothly interpolate current colors toward target colors
        _currentColor1 = LerpColor(_currentColor1, _targetColor1, ColorTransitionSpeed);
        _currentColor2 = LerpColor(_currentColor2, _targetColor2, ColorTransitionSpeed);
        _currentColor3 = LerpColor(_currentColor3, _targetColor3, ColorTransitionSpeed);
        _currentColor4 = LerpColor(_currentColor4, _targetColor4, ColorTransitionSpeed);

        // Update tidal phase for gentle oscillation
        _tidalPhase += TidalSpeed;
        if (_tidalPhase > Math.PI * 2) _tidalPhase -= Math.PI * 2;

        // Move the white-to-colour boundary as one body so the background is
        // always dual-tone instead of becoming a multi-colour gradient.
        var tidalOffset = Math.Sin(_tidalPhase) * 0.12;

        // Apply colors with tidal offset to gradient stops
        _stop1.Color = _currentColor1;
        _stop2.Color = _currentColor2;
        _stop3.Color = _currentColor3;
        _stop4.Color = _currentColor4;

        _stop1.Offset = 0;
        _stop2.Offset = Math.Clamp(0.46 + tidalOffset, 0, 1);
        _stop3.Offset = Math.Clamp(0.72 + tidalOffset, 0, 1);
        _stop4.Offset = 1;
    }

    private static void ApplyPaletteResources(SurfacePaletteCatalog.Palette palette)
    {
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
        SetBrush("HavenAccentBrush", palette.Accent);
        SetBrush("HavenAccentSecondaryBrush", palette.AccentSecondary);
        SetBrush("HavenAccentInkBrush", palette.AccentInk);
        SetBrush("HavenAccentSoftBrush", palette.AccentSoft);
        SetBrush("HavenBlueSoftBrush", palette.AccentSoft);
        SetBrush("HavenNubBrush", palette.AccentSecondary);
        SetBrush("HavenLineBrush", palette.Line);
        SetBrush("HavenLineStrongBrush", palette.LineStrong);
        SetBrush("HavenButtonBrush", palette.Button);
        SetBrush("HavenButtonHoverBrush", palette.ButtonHover);
        SetBrush("HavenButtonPressedBrush", palette.ButtonPressed);
        SetBrush("HavenFocusBrush", palette.Focus);
        SetBrush("HavenAccentBorderBrush", palette.AccentBorder);
        SetBrush("HavenAttentionBrush", palette.Attention);
        SetBrush("HavenAttentionBorderBrush", palette.AttentionBorder);
    }

    private static void SetBrush(string key, Color color)
    {
        var resources = Avalonia.Application.Current?.Resources;
        if (resources is null) return;
        if (resources[key] is SolidColorBrush existing)
        {
            existing.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static Color LerpColor(Color from, Color to, double t)
    {
        return Color.FromArgb(
            (byte)(from.A + (to.A - from.A) * t),
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t));
    }

    public void Dispose()
    {
        _window.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        _animationTimer.Stop();
        _animationTimer.Tick -= OnAnimationTick;
    }
}
