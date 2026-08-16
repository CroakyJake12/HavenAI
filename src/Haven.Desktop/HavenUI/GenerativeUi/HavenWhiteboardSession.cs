using System.Text.Json;
using Haven.UI;

namespace Haven.Desktop.HavenUI.GenerativeUi;

internal enum HavenWhiteboardTool
{
    Select,
    Pen,
    Highlighter,
    Eraser,
    Text,
    Rectangle,
    Ellipse,
    Line,
    Pan
}

internal enum HavenWhiteboardElementKind { Stroke, Text, Rectangle, Ellipse, Line, Image }
internal enum HavenWhiteboardPenEffect { Solid, Glow, Dotted }

internal sealed record HavenWhiteboardInkPoint(double X, double Y, double Pressure = 0.5)
{
    public HavenPoint Position => new(X, Y);
}

internal sealed record HavenWhiteboardElement(
    string Id,
    HavenWhiteboardElementKind Kind,
    string Color,
    double Thickness,
    double Opacity,
    HavenWhiteboardPenEffect Effect,
    bool IsEraser,
    bool AgentGenerated,
    string Text,
    IReadOnlyList<HavenWhiteboardInkPoint> Points)
{
    public HavenWhiteboardElement Translate(double dx, double dy) => this with
    {
        Points = Points.Select(point => point with { X = point.X + dx, Y = point.Y + dy }).ToArray()
    };
}

internal sealed class HavenWhiteboardSession
{
    private const int MaximumElements = 250;
    private readonly List<HavenWhiteboardElement> _elements = [];
    private readonly Stack<IReadOnlyList<HavenWhiteboardElement>> _undo = [];
    private readonly Stack<IReadOnlyList<HavenWhiteboardElement>> _redo = [];
    private HavenWhiteboardElement? _clipboard;
    private HavenWhiteboardTool _tool = HavenWhiteboardTool.Pen;
    private HavenWhiteboardPenEffect _effect = HavenWhiteboardPenEffect.Solid;
    private string _color = "#111111";
    private double _thickness = 6;
    private double _zoom = 1;
    private HavenPoint _offset;
    private bool _showGrid;

    public event Action? Changed;
    public event Action? Invalidated;
    public IReadOnlyList<HavenWhiteboardElement> Elements => _elements;
    public string? SelectedId { get; private set; }

    public HavenWhiteboardTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            _tool = value;
            Changed?.Invoke();
            Invalidated?.Invoke();
        }
    }

    public HavenWhiteboardPenEffect Effect
    {
        get => _effect;
        set
        {
            if (_effect == value) return;
            _effect = value;
            Changed?.Invoke();
            Invalidated?.Invoke();
        }
    }

    public string Color
    {
        get => _color;
        set
        {
            if (!TryNormaliseColor(value, out var colour) || _color.Equals(colour, StringComparison.OrdinalIgnoreCase)) return;
            _color = colour;
            Changed?.Invoke();
            Invalidated?.Invoke();
        }
    }

    public double Thickness
    {
        get => _thickness;
        set
        {
            var next = Math.Clamp(value, 2, 32);
            if (Math.Abs(next - _thickness) < .001) return;
            _thickness = next;
            Changed?.Invoke();
            Invalidated?.Invoke();
        }
    }

    public double Zoom => _zoom;
    public HavenPoint Offset => _offset;

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (_showGrid == value) return;
            _showGrid = value;
            Changed?.Invoke();
            Invalidated?.Invoke();
        }
    }

    public void CommitStroke(IReadOnlyList<HavenWhiteboardInkPoint> points)
    {
        if (points.Count < 2) return;
        Commit(new HavenWhiteboardElement(
            Guid.NewGuid().ToString("N"), HavenWhiteboardElementKind.Stroke, _color, _thickness,
            _tool == HavenWhiteboardTool.Highlighter ? 0.34 : 1, _effect, false, false, string.Empty,
            points.Take(700).ToArray()));
    }

    public void CommitShape(HavenWhiteboardElementKind kind, HavenPoint start, HavenPoint end)
    {
        if (Math.Abs(end.X - start.X) < 3 && Math.Abs(end.Y - start.Y) < 3) return;
        Commit(new HavenWhiteboardElement(
            Guid.NewGuid().ToString("N"), kind, _color, _thickness, 1, _effect, false, false, string.Empty,
            [new HavenWhiteboardInkPoint(start.X, start.Y), new HavenWhiteboardInkPoint(end.X, end.Y)]));
    }

    public void CommitText(HavenPoint point, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Commit(new HavenWhiteboardElement(
            Guid.NewGuid().ToString("N"), HavenWhiteboardElementKind.Text, _color, _thickness, 1,
            HavenWhiteboardPenEffect.Solid, false, false, text.Trim(),
            [new HavenWhiteboardInkPoint(point.X, point.Y), new HavenWhiteboardInkPoint(point.X + 240, point.Y + 70)]));
    }

    private void Commit(HavenWhiteboardElement element)
    {
        PushUndo();
        _elements.Add(element);
        while (_elements.Count > MaximumElements) _elements.RemoveAt(0);
        SelectedId = element.Id;
        Changed?.Invoke();
        Invalidated?.Invoke();
    }

    public bool SelectAt(HavenPoint point)
    {
        SelectedId = _elements.LastOrDefault(element => HitTest(element, point, 14 / _zoom))?.Id;
        Invalidated?.Invoke();
        return SelectedId is not null;
    }

    public bool EraseAt(HavenPoint point)
    {
        var element = _elements.LastOrDefault(candidate => HitTest(candidate, point, 18 / _zoom));
        if (element is null) return false;
        PushUndo();
        _elements.Remove(element);
        if (SelectedId == element.Id) SelectedId = null;
        Changed?.Invoke();
        Invalidated?.Invoke();
        return true;
    }

    public HavenWhiteboardElement? SelectedElement() =>
        SelectedId is null ? null : _elements.FirstOrDefault(item => item.Id == SelectedId);

    public void PreviewMove(HavenWhiteboardElement original, double dx, double dy)
    {
        var index = _elements.FindIndex(item => item.Id == original.Id);
        if (index < 0) return;
        _elements[index] = original.Translate(dx, dy);
        Invalidated?.Invoke();
    }

    public void CommitPreview(HavenWhiteboardElement original)
    {
        var current = SelectedElement();
        if (current is null || current == original) return;
        var previous = CloneElements(_elements).ToList();
        var index = previous.FindIndex(item => item.Id == original.Id);
        if (index >= 0)
        {
            previous[index] = Clone(original);
            _undo.Push(previous);
            _redo.Clear();
        }
        Changed?.Invoke();
    }

    public bool UpdateSelectedText(string text)
    {
        var selected = SelectedElement();
        if (selected is null || selected.Kind != HavenWhiteboardElementKind.Text || string.IsNullOrWhiteSpace(text)) return false;
        PushUndo();
        _elements[_elements.IndexOf(selected)] = selected with { Text = text.Trim() };
        Changed?.Invoke();
        Invalidated?.Invoke();
        return true;
    }

    public void DeleteSelected()
    {
        var selected = SelectedElement();
        if (selected is null) return;
        PushUndo();
        _elements.Remove(selected);
        SelectedId = null;
        Changed?.Invoke();
        Invalidated?.Invoke();
    }

    public void CopySelected()
    {
        var selected = SelectedElement();
        if (selected is not null) _clipboard = Clone(selected);
    }

    public void Paste()
    {
        if (_clipboard is null) return;
        var pasted = _clipboard.Translate(24, 24) with { Id = Guid.NewGuid().ToString("N") };
        _clipboard = Clone(pasted);
        Commit(pasted);
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(CloneElements(_elements));
        ReplaceElements(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(CloneElements(_elements));
        ReplaceElements(_redo.Pop());
    }

    public void Clear()
    {
        if (_elements.Count == 0) return;
        PushUndo();
        _elements.Clear();
        SelectedId = null;
        Changed?.Invoke();
        Invalidated?.Invoke();
    }

    public void PanBy(double dx, double dy)
    {
        _offset = new HavenPoint(_offset.X + dx, _offset.Y + dy);
        Changed?.Invoke();
        Invalidated?.Invoke();
    }

    public void ZoomAt(HavenPoint screenPoint, double factor)
    {
        var before = ToBoard(screenPoint);
        _zoom = Math.Clamp(_zoom * factor, 0.25, 4);
        _offset = new HavenPoint(screenPoint.X - before.X * _zoom, screenPoint.Y - before.Y * _zoom);
        Changed?.Invoke();
        Invalidated?.Invoke();
    }

    public void ResetViewport()
    {
        _zoom = 1;
        _offset = default;
        Changed?.Invoke();
        Invalidated?.Invoke();
    }

    public HavenPoint ToBoard(HavenPoint screenPoint) => new(
        (screenPoint.X - _offset.X) / Math.Max(0.05, _zoom),
        (screenPoint.Y - _offset.Y) / Math.Max(0.05, _zoom));

    public JsonElement ToJson() => JsonSerializer.SerializeToElement(new WhiteboardStateDto
    {
        Version = 2,
        Tool = _tool.ToString(),
        Effect = _effect.ToString(),
        Color = _color,
        Thickness = _thickness,
        Zoom = _zoom,
        OffsetX = _offset.X,
        OffsetY = _offset.Y,
        ShowGrid = _showGrid,
        Elements = _elements.Select(ToDto).ToList()
    });

    public static HavenWhiteboardSession Restore(JsonElement? state)
    {
        var session = new HavenWhiteboardSession();
        if (state is not { ValueKind: JsonValueKind.Object }) return session;
        try
        {
            var dto = JsonSerializer.Deserialize<WhiteboardStateDto>(state.Value.GetRawText());
            if (dto is null) return session;
            session._tool = Enum.TryParse<HavenWhiteboardTool>(dto.Tool, true, out var tool) ? tool : HavenWhiteboardTool.Pen;
            session._effect = Enum.TryParse<HavenWhiteboardPenEffect>(dto.Effect, true, out var effect) ? effect : HavenWhiteboardPenEffect.Solid;
            session._color = TryNormaliseColor(dto.Color, out var color) ? color : "#111111";
            session._thickness = Math.Clamp(dto.Thickness <= 0 ? 6 : dto.Thickness, 2, 32);
            session._zoom = Math.Clamp(dto.Zoom <= 0 ? 1 : dto.Zoom, 0.25, 4);
            session._offset = new HavenPoint(dto.OffsetX, dto.OffsetY);
            session._showGrid = dto.ShowGrid;

            foreach (var item in dto.Elements.Take(MaximumElements))
            {
                var element = FromDto(item);
                if (element is not null) session._elements.Add(element);
            }

            if (session._elements.Count == 0)
            {
                foreach (var stroke in dto.Strokes.Take(MaximumElements))
                {
                    var points = stroke.Points.Take(700)
                        .Select(point => new HavenWhiteboardInkPoint(point.X, point.Y, point.Pressure <= 0 ? 0.5 : point.Pressure))
                        .ToArray();
                    if (points.Length < 2) continue;
                    session._elements.Add(new HavenWhiteboardElement(
                        Guid.NewGuid().ToString("N"), HavenWhiteboardElementKind.Stroke, NormaliseLegacyColor(stroke.Color),
                        Math.Clamp(stroke.Thickness <= 0 ? 6 : stroke.Thickness, 2, 32), 1, HavenWhiteboardPenEffect.Solid,
                        stroke.IsEraser, false, string.Empty, points));
                }
            }
        }
        catch (JsonException)
        {
            return new HavenWhiteboardSession();
        }
        return session;
    }

    public static HavenRect BoundsOf(HavenWhiteboardElement element)
    {
        var minX = element.Points.Min(point => point.X);
        var minY = element.Points.Min(point => point.Y);
        var maxX = element.Points.Max(point => point.X);
        var maxY = element.Points.Max(point => point.Y);
        if (element.Kind == HavenWhiteboardElementKind.Text && maxX - minX < 40) maxX = minX + 240;
        if (element.Kind == HavenWhiteboardElementKind.Text && maxY - minY < 24) maxY = minY + 70;
        return new HavenRect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    internal static bool TryNormaliseColor(string? value, out string colour)
    {
        colour = "#111111";
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        if (trimmed.Length == 7 && trimmed[0] == '#' && trimmed.Skip(1).All(Uri.IsHexDigit))
        {
            colour = trimmed.ToUpperInvariant();
            return true;
        }
        return false;
    }

    private void PushUndo()
    {
        _undo.Push(CloneElements(_elements));
        _redo.Clear();
    }

    private void ReplaceElements(IReadOnlyList<HavenWhiteboardElement> elements)
    {
        _elements.Clear();
        _elements.AddRange(CloneElements(elements));
        if (SelectedId is not null && _elements.All(item => item.Id != SelectedId)) SelectedId = null;
        Changed?.Invoke();
        Invalidated?.Invoke();
    }

    private static bool HitTest(HavenWhiteboardElement element, HavenPoint point, double radius)
    {
        if (element.Points.Count == 0) return false;
        if (element.Kind == HavenWhiteboardElementKind.Stroke)
            return element.Points.Any(candidate => DistanceSquared(candidate.Position, point) <= radius * radius);
        var bounds = Inflate(BoundsOf(element), radius);
        if (!bounds.Contains(point)) return false;
        if (element.Kind == HavenWhiteboardElementKind.Line && element.Points.Count > 1)
            return DistanceToSegment(point, element.Points[0].Position, element.Points[1].Position) <= radius;
        return true;
    }

    private static HavenRect Inflate(HavenRect rect, double amount) =>
        new(rect.X - amount, rect.Y - amount, rect.Width + amount * 2, rect.Height + amount * 2);

    private static double DistanceToSegment(HavenPoint point, HavenPoint start, HavenPoint end)
    {
        var lengthSquared = DistanceSquared(start, end);
        if (lengthSquared <= double.Epsilon) return Math.Sqrt(DistanceSquared(point, start));
        var t = Math.Clamp(((point.X - start.X) * (end.X - start.X) + (point.Y - start.Y) * (end.Y - start.Y)) / lengthSquared, 0, 1);
        return Math.Sqrt(DistanceSquared(point, new HavenPoint(start.X + t * (end.X - start.X), start.Y + t * (end.Y - start.Y))));
    }

    private static double DistanceSquared(HavenPoint first, HavenPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    private static string NormaliseLegacyColor(string value) => value.ToLowerInvariant() switch
    {
        "blue" => "#1E88E5",
        "red" => "#E53935",
        "green" => "#43A047",
        "purple" => "#8E24AA",
        "white" => "#FFFFFF",
        _ => TryNormaliseColor(value, out var color) ? color : "#111111"
    };

    private static HavenWhiteboardElement Clone(HavenWhiteboardElement element) => element with
    {
        Points = element.Points.Select(point => point with { }).ToArray()
    };

    private static IReadOnlyList<HavenWhiteboardElement> CloneElements(IEnumerable<HavenWhiteboardElement> elements) =>
        elements.Select(Clone).ToArray();

    private static WhiteboardElementDto ToDto(HavenWhiteboardElement element) => new()
    {
        Id = element.Id,
        Kind = element.Kind.ToString(),
        Color = element.Color,
        Thickness = element.Thickness,
        Opacity = element.Opacity,
        Effect = element.Effect.ToString(),
        IsEraser = element.IsEraser,
        AgentGenerated = element.AgentGenerated,
        Text = element.Text,
        Points = element.Points.Select(point => new WhiteboardPointDto { X = point.X, Y = point.Y, Pressure = point.Pressure }).ToList()
    };

    private static HavenWhiteboardElement? FromDto(WhiteboardElementDto dto)
    {
        if (!Enum.TryParse<HavenWhiteboardElementKind>(dto.Kind, true, out var kind)) return null;
        var points = dto.Points.Take(700)
            .Select(point => new HavenWhiteboardInkPoint(point.X, point.Y, Math.Clamp(point.Pressure <= 0 ? 0.5 : point.Pressure, 0.05, 1)))
            .ToArray();
        if (points.Length == 0) return null;
        return new HavenWhiteboardElement(
            string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString("N") : dto.Id, kind,
            TryNormaliseColor(dto.Color, out var color) ? color : "#111111",
            Math.Clamp(dto.Thickness <= 0 ? 6 : dto.Thickness, 2, 32),
            Math.Clamp(dto.Opacity <= 0 ? 1 : dto.Opacity, 0.1, 1),
            Enum.TryParse<HavenWhiteboardPenEffect>(dto.Effect, true, out var effect) ? effect : HavenWhiteboardPenEffect.Solid,
            dto.IsEraser, dto.AgentGenerated, dto.Text ?? string.Empty, points);
    }

    private sealed class WhiteboardStateDto
    {
        public int Version { get; set; }
        public string Tool { get; set; } = nameof(HavenWhiteboardTool.Pen);
        public string Effect { get; set; } = nameof(HavenWhiteboardPenEffect.Solid);
        public string Color { get; set; } = "#111111";
        public double Thickness { get; set; } = 6;
        public double Zoom { get; set; } = 1;
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public bool ShowGrid { get; set; }
        public List<WhiteboardElementDto> Elements { get; set; } = [];
        public List<WhiteboardStrokeDto> Strokes { get; set; } = [];
    }

    private sealed class WhiteboardElementDto
    {
        public string? Id { get; set; }
        public string Kind { get; set; } = nameof(HavenWhiteboardElementKind.Stroke);
        public string Color { get; set; } = "#111111";
        public double Thickness { get; set; } = 6;
        public double Opacity { get; set; } = 1;
        public string Effect { get; set; } = nameof(HavenWhiteboardPenEffect.Solid);
        public bool IsEraser { get; set; }
        public bool AgentGenerated { get; set; }
        public string? Text { get; set; }
        public List<WhiteboardPointDto> Points { get; set; } = [];
    }

    private sealed class WhiteboardStrokeDto
    {
        public string Color { get; set; } = "#111111";
        public double Thickness { get; set; } = 6;
        public bool IsEraser { get; set; }
        public List<WhiteboardPointDto> Points { get; set; } = [];
    }

    private sealed class WhiteboardPointDto
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Pressure { get; set; } = 0.5;
    }
}
