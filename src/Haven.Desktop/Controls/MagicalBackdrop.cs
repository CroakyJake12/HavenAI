/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/MagicalBackdrop.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns MagicalBackdrop, AuroraBloom. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Desktop.Services;

namespace Haven.Desktop.Controls;

/// <summary>
/// Lightweight animated aurora backdrop. This intentionally avoids BlurEffect: large blurred
/// surfaces are expensive in Skia and made heavy pages hitch while entering the visual tree.
/// </summary>
public sealed class MagicalBackdrop : Grid, IDisposable
{
    /// <summary>
    /// Stores bloom canvas locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Canvas _bloomCanvas = new() { IsHitTestVisible = false };
    /// <summary>
    /// Stores timer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    /// <summary>
    /// Stores blooms locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly List<AuroraBloom> _blooms = [];
    /// <summary>
    /// Stores reduce motion locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _reduceMotion;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;
    /// <summary>
    /// Stores has arranged locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _hasArranged;
    /// <summary>
    /// Stores phase locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private double _phase;

    public MagicalBackdrop()
    {
        MagicalPalette.Apply();

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

    /// <summary>
    /// Performs the add bloom step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Handles the attached to visual tree event raised by the UI or runtime.
    /// </summary>
    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        ArrangeBlooms(_reduceMotion ? 0 : _phase, snap: true);
        if (!_reduceMotion && !_disposed) _timer.Start();
    }

    /// <summary>
    /// Handles the detached from visual tree event raised by the UI or runtime.
    /// </summary>
    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) => _timer.Stop();

    /// <summary>
    /// Handles the size changed event raised by the UI or runtime.
    /// </summary>
    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_reduceMotion)
            ArrangeBlooms(0, snap: true);
        else if (!_hasArranged)
            ArrangeBlooms(_phase, snap: true);
    }

    /// <summary>
    /// Handles the animation tick event raised by the UI or runtime.
    /// </summary>
    private void OnAnimationTick(object? sender, EventArgs e)
    {
        _phase += 0.05;
        ArrangeBlooms(_phase, snap: false);
    }

    /// <summary>
    /// Performs the arrange blooms step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Represents aurora bloom and keeps its related state and behavior together.
    /// </summary>
    private sealed class AuroraBloom(
        Ellipse shape,
        double baseOpacity,
        double speed,
        double driftX,
        double driftY,
        double phase)
    {
        /// <summary>
        /// Gets or updates shape, the bindable or domain state represented by this property.
        /// </summary>
        public Ellipse Shape { get; } = shape;
        /// <summary>
        /// Gets or updates base opacity, the bindable or domain state represented by this property.
        /// </summary>
        public double BaseOpacity { get; } = baseOpacity;
        /// <summary>
        /// Gets or updates speed, the bindable or domain state represented by this property.
        /// </summary>
        public double Speed { get; } = speed;
        /// <summary>
        /// Gets or updates drift x, the bindable or domain state represented by this property.
        /// </summary>
        public double DriftX { get; } = driftX;
        /// <summary>
        /// Gets or updates drift y, the bindable or domain state represented by this property.
        /// </summary>
        public double DriftY { get; } = driftY;
        /// <summary>
        /// Gets or updates phase, the bindable or domain state represented by this property.
        /// </summary>
        public double Phase { get; } = phase;
        /// <summary>
        /// Gets or updates x, the bindable or domain state represented by this property.
        /// </summary>
        public double X { get; set; }
        /// <summary>
        /// Gets or updates y, the bindable or domain state represented by this property.
        /// </summary>
        public double Y { get; set; }
        /// <summary>
        /// Reports whether has position is true for the current state.
        /// </summary>
        public bool HasPosition { get; set; }
    }
}
