using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
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
    private Color _targetColor2 = Color.Parse("#FFF8FFF9");
    private Color _targetColor3 = Color.Parse("#FFCBFFD3");
    private Color _targetColor4 = Color.Parse("#FFBEFAF8");

    private Color _currentColor1;
    private Color _currentColor2;
    private Color _currentColor3;
    private Color _currentColor4;

    private double _tidalPhase = 0;
    private const double TidalSpeed = 0.0008; // Controls speed of tidal movement
    private const double ColorTransitionSpeed = 0.02; // Controls how fast colors change between modes

    public TidalBackground(Window window)
    {
        _window = window;

        // Create gradient stops
        _stop1 = new GradientStop(Color.Parse("#FFFFFFFF"), 0);
        _stop2 = new GradientStop(Color.Parse("#FFF8FFF9"), 0.33);
        _stop3 = new GradientStop(Color.Parse("#FFCBFFD3"), 0.66);
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
    public void SetMode(HavenMode mode)
    {
        var (c1, c2, c3, c4) = GetModeColors(mode);
        _targetColor1 = c1;
        _targetColor2 = c2;
        _targetColor3 = c3;
        _targetColor4 = c4;
    }

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

        // Create tidal offset effect - colors shift up and down gently
        var tidalOffset = Math.Sin(_tidalPhase) * 0.08; // Subtle 8% shift

        // Apply colors with tidal offset to gradient stops
        _stop1.Color = _currentColor1;
        _stop2.Color = _currentColor2;
        _stop3.Color = _currentColor3;
        _stop4.Color = _currentColor4;

        // Shift gradient stops slightly for tidal effect
        _stop1.Offset = Math.Clamp(0 + tidalOffset, 0, 1);
        _stop2.Offset = Math.Clamp(0.33 + tidalOffset * 0.5, 0, 1);
        _stop3.Offset = Math.Clamp(0.66 - tidalOffset * 0.5, 0, 1);
        _stop4.Offset = Math.Clamp(1 - tidalOffset, 0, 1);

        // Slowly rotate the gradient angle for more organic movement
        var angle = Math.Sin(_tidalPhase * 0.3) * 15; // ±15 degree rotation
        var radians = angle * Math.PI / 180;
        _brush.StartPoint = new RelativePoint(0.5 + Math.Sin(radians) * 0.1, 0, RelativeUnit.Relative);
        _brush.EndPoint = new RelativePoint(0.5 - Math.Sin(radians) * 0.1, 1, RelativeUnit.Relative);
    }

    private static Color LerpColor(Color from, Color to, double t)
    {
        return Color.FromArgb(
            (byte)(from.A + (to.A - from.A) * t),
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t));
    }

    private static (Color, Color, Color, Color) GetModeColors(HavenMode mode) => mode switch
    {
        // Chat: Green/Blue - mint to cyan
        HavenMode.Chat => (
            Color.Parse("#FFFFFFFF"),  // White
            Color.Parse("#FFF0FFF4"),  // Very light mint
            Color.Parse("#FFD4F5E9"),  // Light mint
            Color.Parse("#FFC8F0E8")   // Mint/cyan
        ),

        // Study/Teach: Blue/Purple - calming study colors
        HavenMode.Teach => (
            Color.Parse("#FFFFFFFF"),  // White
            Color.Parse("#FFF0F4FF"),  // Very light blue
            Color.Parse("#FFDCE8FF"),  // Light periwinkle
            Color.Parse("#FFD5DCF8")   // Soft lavender
        ),

        // Go: Green - fresh and energetic
        HavenMode.Go => (
            Color.Parse("#FFFFFFFF"),  // White
            Color.Parse("#FFF1F8E9"),  // Very light green
            Color.Parse("#FFDCEDC8"),  // Light green
            Color.Parse("#FFC8E6C9")   // Soft green
        ),

        // Studio: Pink/Rose - creative colors
        HavenMode.Studio => (
            Color.Parse("#FFFFFFFF"),  // White
            Color.Parse("#FFFFF0F5"),  // Very light pink
            Color.Parse("#FFFCE4EC"),  // Light rose
            Color.Parse("#FFF8D7E8")   // Soft pink
        ),

        // Default (Plan, Browse, etc.): Neutral
        _ => (
            Color.Parse("#FFFFFFFF"),  // White
            Color.Parse("#FFF8FFF9"),  // Very light mint
            Color.Parse("#FFCBFFD3"),  // Light green
            Color.Parse("#FFBEFAF8")   // Mint/cyan
        )
    };

    public void Dispose()
    {
        _animationTimer.Stop();
        _animationTimer.Tick -= OnAnimationTick;
    }
}
