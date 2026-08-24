using System.Globalization;
using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Present;

internal sealed class PresentThumbnailNavigator : HavenElement, IHavenDrawCommandSource, IHavenPointerInputTarget, IHavenScrollInputTarget, IHavenKeyboardInputTarget
{
    private const double HorizontalPadding = 10;
    private const double VerticalPadding = 8;
    private const double ItemGap = 12;
    private const double LabelHeight = 22;
    private const double DragThresholdSquared = 25;

    private PresentDocument? _document;
    private int _selectedIndex;
    private double _scrollOffset;
    private int? _pressedIndex;
    private HavenPoint _pressPoint;
    private bool _dragging;
    private int _dropIndex = -1;

    public PresentThumbnailNavigator()
    {
        Name = "Present.Slides.Navigator";
        Accessibility.Role = HavenAccessibleRole.List;
        Accessibility.Focusable = true;
        Accessibility.AccessibleName = "Slide thumbnails";
        Accessibility.Description = "Scroll, select, and drag slides to reorder them.";
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Height, HavenLength.Percent(100));
        SetValue(HavenProperties.MinHeight, HavenLength.Px(300));
        SetValue(HavenProperties.Clip, true);
        SetValue(HavenProperties.Background, "SurfaceRaised");
    }

    public event Action<int>? SlideSelected;
    public event Action<int, int>? SlideReorderRequested;

    public int SelectedIndex => _selectedIndex;
    public double ScrollOffset => _scrollOffset;
    public int SlideCount => _document?.Slides.Count ?? 0;

    public void SetDocument(PresentDocument document, int selectedIndex)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _document.Normalize();
        _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, _document.Slides.Count - 1));
        _pressedIndex = null;
        _dragging = false;
        _dropIndex = -1;
        ClampScroll();
        EnsureSelectedVisible();
        Accessibility.Description = $"{_document.Slides.Count} slides. Slide {_selectedIndex + 1} selected. Scroll, select, or drag a thumbnail to reorder.";
        Invalidate();
    }

    public bool PointerPressed(HavenPointerInput input)
    {
        if (_document is null || _document.Slides.Count == 0) return false;
        var index = IndexAt(input.LocalPosition);
        if (index < 0) return false;
        _pressedIndex = index;
        _pressPoint = input.LocalPosition;
        _dragging = false;
        _dropIndex = index;
        Invalidate();
        return true;
    }

    public bool PointerMoved(HavenPointerInput input)
    {
        if (_pressedIndex is null || _document is null) return false;
        var dx = input.LocalPosition.X - _pressPoint.X;
        var dy = input.LocalPosition.Y - _pressPoint.Y;
        if (!_dragging && dx * dx + dy * dy < DragThresholdSquared) return true;
        _dragging = true;
        AutoScroll(input.LocalPosition.Y);
        _dropIndex = DropIndexAt(input.LocalPosition.Y);
        Invalidate();
        return true;
    }

    public bool PointerReleased(HavenPointerInput input)
    {
        if (_pressedIndex is not { } pressed || _document is null) return false;
        var wasDragging = _dragging;
        var target = wasDragging ? DropIndexAt(input.LocalPosition.Y) : pressed;
        _pressedIndex = null;
        _dragging = false;
        _dropIndex = -1;
        if (wasDragging)
        {
            if (target >= 0 && target != pressed) SlideReorderRequested?.Invoke(pressed, target);
        }
        else
        {
            _selectedIndex = pressed;
            SlideSelected?.Invoke(pressed);
        }
        Invalidate();
        return true;
    }

    public bool PointerWheel(HavenPoint localPosition, double deltaX, double deltaY)
    {
        if (_document is null || _document.Slides.Count == 0 || Math.Abs(deltaY) < .001) return false;
        var before = _scrollOffset;
        _scrollOffset -= deltaY * 44;
        ClampScroll();
        if (Math.Abs(before - _scrollOffset) < .001) return false;
        Invalidate();
        return true;
    }

    public bool KeyDown(HavenKeyInput input)
    {
        if (_document is null || _document.Slides.Count == 0) return false;
        if (input.PrimaryModifier && input.Key == HavenKey.Up) return RequestKeyboardReorder(-1);
        if (input.PrimaryModifier && input.Key == HavenKey.Down) return RequestKeyboardReorder(1);
        return input.Key switch
        {
            HavenKey.Up => SelectKeyboardSlide(_selectedIndex - 1),
            HavenKey.Down => SelectKeyboardSlide(_selectedIndex + 1),
            HavenKey.Home => SelectKeyboardSlide(0),
            HavenKey.End => SelectKeyboardSlide(_document.Slides.Count - 1),
            _ => false
        };
    }

    public bool KeyUp(HavenKeyInput input) => input.Key is HavenKey.Up or HavenKey.Down or HavenKey.Home or HavenKey.End;

    private bool SelectKeyboardSlide(int index)
    {
        if (_document is null || _document.Slides.Count == 0) return false;
        var target = Math.Clamp(index, 0, _document.Slides.Count - 1);
        if (target == _selectedIndex) return true;
        _selectedIndex = target;
        EnsureSelectedVisible();
        UpdateAccessibilityDescription();
        SlideSelected?.Invoke(target);
        Invalidate();
        return true;
    }

    private bool RequestKeyboardReorder(int delta)
    {
        if (_document is null || _document.Slides.Count < 2) return false;
        var target = Math.Clamp(_selectedIndex + delta, 0, _document.Slides.Count - 1);
        if (target == _selectedIndex) return true;
        var from = _selectedIndex;
        _selectedIndex = target;
        EnsureSelectedVisible();
        UpdateAccessibilityDescription();
        SlideReorderRequested?.Invoke(from, target);
        Invalidate();
        return true;
    }

    private void UpdateAccessibilityDescription()
    {
        var count = _document?.Slides.Count ?? 0;
        Accessibility.Description = count == 0
            ? "No slides."
            : $"{count} slides. Slide {_selectedIndex + 1} selected. Use Up and Down to select, or Control plus Up and Down to reorder.";
    }

    public void Draw(HavenDrawingContext context, double opacity)
    {
        if (_document is null || Bounds.Width <= 1 || Bounds.Height <= 1) return;
        var itemWidth = Math.Max(40, Bounds.Width - HorizontalPadding * 2);
        var thumbHeight = ThumbnailHeight(itemWidth);
        var itemHeight = thumbHeight + LabelHeight;
        var stride = itemHeight + ItemGap;
        var first = Math.Max(0, (int)Math.Floor((_scrollOffset - VerticalPadding) / stride));
        var last = Math.Min(_document.Slides.Count - 1, (int)Math.Ceiling((_scrollOffset + Bounds.Height) / stride));
        context.Add(new HavenPushClipCommand(Bounds));
        for (var index = first; index <= last; index++)
        {
            var localY = VerticalPadding + index * stride - _scrollOffset;
            var itemRect = new HavenRect(Bounds.X + HorizontalPadding, Bounds.Y + localY, itemWidth, itemHeight);
            if (itemRect.Bottom < Bounds.Y || itemRect.Y > Bounds.Bottom) continue;
            DrawThumbnail(context, _document.Slides[index], index, itemRect, opacity);
        }
        if (_dragging && _dropIndex >= 0) DrawInsertionMarker(context, itemWidth, itemHeight, stride, opacity);
        DrawScrollbar(context, opacity);
        context.Add(new HavenPopClipCommand(Bounds));
    }

    private void DrawThumbnail(HavenDrawingContext context, PresentSlide slide, int index, HavenRect itemRect, double opacity)
    {
        var thumbRect = new HavenRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height - LabelHeight);
        var selected = index == _selectedIndex;
        if (selected)
            context.Add(new HavenFillRoundedRectCommand(new HavenRect(itemRect.X - 4, itemRect.Y - 4, itemRect.Width + 8, itemRect.Height + 6), new HavenTokenBrush("AccentSubtle"), 10, opacity));
        context.Add(new HavenShadowCommand(thumbRect, new HavenShadow(new HavenSolidBrush(55, 0, 0, 0), 8, 0, 2, 0, .22), 5));
        context.Add(new HavenFillRoundedRectCommand(thumbRect, SlideBackground(slide), 4, opacity));
        context.Add(new HavenStrokeRoundedRectCommand(thumbRect, new HavenPen(new HavenTokenBrush(selected ? "Accent" : "Border"), selected ? 2 : 1), 4, opacity));

        var titleRect = new HavenRect(thumbRect.X + thumbRect.Width * .07, thumbRect.Y + thumbRect.Height * .06, thumbRect.Width * .86, thumbRect.Height * .16);
        var title = string.IsNullOrWhiteSpace(slide.Title) ? "Untitled slide" : slide.Title.Trim();
        context.Add(new HavenTextCommand(titleRect, new HavenTextLayout(title, "Segoe UI", Math.Max(5, thumbRect.Height * .07), 700, titleRect.Width, false), new HavenTokenBrush("TextPrimary"), opacity));

        foreach (var element in slide.Elements.Where(value => value.Visible && value.Kind != PresentElementKind.Group).OrderBy(value => value.Order))
        {
            var rect = new HavenRect(
                thumbRect.X + element.X * thumbRect.Width,
                thumbRect.Y + element.Y * thumbRect.Height,
                Math.Max(1, element.Width * thumbRect.Width),
                Math.Max(1, element.Height * thumbRect.Height));
            switch (element.Kind)
            {
                case PresentElementKind.Text:
                    if (!string.IsNullOrWhiteSpace(element.Text))
                        context.Add(new HavenTextCommand(rect, new HavenTextLayout(element.Text, "Segoe UI", Math.Max(4, thumbRect.Height * .045), element.TextStyle.Bold ? 700 : 400, rect.Width, false), new HavenTokenBrush("TextPrimary"), opacity * element.Opacity));
                    break;
                case PresentElementKind.Shape:
                    context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("AccentSubtle"), Math.Min(4, rect.Height / 4), opacity * element.Opacity));
                    context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenTokenBrush("Accent"), 1), Math.Min(4, rect.Height / 4), opacity * element.Opacity));
                    break;
                case PresentElementKind.Image:
                    context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("SurfaceRaised"), 3, opacity * element.Opacity));
                    context.Add(new HavenIconCommand(new HavenRect(rect.X + rect.Width * .35, rect.Y + rect.Height * .25, rect.Width * .3, rect.Height * .5), "image", new HavenTokenBrush("TextSecondary"), opacity));
                    break;
                case PresentElementKind.Media:
                    context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("SurfaceRaised"), 3, opacity * element.Opacity));
                    context.Add(new HavenIconCommand(new HavenRect(rect.X + rect.Width * .35, rect.Y + rect.Height * .25, rect.Width * .3, rect.Height * .5), "play", new HavenTokenBrush("TextSecondary"), opacity));
                    break;
                case PresentElementKind.GenUi:
                    context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("SurfaceRaised"), 3, opacity * element.Opacity));
                    break;
            }
        }

        var labelRect = new HavenRect(itemRect.X, thumbRect.Bottom + 3, itemRect.Width, LabelHeight - 3);
        context.Add(new HavenTextCommand(labelRect, new HavenTextLayout($"{index + 1}  {TrimLabel(title)}", "Segoe UI", 11, selected ? 700 : 500, labelRect.Width, false), new HavenTokenBrush(selected ? "TextPrimary" : "TextSecondary"), opacity));
    }

    private void DrawInsertionMarker(HavenDrawingContext context, double itemWidth, double itemHeight, double stride, double opacity)
    {
        var y = VerticalPadding + _dropIndex * stride - _scrollOffset;
        if (_pressedIndex is { } pressed && _dropIndex > pressed) y += itemHeight;
        var start = new HavenPoint(Bounds.X + HorizontalPadding - 2, Bounds.Y + y - ItemGap / 2);
        var end = new HavenPoint(Bounds.X + HorizontalPadding + itemWidth + 2, start.Y);
        context.Add(new HavenLineCommand(start, end, new HavenPen(new HavenTokenBrush("Accent"), 3), opacity));
    }

    private void DrawScrollbar(HavenDrawingContext context, double opacity)
    {
        var contentHeight = ContentHeight();
        if (contentHeight <= Bounds.Height + 1) return;
        var trackHeight = Math.Max(20, Bounds.Height - 12);
        var thumbHeight = Math.Max(28, trackHeight * Bounds.Height / contentHeight);
        var maxScroll = Math.Max(1, contentHeight - Bounds.Height);
        var thumbY = Bounds.Y + 6 + (trackHeight - thumbHeight) * (_scrollOffset / maxScroll);
        context.Add(new HavenFillRoundedRectCommand(new HavenRect(Bounds.Right - 5, thumbY, 3, thumbHeight), new HavenTokenBrush("TextSecondary"), 2, opacity * .45));
    }

    private int IndexAt(HavenPoint local)
    {
        if (_document is null) return -1;
        var itemWidth = Math.Max(40, Bounds.Width - HorizontalPadding * 2);
        var stride = ThumbnailHeight(itemWidth) + LabelHeight + ItemGap;
        var contentY = local.Y + _scrollOffset - VerticalPadding;
        if (contentY < 0) return -1;
        var index = (int)(contentY / stride);
        if (index < 0 || index >= _document.Slides.Count) return -1;
        var within = contentY - index * stride;
        return within <= stride - ItemGap ? index : -1;
    }

    private int DropIndexAt(double localY)
    {
        if (_document is null || _document.Slides.Count == 0) return -1;
        var itemWidth = Math.Max(40, Bounds.Width - HorizontalPadding * 2);
        var stride = ThumbnailHeight(itemWidth) + LabelHeight + ItemGap;
        var contentY = localY + _scrollOffset - VerticalPadding;
        var index = (int)Math.Floor((contentY + stride / 2) / stride);
        return Math.Clamp(index, 0, _document.Slides.Count - 1);
    }

    private void AutoScroll(double localY)
    {
        if (Bounds.Height <= 1) return;
        const double edge = 44;
        if (localY < edge) _scrollOffset -= Math.Min(24, edge - localY);
        else if (localY > Bounds.Height - edge) _scrollOffset += Math.Min(24, localY - (Bounds.Height - edge));
        ClampScroll();
    }

    private void EnsureSelectedVisible()
    {
        if (_document is null || Bounds.Height <= 1) return;
        var itemWidth = Math.Max(40, Bounds.Width - HorizontalPadding * 2);
        var itemHeight = ThumbnailHeight(itemWidth) + LabelHeight;
        var stride = itemHeight + ItemGap;
        var top = VerticalPadding + _selectedIndex * stride;
        var bottom = top + itemHeight;
        if (top < _scrollOffset) _scrollOffset = top;
        else if (bottom > _scrollOffset + Bounds.Height) _scrollOffset = bottom - Bounds.Height;
        ClampScroll();
    }

    private void ClampScroll()
    {
        _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, ContentHeight() - Math.Max(0, Bounds.Height)));
    }

    private double ContentHeight()
    {
        if (_document is null || _document.Slides.Count == 0) return 0;
        var itemWidth = Math.Max(40, Bounds.Width - HorizontalPadding * 2);
        var itemHeight = ThumbnailHeight(itemWidth) + LabelHeight;
        return VerticalPadding * 2 + _document.Slides.Count * itemHeight + Math.Max(0, _document.Slides.Count - 1) * ItemGap;
    }

    private static double ThumbnailHeight(double itemWidth) => Math.Max(36, itemWidth * 9d / 16d);

    private HavenBrush SlideBackground(PresentSlide slide)
    {
        if (_document is null) return new HavenTokenBrush("Surface");
        if (slide.Background.Kind == PresentBackgroundKind.Solid) return Brush(slide.Background.Color, "Surface");
        if (slide.Background.Kind == PresentBackgroundKind.Theme && _document.Theme.Background.Kind == PresentBackgroundKind.Solid)
            return Brush(_document.Theme.Background.Color, "Surface");
        return Brush(_document.Theme.Colors.Background, "Surface");
    }

    private static HavenBrush Brush(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (!text.StartsWith('#')) return new HavenTokenBrush(text);
        var hex = text[1..];
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed)) return new HavenTokenBrush(fallback);
        return hex.Length == 8
            ? new HavenSolidBrush((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed)
            : new HavenSolidBrush(255, (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }

    private static string TrimLabel(string value)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 20 ? normalized : normalized[..17] + "…";
    }
}
