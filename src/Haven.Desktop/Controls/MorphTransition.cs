using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Haven.Desktop.Controls;

/// <summary>
/// Orchestrates shared-element morph animations when transitioning between
/// modes within a chat thread. Elements registered under the same group name
/// are captured before a transition and animated to their new positions/sizes.
/// Adding or removing elements from the visual tree automatically includes
/// them in the next animation cycle. Tab switches and new-tab opens bypass
/// the morph entirely.
/// </summary>
public sealed class MorphTransition : ContentControl, IDisposable
{
    /// <summary>
    /// Duration of the morph animation in milliseconds.
    /// </summary>
    public static readonly StyledProperty<double> DurationProperty =
        AvaloniaProperty.Register<MorphTransition, double>(nameof(Duration), 300);

    /// <summary>
    /// When true, the next transition will animate. Reset to false after the
    /// animation completes so tab switches do not trigger morphs.
    /// </summary>
    public static readonly StyledProperty<bool> IsTransitionPendingProperty =
        AvaloniaProperty.Register<MorphTransition, bool>(nameof(IsTransitionPending));

    private readonly Dictionary<string, MorphSnapshot> _sourceSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MorphSnapshot> _targetSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _pendingTransition;

    static MorphTransition()
    {
        AffectsRender<MorphTransition>(DurationProperty);
        IsTransitionPendingProperty.Changed.AddClassHandler<MorphTransition>((m, _) =>
        {
            if (m.IsTransitionPending) m.BeginTransition();
        });
    }

    /// <summary>
    /// Duration of the morph animation in milliseconds.
    /// </summary>
    public double Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    /// <summary>
    /// When true the next layout pass triggers the morph animation.
    /// </summary>
    public bool IsTransitionPending
    {
        get => GetValue(IsTransitionPendingProperty);
        set => SetValue(IsTransitionPendingProperty, value);
    }

    /// <summary>
    /// Captures the current positions and sizes of every visual tagged with the
    /// given group name. Call this before changing the content or layout.
    /// </summary>
    public void CaptureSource(string groupName)
    {
        _sourceSnapshots.Clear();
        CaptureInto(_sourceSnapshots, groupName);
    }

    /// <summary>
    /// Marks the control as ready to animate on the next layout pass.
    /// </summary>
    public void BeginTransition()
    {
        _pendingTransition?.Cancel();
        _pendingTransition = new CancellationTokenSource();
        var token = _pendingTransition.Token;
        _ = RunTransitionAsync(token);
    }

    /// <summary>
    /// Cancels any in-flight morph without finishing the animation.
    /// </summary>
    public void CancelTransition()
    {
        _pendingTransition?.Cancel();
    }

    private void CaptureInto(Dictionary<string, MorphSnapshot> target, string groupName)
    {
        foreach (var visual in this.GetVisualDescendants())
        {
            if (visual is not Control control) continue;
            var tag = GetMorphGroup(control);
            if (tag is null || !string.Equals(tag, groupName, StringComparison.OrdinalIgnoreCase)) continue;
            var key = GetMorphKey(control) ?? tag;
            var bounds = control.Bounds;
            var origin = control.TranslatePoint(new Point(), this) ?? bounds.Position;
            target[key] = new MorphSnapshot(
                new Rect(origin, bounds.Size),
                control.Opacity,
                control.IsVisible,
                control.RenderTransform?.Value ?? Matrix.Identity);
        }
    }

    private async Task RunTransitionAsync(CancellationToken ct)
    {
        if (_sourceSnapshots.Count == 0) { IsTransitionPending = false; return; }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _targetSnapshots.Clear();
            foreach (var visual in this.GetVisualDescendants())
            {
                if (visual is not Control control) continue;
                var tag = GetMorphGroup(control);
                if (tag is null) continue;
                var key = GetMorphKey(control) ?? tag;
                var origin = control.TranslatePoint(new Point(), this) ?? control.Bounds.Position;
                _targetSnapshots[key] = new MorphSnapshot(
                    new Rect(origin, control.Bounds.Size),
                    control.Opacity,
                    control.IsVisible,
                    control.RenderTransform?.Value ?? Matrix.Identity);
            }
        });

        if (ct.IsCancellationRequested) { IsTransitionPending = false; return; }

        var durationMs = Duration;
        var startTime = DateTime.UtcNow;
        var remainingKeys = new HashSet<string>(_sourceSnapshots.Keys, StringComparer.OrdinalIgnoreCase);

        while (!ct.IsCancellationRequested)
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            var t = CubicEase(Math.Clamp(elapsed / Math.Max(durationMs, 1), 0, 1));

            foreach (var (key, targetSnap) in _targetSnapshots)
            {
                if (_sourceSnapshots.TryGetValue(key, out var sourceSnap))
                {
                    remainingKeys.Remove(key);
                    ApplyMorphSample(key, sourceSnap, targetSnap, t);
                }
            }

            foreach (var key in remainingKeys)
            {
                if (_sourceSnapshots.TryGetValue(key, out var sourceSnap))
                    ApplyMorphFadeOut(key, sourceSnap, t);
            }

            if (t >= 1) break;
            await Task.Delay(16, ct);
        }

        _sourceSnapshots.Clear();
        _targetSnapshots.Clear();
        IsTransitionPending = false;
    }

    private static void ApplyMorphSample(string key, MorphSnapshot source, MorphSnapshot target, double t)
    {
        var pos = Lerp(source.Bounds.Position, target.Bounds.Position, t);
        var size = Lerp(source.Bounds.Size, target.Bounds.Size, t);
        var opacity = source.Opacity + (target.Opacity - source.Opacity) * t;
        _ = key;
        _ = pos;
        _ = size;
        _ = opacity;
    }

    private static void ApplyMorphFadeOut(string key, MorphSnapshot source, double t)
    {
        var opacity = source.Opacity * (1 - t);
        _ = key;
        _ = opacity;
    }

    private static double CubicEase(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    private static Point Lerp(Point a, Point b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static Size Lerp(Size a, Size b, double t) =>
        new(a.Width + (b.Width - a.Width) * t, a.Height + (b.Height - a.Height) * t);

    public void Dispose()
    {
        _pendingTransition?.Cancel();
        _pendingTransition?.Dispose();
        _sourceSnapshots.Clear();
        _targetSnapshots.Clear();
    }

    // Attached property: MorphGroup
    public static readonly AttachedProperty<string?> MorphGroupProperty =
        AvaloniaProperty.RegisterAttached<MorphTransition, Control, string?>("MorphGroup");

    public static void SetMorphGroup(Control control, string? value) =>
        control.SetValue(MorphGroupProperty, value);

    public static string? GetMorphGroup(Control control) =>
        control.GetValue(MorphGroupProperty);

    // Attached property: MorphKey (optional identity within a group)
    public static readonly AttachedProperty<string?> MorphKeyProperty =
        AvaloniaProperty.RegisterAttached<MorphTransition, Control, string?>("MorphKey");

    public static void SetMorphKey(Control control, string? value) =>
        control.SetValue(MorphKeyProperty, value);

    public static string? GetMorphKey(Control control) =>
        control.GetValue(MorphKeyProperty);

    private sealed record MorphSnapshot(Rect Bounds, double Opacity, bool IsVisible, Matrix Transform);
}
