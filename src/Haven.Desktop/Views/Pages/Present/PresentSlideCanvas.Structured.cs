using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Present;

internal sealed partial class PresentSlideCanvas
{
    internal HavenRect GetElementEditorBounds(PresentElement element) => ElementRect(element, SlideRectLocal(), false);

    private static void DrawTable(HavenDrawingContext context, PresentTable table, HavenRect rect, double opacity)
    {
        context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("SurfaceRaised"), 4, opacity));
        var pen = new HavenPen(new HavenTokenBrush("Border"), 1);
        context.Add(new HavenStrokeRoundedRectCommand(rect, pen, 4, opacity));
        var cellWidth = rect.Width / Math.Max(1, table.Columns);
        var cellHeight = rect.Height / Math.Max(1, table.Rows);
        for (var column = 1; column < table.Columns; column++)
        {
            var x = rect.X + cellWidth * column;
            context.Add(new HavenLineCommand(new HavenPoint(x, rect.Y), new HavenPoint(x, rect.Bottom), pen, opacity));
        }
        for (var row = 1; row < table.Rows; row++)
        {
            var y = rect.Y + cellHeight * row;
            context.Add(new HavenLineCommand(new HavenPoint(rect.X, y), new HavenPoint(rect.Right, y), pen, opacity));
        }
        for (var row = 0; row < table.Rows; row++)
        for (var column = 0; column < table.Columns; column++)
        {
            var text = table.GetCell(row, column).Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            var cellRect = new HavenRect(rect.X + column * cellWidth + 5, rect.Y + row * cellHeight + 4, Math.Max(1, cellWidth - 10), Math.Max(1, cellHeight - 8));
            context.Add(new HavenTextCommand(cellRect, new HavenTextLayout(text, "Segoe UI", Math.Clamp(cellHeight * .22, 8, 14), table.HeaderRow && row == 0 ? 700 : 500, cellRect.Width, true), new HavenTokenBrush("TextPrimary"), opacity));
        }
    }

    private static void DrawChart(HavenDrawingContext context, PresentChart chart, HavenRect rect, double opacity)
    {
        context.Add(new HavenFillRoundedRectCommand(rect, new HavenTokenBrush("SurfaceRaised"), 6, opacity));
        context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenTokenBrush("Border"), 1), 6, opacity));
        var heading = string.IsNullOrWhiteSpace(chart.Title) ? $"{chart.Type} chart" : chart.Title;
        var headingRect = new HavenRect(rect.X + 10, rect.Y + 7, Math.Max(1, rect.Width - 20), 22);
        context.Add(new HavenTextCommand(headingRect, new HavenTextLayout(heading, "Segoe UI", 12, 700, headingRect.Width, true), new HavenTokenBrush("TextPrimary"), opacity));
        var values = chart.Series.FirstOrDefault()?.Values ?? [];
        if (values.Count == 0) return;
        var plot = new HavenRect(rect.X + 14, rect.Y + 34, Math.Max(1, rect.Width - 28), Math.Max(1, rect.Height - 48));
        var max = Math.Max(1d, values.Max(value => Math.Abs(value)));
        if (chart.Type is PresentChartType.Line or PresentChartType.Area or PresentChartType.Scatter)
        {
            HavenPoint? previous = null;
            for (var index = 0; index < values.Count; index++)
            {
                var x = values.Count == 1 ? plot.X + plot.Width / 2 : plot.X + plot.Width * index / (values.Count - 1d);
                var y = plot.Bottom - (Math.Max(0, values[index]) / max) * plot.Height;
                var point = new HavenPoint(x, y);
                if (previous is { } prior) context.Add(new HavenLineCommand(prior, point, new HavenPen(new HavenTokenBrush("Accent"), 2), opacity));
                context.Add(new HavenEllipseCommand(new HavenRect(x - 3, y - 3, 6, 6), new HavenTokenBrush("Accent"), null, opacity));
                previous = point;
            }
            return;
        }
        var gap = Math.Max(2d, plot.Width * .015);
        var barWidth = Math.Max(2d, (plot.Width - gap * (values.Count + 1)) / values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var height = Math.Max(1, Math.Abs(values[index]) / max * plot.Height);
            var bar = new HavenRect(plot.X + gap + index * (barWidth + gap), plot.Bottom - height, barWidth, height);
            context.Add(new HavenFillRoundedRectCommand(bar, new HavenTokenBrush("Accent"), 2, opacity));
        }
    }

}
