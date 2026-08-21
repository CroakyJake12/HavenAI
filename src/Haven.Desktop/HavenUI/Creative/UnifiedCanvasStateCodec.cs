using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.HavenUI.Creative;

internal sealed record UnifiedCanvasRestoredState(
    CanvasInteractionController Controller,
    UnifiedCanvasTool Tool,
    bool ShowGrid);

/// <summary>
/// Persists the canonical Canvas model for embedded creative surfaces and migrates
/// the pre-convergence Haven whiteboard state without discarding existing boards.
/// </summary>
internal static class UnifiedCanvasStateCodec
{
    private const int CurrentVersion = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static UnifiedCanvasRestoredState Restore(JsonElement? state)
    {
        if (state is not { ValueKind: JsonValueKind.Object } value) return Empty();

        try
        {
            var version = ReadInt(value, "version", "Version");
            if (version >= CurrentVersion && TryProperty(value, out var boardElement, "board", "Board"))
            {
                var board = JsonSerializer.Deserialize<NotesCanvasData>(boardElement.GetRawText(), JsonOptions) ?? new NotesCanvasData();
                var controller = new CanvasInteractionController(board);
                controller.PenColour = NormaliseColour(ReadString(value, "color", "Color"), "#111111");
                controller.PenWidth = Math.Clamp(ReadDouble(value, 6, "thickness", "Thickness"), 1, 32);
                controller.GridSize = Math.Max(0, ReadDouble(value, 0, "gridSize", "GridSize"));
                var tool = ParseTool(ReadString(value, "tool", "Tool"));
                ApplyControllerTool(controller, tool);
                return new UnifiedCanvasRestoredState(controller, tool, ReadBool(value, false, "showGrid", "ShowGrid"));
            }

            return RestoreLegacy(value);
        }
        catch (JsonException)
        {
            return Empty();
        }
        catch (NotSupportedException)
        {
            return Empty();
        }
    }

    public static JsonElement ToJson(CanvasInteractionController controller, UnifiedCanvasTool tool, bool showGrid)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return JsonSerializer.SerializeToElement(new UnifiedCanvasStateDto
        {
            Version = CurrentVersion,
            Tool = tool.ToString(),
            Color = controller.PenColour,
            Thickness = controller.PenWidth,
            GridSize = controller.GridSize,
            ShowGrid = showGrid,
            Board = CanvasDocumentModel.CloneBoard(controller.Board)
        }, JsonOptions);
    }

    private static UnifiedCanvasRestoredState RestoreLegacy(JsonElement state)
    {
        var board = new NotesCanvasData
        {
            Infinite = true,
            Width = 2400,
            Height = 1800,
            Zoom = Math.Clamp(ReadDouble(state, 1, "zoom", "Zoom"), .1, 6),
            OffsetX = ReadDouble(state, 0, "offsetX", "OffsetX"),
            OffsetY = ReadDouble(state, 0, "offsetY", "OffsetY")
        };

        if (TryProperty(state, out var elements, "elements", "Elements") && elements.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in elements.EnumerateArray().Take(1000)) MigrateLegacyElement(board, element);
        }

        if (board.Strokes.Count == 0 && TryProperty(state, out var strokes, "strokes", "Strokes") && strokes.ValueKind == JsonValueKind.Array)
        {
            foreach (var stroke in strokes.EnumerateArray().Take(1000)) MigrateLegacyStroke(board, stroke);
        }

        var controller = new CanvasInteractionController(board)
        {
            PenColour = NormaliseColour(ReadString(state, "color", "Color"), "#111111"),
            PenWidth = Math.Clamp(ReadDouble(state, 6, "thickness", "Thickness"), 1, 32)
        };
        var tool = ParseTool(ReadString(state, "tool", "Tool"));
        ApplyControllerTool(controller, tool);
        return new UnifiedCanvasRestoredState(controller, tool, ReadBool(state, false, "showGrid", "ShowGrid"));
    }

    private static void MigrateLegacyElement(NotesCanvasData board, JsonElement element)
    {
        var kind = ReadString(element, "kind", "Kind") ?? string.Empty;
        var points = ReadPoints(element).ToArray();
        if (kind.Equals("Stroke", StringComparison.OrdinalIgnoreCase))
        {
            if (points.Length < 2) return;
            board.Strokes.Add(ToStroke(element, points));
            return;
        }
        if (points.Length == 0) return;

        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);
        var text = ReadString(element, "text", "Text") ?? string.Empty;
        var shape = kind.ToLowerInvariant() switch
        {
            "ellipse" => "ellipse",
            "line" => "line",
            _ => "rectangle"
        };
        var objectKind = kind.ToLowerInvariant() switch
        {
            "text" => NotesCanvasObjectKind.Text,
            "image" => NotesCanvasObjectKind.Image,
            _ => NotesCanvasObjectKind.Shape
        };
        var idText = ReadString(element, "id", "Id");
        var value = new NotesCanvasObject
        {
            Id = Guid.TryParse(idText, out var id) ? id : Guid.NewGuid(),
            Kind = objectKind,
            Text = text,
            X = left,
            Y = top,
            Width = Math.Max(objectKind == NotesCanvasObjectKind.Text ? 80 : 1, right - left),
            Height = Math.Max(objectKind == NotesCanvasObjectKind.Text ? 32 : 1, bottom - top),
            ZIndex = board.Objects.Count,
            StyleJson = JsonSerializer.Serialize(new LegacyStyle
            {
                Shape = shape,
                Color = NormaliseColour(ReadString(element, "color", "Color"), "#111111"),
                Thickness = ReadDouble(element, 2, "thickness", "Thickness"),
                Opacity = ReadDouble(element, 1, "opacity", "Opacity"),
                Effect = ReadString(element, "effect", "Effect") ?? "Solid",
                AgentGenerated = ReadBool(element, false, "agentGenerated", "AgentGenerated")
            }, JsonOptions)
        };
        board.Objects.Add(value);
    }

    private static void MigrateLegacyStroke(NotesCanvasData board, JsonElement stroke)
    {
        var points = ReadPoints(stroke).ToArray();
        if (points.Length < 2) return;
        board.Strokes.Add(ToStroke(stroke, points));
    }

    private static NotesInkStroke ToStroke(JsonElement source, IReadOnlyList<LegacyPoint> points)
    {
        var isEraser = ReadBool(source, false, "isEraser", "IsEraser");
        var opacity = Math.Clamp(ReadDouble(source, 1, "opacity", "Opacity"), .01, 1);
        var colour = isEraser ? "#FFFFFF" : NormaliseColour(ReadString(source, "color", "Color"), "#111111");
        return new NotesInkStroke
        {
            Tool = isEraser ? "eraser" : opacity < .7 ? "highlighter" : "pen",
            Colour = colour,
            BaseWidth = Math.Clamp(ReadDouble(source, 6, "thickness", "Thickness"), .5, 64),
            Opacity = opacity,
            Points = points.Select(point => new NotesInkPoint { X = point.X, Y = point.Y, Pressure = point.Pressure }).ToList()
        };
    }

    private static IEnumerable<LegacyPoint> ReadPoints(JsonElement source)
    {
        if (!TryProperty(source, out var points, "points", "Points") || points.ValueKind != JsonValueKind.Array) yield break;
        foreach (var point in points.EnumerateArray().Take(2000))
        {
            yield return new LegacyPoint(
                ReadDouble(point, 0, "x", "X"),
                ReadDouble(point, 0, "y", "Y"),
                Math.Clamp(ReadDouble(point, .5, "pressure", "Pressure"), 0, 1));
        }
    }

    private static UnifiedCanvasRestoredState Empty()
    {
        var board = new NotesCanvasData { Infinite = true, Width = 2400, Height = 1800, Zoom = 1 };
        var controller = new CanvasInteractionController(board) { PenColour = "#111111", PenWidth = 6 };
        return new UnifiedCanvasRestoredState(controller, UnifiedCanvasTool.Pen, false);
    }

    private static UnifiedCanvasTool ParseTool(string? value) => Enum.TryParse<UnifiedCanvasTool>(value, true, out var tool) ? tool : UnifiedCanvasTool.Pen;

    private static void ApplyControllerTool(CanvasInteractionController controller, UnifiedCanvasTool tool) => controller.Tool = tool switch
    {
        UnifiedCanvasTool.Pen => CanvasTool.Pen,
        UnifiedCanvasTool.Highlighter => CanvasTool.Highlighter,
        UnifiedCanvasTool.Eraser => CanvasTool.Eraser,
        UnifiedCanvasTool.Pan => CanvasTool.Pan,
        _ => CanvasTool.Select
    };

    private static bool TryProperty(JsonElement source, out JsonElement value, params string[] names)
    {
        foreach (var name in names) if (source.TryGetProperty(name, out value)) return true;
        value = default;
        return false;
    }

    private static string? ReadString(JsonElement source, params string[] names) =>
        TryProperty(source, out var value, names) ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString() : null;

    private static int ReadInt(JsonElement source, params string[] names) =>
        TryProperty(source, out var value, names) && value.TryGetInt32(out var result) ? result : 0;

    private static double ReadDouble(JsonElement source, double fallback, params string[] names) =>
        TryProperty(source, out var value, names) && value.TryGetDouble(out var result) && double.IsFinite(result) ? result : fallback;

    private static bool ReadBool(JsonElement source, bool fallback, params string[] names) =>
        TryProperty(source, out var value, names) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;

    private static string NormaliseColour(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var colour = value.Trim();
        return colour.StartsWith('#') && colour.Length is 7 or 9 && colour.Skip(1).All(Uri.IsHexDigit) ? colour.ToUpperInvariant() : fallback;
    }

    private sealed class UnifiedCanvasStateDto
    {
        public int Version { get; set; }
        public string Tool { get; set; } = nameof(UnifiedCanvasTool.Pen);
        public string Color { get; set; } = "#111111";
        public double Thickness { get; set; } = 6;
        public double GridSize { get; set; }
        public bool ShowGrid { get; set; }
        public NotesCanvasData Board { get; set; } = new();
    }

    private sealed class LegacyStyle
    {
        public string Shape { get; set; } = "rectangle";
        public string Color { get; set; } = "#111111";
        public double Thickness { get; set; } = 2;
        public double Opacity { get; set; } = 1;
        public string Effect { get; set; } = "Solid";
        public bool AgentGenerated { get; set; }
    }

    private readonly record struct LegacyPoint(double X, double Y, double Pressure);
}
