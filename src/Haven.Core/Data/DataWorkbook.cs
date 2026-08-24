using System.Text;

namespace Haven.Core;

public enum DataCellKind { Text = 0, Number = 1, Boolean = 2, Date = 3, Formula = 4 }
public enum DataDrawingKind { CustomShape = 0 }
public enum DataSqlRisk { ReadOnly = 0, Mutating = 1, Destructive = 2, MultipleStatements = 3, Unknown = 4 }
public sealed record DataSqlSafetyResult(DataSqlRisk Risk, bool IsReadOnly, string Message);

public static class DataSqlSafety
{
    private static readonly string[] DestructiveTokens = ["DROP", "TRUNCATE", "DELETE", "ALTER", "GRANT", "REVOKE"];
    private static readonly string[] MutatingTokens = ["INSERT", "UPDATE", "MERGE", "REPLACE", "CREATE", "VACUUM", "ATTACH", "DETACH"];

    public static DataSqlSafetyResult Analyze(string? sql)
    {
        var cleaned = StripComments(sql ?? string.Empty).Trim();
        if (cleaned.Length == 0) return new(DataSqlRisk.Unknown, false, "Enter a SQL query to analyse it.");
        var statements = cleaned.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (statements.Length > 1) return new(DataSqlRisk.MultipleStatements, false, "Multiple SQL statements require separate review before execution.");
        var first = FirstToken(statements[0]);
        if (first is "SELECT" or "EXPLAIN") return new(DataSqlRisk.ReadOnly, true, "Read-only query.");
        if (first == "WITH") return new(DataSqlRisk.Unknown, false, "Common-table expressions are not executed by the first Data slice because WITH can prefix mutating statements.");
        if (DestructiveTokens.Contains(first, StringComparer.OrdinalIgnoreCase)) return new(DataSqlRisk.Destructive, false, $"{first} can remove or materially alter data. Review it before any future execution.");
        if (MutatingTokens.Contains(first, StringComparer.OrdinalIgnoreCase)) return new(DataSqlRisk.Mutating, false, $"{first} changes database state. Review it before any future execution.");
        return new(DataSqlRisk.Unknown, false, "Haven could not classify this query as read-only. Treat it as potentially mutating.");
    }

    private static string FirstToken(string sql)
    {
        var index = 0; while (index < sql.Length && !char.IsLetter(sql[index])) index++; var start = index; while (index < sql.Length && (char.IsLetter(sql[index]) || sql[index] == '_')) index++;
        return start == index ? string.Empty : sql[start..index].ToUpperInvariant();
    }

    private static string StripComments(string sql)
    {
        var builder = new StringBuilder(sql.Length); var inBlock = false; var inLine = false;
        for (var i = 0; i < sql.Length; i++)
        {
            if (!inBlock && !inLine && i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*') { inBlock = true; i++; continue; }
            if (inBlock && i + 1 < sql.Length && sql[i] == '*' && sql[i + 1] == '/') { inBlock = false; i++; builder.Append(' '); continue; }
            if (!inBlock && !inLine && i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-') { inLine = true; i++; continue; }
            if (!inBlock && !inLine && sql[i] == '#') { inLine = true; continue; }
            if (inLine && (sql[i] == '\r' || sql[i] == '\n')) { inLine = false; builder.Append(' '); continue; }
            if (!inBlock && !inLine) builder.Append(sql[i]);
        }
        return builder.ToString();
    }
}

public sealed class DataWorkbook
{
    public const int CurrentSchemaVersion = 3;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled workbook";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int Version { get; set; }
    public List<DataSheet> Sheets { get; set; } = [];
    public List<DataQuery> Queries { get; set; } = [];
    public List<DataNamedRange> NamedRanges { get; set; } = [];
    public List<DataTableDefinition> Tables { get; set; } = [];
    public List<DataValidationRule> Validations { get; set; } = [];
    public List<DataChartDefinition> Charts { get; set; } = [];
    public DataSchemaSnapshot Schema { get; set; } = new();
    public DataRecoveryState Recovery { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);

    public static DataWorkbook Create(string? title = null)
    {
        var workbook = new DataWorkbook { Title = string.IsNullOrWhiteSpace(title) ? "Untitled workbook" : title.Trim() };
        workbook.Sheets.Add(DataSheet.Create(0, "Sheet 1")); workbook.Queries.Add(DataQuery.Create("Query 1")); return workbook;
    }

    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion; Title = string.IsNullOrWhiteSpace(Title) ? "Untitled workbook" : Title.Trim(); Sheets ??= []; Queries ??= []; NamedRanges ??= []; Tables ??= []; Validations ??= []; Charts ??= []; Schema ??= new(); Recovery ??= new(); Metadata ??= new(StringComparer.Ordinal);
        if (Sheets.Count == 0) Sheets.Add(DataSheet.Create(0, "Sheet 1")); if (Queries.Count == 0) Queries.Add(DataQuery.Create("Query 1"));
        for (var i = 0; i < Sheets.Count; i++) { Sheets[i] ??= DataSheet.Create(i, $"Sheet {i + 1}"); Sheets[i].Normalize(i); }
        for (var i = 0; i < Queries.Count; i++) { Queries[i] ??= DataQuery.Create($"Query {i + 1}"); Queries[i].Normalize(i); }
        var rangeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); for (var i = NamedRanges.Count - 1; i >= 0; i--) { var range = NamedRanges[i]; if (range is null) { NamedRanges.RemoveAt(i); continue; } range.Normalize(); if (!rangeNames.Add(range.Name)) NamedRanges.RemoveAt(i); }
        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); for (var i = Tables.Count - 1; i >= 0; i--) { var table = Tables[i]; if (table is null) { Tables.RemoveAt(i); continue; } table.Normalize(); if (!tableNames.Add(table.Name)) Tables.RemoveAt(i); }
        foreach (var validation in Validations.Where(value => value is not null)) validation.Normalize(); Validations = Validations.Where(value => value is not null).ToList();
        foreach (var chart in Charts.Where(value => value is not null)) chart.Normalize(); Charts = Charts.Where(value => value is not null).ToList();
        Schema.Normalize();
    }
}

public sealed class DataSheet
{
    private Dictionary<long, DataCell>? _cellIndex;
    private List<DataCell>? _indexedCells;
    private int _indexedCellCount = -1;

    public Guid Id { get; set; } = Guid.NewGuid();
    public int Order { get; set; }
    public string Name { get; set; } = "Sheet";
    public List<DataCell> Cells { get; set; } = [];
    public List<DataDrawingObject> Drawings { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);

    public static DataSheet Create(int order, string? name = null) =>
        new() { Order = order, Name = string.IsNullOrWhiteSpace(name) ? $"Sheet {order + 1}" : name.Trim() };

    public DataCell? GetCell(int row, int column)
    {
        if (row < 0 || column < 0) return null;
        EnsureCellIndex();
        return _cellIndex!.GetValueOrDefault(CellKey(row, column));
    }

    public DataCell GetOrCreateCell(int row, int column)
    {
        if (row < 0 || column < 0)
            throw new ArgumentOutOfRangeException(row < 0 ? nameof(row) : nameof(column));
        EnsureCellIndex();
        var key = CellKey(row, column);
        if (_cellIndex!.TryGetValue(key, out var existing)) return existing;
        var cell = new DataCell { Row = row, Column = column };
        Cells.Add(cell);
        _cellIndex[key] = cell;
        _indexedCellCount = Cells.Count;
        return cell;
    }

    public void SetCell(int row, int column, string? value, string? formula = null, DataCellKind? kind = null)
    {
        var cell = GetOrCreateCell(row, column);
        cell.Value = value ?? string.Empty;
        cell.Formula = formula ?? string.Empty;
        cell.Kind = !string.IsNullOrWhiteSpace(cell.Formula)
            ? DataCellKind.Formula
            : kind ?? DataCell.InferKind(cell.Value);
        if (string.IsNullOrEmpty(cell.Value)
            && string.IsNullOrEmpty(cell.Formula)
            && cell.Metadata.Count == 0)
        {
            Cells.Remove(cell);
            _cellIndex?.Remove(CellKey(row, column));
            _indexedCellCount = Cells.Count;
        }
    }

    public void Normalize(int order)
    {
        Order = order;
        Name = string.IsNullOrWhiteSpace(Name) ? $"Sheet {order + 1}" : Name.Trim();
        Cells ??= [];
        Drawings ??= [];
        Metadata ??= new(StringComparer.Ordinal);
        var unique = new Dictionary<(int, int), DataCell>();
        foreach (var cell in Cells.Where(cell => cell is not null && cell.Row >= 0 && cell.Column >= 0))
        {
            cell.Normalize();
            unique[(cell.Row, cell.Column)] = cell;
        }
        Cells = unique.Values.OrderBy(cell => cell.Row).ThenBy(cell => cell.Column).ToList();
        InvalidateCellIndex();
        EnsureCellIndex();

        var drawingIds = new HashSet<Guid>();
        foreach (var drawing in Drawings.Where(value => value is not null))
        {
            drawing.Normalize();
            if (drawing.Id == Guid.Empty || !drawingIds.Add(drawing.Id))
            {
                drawing.Id = Guid.NewGuid();
                drawingIds.Add(drawing.Id);
            }
        }
        Drawings = Drawings.Where(value => value is not null).OrderBy(value => value.ZIndex).ToList();
    }

    private void EnsureCellIndex()
    {
        Cells ??= [];
        if (_cellIndex is not null
            && ReferenceEquals(_indexedCells, Cells)
            && _indexedCellCount == Cells.Count)
            return;

        var index = new Dictionary<long, DataCell>(Cells.Count);
        foreach (var cell in Cells)
        {
            if (cell is null || cell.Row < 0 || cell.Column < 0) continue;
            index[CellKey(cell.Row, cell.Column)] = cell;
        }
        _cellIndex = index;
        _indexedCells = Cells;
        _indexedCellCount = Cells.Count;
    }

    private void InvalidateCellIndex()
    {
        _cellIndex = null;
        _indexedCells = null;
        _indexedCellCount = -1;
    }

    private static long CellKey(int row, int column) =>
        ((long)(uint)row << 32) | (uint)column;
}

public sealed class DataCell
{
    public int Row { get; set; } public int Column { get; set; } public DataCellKind Kind { get; set; } = DataCellKind.Text; public string Value { get; set; } = string.Empty; public string Formula { get; set; } = string.Empty; public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
    public void Normalize() { Value ??= string.Empty; Formula ??= string.Empty; Metadata ??= new(StringComparer.Ordinal); if (!string.IsNullOrWhiteSpace(Formula)) Kind = DataCellKind.Formula; }
    public static DataCellKind InferKind(string value) { if (bool.TryParse(value, out _)) return DataCellKind.Boolean; if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)) return DataCellKind.Number; if (DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _)) return DataCellKind.Date; return DataCellKind.Text; }
}

public sealed class DataQuery
{
    public Guid Id { get; set; } = Guid.NewGuid(); public int Order { get; set; } public string Name { get; set; } = "Query"; public string Sql { get; set; } = "SELECT * FROM \"Sheet 1\";"; public DataVisualQuery Visual { get; set; } = new(); public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
    public static DataQuery Create(string? name = null) => new() { Name = string.IsNullOrWhiteSpace(name) ? "Query" : name.Trim() };
    public void Normalize(int order) { Order = order; Name = string.IsNullOrWhiteSpace(Name) ? $"Query {order + 1}" : Name.Trim(); Sql ??= string.Empty; Visual ??= new(); Visual.Normalize(); Metadata ??= new(StringComparer.Ordinal); }
}

public sealed class DataVisualQuery
{
    public string Source { get; set; } = "Sheet 1"; public string Columns { get; set; } = "*"; public string Filter { get; set; } = string.Empty; public string GroupBy { get; set; } = string.Empty; public string OrderBy { get; set; } = string.Empty; public int? Limit { get; set; }
    public void Normalize() { Source ??= string.Empty; Columns ??= "*"; Filter ??= string.Empty; GroupBy ??= string.Empty; OrderBy ??= string.Empty; if (Limit is < 1) Limit = null; }
    public string BuildSql() { Normalize(); var source = string.IsNullOrWhiteSpace(Source) ? "Sheet 1" : Source.Trim(); var columns = string.IsNullOrWhiteSpace(Columns) ? "*" : Columns.Trim(); var builder = new StringBuilder($"SELECT {columns} FROM {QuoteIdentifier(source)}"); if (!string.IsNullOrWhiteSpace(Filter)) builder.Append(" WHERE " + Filter.Trim()); if (!string.IsNullOrWhiteSpace(GroupBy)) builder.Append(" GROUP BY " + GroupBy.Trim()); if (!string.IsNullOrWhiteSpace(OrderBy)) builder.Append(" ORDER BY " + OrderBy.Trim()); if (Limit is > 0) builder.Append(" LIMIT " + Limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)); builder.Append(';'); return builder.ToString(); }
    private static string QuoteIdentifier(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

public sealed class DataSchemaSnapshot { public List<DataSchemaTable> Tables { get; set; } = []; public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal); public void Normalize() { Tables ??= []; Metadata ??= new(StringComparer.Ordinal); foreach (var table in Tables) table?.Normalize(); Tables = Tables.Where(table => table is not null).OrderBy(table => table.Name, StringComparer.OrdinalIgnoreCase).ToList(); } }
public sealed class DataSchemaTable { public string Name { get; set; } = string.Empty; public string Kind { get; set; } = "table"; public List<DataSchemaColumn> Columns { get; set; } = []; public void Normalize() { Name ??= string.Empty; Kind ??= "table"; Columns ??= []; foreach (var column in Columns) column?.Normalize(); Columns = Columns.Where(column => column is not null).ToList(); } }
public sealed class DataSchemaColumn { public string Name { get; set; } = string.Empty; public string DataType { get; set; } = string.Empty; public bool IsPrimaryKey { get; set; } public bool IsNullable { get; set; } = true; public void Normalize() { Name ??= string.Empty; DataType ??= string.Empty; } }
public sealed class DataRecoveryState { public bool RecoveredFromBackup { get; set; } public string Message { get; set; } = string.Empty; public DateTimeOffset? RecoveredAt { get; set; } }
