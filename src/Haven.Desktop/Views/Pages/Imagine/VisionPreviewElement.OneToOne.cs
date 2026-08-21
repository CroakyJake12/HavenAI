namespace Haven.Desktop.Views.Pages.Imagine;

internal sealed partial class VisionPreviewElement
{
    private const double MinimumSafeZoom = .01;
    private const double MaximumSafeZoom = 4096;

    internal bool OneToOne(double renderScaling)
    {
        if (!VisionImageMetadata.TryRead(_source, out var imageInfo)) return false;
        var viewportWidth = Bounds.Width - 24;
        var viewportHeight = Bounds.Height - 24;
        var targetZoom = CalculateOneToOneZoom(viewportWidth, viewportHeight, imageInfo.Width, imageInfo.Height, renderScaling);
        if (!double.IsFinite(targetZoom) || targetZoom < MinimumSafeZoom || targetZoom > MaximumSafeZoom) return false;

        _zoom = targetZoom;
        _pan = new Haven.UI.HavenPoint(0, 0);
        _dragging = false;
        ViewChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
        return true;
    }

    internal static double CalculateOneToOneZoom(double viewportWidth, double viewportHeight, int sourceWidth, int sourceHeight, double renderScaling)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || sourceWidth <= 0 || sourceHeight <= 0 || !double.IsFinite(renderScaling) || renderScaling <= 0)
            return double.NaN;
        var fitScale = Math.Min(viewportWidth / sourceWidth, viewportHeight / sourceHeight);
        return 1d / (fitScale * renderScaling);
    }
}
