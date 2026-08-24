using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.HavenUI.Tokens;

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

    private Color _targetColor1;
    private Color _targetColor2;
    private Color _targetColor3;
    private Color _targetColor4;

    private Color _currentColor1;
    private Color _currentColor2;
    private Color _currentColor3;
    private Color _currentColor4;
    private HavenSurface _surface = HavenSurface.Home;
    private HavenUiAppearance _appearance;

    private double _tidalPhase = 0;
    private const double TidalSpeed = 0.002; // About one slow rise-and-fall every 52 seconds.
    private const double ColorTransitionSpeed = 0.10; // Responsive without snapping when the active App changes.

    public TidalBackground(Window window, HavenUiAppearance appearance = HavenUiAppearance.SuperDark)
    {
        _window = window;
        _appearance = appearance;
        _window.ActualThemeVariantChanged += OnActualThemeVariantChanged;

        // The first frame must already use the requested Haven appearance. The
        // old white defaults visibly faded through a light theme on every launch.
        var initialPalette = SurfacePaletteCatalog.For(_surface, _appearance);
        _targetColor1 = initialPalette.TideBase;
        _targetColor2 = initialPalette.TideBase;
        _targetColor3 = initialPalette.TideColour;
        _targetColor4 = initialPalette.TideColour;

        // Create gradient stops
        _stop1 = new GradientStop(_targetColor1, 0);
        _stop2 = new GradientStop(_targetColor2, 0.24);
        _stop3 = new GradientStop(_targetColor3, 0.86);
        _stop4 = new GradientStop(_targetColor4, 1);

        // Initialize current colors
        _currentColor1 = _stop1.Color;
        _currentColor2 = _stop2.Color;
        _currentColor3 = _stop3.Color;
        _currentColor4 = _stop4.Color;

        // Create the gradient brush
        _brush = new LinearGradientBrush
        {
            // The supplied desktop compositions move from near-black at the
            // navigation edge into the active App colour across the workspace.
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = new GradientStops { _stop1, _stop2, _stop3, _stop4 }
        };

        _window.Background = _brush;
        HavenUiResourceApplier.Apply(initialPalette);

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
        ApplySurfacePalette();
    }

    /// <summary>Updates colours without changing the active App/surface.</summary>
    public void SetAppearance(HavenUiAppearance appearance)
    {
        _appearance = appearance;
        ApplySurfacePalette();
    }

    private void ApplySurfacePalette()
    {
        var palette = SurfacePaletteCatalog.For(_surface, _appearance);
        _targetColor1 = palette.TideBase;
        _targetColor2 = palette.TideBase;
        _targetColor3 = palette.TideColour;
        _targetColor4 = palette.TideColour;
        HavenUiResourceApplier.Apply(palette);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e) => ApplySurfacePalette();

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
        var tidalOffset = Math.Sin(_tidalPhase) * 0.055;

        // Apply colors with tidal offset to gradient stops
        _stop1.Color = _currentColor1;
        _stop2.Color = _currentColor2;
        _stop3.Color = _currentColor3;
        _stop4.Color = _currentColor4;

        _stop1.Offset = 0;
        _stop2.Offset = Math.Clamp(0.24 + tidalOffset, 0, 1);
        _stop3.Offset = Math.Clamp(0.86 + tidalOffset, 0, 1);
        _stop4.Offset = 1;
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
