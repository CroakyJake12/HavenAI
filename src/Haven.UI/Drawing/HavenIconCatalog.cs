namespace Haven.UI;

/// <summary>
/// Minimal canonical line-icon geometry used by the Haven renderer. Geometry
/// is expressed in a 24x24 Haven view box; platform backends only rasterise it.
/// Unknown keys deliberately return a visible fallback instead of disappearing.
/// </summary>
public static class HavenIconCatalog
{
    private static readonly HavenRect ViewBox = new(0, 0, 24, 24);

    public static HavenGeometry Resolve(string? key) => (key ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "plus" or "add" => Geometry(Line((12, 4), (12, 20)), Line((4, 12), (20, 12))),
        "close" or "x" => Geometry(Line((5, 5), (19, 19)), Line((19, 5), (5, 19))),
        "check" => Geometry(Line((4, 13), (9, 18), (20, 6))),
        "arrow-up" => Geometry(Line((5, 12), (12, 5), (19, 12)), Line((12, 5), (12, 20))),
        "search" => Geometry(Circle(10, 10, 6), Line((14.5, 14.5), (20, 20))),
        "chat" => Geometry(new HavenPathFigure(new HavenPoint(4, 5),
            [new HavenLineSegment(new HavenPoint(20, 5)), new HavenLineSegment(new HavenPoint(20, 16)), new HavenLineSegment(new HavenPoint(11, 16)), new HavenLineSegment(new HavenPoint(6, 20)), new HavenLineSegment(new HavenPoint(6, 16)), new HavenLineSegment(new HavenPoint(4, 16))], true)),
        "folder" => Geometry(new HavenPathFigure(new HavenPoint(3, 6),
            [new HavenLineSegment(new HavenPoint(10, 6)), new HavenLineSegment(new HavenPoint(12, 9)), new HavenLineSegment(new HavenPoint(21, 9)), new HavenLineSegment(new HavenPoint(21, 19)), new HavenLineSegment(new HavenPoint(3, 19))], true)),
        "refresh" => Geometry(Line((19, 8), (19, 4), (15, 4)), Line((19, 4), (16, 7)), Arc((16, 7), (6, 17), (8, 8)), Line((5, 16), (5, 20), (9, 20))),
        "study" => Geometry(new HavenPathFigure(new HavenPoint(3, 5),
            [new HavenLineSegment(new HavenPoint(10, 7)), new HavenLineSegment(new HavenPoint(10, 20)), new HavenLineSegment(new HavenPoint(3, 18))], true),
            new HavenPathFigure(new HavenPoint(21, 5),
            [new HavenLineSegment(new HavenPoint(14, 7)), new HavenLineSegment(new HavenPoint(14, 20)), new HavenLineSegment(new HavenPoint(21, 18))], true)),
        "file" or "notes" => Geometry(new HavenPathFigure(new HavenPoint(6, 3),
            [new HavenLineSegment(new HavenPoint(15, 3)), new HavenLineSegment(new HavenPoint(20, 8)), new HavenLineSegment(new HavenPoint(20, 21)), new HavenLineSegment(new HavenPoint(6, 21))], true),
            Line((15, 3), (15, 8), (20, 8)), Line((9, 12), (17, 12)), Line((9, 16), (16, 16))),
        "agents" => Geometry(Circle(8, 8, 3), Circle(16.5, 8.5, 2.5), Line((3, 20), (3, 17), (5, 14), (11, 14), (13, 17), (13, 20)), Line((14, 14), (19, 14), (21, 17), (21, 20))),
        "bolt" or "rapid" or "build" => Geometry(new HavenPathFigure(new HavenPoint(13, 2),
            [new HavenLineSegment(new HavenPoint(5, 13)), new HavenLineSegment(new HavenPoint(11, 13)), new HavenLineSegment(new HavenPoint(10, 22)), new HavenLineSegment(new HavenPoint(19, 10)), new HavenLineSegment(new HavenPoint(13, 10))], true)),
        "prompt" => Geometry(Circle(12, 9, 6), Line((9, 15), (9, 18), (15, 18), (15, 15)), Line((10, 21), (14, 21))),
        "rocket" => Geometry(new HavenPathFigure(new HavenPoint(14, 3),
            [new HavenLineSegment(new HavenPoint(21, 3)), new HavenLineSegment(new HavenPoint(21, 10)), new HavenLineSegment(new HavenPoint(13, 18)), new HavenLineSegment(new HavenPoint(8, 16)), new HavenLineSegment(new HavenPoint(6, 11))], true), Circle(16.5, 7.5, 1.8), Line((7, 15), (3, 21), (9, 19))),
        "browse" => Geometry(Circle(12, 12, 9), Line((3, 12), (21, 12)), Line((12, 3), (12, 21)), Arc((12, 3), (12, 21), (5, 9)), Arc((12, 21), (12, 3), (5, 9))),
        "tasks" or "plan" => Geometry(Line((5, 5), (8, 5), (8, 8), (5, 8), (5, 5)), Line((11, 6.5), (20, 6.5)), Line((5, 11), (8, 11), (8, 14), (5, 14), (5, 11)), Line((11, 12.5), (20, 12.5)), Line((5, 17), (8, 17), (8, 20), (5, 20), (5, 17)), Line((11, 18.5), (18, 18.5))),
        "studio" => Geometry(Line((9, 5), (3, 12), (9, 19)), Line((15, 5), (21, 12), (15, 19)), Line((13, 4), (11, 20))),
        "test" or "experiment" => Geometry(Line((9, 3), (15, 3)), Line((11, 3), (11, 9), (5, 20), (19, 20), (13, 9), (13, 3)), Line((8, 15), (16, 15))),
        "bookmark" => Geometry(new HavenPathFigure(new HavenPoint(7, 3),
            [new HavenLineSegment(new HavenPoint(17, 3)), new HavenLineSegment(new HavenPoint(17, 21)), new HavenLineSegment(new HavenPoint(12, 17)), new HavenLineSegment(new HavenPoint(7, 21))], true)),
        _ => Geometry(new HavenPathFigure(new HavenPoint(4, 4),
            [new HavenLineSegment(new HavenPoint(20, 4)), new HavenLineSegment(new HavenPoint(20, 20)), new HavenLineSegment(new HavenPoint(4, 20))], true), Line((7, 7), (17, 17)), Line((17, 7), (7, 17)))
    };

    private static HavenGeometry Geometry(params HavenPathFigure[] figures) => new(new HavenPath(figures), ViewBox);

    private static HavenPathFigure Line(params (double X, double Y)[] points) => new(
        new HavenPoint(points[0].X, points[0].Y),
        points.Skip(1).Select(point => (HavenPathSegment)new HavenLineSegment(new HavenPoint(point.X, point.Y))).ToArray());

    private static HavenPathFigure Circle(double centerX, double centerY, double radius) => new(
        new HavenPoint(centerX + radius, centerY),
        [
            new HavenArcSegment(new HavenPoint(centerX - radius, centerY), new HavenSize(radius, radius), SweepDirection: HavenSweepDirection.Clockwise),
            new HavenArcSegment(new HavenPoint(centerX + radius, centerY), new HavenSize(radius, radius), SweepDirection: HavenSweepDirection.Clockwise)
        ],
        true);

    private static HavenPathFigure Arc((double X, double Y) start, (double X, double Y) end, (double X, double Y) radius) => new(
        new HavenPoint(start.X, start.Y),
        [new HavenArcSegment(new HavenPoint(end.X, end.Y), new HavenSize(radius.X, radius.Y), SweepDirection: HavenSweepDirection.Clockwise)]);
}
