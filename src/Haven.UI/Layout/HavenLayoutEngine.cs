using Haven.UI.Components;

namespace Haven.UI;

public interface IHavenMeasureContext
{
    HavenSize MeasureLeaf(HavenElement element, HavenSize available);
}

/// <summary>
/// Haven-owned measure/arrange engine. It resolves Haven units, margins,
/// alignment, fractional sizing, grids, wrapping, scrolling, and clipping
/// without exposing a platform layout type.
/// </summary>
public sealed class HavenLayoutEngine
{
    private HavenSize _viewport;
    private HavenRenderContext _context = new(HavenPlatform.Unknown, HavenSize.Zero);
    private IHavenMeasureContext _measure = null!;

    public void Layout(HavenElement root, HavenSize viewport, HavenPlatform platform, IHavenMeasureContext measure)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(measure);
        _viewport = Sanitize(viewport);
        _context = new HavenRenderContext(platform, _viewport);
        _measure = measure;
        Measure(root, _viewport);
        Arrange(root, new HavenRect(0, 0, root.DesiredSize.Width, root.DesiredSize.Height));
    }

    private HavenSize Measure(HavenElement element, HavenSize available)
    {
        available = Sanitize(available);
        element.IsIncluded = element.MatchesConditions(_context)
            && element.GetValue(HavenProperties.Visibility) != HavenVisibility.Collapsed;
        if (!element.IsIncluded)
        {
            element.DesiredSize = HavenSize.Zero;
            return HavenSize.Zero;
        }

        var measureAvailable = ResolveMeasureAvailable(element, available);
        HavenSize desired;
        if (element is Container container)
        {
            desired = MeasureContainer(container, measureAvailable);
        }
        else
        {
            var padding = ResolveThickness(element.GetValue(HavenProperties.Padding), measureAvailable);
            var contentAvailable = new HavenSize(
                Math.Max(0, measureAvailable.Width - padding.Horizontal),
                Math.Max(0, measureAvailable.Height - padding.Vertical));
            var content = _measure.MeasureLeaf(element, contentAvailable);
            desired = new HavenSize(content.Width + padding.Horizontal, content.Height + padding.Vertical);
        }
        desired = ApplySize(element, Sanitize(desired), available);
        element.DesiredSize = desired;
        return desired;
    }

    private HavenSize ResolveMeasureAvailable(HavenElement element, HavenSize available)
    {
        var minWidth = Math.Max(0, Resolve(element.GetValue(HavenProperties.MinWidth), available.Width, 0));
        var minHeight = Math.Max(0, Resolve(element.GetValue(HavenProperties.MinHeight), available.Height, 0));
        var maxWidth = Resolve(element.GetValue(HavenProperties.MaxWidth), available.Width, double.PositiveInfinity);
        var maxHeight = Resolve(element.GetValue(HavenProperties.MaxHeight), available.Height, double.PositiveInfinity);
        if (element.GetValue(HavenProperties.Responsive))
        {
            maxWidth = Math.Min(maxWidth, available.Width);
            maxHeight = Math.Min(maxHeight, available.Height);
        }
        maxWidth = Math.Max(minWidth, maxWidth);
        maxHeight = Math.Max(minHeight, maxHeight);
        var width = Resolve(element.GetValue(HavenProperties.Width), available.Width, available.Width);
        var height = Resolve(element.GetValue(HavenProperties.Height), available.Height, available.Height);
        return new HavenSize(
            Math.Clamp(Math.Max(0, width), minWidth, maxWidth),
            Math.Clamp(Math.Max(0, height), minHeight, maxHeight));
    }

    private HavenSize MeasureContainer(Container container, HavenSize available)
    {
        var padding = ResolveThickness(container.GetValue(HavenProperties.Padding), available);
        var inner = new HavenSize(
            Math.Max(0, available.Width - padding.Horizontal),
            Math.Max(0, available.Height - padding.Vertical));
        var children = IncludedChildren(container).ToArray();
        var gap = Resolve(container.GetValue(HavenProperties.Gap), inner.Width, 0);

        var content = container.Layout switch
        {
            HavenLayout.Horizontal => MeasureHorizontal(children, inner, gap),
            HavenLayout.Wrap => MeasureWrap(children, inner, gap),
            HavenLayout.Grid => MeasureGrid(container, children, inner, gap),
            HavenLayout.Canvas => MeasureCanvas(children, inner),
            HavenLayout.Overlay => MeasureOverlay(children, inner),
            _ => MeasureVertical(children, inner, gap)
        };
        container.MeasuredContentSize = content;
        return new HavenSize(content.Width + padding.Horizontal, content.Height + padding.Vertical);
    }

    private IEnumerable<HavenElement> IncludedChildren(Container container)
    {
        foreach (var child in container.Children)
        {
            child.IsIncluded = child.MatchesConditions(_context)
                && child.GetValue(HavenProperties.Visibility) != HavenVisibility.Collapsed;
            if (!child.IsIncluded)
            {
                child.DesiredSize = HavenSize.Zero;
                continue;
            }
            yield return child;
        }
    }

    private HavenSize MeasureVertical(IReadOnlyList<HavenElement> children, HavenSize available, double gap)
    {
        double width = 0, height = 0;
        foreach (var child in children)
        {
            var outer = MeasureOuter(child, available);
            width = Math.Max(width, outer.Width);
            height += outer.Height;
        }
        return new HavenSize(width, height + gap * Math.Max(0, children.Count - 1));
    }

    private HavenSize MeasureHorizontal(IReadOnlyList<HavenElement> children, HavenSize available, double gap)
    {
        double width = 0, height = 0;
        foreach (var child in children)
        {
            var outer = MeasureOuter(child, available);
            width += outer.Width;
            height = Math.Max(height, outer.Height);
        }
        return new HavenSize(width + gap * Math.Max(0, children.Count - 1), height);
    }

    private HavenSize MeasureWrap(IReadOnlyList<HavenElement> children, HavenSize available, double gap)
    {
        double x = 0, lineHeight = 0, width = 0, height = 0;
        foreach (var child in children)
        {
            var outer = MeasureOuter(child, available);
            if (x > 0 && x + gap + outer.Width > available.Width)
            {
                height += lineHeight + gap;
                x = 0;
                lineHeight = 0;
            }
            if (x > 0) x += gap;
            x += outer.Width;
            lineHeight = Math.Max(lineHeight, outer.Height);
            width = Math.Max(width, x);
        }
        return new HavenSize(width, height + lineHeight);
    }

    private HavenSize MeasureGrid(Container container, IReadOnlyList<HavenElement> children, HavenSize available, double gap)
    {
        foreach (var child in children) MeasureOuter(child, available);
        var tracks = ComputeGridTracks(container, children, available, gap);

        // Re-measure against the resolved spanned cell. This gives wrapped text
        // and Auto rows a cell-aware desired size rather than a viewport guess.
        foreach (var child in children)
        {
            var column = Math.Max(0, child.GetValue(HavenProperties.Column));
            var span = Math.Max(1, child.GetValue(HavenProperties.ColumnSpan));
            var width = SpanExtent(tracks.Columns, column, span, gap);
            MeasureOuter(child, new HavenSize(width, available.Height));
        }
        tracks = ComputeGridTracks(container, children, available, gap);
        return new HavenSize(tracks.Width(gap), tracks.Height(gap));
    }

    private HavenSize MeasureCanvas(IReadOnlyList<HavenElement> children, HavenSize available)
    {
        double width = 0, height = 0;
        foreach (var child in children)
        {
            var outer = MeasureOuter(child, available);
            width = Math.Max(width, Resolve(child.GetValue(HavenProperties.Left), available.Width, 0) + outer.Width);
            height = Math.Max(height, Resolve(child.GetValue(HavenProperties.Top), available.Height, 0) + outer.Height);
        }
        return new HavenSize(width, height);
    }

    private HavenSize MeasureOverlay(IReadOnlyList<HavenElement> children, HavenSize available)
    {
        double width = 0, height = 0;
        foreach (var child in children)
        {
            var outer = MeasureOuter(child, available);
            width = Math.Max(width, outer.Width);
            height = Math.Max(height, outer.Height);
        }
        return new HavenSize(width, height);
    }

    private HavenSize MeasureOuter(HavenElement child, HavenSize available)
    {
        var margin = ResolveThickness(child.GetValue(HavenProperties.Margin), available);
        var childAvailable = new HavenSize(
            Math.Max(0, available.Width - margin.Horizontal),
            Math.Max(0, available.Height - margin.Vertical));
        var desired = Measure(child, childAvailable);
        return new HavenSize(desired.Width + margin.Horizontal, desired.Height + margin.Vertical);
    }

    private void Arrange(HavenElement element, HavenRect bounds)
    {
        element.Bounds = new HavenRect(bounds.X, bounds.Y, Math.Max(0, bounds.Width), Math.Max(0, bounds.Height));
        if (element is not Container container || !element.IsIncluded) return;

        var padding = ResolveThickness(container.GetValue(HavenProperties.Padding), new HavenSize(bounds.Width, bounds.Height));
        var viewport = new HavenRect(
            bounds.X + padding.Left,
            bounds.Y + padding.Top,
            Math.Max(0, bounds.Width - padding.Horizontal),
            Math.Max(0, bounds.Height - padding.Vertical));
        var children = container.Children.Where(child => child.IsIncluded).ToArray();
        var gap = Resolve(container.GetValue(HavenProperties.Gap), viewport.Width, 0);
        var content = new HavenSize(
            Math.Max(viewport.Width, container.MeasuredContentSize.Width),
            Math.Max(viewport.Height, container.MeasuredContentSize.Height));
        container.UpdateScrollMetrics(new HavenSize(viewport.Width, viewport.Height), content);

        var scrollable = container.GetValue(HavenProperties.Overflow) == HavenOverflow.Scroll;
        var contentBounds = new HavenRect(
            viewport.X - (scrollable ? container.ScrollX : 0),
            viewport.Y - (scrollable ? container.ScrollY : 0),
            content.Width,
            content.Height);

        switch (container.Layout)
        {
            case HavenLayout.Horizontal: ArrangeHorizontal(children, contentBounds, gap); break;
            case HavenLayout.Wrap: ArrangeWrap(children, contentBounds, gap); break;
            case HavenLayout.Grid: ArrangeGrid(container, children, contentBounds, gap); break;
            case HavenLayout.Canvas: ArrangeCanvas(children, contentBounds); break;
            case HavenLayout.Overlay: foreach (var child in children) ArrangeInSlot(child, contentBounds); break;
            default: ArrangeVertical(children, contentBounds, gap); break;
        }
    }

    private void ArrangeVertical(IReadOnlyList<HavenElement> children, HavenRect bounds, double gap)
    {
        var remaining = Math.Max(0, bounds.Height - gap * Math.Max(0, children.Count - 1));
        var totalFraction = 0d;
        foreach (var child in children)
        {
            var margin = ResolveThickness(child.GetValue(HavenProperties.Margin), new HavenSize(bounds.Width, bounds.Height));
            remaining -= margin.Vertical;
            var height = child.GetValue(HavenProperties.Height);
            if (height.Unit == HavenLengthUnit.Fraction) totalFraction += Math.Max(0, height.Value);
            else remaining -= child.DesiredSize.Height;
        }

        var y = bounds.Y;
        foreach (var child in children)
        {
            var margin = ResolveThickness(child.GetValue(HavenProperties.Margin), new HavenSize(bounds.Width, bounds.Height));
            var height = child.GetValue(HavenProperties.Height);
            var allocated = height.Unit == HavenLengthUnit.Fraction && totalFraction > 0
                ? Math.Max(0, remaining) * Math.Max(0, height.Value) / totalFraction
                : child.DesiredSize.Height;
            var slotHeight = allocated + margin.Vertical;
            ArrangeInSlot(child, new HavenRect(bounds.X, y, bounds.Width, slotHeight), forcedHeight: allocated);
            y += slotHeight + gap;
        }
    }

    private void ArrangeHorizontal(IReadOnlyList<HavenElement> children, HavenRect bounds, double gap)
    {
        var remaining = Math.Max(0, bounds.Width - gap * Math.Max(0, children.Count - 1));
        var totalFraction = 0d;
        foreach (var child in children)
        {
            var margin = ResolveThickness(child.GetValue(HavenProperties.Margin), new HavenSize(bounds.Width, bounds.Height));
            remaining -= margin.Horizontal;
            var width = child.GetValue(HavenProperties.Width);
            if (width.Unit == HavenLengthUnit.Fraction) totalFraction += Math.Max(0, width.Value);
            else remaining -= child.DesiredSize.Width;
        }

        var x = bounds.X;
        foreach (var child in children)
        {
            var margin = ResolveThickness(child.GetValue(HavenProperties.Margin), new HavenSize(bounds.Width, bounds.Height));
            var width = child.GetValue(HavenProperties.Width);
            var allocated = width.Unit == HavenLengthUnit.Fraction && totalFraction > 0
                ? Math.Max(0, remaining) * Math.Max(0, width.Value) / totalFraction
                : child.DesiredSize.Width;
            var slotWidth = allocated + margin.Horizontal;
            ArrangeInSlot(child, new HavenRect(x, bounds.Y, slotWidth, bounds.Height), forcedWidth: allocated);
            x += slotWidth + gap;
        }
    }

    private void ArrangeWrap(IReadOnlyList<HavenElement> children, HavenRect bounds, double gap)
    {
        var x = bounds.X;
        var y = bounds.Y;
        double lineHeight = 0;
        foreach (var child in children)
        {
            var margin = ResolveThickness(child.GetValue(HavenProperties.Margin), new HavenSize(bounds.Width, bounds.Height));
            var outerWidth = child.DesiredSize.Width + margin.Horizontal;
            var outerHeight = child.DesiredSize.Height + margin.Vertical;
            if (x > bounds.X && x + gap + outerWidth > bounds.Right)
            {
                x = bounds.X;
                y += lineHeight + gap;
                lineHeight = 0;
            }
            if (x > bounds.X) x += gap;
            ArrangeInSlot(child, new HavenRect(x, y, outerWidth, outerHeight));
            x += outerWidth;
            lineHeight = Math.Max(lineHeight, outerHeight);
        }
    }

    private void ArrangeGrid(Container container, IReadOnlyList<HavenElement> children, HavenRect bounds, double gap)
    {
        var tracks = ComputeGridTracks(container, children, new HavenSize(bounds.Width, bounds.Height), gap);
        foreach (var child in children)
        {
            var column = Math.Max(0, child.GetValue(HavenProperties.Column));
            var row = Math.Max(0, child.GetValue(HavenProperties.Row));
            var columnSpan = Math.Max(1, child.GetValue(HavenProperties.ColumnSpan));
            var rowSpan = Math.Max(1, child.GetValue(HavenProperties.RowSpan));
            var x = bounds.X + TrackOffset(tracks.Columns, column, gap);
            var y = bounds.Y + TrackOffset(tracks.Rows, row, gap);
            ArrangeInSlot(child, new HavenRect(
                x,
                y,
                SpanExtent(tracks.Columns, column, columnSpan, gap),
                SpanExtent(tracks.Rows, row, rowSpan, gap)));
        }
    }

    private void ArrangeCanvas(IReadOnlyList<HavenElement> children, HavenRect bounds)
    {
        foreach (var child in children)
        {
            var margin = ResolveThickness(child.GetValue(HavenProperties.Margin), new HavenSize(bounds.Width, bounds.Height));
            var slot = new HavenRect(
                bounds.X + Resolve(child.GetValue(HavenProperties.Left), bounds.Width, 0),
                bounds.Y + Resolve(child.GetValue(HavenProperties.Top), bounds.Height, 0),
                child.DesiredSize.Width + margin.Horizontal,
                child.DesiredSize.Height + margin.Vertical);
            ArrangeInSlot(child, slot);
        }
    }

    private void ArrangeInSlot(HavenElement child, HavenRect slot, double? forcedWidth = null, double? forcedHeight = null)
    {
        var margin = ResolveThickness(child.GetValue(HavenProperties.Margin), new HavenSize(slot.Width, slot.Height));
        var availableWidth = Math.Max(0, slot.Width - margin.Horizontal);
        var availableHeight = Math.Max(0, slot.Height - margin.Vertical);
        var responsive = child.GetValue(HavenProperties.Responsive);
        var widthProperty = child.GetValue(HavenProperties.Width);
        var heightProperty = child.GetValue(HavenProperties.Height);
        var width = forcedWidth ?? (responsive && widthProperty.Unit is HavenLengthUnit.Auto or HavenLengthUnit.Fraction
            && child.GetValue(HavenProperties.HorizontalAlignment) == HavenHorizontalAlignment.Stretch
                ? availableWidth
                : child.DesiredSize.Width);
        var height = forcedHeight ?? (responsive && heightProperty.Unit is HavenLengthUnit.Auto or HavenLengthUnit.Fraction
            && child.GetValue(HavenProperties.VerticalAlignment) == HavenVerticalAlignment.Stretch
                ? availableHeight
                : child.DesiredSize.Height);
        if (responsive)
        {
            width = Math.Min(width, availableWidth);
            height = Math.Min(height, availableHeight);
        }

        var x = slot.X + margin.Left + AlignOffset(availableWidth, width, child.GetValue(HavenProperties.HorizontalAlignment));
        var y = slot.Y + margin.Top + AlignOffset(availableHeight, height, child.GetValue(HavenProperties.VerticalAlignment));
        Arrange(child, new HavenRect(x, y, Math.Max(0, width), Math.Max(0, height)));
    }

    private GridTracks ComputeGridTracks(Container container, IReadOnlyList<HavenElement> children, HavenSize available, double gap)
    {
        var columnCount = Math.Max(container.ColumnTracks.Count, children.Count == 0 ? 1 : children.Max(child => Math.Max(0, child.GetValue(HavenProperties.Column)) + Math.Max(1, child.GetValue(HavenProperties.ColumnSpan))));
        var rowCount = Math.Max(container.RowTracks.Count, children.Count == 0 ? 1 : children.Max(child => Math.Max(0, child.GetValue(HavenProperties.Row)) + Math.Max(1, child.GetValue(HavenProperties.RowSpan))));
        var columnSpecs = ExtendTracks(container.ColumnTracks, columnCount, HavenLength.Auto);
        var rowSpecs = ExtendTracks(container.RowTracks, rowCount, HavenLength.Auto);
        var columnAuto = new double[columnCount];
        var rowAuto = new double[rowCount];

        foreach (var child in children)
        {
            var margin = ResolveThickness(child.GetValue(HavenProperties.Margin), available);
            var column = Math.Max(0, child.GetValue(HavenProperties.Column));
            var row = Math.Max(0, child.GetValue(HavenProperties.Row));
            if (child.GetValue(HavenProperties.ColumnSpan) == 1 && column < columnAuto.Length)
                columnAuto[column] = Math.Max(columnAuto[column], child.DesiredSize.Width + margin.Horizontal);
            if (child.GetValue(HavenProperties.RowSpan) == 1 && row < rowAuto.Length)
                rowAuto[row] = Math.Max(rowAuto[row], child.DesiredSize.Height + margin.Vertical);
        }

        var columns = ResolveTracks(columnSpecs, columnAuto, available.Width, gap);
        var rows = ResolveTracks(rowSpecs, rowAuto, available.Height, gap);
        EnsureSpans(children, columns, columnSpecs, available, gap, horizontal: true);
        EnsureSpans(children, rows, rowSpecs, available, gap, horizontal: false);
        if (container.GetValue(HavenProperties.Responsive))
        {
            ConstrainFlexibleTracks(columns, columnSpecs, available.Width, gap);
            ConstrainFlexibleTracks(rows, rowSpecs, available.Height, gap);
        }
        return new GridTracks(columns, rows);
    }

    private double[] ResolveTracks(IReadOnlyList<HavenLength> specs, IReadOnlyList<double> auto, double available, double gap)
    {
        var result = new double[specs.Count];
        var totalFraction = 0d;
        for (var index = 0; index < specs.Count; index++)
        {
            var spec = specs[index];
            if (spec.Unit == HavenLengthUnit.Auto) result[index] = auto[index];
            else if (spec.Unit == HavenLengthUnit.Fraction)
            {
                result[index] = auto[index];
                totalFraction += Math.Max(0, spec.Value);
            }
            else result[index] = Math.Max(0, Resolve(spec, available, 0));
        }

        var gaps = gap * Math.Max(0, specs.Count - 1);
        var remaining = Math.Max(0, available - gaps - result.Sum());
        if (totalFraction > 0)
        {
            for (var index = 0; index < specs.Count; index++)
                if (specs[index].Unit == HavenLengthUnit.Fraction)
                    result[index] += remaining * Math.Max(0, specs[index].Value) / totalFraction;
        }
        return result;
    }

    private void EnsureSpans(
        IReadOnlyList<HavenElement> children,
        double[] tracks,
        IReadOnlyList<HavenLength> specs,
        HavenSize available,
        double gap,
        bool horizontal)
    {
        foreach (var child in children)
        {
            var start = Math.Max(0, child.GetValue(horizontal ? HavenProperties.Column : HavenProperties.Row));
            var span = Math.Max(1, child.GetValue(horizontal ? HavenProperties.ColumnSpan : HavenProperties.RowSpan));
            if (start >= tracks.Length) continue;
            span = Math.Min(span, tracks.Length - start);
            var margin = ResolveThickness(child.GetValue(HavenProperties.Margin), available);
            var desired = horizontal
                ? child.DesiredSize.Width + margin.Horizontal
                : child.DesiredSize.Height + margin.Vertical;
            var actual = SpanExtent(tracks, start, span, gap);
            var deficit = desired - actual;
            if (deficit <= .0001d) continue;
            var candidates = Enumerable.Range(start, span).Where(index => specs[index].Unit == HavenLengthUnit.Auto).ToArray();
            if (candidates.Length == 0) candidates = Enumerable.Range(start, span).Where(index => specs[index].Unit == HavenLengthUnit.Fraction).ToArray();
            if (candidates.Length == 0) candidates = Enumerable.Range(start, span).ToArray();
            foreach (var index in candidates) tracks[index] += deficit / candidates.Length;
        }
    }

    private static void ConstrainFlexibleTracks(double[] tracks, IReadOnlyList<HavenLength> specs, double available, double gap)
    {
        var overflow = tracks.Sum() + gap * Math.Max(0, tracks.Length - 1) - available;
        if (overflow <= .0001d) return;
        var flexible = Enumerable.Range(0, tracks.Length)
            .Where(index => specs[index].Unit is HavenLengthUnit.Auto or HavenLengthUnit.Fraction && tracks[index] > 0)
            .ToArray();
        while (overflow > .0001d && flexible.Length > 0)
        {
            var share = overflow / flexible.Length;
            var removed = 0d;
            foreach (var index in flexible)
            {
                var amount = Math.Min(share, tracks[index]);
                tracks[index] -= amount;
                removed += amount;
            }
            if (removed <= .0001d) break;
            overflow -= removed;
            flexible = flexible.Where(index => tracks[index] > .0001d).ToArray();
        }
    }

    private HavenSize ApplySize(HavenElement element, HavenSize desired, HavenSize available)
    {
        var widthProperty = element.GetValue(HavenProperties.Width);
        var heightProperty = element.GetValue(HavenProperties.Height);
        var width = Resolve(widthProperty, available.Width, desired.Width);
        var height = Resolve(heightProperty, available.Height, desired.Height);
        var minWidth = Math.Max(0, Resolve(element.GetValue(HavenProperties.MinWidth), available.Width, 0));
        var minHeight = Math.Max(0, Resolve(element.GetValue(HavenProperties.MinHeight), available.Height, 0));
        var maxWidth = Resolve(element.GetValue(HavenProperties.MaxWidth), available.Width, double.PositiveInfinity);
        var maxHeight = Resolve(element.GetValue(HavenProperties.MaxHeight), available.Height, double.PositiveInfinity);
        if (element.GetValue(HavenProperties.Responsive))
        {
            maxWidth = Math.Min(maxWidth, available.Width);
            maxHeight = Math.Min(maxHeight, available.Height);
        }

        var aspectRatio = element.GetValue(HavenProperties.AspectRatio);
        if (aspectRatio is > 0d and var ratio)
        {
            if (widthProperty.IsAuto && !heightProperty.IsAuto) width = height * ratio;
            else if (!widthProperty.IsAuto && heightProperty.IsAuto) height = width / ratio;
        }

        maxWidth = Math.Max(minWidth, maxWidth);
        maxHeight = Math.Max(minHeight, maxHeight);
        width = Math.Clamp(Math.Max(0, width), minWidth, maxWidth);
        height = Math.Clamp(Math.Max(0, height), minHeight, maxHeight);
        if (aspectRatio is > 0d and var constrainedRatio)
        {
            if (widthProperty.IsAuto && !heightProperty.IsAuto)
                width = Math.Clamp(height * constrainedRatio, minWidth, maxWidth);
            else if (!widthProperty.IsAuto && heightProperty.IsAuto)
                height = Math.Clamp(width / constrainedRatio, minHeight, maxHeight);
        }
        return new HavenSize(width, height);
    }

    private static IReadOnlyList<HavenLength> ExtendTracks(IReadOnlyList<HavenLength> source, int count, HavenLength fallback)
    {
        var result = new HavenLength[count];
        for (var index = 0; index < count; index++) result[index] = index < source.Count ? source[index] : fallback;
        return result;
    }

    private static double TrackOffset(IReadOnlyList<double> tracks, int index, double gap) =>
        tracks.Take(Math.Min(index, tracks.Count)).Sum() + gap * Math.Min(index, tracks.Count);

    private static double SpanExtent(IReadOnlyList<double> tracks, int start, int span, double gap)
    {
        if (start >= tracks.Count) return 0;
        var count = Math.Min(Math.Max(1, span), tracks.Count - start);
        return tracks.Skip(start).Take(count).Sum() + gap * Math.Max(0, count - 1);
    }

    private static double AlignOffset(double available, double desired, HavenHorizontalAlignment alignment) => alignment switch
    {
        HavenHorizontalAlignment.Center => Math.Max(0, available - desired) / 2d,
        HavenHorizontalAlignment.End => Math.Max(0, available - desired),
        _ => 0
    };

    private static double AlignOffset(double available, double desired, HavenVerticalAlignment alignment) => alignment switch
    {
        HavenVerticalAlignment.Center => Math.Max(0, available - desired) / 2d,
        HavenVerticalAlignment.End => Math.Max(0, available - desired),
        _ => 0
    };

    private double Resolve(HavenLength length, double parentExtent, double fallback)
    {
        var value = length.Resolve(parentExtent, _viewport);
        return double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
    }

    private ResolvedThickness ResolveThickness(HavenThickness value, HavenSize available) => new(
        Resolve(value.Left, available.Width, 0),
        Resolve(value.Top, available.Height, 0),
        Resolve(value.Right, available.Width, 0),
        Resolve(value.Bottom, available.Height, 0));

    private static HavenSize Sanitize(HavenSize size) => new(
        double.IsNaN(size.Width) || size.Width < 0 ? 0 : size.Width,
        double.IsNaN(size.Height) || size.Height < 0 ? 0 : size.Height);

    private readonly record struct ResolvedThickness(double Left, double Top, double Right, double Bottom)
    {
        public double Horizontal => Left + Right;
        public double Vertical => Top + Bottom;
    }

    private sealed record GridTracks(IReadOnlyList<double> Columns, IReadOnlyList<double> Rows)
    {
        public double Width(double gap) => Columns.Sum() + gap * Math.Max(0, Columns.Count - 1);
        public double Height(double gap) => Rows.Sum() + gap * Math.Max(0, Rows.Count - 1);
    }
}
