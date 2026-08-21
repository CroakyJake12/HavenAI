using System.Globalization;
using Haven.Application;
using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Present;

internal enum PresentVectorHandleKind { Node = 0, Control1 = 1, Control2 = 2 }

internal sealed partial class PresentSlideCanvas : HavenElement, IHavenDrawCommandSource, IHavenPointerInputTarget, IHavenKeyboardInputTarget, IHavenTextInputTarget, IHavenClipboardInputTarget
{
    private PresentDocument? _document;
    private PresentSlide? _slide;
    private HashSet<Guid> _selectedIds = [];
    private Guid? _dragElementId;
    private VectorHandleHit? _dragVectorHandle;
    private HavenPoint _pointerStart;
    private HavenPoint _pointerCurrent;

    public PresentSlideCanvas()
    {
        Name = "Present.Slide.Canvas"; Accessibility.Role = HavenAccessibleRole.Image; Accessibility.Focusable = true; Accessibility.AccessibleName = "Editable presentation slide canvas"; Accessibility.Description = "Select and drag slide objects.";
        SetValue(HavenProperties.Width, HavenLength.Percent(100)); SetValue(HavenProperties.MinHeight, HavenLength.Px(420)); SetValue(HavenProperties.Background, "Surface"); SetValue(HavenProperties.BorderColor, "Border"); SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12))); SetValue(HavenProperties.Clip, true);
    }

    public event Action<Guid?>? SelectionRequested;
    public event Action<double, double>? MoveSelectionRequested;
    public event Action<Guid, Guid, PresentVectorHandleKind, double, double>? VectorHandleMoveRequested;

    public void SetSlide(PresentDocument document, PresentSlide slide, IEnumerable<Guid> selectedIds)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document)); _slide = slide ?? throw new ArgumentNullException(nameof(slide)); _selectedIds = selectedIds?.ToHashSet() ?? []; _dragElementId = null; _dragVectorHandle = null; _pointerStart = _pointerCurrent = default; Accessibility.Description = $"Slide {slide.Order + 1}. {slide.Elements.Count} editable objects."; Invalidate();
    }

    public bool PointerPressed(HavenPointerInput input)
    {
        if (_slide is null || _document is null) return false;
        if (IsTextEditing)
        {
            if (TryHandleTextPointerPress(input)) return true;
            CommitTextEdit();
        }
        ResetDirectGesture();
        _pointerStart = _pointerCurrent = input.LocalPosition;

        if (TryBeginTitleEdit(input.LocalPosition, ToInputModifiers(input.Modifiers))) return true;

        if (HitVectorHandle(input.LocalPosition) is { } vectorHandle)
        {
            SelectionRequested?.Invoke(vectorHandle.ElementId);
            _pointerStart = _pointerCurrent = input.LocalPosition;
            _dragVectorHandle = vectorHandle;
            Invalidate();
            return true;
        }

        if (HitTransformHandle(input.LocalPosition) is { } transformHandle)
        {
            _pointerStart = _pointerCurrent = input.LocalPosition;
            _activeTransformHandle = transformHandle;
            Invalidate();
            return true;
        }

        var hit = HitElement(input.LocalPosition);
        if (hit is not null)
        {
            if (!input.Modifiers.HasFlag(HavenKeyModifiers.Shift) && hit.Kind == PresentElementKind.Text && _selectedIds.Contains(hit.Id))
            {
                BeginElementTextEdit(hit, input.LocalPosition, ToInputModifiers(input.Modifiers));
                return true;
            }
            var selected = _selectedIds.ToHashSet();
            if (input.Modifiers.HasFlag(HavenKeyModifiers.Shift))
            {
                if (!selected.Add(hit.Id)) selected.Remove(hit.Id);
            }
            else if (!selected.Contains(hit.Id))
            {
                selected.Clear();
                selected.Add(hit.Id);
            }
            SelectionSetRequested?.Invoke(selected);
            _pointerStart = _pointerCurrent = input.LocalPosition;
            _selectedIds = selected;
            if (selected.Contains(hit.Id) && !hit.Locked) _dragElementId = hit.Id;
            Invalidate();
            return true;
        }

        if (!input.Modifiers.HasFlag(HavenKeyModifiers.Shift)) SelectionSetRequested?.Invoke(Array.Empty<Guid>());
        _pointerStart = _pointerCurrent = input.LocalPosition;
        _marqueeSelecting = true;
        Invalidate();
        return true;
    }

    public bool PointerMoved(HavenPointerInput input)
    {
        if (MoveTextPointer(input)) return true;
        if (_dragVectorHandle is null && _dragElementId is null && _activeTransformHandle == PresentTransformHandle.None && !_marqueeSelecting) return false;
        _pointerCurrent = input.LocalPosition;
        Invalidate();
        return true;
    }

    public bool PointerReleased(HavenPointerInput input)
    {
        if (ReleaseTextPointer()) return true;
        _pointerCurrent = input.LocalPosition;
        if (_dragVectorHandle is { } vectorHandle)
        {
            if (DistanceSquared(_pointerStart, _pointerCurrent) > 0.25
                && TryLocalToVectorPoint(vectorHandle.ElementId, _pointerCurrent, out var point))
                VectorHandleMoveRequested?.Invoke(vectorHandle.ElementId, vectorHandle.NodeId, vectorHandle.Kind, point.X, point.Y);
            ResetDirectGesture();
            Invalidate();
            return true;
        }

        var slide = SlideRectLocal();
        var dx = slide.Width <= 0 ? 0 : (_pointerCurrent.X - _pointerStart.X) / slide.Width;
        var dy = slide.Height <= 0 ? 0 : (_pointerCurrent.Y - _pointerStart.Y) / slide.Height;

        if (_activeTransformHandle != PresentTransformHandle.None)
        {
            var transform = BuildDirectTransform(_activeTransformHandle, dx, dy);
            if (Math.Abs(transform.DeltaX) > .0005 || Math.Abs(transform.DeltaY) > .0005
                || Math.Abs(transform.DeltaWidth) > .0005 || Math.Abs(transform.DeltaHeight) > .0005
                || Math.Abs(transform.DeltaRotation) > .05)
                TransformSelectionRequested?.Invoke(transform.DeltaX, transform.DeltaY, transform.DeltaWidth, transform.DeltaHeight, transform.DeltaRotation);
            ResetDirectGesture();
            Invalidate();
            return true;
        }

        if (_marqueeSelecting)
        {
            SelectionSetRequested?.Invoke(HitElementsInMarquee(_pointerStart, _pointerCurrent));
            ResetDirectGesture();
            Invalidate();
            return true;
        }

        if (_dragElementId is null) return false;
        if (Math.Abs(dx) > 0.0005 || Math.Abs(dy) > 0.0005) MoveSelectionRequested?.Invoke(dx, dy);
        ResetDirectGesture();
        Invalidate();
        return true;
    }

    public void Draw(HavenDrawingContext context, double opacity)
    {
        if (_document is null || _slide is null || Bounds.Width <= 1 || Bounds.Height <= 1) return; var slideRect = SlideRectAbsolute();
        context.Add(new HavenShadowCommand(slideRect, new HavenShadow(new HavenSolidBrush(70, 0, 0, 0), 18, 0, 5, 0, .3), 4)); context.Add(new HavenFillRoundedRectCommand(slideRect, BackgroundBrush(_document, _slide), 4, opacity)); context.Add(new HavenStrokeRoundedRectCommand(slideRect, new HavenPen(new HavenTokenBrush("Border"), 1), 4, opacity));
        DrawSlideTitle(context, slideRect, opacity);
        foreach (var element in _slide.Elements.Where(value => value.Visible && value.Kind != PresentElementKind.Group).OrderBy(value => value.Order)) DrawElement(context, element, slideRect, opacity);
        var showHandles = _selectedIds.Count == 1;
        foreach (var element in _slide.Elements.Where(value => _selectedIds.Contains(value.Id) && value.Kind != PresentElementKind.Group))
            DrawDirectSelection(context, element, ElementRect(element, slideRect, _dragElementId is not null && _selectedIds.Contains(element.Id)), opacity, showHandles);
        DrawMarquee(context, opacity);
    }

    private void DrawElement(HavenDrawingContext context, PresentElement element, HavenRect slideRect, double opacity)
    {
        var rect = ElementRect(element, slideRect, _dragElementId == element.Id);
        if (Math.Abs(element.RotationDegrees) > .001) context.Add(new HavenPushTransformCommand(rect, new HavenTransform(RotationDegrees: element.RotationDegrees), new HavenPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)));
        switch (element.Kind)
        {
            case PresentElementKind.Text:
                DrawEditableElementText(context, element, rect, opacity);
                break;
            case PresentElementKind.Shape when element.VectorShape is { } vector:
                var renderedVector = BuildPreviewVector(element, vector);
                DrawVectorShape(context, renderedVector, rect, opacity * element.Opacity);
                if (_selectedIds.Count == 1 && _selectedIds.Contains(element.Id))
                    DrawVectorHandles(context, renderedVector, rect, opacity);
                break;
            case PresentElementKind.Shape:
                context.Add(new HavenFillRoundedRectCommand(rect, Brush(element.Style.FillColor, "AccentSubtle"), Math.Max(2, rect.Width * element.Style.CornerRadius), opacity * element.Opacity));
                context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(Brush(element.Style.StrokeColor, "Accent"), Math.Max(1, element.Style.StrokeWidth)), Math.Max(2, rect.Width * element.Style.CornerRadius), opacity * element.Opacity));
                break;
            case PresentElementKind.Image:
                context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("SurfaceRaised"), 6, opacity * element.Opacity));
                context.Add(new HavenTextCommand(rect, new HavenTextLayout(string.IsNullOrWhiteSpace(element.AlternativeText) ? "Image" : element.AlternativeText, "Segoe UI", 14, 600, rect.Width, true), new HavenTokenBrush("TextSecondary"), opacity));
                break;
            case PresentElementKind.GenUi:
                context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("SurfaceRaised"), 8, opacity * element.Opacity)); context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenTokenBrush("Accent"), 1.5), 8, opacity));
                context.Add(new HavenTextCommand(rect, new HavenTextLayout("GenUI · " + (string.IsNullOrWhiteSpace(element.AlternativeText) ? "interactive object" : element.AlternativeText), "Segoe UI", 13, 600, rect.Width, true), new HavenTokenBrush("TextPrimary"), opacity));
                break;
            case PresentElementKind.Media:
                context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("SurfaceRaised"), 8, opacity * element.Opacity)); context.Add(new HavenTextCommand(rect, new HavenTextLayout("Media", "Segoe UI", 14, 600, rect.Width, true), new HavenTokenBrush("TextSecondary"), opacity));
                break;
        }
        if (Math.Abs(element.RotationDegrees) > .001) context.Add(new HavenPopTransformCommand(rect));
    }

    private static void DrawVectorShape(HavenDrawingContext context, DocumentVectorShape shape, HavenRect rect, double opacity)
    {
        foreach (var path in shape.Paths)
        {
            var figures = new List<HavenPathFigure>();
            foreach (var subpath in path.Subpaths.Where(value => value.Nodes.Count > 0))
            {
                var first = DocumentVectorShapes.TransformPoint(shape, subpath.Nodes[0].Point); var segments = new List<HavenPathSegment>();
                for (var index = 1; index < subpath.Nodes.Count; index++)
                {
                    var node = subpath.Nodes[index]; var end = DocumentVectorShapes.TransformPoint(shape, node.Point);
                    segments.Add(node.IncomingSegment switch
                    {
                        DocumentVectorSegmentKind.Quadratic when node.Control1 is { } c1 => new HavenQuadraticBezierSegment(ToHaven(DocumentVectorShapes.TransformPoint(shape, c1)), ToHaven(end)),
                        DocumentVectorSegmentKind.Cubic when node.Control1 is { } c1 && node.Control2 is { } c2 => new HavenCubicBezierSegment(ToHaven(DocumentVectorShapes.TransformPoint(shape, c1)), ToHaven(DocumentVectorShapes.TransformPoint(shape, c2)), ToHaven(end)),
                        _ => new HavenLineSegment(ToHaven(end))
                    });
                }
                figures.Add(new HavenPathFigure(ToHaven(first), segments, subpath.Closed));
            }
            if (figures.Count == 0) continue;
            var geometry = new HavenGeometry(new HavenPath(figures, path.FillRule == DocumentVectorFillRule.NonZero ? HavenFillRule.NonZero : HavenFillRule.EvenOdd), new HavenRect(shape.ViewBox.X, shape.ViewBox.Y, shape.ViewBox.Width, shape.ViewBox.Height));
            HavenBrush? fill = path.Fill.Kind == DocumentVectorFillKind.None ? null : Brush(path.Fill.Color, "AccentSubtle", path.Fill.Opacity); var stroke = path.Stroke.Enabled && path.Stroke.Width > 0 ? new HavenPen(Brush(path.Stroke.Color, "Accent", path.Stroke.Opacity), path.Stroke.Width) : null; context.Add(new HavenGeometryCommand(rect, geometry, fill, stroke, opacity * path.Opacity));
        }
    }
    private DocumentVectorShape BuildPreviewVector(PresentElement element, DocumentVectorShape source)
    {
        if (_dragVectorHandle is not { } handle || handle.ElementId != element.Id
            || !TryLocalToVectorPoint(element.Id, _pointerCurrent, out var point)) return source;
        var editor = new DocumentVectorShapeEditor(DocumentVectorShapes.Clone(source));
        if (handle.Kind == PresentVectorHandleKind.Node) editor.MoveNode(handle.NodeId, point.X, point.Y);
        else editor.MoveControlPoint(handle.NodeId, handle.Kind == PresentVectorHandleKind.Control1 ? 1 : 2, point.X, point.Y);
        return editor.Shape;
    }

    private static void DrawVectorHandles(HavenDrawingContext context, DocumentVectorShape shape, HavenRect rect, double opacity)
    {
        var guidePen = new HavenPen(new HavenTokenBrush("Accent"), 1);
        foreach (var subpath in shape.Paths.SelectMany(path => path.Subpaths))
        {
            for (var index = 0; index < subpath.Nodes.Count; index++)
            {
                var node = subpath.Nodes[index];
                var nodePoint = VectorPointToScreen(shape, node.Point, rect);
                context.Add(new HavenEllipseCommand(new HavenRect(nodePoint.X - 4, nodePoint.Y - 4, 8, 8), new HavenTokenBrush("Accent"), null, opacity));
                if (index == 0) continue;
                var previous = VectorPointToScreen(shape, subpath.Nodes[index - 1].Point, rect);
                if (node.Control1 is { } control1 && node.IncomingSegment != DocumentVectorSegmentKind.Line)
                {
                    var firstControl = VectorPointToScreen(shape, control1, rect);
                    context.Add(new HavenLineCommand(previous, firstControl, guidePen, opacity));
                    if (node.IncomingSegment == DocumentVectorSegmentKind.Quadratic) context.Add(new HavenLineCommand(firstControl, nodePoint, guidePen, opacity));
                    context.Add(new HavenEllipseCommand(new HavenRect(firstControl.X - 3, firstControl.Y - 3, 6, 6), new HavenTokenBrush("SurfaceRaised"), guidePen, opacity));
                }
                if (node.Control2 is { } control2 && node.IncomingSegment == DocumentVectorSegmentKind.Cubic)
                {
                    var secondControl = VectorPointToScreen(shape, control2, rect);
                    context.Add(new HavenLineCommand(nodePoint, secondControl, guidePen, opacity));
                    context.Add(new HavenEllipseCommand(new HavenRect(secondControl.X - 3, secondControl.Y - 3, 6, 6), new HavenTokenBrush("SurfaceRaised"), guidePen, opacity));
                }
            }
        }
    }

    private VectorHandleHit? HitVectorHandle(HavenPoint local)
    {
        if (_slide is null || _selectedIds.Count != 1) return null;
        var element = _slide.Elements.FirstOrDefault(value => _selectedIds.Contains(value.Id) && !value.Locked && value.VectorShape is not null);
        if (element?.VectorShape is not { } shape) return null;
        var rect = ElementRect(element, SlideRectLocal(), false);
        var point = InverseRotatePoint(local, rect, element.RotationDegrees);
        VectorHandleHit? best = null;
        var bestDistance = 100d;
        void Consider(Guid nodeId, PresentVectorHandleKind kind, DocumentVectorPoint source)
        {
            var screen = VectorPointToScreen(shape, source, rect);
            var distance = DistanceSquared(point, screen);
            if (distance >= bestDistance) return;
            bestDistance = distance;
            best = new VectorHandleHit(element.Id, nodeId, kind);
        }
        foreach (var node in shape.Paths.SelectMany(path => path.Subpaths).SelectMany(subpath => subpath.Nodes))
        {
            Consider(node.Id, PresentVectorHandleKind.Node, node.Point);
            if (node.Control1 is { } control1 && node.IncomingSegment != DocumentVectorSegmentKind.Line) Consider(node.Id, PresentVectorHandleKind.Control1, control1);
            if (node.Control2 is { } control2 && node.IncomingSegment == DocumentVectorSegmentKind.Cubic) Consider(node.Id, PresentVectorHandleKind.Control2, control2);
        }
        return best;
    }

    private bool TryLocalToVectorPoint(Guid elementId, HavenPoint local, out DocumentVectorPoint point)
    {
        point = new DocumentVectorPoint(0, 0);
        if (_slide?.Elements.FirstOrDefault(value => value.Id == elementId) is not { VectorShape: { } shape } element) return false;
        var rect = ElementRect(element, SlideRectLocal(), false);
        if (rect.Width <= 0.0001 || rect.Height <= 0.0001) return false;
        var unrotated = InverseRotatePoint(local, rect, element.RotationDegrees);
        var transformed = new DocumentVectorPoint(
            shape.ViewBox.X + (unrotated.X - rect.X) / rect.Width * shape.ViewBox.Width,
            shape.ViewBox.Y + (unrotated.Y - rect.Y) / rect.Height * shape.ViewBox.Height);
        point = DocumentVectorShapes.InverseTransformPoint(shape, transformed);
        return double.IsFinite(point.X) && double.IsFinite(point.Y);
    }

    private static HavenPoint VectorPointToScreen(DocumentVectorShape shape, DocumentVectorPoint point, HavenRect rect)
    {
        var transformed = DocumentVectorShapes.TransformPoint(shape, point);
        return new HavenPoint(
            rect.X + (transformed.X - shape.ViewBox.X) / shape.ViewBox.Width * rect.Width,
            rect.Y + (transformed.Y - shape.ViewBox.Y) / shape.ViewBox.Height * rect.Height);
    }

    private static HavenPoint InverseRotatePoint(HavenPoint point, HavenRect rect, double degrees)
    {
        if (Math.Abs(degrees) < 0.001) return point;
        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        var radians = -degrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var x = point.X - centerX;
        var y = point.Y - centerY;
        return new HavenPoint(x * cos - y * sin + centerX, x * sin + y * cos + centerY);
    }

    private static double DistanceSquared(HavenPoint left, HavenPoint right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        return x * x + y * y;
    }

    private readonly record struct VectorHandleHit(Guid ElementId, Guid NodeId, PresentVectorHandleKind Kind);

    private PresentElement? HitElement(HavenPoint local)
    {
        if (_slide is null) return null;
        var slideRect = SlideRectLocal();
        if (!slideRect.Contains(local)) return null;
        foreach (var element in _slide.Elements.Where(element => element.Visible && element.Kind != PresentElementKind.Group).OrderByDescending(element => element.Order))
        {
            var rect = ElementRect(element, slideRect, false);
            if (rect.Contains(InverseRotatePoint(local, rect, element.RotationDegrees))) return element;
        }
        return null;
    }

    private HavenRect SlideRectAbsolute() { var local = SlideRectLocal(); return new HavenRect(Bounds.X + local.X, Bounds.Y + local.Y, local.Width, local.Height); }
    private HavenRect SlideRectLocal()
    {
        var ratio = _document?.SlideSize is { HeightInches: > 0 } size ? size.WidthInches / size.HeightInches : 16d / 9d; var availableWidth = Math.Max(1, Bounds.Width - 32); var availableHeight = Math.Max(1, Bounds.Height - 32); var width = Math.Min(availableWidth, availableHeight * ratio); var height = width / ratio; return new HavenRect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
    }

    private HavenRect ElementRect(PresentElement element, HavenRect slideRect, bool preview)
    {
        var dx = preview ? _pointerCurrent.X - _pointerStart.X : 0; var dy = preview ? _pointerCurrent.Y - _pointerStart.Y : 0; return new HavenRect(slideRect.X + element.X * slideRect.Width + dx, slideRect.Y + element.Y * slideRect.Height + dy, element.Width * slideRect.Width, element.Height * slideRect.Height);
    }

    private static HavenPoint ToHaven(DocumentVectorPoint point) => new(point.X, point.Y);
    private static HavenBrush BackgroundBrush(PresentDocument document, PresentSlide slide)
    {
        var themeColor = document.Theme.Colors.Background;
        if (slide.Background.Kind == PresentBackgroundKind.Solid) return Brush(slide.Background.Color, themeColor);
        if (slide.Background.Kind == PresentBackgroundKind.Theme && document.Theme.Background.Kind == PresentBackgroundKind.Solid)
            return Brush(document.Theme.Background.Color, themeColor);
        return Brush(themeColor, "Surface");
    }

    private static HavenBrush Brush(string? value, string fallback, double opacity = 1)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim(); if (!text.StartsWith('#')) return new HavenTokenBrush(text);
        var hex = text[1..]; if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed)) return new HavenTokenBrush(fallback);
        byte a, r, g, b; if (hex.Length == 8) { a = (byte)(packed >> 24); r = (byte)(packed >> 16); g = (byte)(packed >> 8); b = (byte)packed; } else { a = 255; r = (byte)(packed >> 16); g = (byte)(packed >> 8); b = (byte)packed; } a = (byte)Math.Clamp(a * opacity, 0, 255); return new HavenSolidBrush(a, r, g, b);
    }
}
