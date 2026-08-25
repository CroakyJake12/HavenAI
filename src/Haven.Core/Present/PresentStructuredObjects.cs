using System.Text.Json;

namespace Haven.Core;

public sealed class PresentTableCell
{
    public string Text { get; set; } = string.Empty;
    public string FillColor { get; set; } = string.Empty;

    public void Normalize()
    {
        Text ??= string.Empty;
        FillColor ??= string.Empty;
    }
}

public sealed class PresentTable
{
    public int Rows { get; set; } = 2;
    public int Columns { get; set; } = 2;
    public List<PresentTableCell> Cells { get; set; } = [];
    public bool HeaderRow { get; set; } = true;

    public static PresentTable Create(int rows, int columns)
    {
        if (rows is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(columns));
        var table = new PresentTable { Rows = rows, Columns = columns };
        table.Normalize();
        return table;
    }

    public PresentTableCell GetCell(int row, int column)
    {
        ValidateCoordinate(row, column);
        return Cells[(row * Columns) + column];
    }

    public void InsertRow(int index)
    {
        if (Rows >= 100) throw new InvalidOperationException("A table cannot contain more than 100 rows.");
        index = Math.Clamp(index, 0, Rows);
        Cells.InsertRange(index * Columns, Enumerable.Range(0, Columns).Select(_ => new PresentTableCell()));
        Rows++;
    }

    public bool DeleteRow(int index)
    {
        if (Rows <= 1 || index < 0 || index >= Rows) return false;
        Cells.RemoveRange(index * Columns, Columns);
        Rows--;
        return true;
    }

    public void InsertColumn(int index)
    {
        if (Columns >= 100) throw new InvalidOperationException("A table cannot contain more than 100 columns.");
        index = Math.Clamp(index, 0, Columns);
        for (var row = Rows - 1; row >= 0; row--)
            Cells.Insert((row * Columns) + index, new PresentTableCell());
        Columns++;
    }

    public bool DeleteColumn(int index)
    {
        if (Columns <= 1 || index < 0 || index >= Columns) return false;
        for (var row = Rows - 1; row >= 0; row--)
            Cells.RemoveAt((row * Columns) + index);
        Columns--;
        return true;
    }

    public void Normalize()
    {
        Rows = Math.Clamp(Rows, 1, 100);
        Columns = Math.Clamp(Columns, 1, 100);
        Cells ??= [];
        var required = checked(Rows * Columns);
        if (Cells.Count > required) Cells.RemoveRange(required, Cells.Count - required);
        while (Cells.Count < required) Cells.Add(new PresentTableCell());
        for (var index = 0; index < Cells.Count; index++)
        {
            Cells[index] ??= new PresentTableCell();
            Cells[index].Normalize();
        }
    }

    private void ValidateCoordinate(int row, int column)
    {
        if (row < 0 || row >= Rows) throw new ArgumentOutOfRangeException(nameof(row));
        if (column < 0 || column >= Columns) throw new ArgumentOutOfRangeException(nameof(column));
    }
}

public enum PresentChartType
{
    Column = 0,
    Bar = 1,
    Line = 2,
    Area = 3,
    Pie = 4,
    Doughnut = 5,
    Scatter = 6
}

public sealed class PresentChartSeries
{
    public string Name { get; set; } = "Series";
    public List<double> Values { get; set; } = [];

    public void Normalize(int categoryCount)
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Series" : Name.Trim();
        Values ??= [];
        if (Values.Count > categoryCount) Values.RemoveRange(categoryCount, Values.Count - categoryCount);
        while (Values.Count < categoryCount) Values.Add(0);
        for (var index = 0; index < Values.Count; index++)
            Values[index] = double.IsFinite(Values[index]) ? Values[index] : 0;
    }
}

public sealed class PresentChart
{
    public PresentChartType Type { get; set; } = PresentChartType.Column;
    public string Title { get; set; } = string.Empty;
    public List<string> Categories { get; set; } = ["A", "B", "C"];
    public List<PresentChartSeries> Series { get; set; } =
    [
        new PresentChartSeries { Name = "Series 1", Values = [3, 5, 4] }
    ];
    public bool ShowLegend { get; set; } = true;
    public string Style { get; set; } = "Haven";

    public void Normalize()
    {
        Title ??= string.Empty;
        Style = string.IsNullOrWhiteSpace(Style) ? "Haven" : Style.Trim();
        Categories ??= [];
        if (Categories.Count == 0) Categories.Add("A");
        if (Categories.Count > 100) Categories.RemoveRange(100, Categories.Count - 100);
        for (var index = 0; index < Categories.Count; index++)
            Categories[index] = string.IsNullOrWhiteSpace(Categories[index]) ? $"Category {index + 1}" : Categories[index].Trim();
        Series ??= [];
        if (Series.Count == 0) Series.Add(new PresentChartSeries { Name = "Series 1" });
        if (Series.Count > 32) Series.RemoveRange(32, Series.Count - 32);
        for (var index = 0; index < Series.Count; index++)
        {
            Series[index] ??= new PresentChartSeries();
            Series[index].Normalize(Categories.Count);
        }
    }
}

public static class PresentStructuredElementData
{
    private const string TableKey = "haven.present.table.v1";
    private const string ChartKey = "haven.present.chart.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static PresentTable ReadTable(this PresentElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.Kind != PresentElementKind.Table) throw new InvalidOperationException("This presentation object is not a table.");
        var table = element.Properties.TryGetValue(TableKey, out var json) && !string.IsNullOrWhiteSpace(json)
            ? JsonSerializer.Deserialize<PresentTable>(json, JsonOptions) ?? new PresentTable()
            : new PresentTable();
        table.Normalize();
        return table;
    }

    public static void WriteTable(this PresentElement element, PresentTable table)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(table);
        if (element.Kind != PresentElementKind.Table) throw new InvalidOperationException("This presentation object is not a table.");
        table.Normalize();
        element.Properties[TableKey] = JsonSerializer.Serialize(table, JsonOptions);
    }

    public static PresentChart ReadChart(this PresentElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.Kind != PresentElementKind.Chart) throw new InvalidOperationException("This presentation object is not a chart.");
        var chart = element.Properties.TryGetValue(ChartKey, out var json) && !string.IsNullOrWhiteSpace(json)
            ? JsonSerializer.Deserialize<PresentChart>(json, JsonOptions) ?? new PresentChart()
            : new PresentChart();
        chart.Normalize();
        return chart;
    }

    public static void WriteChart(this PresentElement element, PresentChart chart)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(chart);
        if (element.Kind != PresentElementKind.Chart) throw new InvalidOperationException("This presentation object is not a chart.");
        chart.Normalize();
        element.Properties[ChartKey] = JsonSerializer.Serialize(chart, JsonOptions);
    }
}
