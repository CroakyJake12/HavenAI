using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

namespace Haven.Desktop.Controls;

/// <summary>
/// A compositor-friendly aurora backdrop made from a dark gradient and a small number of
/// softly blurred colour blooms. It deliberately animates at 25fps and stops completely when
/// reduced motion is enabled, keeping the effect calm and inexpensive on laptops.
/// </summary>
public sealed class MagicalBackdrop : Grid, IDisposable
{
    private readonly Canvas _bloomCanvas = new() { IsHitTestVisible = false };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly List<AuroraBloom> _blooms = [];
    private bool _reduceMotion;
    private bool _disposed;
    private double _phase;

    public MagicalBackdrop()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#07101F"), 0),
                new GradientStop(Color.Parse("#0A1329"), 0.32),
                new GradientStop(Color.Parse("#071A1B"), 0.7),
                new GradientStop(Color.Parse("#120D20"), 1)
            ]
        };

        Children.Add(new Border
        {
            IsHitTestVisible = false,
            Opacity = 0.38,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(Color.Parse("#001BE7C8"), 0),
                    new GradientStop(Color.Parse("#332D7CFF"), 0.45),
                    new GradientStop(Color.Parse("#00FF77C8"), 1)
                ]
            }
        });
        Children.Add(_bloomCanvas);

        AddBloom("#2BE7C8", 540, 0.17, 0.35, 0.19, 0.11, 0.00);
        AddBloom("#2D7CFF", 620, 0.20, 0.28, 0.13, 0.16, 1.10);
        AddBloom("#A45CFF", 510, 0.13, 0.24, 0.17, 0.12, 2.15);
        AddBloom("#FF5FA2", 430, 0.10, 0.20, 0.16, 0.10, 3.05);
        AddBloom("#FFCB5C", 360, 0.075, 0.17, 0.11, 0.09, 4.20);
        AddBloom("#53E56B", 470, 0.095, 0.22, 0.15, 0.13, 5.05);

        _timer.Tick += OnAnimationTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        ArrangeBlooms(0);
    }

    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (_reduceMotion == value) return;
            _reduceMotion = value;
            if (value)
            {
                _timer.Stop();
                ArrangeBlooms(0);
            }
            else if (!_disposed)
            {
                _timer.Start();
            }
        }
    }

    private void AddBloom(string colour, double size, double opacity, double speed, double driftX, double driftY, double phase)
    {
        var ellipse = new Ellipse
        {
            Width = size,
            Height = size,
            Opacity = opacity,
            Fill = new SolidColorBrush(Color.Parse(colour)),
            Effect = new BlurEffect { Radius = 86 },
            IsHitTestVisible = false
        };
        _blooms.Add(new AuroraBloom(ellipse, opacity, speed, driftX, driftY, phase));
        _bloomCanvas.Children.Add(ellipse);
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (!_reduceMotion && !_disposed) _timer.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) => _timer.Stop();

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        _phase += 0.04;
        ArrangeBlooms(_phase);
    }

    private void ArrangeBlooms(double phase)
    {
        var width = Math.Max(Bounds.Width, 1100);
        var height = Math.Max(Bounds.Height, 720);

        for (var index = 0; index < _blooms.Count; index++)
        {
            var bloom = _blooms[index];
            var angle = phase * bloom.Speed + bloom.Phase;
            var anchorX = index switch
            {
                0 => 0.12,
                1 => 0.72,
                2 => 0.42,
                3 => 0.86,
                4 => 0.57,
                _ => 0.22
            };
            var anchorY = index switch
            {
                0 => 0.16,
                1 => 0.14,
                2 => 0.70,
                3 => 0.62,
                4 => 0.86,
                _ => 0.54
            };

            var x = width * (anchorX + Math.Sin(angle) * bloom.DriftX) - bloom.Shape.Width / 2;
            var y = height * (anchorY + Math.Cos(angle * 0.82) * bloom.DriftY) - bloom.Shape.Height / 2;
            Canvas.SetLeft(bloom.Shape, x);
            Canvas.SetTop(bloom.Shape, y);

            if (!_reduceMotion)
                bloom.Shape.Opacity = bloom.BaseOpacity * (0.82 + 0.18 * Math.Sin(angle * 1.7 + index));
            else
                bloom.Shape.Opacity = bloom.BaseOpacity * 0.86;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnAnimationTick;
        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        GC.SuppressFinalize(this);
    }

    private sealed record AuroraBloom(
        Ellipse Shape,
        double BaseOpacity,
        double Speed,
        double DriftX,
        double DriftY,
        double Phase);
}
