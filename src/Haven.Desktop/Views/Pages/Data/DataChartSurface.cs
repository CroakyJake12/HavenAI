using System.Globalization;
using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Data;

internal sealed class DataChartSurface : HavenElement, IHavenDrawCommandSource
{
    private static readonly string[] Tokens = ["Accent", "Success", "Warning", "Danger", "TextSecondary"];
    private DataSheet _sheet;
    private DataChartDefinition _chart;

    public DataChartSurface(DataSheet sheet, DataChartDefinition chart)
    {
        _sheet = sheet; _chart = chart; Accessibility.Role = HavenAccessibleRole.Image;
        SetValue(HavenProperties.MinHeight, HavenLength.Px(280)); SetValue(HavenProperties.Width, HavenLength.Percent(100)); SetValue(HavenProperties.Background, "SurfaceRaised"); SetValue(HavenProperties.BorderColor, "Border"); SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16))); SetValue(HavenProperties.Clip, true); RefreshAccessibleName();
    }

    public void Update(DataSheet sheet, DataChartDefinition chart) { _sheet = sheet; _chart = chart; RefreshAccessibleName(); Invalidate(); }

    public void Draw(HavenDrawingContext c, double opacity)
    {
        var r = Bounds; if (r.Width < 100 || r.Height < 120) return;
        Text(c, _chart.Title, new HavenRect(r.X + 18, r.Y + 12, r.Width - 36, 26), 16, 700, "TextPrimary", opacity);
        var series = ReadSeries();
        if (series.Count == 0) { Text(c, "No numeric data in chart range.", new HavenRect(r.X + 18, r.Y + 58, r.Width - 36, 22), 12, 400, "TextSecondary", opacity); return; }
        if (_chart.Type == DataChartType.Pie) { DrawPie(c, r, series[0].Values, opacity); DrawLegend(c, r, series, opacity); return; }

        var left = r.X + 54; var top = r.Y + 54; var right = r.Right - (_chart.ShowLegend ? 126 : 22); var bottom = r.Bottom - 46; if (right <= left || bottom <= top) return;
        var all = series.SelectMany(value => value.Values).Where(double.IsFinite).ToArray(); if (all.Length == 0) return; var min = Math.Min(0, all.Min()); var max = Math.Max(0, all.Max()); if (Math.Abs(max - min) < 1e-9) { min -= 1; max += 1; }
        DrawAxes(c, left, top, right, bottom, min, max, opacity);
        if (_chart.Type == DataChartType.Column) DrawColumns(c, series, left, top, right, bottom, min, max, opacity);
        else if (_chart.Type == DataChartType.Bar) DrawBars(c, series, left, top, right, bottom, min, max, opacity);
        else DrawLines(c, series, left, top, right, bottom, min, max, opacity, _chart.Type == DataChartType.Area, _chart.Type == DataChartType.Scatter);
        if (!string.IsNullOrWhiteSpace(_chart.XAxisTitle)) Text(c, _chart.XAxisTitle, new HavenRect(left, bottom + 19, right - left, 18), 10, 600, "TextSecondary", opacity);
        if (!string.IsNullOrWhiteSpace(_chart.YAxisTitle)) Text(c, _chart.YAxisTitle, new HavenRect(r.X + 8, top - 22, right - left, 18), 10, 600, "TextSecondary", opacity);
        DrawLegend(c, r, series, opacity);
    }

    internal IReadOnlyList<double[]> SnapshotSeriesValues() => ReadSeries().Select(series => series.Values.ToArray()).ToArray();

    private void DrawAxes(HavenDrawingContext c, double left, double top, double right, double bottom, double min, double max, double opacity)
    {
        var grid = new HavenPen(new HavenTokenBrush("Border"), 1);
        for (var index = 0; index <= 4; index++) { var y = top + (bottom - top) * index / 4d; c.Add(new HavenLineCommand(new(left, y), new(right, y), grid, opacity * .45)); var value = max - (max - min) * index / 4d; Text(c, value.ToString("0.##", CultureInfo.InvariantCulture), new HavenRect(left - 48, y - 8, 44, 17), 9, 400, "TextSecondary", opacity); }
        var axis = new HavenPen(new HavenTokenBrush("TextSecondary"), 1.2); c.Add(new HavenLineCommand(new(left, top), new(left, bottom), axis, opacity)); var zero = Y(0, top, bottom, min, max); c.Add(new HavenLineCommand(new(left, zero), new(right, zero), axis, opacity));
    }

    private void DrawColumns(HavenDrawingContext c, List<Series> series, double left, double top, double right, double bottom, double min, double max, double opacity)
    {
        var count = Math.Max(1, series.Max(value => value.Values.Length)); var slot = (right - left) / count; var groupWidth = slot * .76; var width = Math.Max(2, groupWidth / series.Count); var zero = Y(0, top, bottom, min, max);
        for (var s = 0; s < series.Count; s++) for (var i = 0; i < series[s].Values.Length; i++) { var value = series[s].Values[i]; if (!double.IsFinite(value)) continue; var y = Y(value, top, bottom, min, max); var x = left + i * slot + (slot - groupWidth) / 2 + s * width; c.Add(new HavenFillRoundedRectCommand(new(x, Math.Min(zero, y), Math.Max(1, width - 2), Math.Max(1, Math.Abs(zero - y))), new HavenTokenBrush(Token(s)), 3, opacity * .9)); }
    }

    private void DrawBars(HavenDrawingContext c, List<Series> series, double left, double top, double right, double bottom, double min, double max, double opacity)
    {
        var count = Math.Max(1, series.Max(value => value.Values.Length)); var slot = (bottom - top) / count; var groupHeight = slot * .76; var height = Math.Max(2, groupHeight / series.Count); var zero = X(0, left, right, min, max);
        for (var s = 0; s < series.Count; s++) for (var i = 0; i < series[s].Values.Length; i++) { var value = series[s].Values[i]; if (!double.IsFinite(value)) continue; var x = X(value, left, right, min, max); var y = top + i * slot + (slot - groupHeight) / 2 + s * height; c.Add(new HavenFillRoundedRectCommand(new(Math.Min(zero, x), y, Math.Max(1, Math.Abs(zero - x)), Math.Max(1, height - 2)), new HavenTokenBrush(Token(s)), 3, opacity * .9)); }
    }

    private void DrawLines(HavenDrawingContext c, List<Series> series, double left, double top, double right, double bottom, double min, double max, double opacity, bool area, bool scatter)
    {
        for (var s = 0; s < series.Count; s++)
        {
            var values = series[s].Values; var points = new List<HavenPoint>(); HavenPoint? previous = null; var pen = new HavenPen(new HavenTokenBrush(Token(s)), 2);
            for (var i = 0; i < values.Length; i++) { if (!double.IsFinite(values[i])) { previous = null; continue; } var x = values.Length == 1 ? (left + right) / 2 : left + (right - left) * i / (values.Length - 1d); var y = Y(values[i], top, bottom, min, max); var point = new HavenPoint(x, y); if (!scatter && previous is { } prior) c.Add(new HavenLineCommand(prior, point, pen, opacity)); c.Add(new HavenEllipseCommand(new(x - 3, y - 3, 6, 6), new HavenTokenBrush(Token(s)), null, opacity)); previous = point; points.Add(point); }
            if (area && points.Count > 1) { var zero = Y(0, top, bottom, min, max); var polygon = new List<HavenPoint> { new(points[0].X, zero) }; polygon.AddRange(points); polygon.Add(new(points[^1].X, zero)); c.Add(new HavenGeometryCommand(new(left, top, right - left, bottom - top), new HavenGeometry(new HavenPath(polygon, true)), new HavenTokenBrush(Token(s)), null, opacity * .14)); }
        }
    }

    private void DrawPie(HavenDrawingContext c, HavenRect rect, double[] values, double opacity)
    {
        var normalized = values.Select(value => double.IsFinite(value) ? Math.Abs(value) : 0).ToArray(); var total = normalized.Sum(); if (total <= 0) return; var radius = Math.Max(24, Math.Min(rect.Width * .25, (rect.Height - 80) * .4)); var center = new HavenPoint(rect.X + 38 + radius, rect.Y + 58 + radius); double angle = -Math.PI / 2;
        for (var i = 0; i < normalized.Length; i++) { if (normalized[i] <= 0) continue; var sweep = 2 * Math.PI * normalized[i] / total; var end = angle + sweep; var p1 = new HavenPoint(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)); var p2 = new HavenPoint(center.X + radius * Math.Cos(end), center.Y + radius * Math.Sin(end)); var figure = new HavenPathFigure(center, [new HavenLineSegment(p1), new HavenArcSegment(p2, new HavenSize(radius, radius), 0, sweep > Math.PI, HavenSweepDirection.Clockwise), new HavenLineSegment(center)], true); c.Add(new HavenGeometryCommand(new(center.X - radius, center.Y - radius, radius * 2, radius * 2), new HavenGeometry(new HavenPath([figure])), new HavenTokenBrush(Token(i)), new HavenPen(new HavenTokenBrush("Surface"), 1), opacity * .92)); angle = end; }
    }

    private void DrawLegend(HavenDrawingContext c, HavenRect rect, List<Series> series, double opacity)
    {
        if (!_chart.ShowLegend) return; var x = rect.Right - 114; var y = rect.Y + 54;
        for (var i = 0; i < Math.Min(7, series.Count); i++) { c.Add(new HavenFillRoundedRectCommand(new(x, y + i * 23 + 4, 10, 10), new HavenTokenBrush(Token(i)), 2, opacity)); Text(c, series[i].Name, new HavenRect(x + 15, y + i * 23, 96, 18), 9, 500, "TextSecondary", opacity); }
    }

    private List<Series> ReadSeries()
    {
        _chart.Normalize(); var range = _chart.SourceRange; var first = range.StartRow + (_chart.FirstRowIsHeaders ? 1 : 0); if (first > range.EndRow) return [];
        var columns = _chart.SeriesColumns.Count > 0 ? _chart.SeriesColumns.Where(column => column >= range.StartColumn && column <= range.EndColumn).ToArray() : Enumerable.Range(range.StartColumn, range.ColumnCount).Where(column => column != _chart.CategoryColumn).ToArray();
        var result = new List<Series>();
        foreach (var column in columns)
        {
            var name = _chart.FirstRowIsHeaders ? (_sheet.GetCell(range.StartRow, column)?.Value ?? $"Series {result.Count + 1}") : $"Series {result.Count + 1}";
            var values = new double[range.EndRow - first + 1];
            for (var row = first; row <= range.EndRow; row++) { var raw = _sheet.GetCell(row, column)?.Value; values[row - first] = double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value) ? value : double.NaN; }
            if (values.Any(double.IsFinite)) result.Add(new(name, values));
        }
        return result;
    }

    private void RefreshAccessibleName() => Accessibility.AccessibleName = $"Chart: {_chart.Title}";
    private static double Y(double value, double top, double bottom, double min, double max) => bottom - (value - min) / (max - min) * (bottom - top);
    private static double X(double value, double left, double right, double min, double max) => left + (value - min) / (max - min) * (right - left);
    private static string Token(int index) => Tokens[index % Tokens.Length];
    private static void Text(HavenDrawingContext context, string text, HavenRect rect, double size, int weight, string token, double opacity) => context.Add(new HavenTextCommand(rect, new HavenTextLayout(text, "Montserrat", size, weight, rect.Width), new HavenTokenBrush(token), opacity));
    private sealed record Series(string Name, double[] Values);
}
