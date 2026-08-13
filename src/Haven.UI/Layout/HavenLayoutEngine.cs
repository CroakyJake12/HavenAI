using Haven.UI.Components;

namespace Haven.UI;

public interface IHavenMeasureContext
{
    HavenSize MeasureLeaf(HavenElement element, HavenSize available);
}

/// <summary>Haven-owned normal measure/arrange engine with no Avalonia types.</summary>
public sealed class HavenLayoutEngine
{
    private HavenSize _viewport;
    private HavenRenderContext _context = new(HavenPlatform.Unknown, HavenSize.Zero);
    private IHavenMeasureContext _measure = null!;

    public void Layout(HavenElement root, HavenSize viewport, HavenPlatform platform, IHavenMeasureContext measure)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(measure);
        _viewport = viewport;
        _context = new HavenRenderContext(platform, viewport);
        _measure = measure;
        Measure(root, viewport);
        Arrange(root, new HavenRect(0, 0, root.DesiredSize.Width, root.DesiredSize.Height));
    }

    private HavenSize Measure(HavenElement element, HavenSize available)
    {
        element.IsIncluded = element.MatchesConditions(_context)
            && element.GetValue(HavenProperties.Visibility) != HavenVisibility.Collapsed;
        if (!element.IsIncluded)
        {
            element.DesiredSize = HavenSize.Zero;
            return HavenSize.Zero;
        }

        var desired = element is Container container
            ? MeasureContainer(container, available)
            : _measure.MeasureLeaf(element, available);
        desired = ApplySize(element, desired, available);
        element.DesiredSize = desired;
        return desired;
    }

    private HavenSize MeasureContainer(Container container, HavenSize available)
    {
        var padding = ResolveThickness(container.GetValue(HavenProperties.Padding), available);
        var inner = new HavenSize(
            Math.Max(0, available.Width - padding.Left - padding.Right),
            Math.Max(0, available.Height - padding.Top - padding.Bottom));
        var children = container.Children.Where(child =>
        {
            child.IsIncluded = child.MatchesConditions(_context)
                && child.GetValue(HavenProperties.Visibility) != HavenVisibility.Collapsed;
            if (!child.IsIncluded)
                child.DesiredSize = HavenSize.Zero;
            return child.IsIncluded;
        }).ToArray();
        var gap = Resolve(container.GetValue(HavenProperties.Gap), inner.Width, 0);

        HavenSize content = container.Layout switch
        {
            HavenLayout.Horizontal => MeasureHorizontal(children, inner, gap),
            HavenLayout.Wrap => MeasureWrap(children, inner, gap),
            HavenLayout.Grid => MeasureGrid(container, children, inner, gap),
            HavenLayout.Canvas => MeasureCanvas(children, inner),
            HavenLayout.Overlay => MeasureOverlay(children, inner),
            _ => MeasureVertical(children, inner, gap)
        };
        return new HavenSize(content.Width + padding.Left + padding.Right, content.Height + padding.Top + padding.Bottom);
    }

    private HavenSize MeasureVertical(IReadOnlyList<HavenElement> children, HavenSize available, double gap)
    {
        double width = 0, height = 0;
        foreach (var child in children)
        {
            var size = Measure(child, available);
            width = Math.Max(width, size.Width);
            height += size.Height;
        }
        return new HavenSize(width, height + gap * Math.Max(0, children.Count - 1));
    }

    private HavenSize MeasureHorizontal(IReadOnlyList<HavenElement> children, HavenSize available, double gap)
    {
        double width = 0, height = 0;
        foreach (var child in children)
        {
            var size = Measure(child, available);
            width += size.Width;
            height = Math.Max(height, size.Height);
        }
        return new HavenSize(width + gap * Math.Max(0, children.Count - 1), height);
    }

    private HavenSize MeasureWrap(IReadOnlyList<HavenElement> children, HavenSize available, double gap)
    {
        double x = 0, lineHeight = 0, width = 0, height = 0;
        foreach (var child in children)
        {
            var size = Measure(child, available);
            if (x > 0 && x + size.Width > available.Width)
            {
                height += lineHeight + gap;
                x = 0;
                lineHeight = 0;
            }
            x += (x > 0 ? gap : 0) + size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
            width = Math.Max(width, x);
        }
        return new HavenSize(width, height + lineHeight);
    }

    private HavenSize MeasureGrid(Container container, IReadOnlyList<HavenElement> children, HavenSize available, double gap)
    {
        var columnCount = Math.Max(1, container.ColumnTracks.Count);
        var cellWidth = Math.Max(0, (available.Width - gap * (columnCount - 1)) / columnCount);
        var rowHeights = new Dictionary<int, double>();
        foreach (var child in children)
        {
            var row = Math.Max(0, child.GetValue(HavenProperties.Row));
            var span = Math.Max(1, child.GetValue(HavenProperties.ColumnSpan));
            var width = Math.Min(available.Width, cellWidth * span + gap * (span - 1));
            var size = Measure(child, new HavenSize(width, available.Height));
            rowHeights[row] = Math.Max(rowHeights.GetValueOrDefault(row), size.Height);
        }
        var rowCount = rowHeights.Count == 0 ? 0 : rowHeights.Keys.Max() + 1;
        var height = Enumerable.Range(0, rowCount).Sum(row => rowHeights.GetValueOrDefault(row))
            + gap * Math.Max(0, rowCount - 1);
        return new HavenSize(available.Width, height);
    }

    private HavenSize MeasureCanvas(IReadOnlyList<HavenElement> children, HavenSize available)
    {
        double width = 0, height = 0;
        foreach (var child in children)
        {
            var size = Measure(child, available);
            width = Math.Max(width, Resolve(child.GetValue(HavenProperties.Left), available.Width, 0) + size.Width);
            height = Math.Max(height, Resolve(child.GetValue(HavenProperties.Top), available.Height, 0) + size.Height);
        }
        return new HavenSize(width, height);
    }

    private HavenSize MeasureOverlay(IReadOnlyList<HavenElement> children, HavenSize available)
    {
        double width = 0, height = 0;
        foreach (var child in children)
        {
            var size = Measure(child, available);
            width = Math.Max(width, size.Width);
            height = Math.Max(height, size.Height);
        }
        return new HavenSize(width, height);
    }

    private void Arrange(HavenElement element, HavenRect bounds)
    {
        element.Bounds = bounds;
        if (element is not Container container || !element.IsIncluded) return;

        var padding = ResolveThickness(container.GetValue(HavenProperties.Padding), new HavenSize(bounds.Width, bounds.Height));
        var inner = new HavenRect(
            bounds.X + padding.Left,
            bounds.Y + padding.Top,
            Math.Max(0, bounds.Width - padding.Left - padding.Right),
            Math.Max(0, bounds.Height - padding.Top - padding.Bottom));
        var children = container.Children.Where(child => child.IsIncluded).ToArray();
        var gap = Resolve(container.GetValue(HavenProperties.Gap), inner.Width, 0);

        switch (container.Layout)
        {
            case HavenLayout.Horizontal: ArrangeHorizontal(children, inner, gap); break;
            case HavenLayout.Wrap: ArrangeWrap(children, inner, gap); break;
            case HavenLayout.Grid: ArrangeGrid(container, children, inner, gap); break;
            case HavenLayout.Canvas: ArrangeCanvas(children, inner); break;
            case HavenLayout.Overlay: foreach (var child in children) Arrange(child, inner); break;
            default: ArrangeVertical(children, inner, gap); break;
        }
    }

    private void ArrangeVertical(IReadOnlyList<HavenElement> children, HavenRect bounds, double gap)
    {
        var y = bounds.Y;
        foreach (var child in children)
        {
            Arrange(child, new HavenRect(bounds.X, y, Math.Min(bounds.Width, child.DesiredSize.Width), child.DesiredSize.Height));
            y += child.DesiredSize.Height + gap;
        }
    }

    private void ArrangeHorizontal(IReadOnlyList<HavenElement> children, HavenRect bounds, double gap)
    {
        var x = bounds.X;
        foreach (var child in children)
        {
            Arrange(child, new HavenRect(x, bounds.Y, child.DesiredSize.Width, Math.Min(bounds.Height, child.DesiredSize.Height)));
            x += child.DesiredSize.Width + gap;
        }
    }

    private void ArrangeWrap(IReadOnlyList<HavenElement> children, HavenRect bounds, double gap)
    {
        var x = bounds.X;
        var y = bounds.Y;
        double lineHeight = 0;
        foreach (var child in children)
        {
            if (x > bounds.X && x + child.DesiredSize.Width > bounds.Right)
            {
                x = bounds.X;
                y += lineHeight + gap;
                lineHeight = 0;
            }
            Arrange(child, new HavenRect(x, y, child.DesiredSize.Width, child.DesiredSize.Height));
            x += child.DesiredSize.Width + gap;
            lineHeight = Math.Max(lineHeight, child.DesiredSize.Height);
        }
    }

    private void ArrangeGrid(Container container, IReadOnlyList<HavenElement> children, HavenRect bounds, double gap)
    {
        var columnCount = Math.Max(1, container.ColumnTracks.Count);
        var cellWidth = Math.Max(0, (bounds.Width - gap * (columnCount - 1)) / columnCount);
        var rowHeights = children
            .GroupBy(child => Math.Max(0, child.GetValue(HavenProperties.Row)))
            .ToDictionary(group => group.Key, group => group.Max(child => child.DesiredSize.Height));
        foreach (var child in children)
        {
            var column = Math.Clamp(child.GetValue(HavenProperties.Column), 0, columnCount - 1);
            var row = Math.Max(0, child.GetValue(HavenProperties.Row));
            var y = bounds.Y + Enumerable.Range(0, row).Sum(index => rowHeights.GetValueOrDefault(index) + gap);
            var span = Math.Min(columnCount - column, Math.Max(1, child.GetValue(HavenProperties.ColumnSpan)));
            var width = cellWidth * span + gap * (span - 1);
            Arrange(child, new HavenRect(bounds.X + column * (cellWidth + gap), y, width, child.DesiredSize.Height));
        }
    }

    private void ArrangeCanvas(IReadOnlyList<HavenElement> children, HavenRect bounds)
    {
        foreach (var child in children)
        {
            Arrange(child, new HavenRect(
                bounds.X + Resolve(child.GetValue(HavenProperties.Left), bounds.Width, 0),
                bounds.Y + Resolve(child.GetValue(HavenProperties.Top), bounds.Height, 0),
                child.DesiredSize.Width,
                child.DesiredSize.Height));
        }
    }

    private HavenSize ApplySize(HavenElement element, HavenSize desired, HavenSize available)
    {
        var width = Resolve(element.GetValue(HavenProperties.Width), available.Width, desired.Width);
        var height = Resolve(element.GetValue(HavenProperties.Height), available.Height, desired.Height);
        var minWidth = Resolve(element.GetValue(HavenProperties.MinWidth), available.Width, 0);
        var minHeight = Resolve(element.GetValue(HavenProperties.MinHeight), available.Height, 0);
        var maxWidth = Resolve(element.GetValue(HavenProperties.MaxWidth), available.Width, double.PositiveInfinity);
        var maxHeight = Resolve(element.GetValue(HavenProperties.MaxHeight), available.Height, double.PositiveInfinity);
        width = Math.Clamp(width, minWidth, maxWidth);
        height = Math.Clamp(height, minHeight, maxHeight);
        if (element.GetValue(HavenProperties.AspectRatio) is > 0d and var ratio)
        {
            if (element.GetValue(HavenProperties.Width).IsAuto && !element.GetValue(HavenProperties.Height).IsAuto) width = height * ratio;
            else if (!element.GetValue(HavenProperties.Width).IsAuto && element.GetValue(HavenProperties.Height).IsAuto) height = width / ratio;
        }
        return new HavenSize(width, height);
    }

    private double Resolve(HavenLength length, double parentExtent, double fallback)
    {
        var value = length.Resolve(parentExtent, _viewport);
        return double.IsNaN(value) ? fallback : value;
    }

    private (double Left, double Top, double Right, double Bottom) ResolveThickness(HavenThickness value, HavenSize available) => (
        Resolve(value.Left, available.Width, 0),
        Resolve(value.Top, available.Height, 0),
        Resolve(value.Right, available.Width, 0),
        Resolve(value.Bottom, available.Height, 0));
}
