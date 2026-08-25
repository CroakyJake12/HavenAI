using Haven.Core;

namespace Haven.Application;

public sealed partial class PresentEditor
{
    public PresentElement AddTable(Guid slideId, int rows = 2, int columns = 2)
    {
        var table = PresentTable.Create(rows, columns);
        var element = new PresentElement
        {
            Kind = PresentElementKind.Table,
            X = 0.12, Y = 0.18, Width = 0.76, Height = 0.48,
            AlternativeText = $"{rows} by {columns} table"
        };
        element.WriteTable(table);
        return AddElement(slideId, element);
    }

    public bool SetTableCellText(Guid slideId, Guid elementId, int row, int column, string? text)
    {
        var element = RequireStructuredElement(slideId, elementId, PresentElementKind.Table);
        var table = element.ReadTable();
        var value = text ?? string.Empty;
        if (string.Equals(table.GetCell(row, column).Text, value, StringComparison.Ordinal)) return false;
        Mutate(() => { table.GetCell(row, column).Text = value; element.WriteTable(table); });
        return true;
    }

    public bool ResizeTable(Guid slideId, Guid elementId, int rows, int columns)
    {
        rows = Math.Clamp(rows, 1, 100);
        columns = Math.Clamp(columns, 1, 100);
        var element = RequireStructuredElement(slideId, elementId, PresentElementKind.Table);
        var table = element.ReadTable();
        if (table.Rows == rows && table.Columns == columns) return false;
        Mutate(() =>
        {
            while (table.Rows < rows) table.InsertRow(table.Rows);
            while (table.Rows > rows) table.DeleteRow(table.Rows - 1);
            while (table.Columns < columns) table.InsertColumn(table.Columns);
            while (table.Columns > columns) table.DeleteColumn(table.Columns - 1);
            element.WriteTable(table);
        });
        return true;
    }

    public bool InsertTableRow(Guid slideId, Guid elementId, int index)
    {
        var element = RequireStructuredElement(slideId, elementId, PresentElementKind.Table);
        var table = element.ReadTable();
        if (table.Rows >= 100) return false;
        Mutate(() => { table.InsertRow(index); element.WriteTable(table); });
        return true;
    }

    public bool DeleteTableRow(Guid slideId, Guid elementId, int index)
    {
        var element = RequireStructuredElement(slideId, elementId, PresentElementKind.Table);
        var table = element.ReadTable();
        if (table.Rows <= 1 || index < 0 || index >= table.Rows) return false;
        Mutate(() => { table.DeleteRow(index); element.WriteTable(table); });
        return true;
    }

    public bool InsertTableColumn(Guid slideId, Guid elementId, int index)
    {
        var element = RequireStructuredElement(slideId, elementId, PresentElementKind.Table);
        var table = element.ReadTable();
        if (table.Columns >= 100) return false;
        Mutate(() => { table.InsertColumn(index); element.WriteTable(table); });
        return true;
    }

    public bool DeleteTableColumn(Guid slideId, Guid elementId, int index)
    {
        var element = RequireStructuredElement(slideId, elementId, PresentElementKind.Table);
        var table = element.ReadTable();
        if (table.Columns <= 1 || index < 0 || index >= table.Columns) return false;
        Mutate(() => { table.DeleteColumn(index); element.WriteTable(table); });
        return true;
    }

    public PresentElement AddChart(Guid slideId, PresentChartType type = PresentChartType.Column)
    {
        var chart = new PresentChart { Type = type };
        chart.Normalize();
        var element = new PresentElement
        {
            Kind = PresentElementKind.Chart,
            X = 0.14, Y = 0.16, Width = 0.72, Height = 0.56,
            AlternativeText = $"{type} chart"
        };
        element.WriteChart(chart);
        return AddElement(slideId, element);
    }

    public bool SetChartType(Guid slideId, Guid elementId, PresentChartType type)
    {
        var element = RequireStructuredElement(slideId, elementId, PresentElementKind.Chart);
        var chart = element.ReadChart();
        if (chart.Type == type) return false;
        Mutate(() => { chart.Type = type; element.AlternativeText = $"{type} chart"; element.WriteChart(chart); });
        return true;
    }

    public bool SetChartData(Guid slideId, Guid elementId, IEnumerable<string> categories, IEnumerable<PresentChartSeries> series)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(series);
        var element = RequireStructuredElement(slideId, elementId, PresentElementKind.Chart);
        var chart = element.ReadChart();
        var nextCategories = categories.Select(value => value ?? string.Empty).ToList();
        var nextSeries = series.Select(CloneSeries).ToList();
        Mutate(() =>
        {
            chart.Categories = nextCategories;
            chart.Series = nextSeries;
            chart.Normalize();
            element.WriteChart(chart);
        });
        return true;
    }

    public bool SetChartStyle(Guid slideId, Guid elementId, string? style, bool? showLegend = null)
    {
        var element = RequireStructuredElement(slideId, elementId, PresentElementKind.Chart);
        var chart = element.ReadChart();
        var nextStyle = string.IsNullOrWhiteSpace(style) ? "Haven" : style.Trim();
        var nextLegend = showLegend ?? chart.ShowLegend;
        if (string.Equals(chart.Style, nextStyle, StringComparison.Ordinal) && chart.ShowLegend == nextLegend) return false;
        Mutate(() => { chart.Style = nextStyle; chart.ShowLegend = nextLegend; element.WriteChart(chart); });
        return true;
    }

    private PresentElement RequireStructuredElement(Guid slideId, Guid elementId, PresentElementKind kind)
    {
        var element = RequireSlide(slideId).Elements.FirstOrDefault(item => item.Id == elementId && item.Kind == kind)
            ?? throw new ArgumentOutOfRangeException(nameof(elementId), $"The {kind} object does not exist on this slide.");
        if (element.Locked) throw new InvalidOperationException($"The {kind} object is locked.");
        return element;
    }

    private static PresentChartSeries CloneSeries(PresentChartSeries source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new PresentChartSeries { Name = source.Name, Values = source.Values?.ToList() ?? [] };
    }
}
