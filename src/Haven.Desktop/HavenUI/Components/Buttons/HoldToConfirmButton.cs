using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Desktop.HavenUI.Tokens;

namespace Haven.Desktop.HavenUI.Components.Buttons;

/// <summary>
/// Canonical destructive HavenUI button. Pointer, touch and keyboard users must
/// hold continuously for the configured duration; an interrupted hold visibly
/// unwinds for the same amount of time accumulated.
/// </summary>
public sealed class HoldToConfirmButton : Button
{
    public static readonly StyledProperty<TimeSpan> HoldDurationProperty =
        AvaloniaProperty.Register<HoldToConfirmButton, TimeSpan>(
            nameof(HoldDuration),
            HavenUiMotion.HoldToConfirm);

    public static readonly StyledProperty<string> ActionLabelProperty =
        AvaloniaProperty.Register<HoldToConfirmButton, string>(
            nameof(ActionLabel),
            "delete");

    public static readonly DirectProperty<HoldToConfirmButton, double> HoldProgressProperty =
        AvaloniaProperty.RegisterDirect<HoldToConfirmButton, double>(
            nameof(HoldProgress),
            button => button.HoldProgress);

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _phaseClock = new();
    private object? _originalContent;
    private double _holdProgress;
    private double _windDownStartProgress;
    private TimeSpan _windDownDuration;
    private bool _holding;
    private bool _windingDown;
    private bool _allowInvocation;

    public HoldToConfirmButton()
    {
        Classes.Add("danger");
        Classes.Add("destructive");
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTimerTick;
        DetachedFromVisualTree += (_, _) => ResetVisuals();
    }

    public TimeSpan HoldDuration
    {
        get => GetValue(HoldDurationProperty);
        set => SetValue(HoldDurationProperty, value);
    }

    public string ActionLabel
    {
        get => GetValue(ActionLabelProperty);
        set => SetValue(ActionLabelProperty, value);
    }

    public double HoldProgress
    {
        get => _holdProgress;
        private set => SetAndRaise(HoldProgressProperty, ref _holdProgress, Math.Clamp(value, 0d, 1d));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && IsEffectivelyEnabled)
        {
            e.Handled = true;
            e.Pointer.Capture(this);
            BeginHold();
            return;
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_holding)
        {
            e.Handled = true;
            e.Pointer.Capture(null);
            BeginWindDown();
            return;
        }

        base.OnPointerReleased(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        if (_holding) BeginWindDown();
        base.OnPointerExited(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_holding) BeginWindDown();
        base.OnPointerCaptureLost(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            e.Handled = true;
            if (!_holding) BeginHold();
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter)
        {
            e.Handled = true;
            if (_holding) BeginWindDown();
            return;
        }

        base.OnKeyUp(e);
    }

    protected override void OnClick()
    {
        if (!_allowInvocation) return;
        _allowInvocation = false;
        base.OnClick();
    }

    internal void BeginHold()
    {
        if (HoldDuration <= TimeSpan.Zero)
            throw new InvalidOperationException("A destructive hold duration must be positive.");

        _originalContent ??= Content;
        _windingDown = false;
        _holding = true;
        HoldProgress = 0;
        _phaseClock.Restart();
        _timer.Start();
        Classes.Set("holding", true);
        UpdateVisuals();
    }

    internal void BeginWindDown()
    {
        _holding = false;
        if (HoldProgress <= 0)
        {
            ResetVisuals();
            return;
        }

        _windingDown = true;
        _windDownStartProgress = HoldProgress;
        _windDownDuration = TimeSpan.FromTicks((long)(HoldDuration.Ticks * HoldProgress));
        _phaseClock.Restart();
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!IsEffectivelyEnabled)
        {
            ResetVisuals();
            return;
        }

        if (_holding)
        {
            HoldProgress = _phaseClock.Elapsed.TotalMilliseconds / HoldDuration.TotalMilliseconds;
            UpdateVisuals();
            if (HoldProgress < 1) return;

            _holding = false;
            _timer.Stop();
            _allowInvocation = true;
            ResetVisuals();
            OnClick();
            return;
        }

        if (!_windingDown)
        {
            _timer.Stop();
            return;
        }

        var elapsed = _windDownDuration <= TimeSpan.Zero
            ? 1d
            : _phaseClock.Elapsed.TotalMilliseconds / _windDownDuration.TotalMilliseconds;
        HoldProgress = _windDownStartProgress * (1d - Math.Clamp(elapsed, 0d, 1d));
        UpdateVisuals();
        if (elapsed >= 1d) ResetVisuals();
    }

    private void UpdateVisuals()
    {
        var remaining = Math.Max(0d, HoldDuration.TotalSeconds * (1d - HoldProgress));
        Content = $"Hold to {ActionLabel} · {remaining:0.0}s";
        Opacity = 0.90d + (0.10d * HoldProgress);
        RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        RenderTransform = new ScaleTransform(1d + (0.025d * HoldProgress), 1d + (0.025d * HoldProgress));

        var baseColour = Color.Parse("#FFD32F2F");
        var bright = Color.Parse("#FFFF6868");
        Background = new SolidColorBrush(Lerp(baseColour, bright, HoldProgress));
    }

    private void ResetVisuals()
    {
        _timer.Stop();
        _phaseClock.Reset();
        _holding = false;
        _windingDown = false;
        HoldProgress = 0;
        if (_originalContent is not null) Content = _originalContent;
        ClearValue(OpacityProperty);
        ClearValue(RenderTransformProperty);
        ClearValue(BackgroundProperty);
        Classes.Set("holding", false);
    }

    private static Color Lerp(Color from, Color to, double amount)
    {
        var value = Math.Clamp(amount, 0d, 1d);
        return Color.FromArgb(
            (byte)Math.Round(from.A + ((to.A - from.A) * value)),
            (byte)Math.Round(from.R + ((to.R - from.R) * value)),
            (byte)Math.Round(from.G + ((to.G - from.G) * value)),
            (byte)Math.Round(from.B + ((to.B - from.B) * value)));
    }
}
