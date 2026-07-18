using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

namespace Haven.Desktop.Controls;

/// <summary>
/// Lightweight animated aurora backdrop. This intentionally avoids BlurEffect: large blurred
/// surfaces are expensive in Skia and made heavy pages hitch while entering the visual tree.
/// </summary>
public sealed class MagicalBackdrop : Grid, IDisposable
{
    private readonly Canvas _bloomCanvas = new() { IsHitTestVisible = false };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly List<AuroraBloom> _blooms = [];
    private bool _reduceMotion;
    private bool _disposed;
    private bool _hasArranged;
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
                new GradientStop(Color.Parse("#050B18"), 0),
                new GradientStop(Color.Parse("#09162D"), 0.34),
                new GradientStop(Color.Parse("#051B1F"), 0.72),
                new GradientStop(Color.Parse("#150D24"), 1)
            ]
        };

        Children.Add(new Border
        {
            IsHitTestVisible = false,
            Opacity = 0.42,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(Color.Parse("#001BE7C8"), 0),
                    new GradientStop(Color.Parse("#2A2D7CFF"), 0.42),
                    new GradientStop(Color.Parse("#1FFF5FA2"), 0.72),
                    new GradientStop(Color.Parse("#0053E56B"), 1)
                ]
            }
        });
        Children.Add(_bloomCanvas);

        AddBloom("#2BE7C8", 440, 0.105, 0.19, 0.16, 0.09, 0.00);
        AddBloom("#2D7CFF", 500, 0.120, 0.15, 0.11, 0.13, 1.10);
        AddBloom("#A45CFF", 400, 0.090, 0.13, 0.14, 0.10, 2.15);
        AddBloom("#FF5FA2", 340, 0.070, 0.10, 0.13, 0.08, 3.05);
        AddBloom("#53E56B", 390, 0.065, 0.12, 0.12, 0.10, 5.05);

        _timer.Tick += OnAnimationTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
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
                ArrangeBlooms(0, snap: true);
            }
            else if (!_disposed)
            {
                ArrangeBlooms(_phase, snap: !_hasArranged);
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
            IsHitTestVisible = false
        };
        _blooms.Add(new AuroraBloom(ellipse, opacity, speed, driftX, driftY, phase));
        _bloomCanvas.Children.Add(ellipse);
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        ArrangeBlooms(_reduceMotion ? 0 : _phase, snap: true);
        if (!_reduceMotion && !_disposed) _timer.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) => _timer.Stop();

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_reduceMotion)
            ArrangeBlooms(0, snap: true);
        else if (!_hasArranged)
            ArrangeBlooms(_phase, snap: true);
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        _phase += 0.05;
        ArrangeBlooms(_phase, snap: false);
    }

    private void ArrangeBlooms(double phase, bool snap)
    {
        var width = Math.Max(Bounds.Width, 1);
        var height = Math.Max(Bounds.Height, 1);
        if (width <= 1 || height <= 1) return;

        for (var index = 0; index < _blooms.Count; index++)
        {
            var bloom = _blooms[index];
            var angle = phase * bloom.Speed + bloom.Phase;
            var anchorX = index switch
            {
                0 => 0.14,
                1 => 0.74,
                2 => 0.43,
                3 => 0.86,
                _ => 0.22
            };
            var anchorY = index switch
            {
                0 => 0.17,
                1 => 0.16,
                2 => 0.72,
                3 => 0.62,
                _ => 0.54
            };

            var targetX = width * (anchorX + Math.Sin(angle) * bloom.DriftX) - bloom.Shape.Width / 2;
            var targetY = height * (anchorY + Math.Cos(angle * 0.82) * bloom.DriftY) - bloom.Shape.Height / 2;

            if (snap || !bloom.HasPosition)
            {
                bloom.X = targetX;
                bloom.Y = targetY;
                bloom.HasPosition = true;
            }
            else
            {
                bloom.X += (targetX - bloom.X) * 0.08;
                bloom.Y += (targetY - bloom.Y) * 0.08;
            }

            Canvas.SetLeft(bloom.Shape, bloom.X);
            Canvas.SetTop(bloom.Shape, bloom.Y);

            bloom.Shape.Opacity = _reduceMotion
                ? bloom.BaseOpacity * 0.78
                : bloom.BaseOpacity * (0.80 + 0.20 * Math.Sin(angle * 1.45 + index));
        }

        _hasArranged = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnAnimationTick;
        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        SizeChanged -= OnSizeChanged;
        GC.SuppressFinalize(this);
    }

    private sealed class AuroraBloom(
        Ellipse shape,
        double baseOpacity,
        double speed,
        double driftX,
        double driftY,
        double phase)
    {
        public Ellipse Shape { get; } = shape;
        public double BaseOpacity { get; } = baseOpacity;
        public double Speed { get; } = speed;
        public double DriftX { get; } = driftX;
        public double DriftY { get; } = driftY;
        public double Phase { get; } = phase;
        public double X { get; set; }
        public double Y { get; set; }
        public bool HasPosition { get; set; }
    }
}
