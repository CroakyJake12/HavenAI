using System.Globalization;

namespace Haven.Core;

public enum DataChartType { Column = 0, Bar = 1, Line = 2, Area = 3, Pie = 4, Scatter = 5 }
public enum DataValidationKind { Any = 0, List = 1, WholeNumber = 2, Decimal = 3, Date = 4, TextLength = 5 }
public enum DataFilterOperator { Contains = 0, Equals = 1, NotEquals = 2, GreaterThan = 3, GreaterThanOrEqual = 4, LessThan = 5, LessThanOrEqual = 6, IsBlank = 7, IsNotBlank = 8, StartsWith = 9, EndsWith = 10 }

public sealed class DataCellRange
{
    public int StartRow { get; set; }
    public int StartColumn { get; set; }
    public int EndRow { get; set; }
    public int EndColumn { get; set; }
    public int RowCount => EndRow - StartRow + 1;
    public int ColumnCount => EndColumn - StartColumn + 1;

    public void Normalize() { StartRow = Math.Max(0, StartRow); StartColumn = Math.Max(0, StartColumn); EndRow = Math.Max(StartRow, EndRow); EndColumn = Math.Max(StartColumn, EndColumn); }
    public bool Contains(int row, int column) => row >= StartRow && row <= EndRow && column >= StartColumn && column <= EndColumn;
    public DataCellRange Clone() => new() { StartRow = StartRow, StartColumn = StartColumn, EndRow = EndRow, EndColumn = EndColumn };
    public override string ToString() => $"{ColumnName(StartColumn)}{StartRow + 1}:{ColumnName(EndColumn)}{EndRow + 1}";

    public static bool TryParse(string? value, out DataCellRange range)
    {
        range = new();
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Trim().Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2 || !TryParseCell(parts[0], out var startRow, out var startColumn)) return false;
        var endRow = startRow; var endColumn = startColumn;
        if (parts.Length == 2 && !TryParseCell(parts[1], out endRow, out endColumn)) return false;
        range = new DataCellRange { StartRow = Math.Min(startRow, endRow), StartColumn = Math.Min(startColumn, endColumn), EndRow = Math.Max(startRow, endRow), EndColumn = Math.Max(startColumn, endColumn) };
        return true;
    }

    private static bool TryParseCell(string value, out int row, out int column)
    {
        row = column = -1; var text = value.Trim().Replace("$", string.Empty, StringComparison.Ordinal); var letterCount = 0;
        while (letterCount < text.Length && char.IsLetter(text[letterCount])) letterCount++;
        if (letterCount == 0 || letterCount == text.Length || !int.TryParse(text[letterCount..], NumberStyles.None, CultureInfo.InvariantCulture, out var oneBasedRow) || oneBasedRow < 1) return false;
        long oneBasedColumn = 0;
        foreach (var letter in text[..letterCount].ToUpperInvariant()) { if (letter is < 'A' or > 'Z') return false; oneBasedColumn = checked(oneBasedColumn * 26 + letter - 'A' + 1); if (oneBasedColumn > int.MaxValue) return false; }
        row = oneBasedRow - 1; column = (int)oneBasedColumn - 1; return true;
    }

    private static string ColumnName(int column) { column = Math.Max(0, column); var name = string.Empty; do { name = (char)('A' + column % 26) + name; column = column / 26 - 1; } while (column >= 0); return name; }
}

public sealed class DataTableFilter
{
    public int Column { get; set; }
    public DataFilterOperator Operator { get; set; } = DataFilterOperator.Contains;
    public string Value { get; set; } = string.Empty;
    public void Normalize() { Column = Math.Max(0, Column); Value ??= string.Empty; }
}

public sealed class DataTableDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SheetId { get; set; }
    public string Name { get; set; } = "Table1";
    public DataCellRange Range { get; set; } = new();
    public bool HasHeaders { get; set; } = true;
    public int? SortColumn { get; set; }
    public bool SortDescending { get; set; }
    public List<DataTableFilter> Filters { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
    public void Normalize() { if (Id == Guid.Empty) Id = Guid.NewGuid(); Name = string.IsNullOrWhiteSpace(Name) ? "Table1" : Name.Trim(); Range ??= new(); Range.Normalize(); if (SortColumn is < 0 || SortColumn < Range.StartColumn || SortColumn > Range.EndColumn) SortColumn = null; Filters ??= []; foreach (var filter in Filters.Where(value => value is not null)) filter.Normalize(); Filters = Filters.Where(value => value is not null && value.Column >= Range.StartColumn && value.Column <= Range.EndColumn).OrderBy(value => value.Column).ThenBy(value => value.Operator).ThenBy(value => value.Value, StringComparer.OrdinalIgnoreCase).ToList(); Metadata ??= new(StringComparer.Ordinal); }
}

public sealed class DataValidationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SheetId { get; set; }
    public DataCellRange Range { get; set; } = new();
    public DataValidationKind Kind { get; set; }
    public bool AllowBlank { get; set; } = true;
    public List<string> AllowedValues { get; set; } = [];
    public string Minimum { get; set; } = string.Empty;
    public string Maximum { get; set; } = string.Empty;
    public string InputMessage { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public void Normalize() { if (Id == Guid.Empty) Id = Guid.NewGuid(); Range ??= new(); Range.Normalize(); AllowedValues ??= []; AllowedValues = AllowedValues.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); Minimum ??= string.Empty; Maximum ??= string.Empty; InputMessage ??= string.Empty; ErrorMessage ??= string.Empty; }
}

public sealed record DataValidationResult(bool IsValid, Guid? RuleId = null, string Message = "")
{
    public static DataValidationResult Valid { get; } = new(true);
}

public sealed class DataChartDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SheetId { get; set; }
    public DataChartType Type { get; set; } = DataChartType.Column;
    public DataCellRange SourceRange { get; set; } = new();
    public string Title { get; set; } = "Chart";
    public string XAxisTitle { get; set; } = string.Empty;
    public string YAxisTitle { get; set; } = string.Empty;
    public bool ShowLegend { get; set; } = true;
    public bool FirstRowIsHeaders { get; set; } = true;
    public int CategoryColumn { get; set; }
    public List<int> SeriesColumns { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
    public void Normalize() { if (Id == Guid.Empty) Id = Guid.NewGuid(); SourceRange ??= new(); SourceRange.Normalize(); Title = string.IsNullOrWhiteSpace(Title) ? "Chart" : Title.Trim(); XAxisTitle ??= string.Empty; YAxisTitle ??= string.Empty; CategoryColumn = Math.Max(0, CategoryColumn); SeriesColumns ??= []; SeriesColumns = SeriesColumns.Where(value => value >= 0).Distinct().ToList(); Metadata ??= new(StringComparer.Ordinal); }
}

public static class DataCellFormatMetadata
{
    public const string NumberFormat = "format.number"; public const string FontFamily = "format.fontFamily"; public const string FontSize = "format.fontSize"; public const string FontWeight = "format.fontWeight"; public const string Italic = "format.italic"; public const string Underline = "format.underline"; public const string Foreground = "format.foreground"; public const string Fill = "format.fill"; public const string Border = "format.border"; public const string HorizontalAlignment = "format.horizontalAlignment";
}

public static class DataSpreadsheetOperations
{
    public static void FillRange(DataSheet sheet, DataCellRange range, string? value, string? formula = null) { ArgumentNullException.ThrowIfNull(sheet); ArgumentNullException.ThrowIfNull(range); range.Normalize(); for (var row = range.StartRow; row <= range.EndRow; row++) for (var column = range.StartColumn; column <= range.EndColumn; column++) sheet.SetCell(row, column, value, formula, string.IsNullOrWhiteSpace(formula) ? null : DataCellKind.Formula); }

    public static void ApplyFormat(DataSheet sheet, DataCellRange range, IReadOnlyDictionary<string, string?> properties)
    {
        ArgumentNullException.ThrowIfNull(sheet); ArgumentNullException.ThrowIfNull(range); ArgumentNullException.ThrowIfNull(properties); range.Normalize();
        for (var row = range.StartRow; row <= range.EndRow; row++) for (var column = range.StartColumn; column <= range.EndColumn; column++) { var cell = sheet.GetOrCreateCell(row, column); foreach (var (key, value) in properties) { if (string.IsNullOrWhiteSpace(value)) cell.Metadata.Remove(key); else cell.Metadata[key] = value; } }
    }

    public static void InsertRows(DataSheet sheet, int index, int count = 1) { ArgumentNullException.ThrowIfNull(sheet); ValidateStructural(index, count); foreach (var cell in sheet.Cells.Where(cell => cell.Row >= index).OrderByDescending(cell => cell.Row)) cell.Row += count; sheet.Normalize(sheet.Order); }
    public static void DeleteRows(DataSheet sheet, int index, int count = 1) { ArgumentNullException.ThrowIfNull(sheet); ValidateStructural(index, count); var end = checked(index + count); sheet.Cells.RemoveAll(cell => cell.Row >= index && cell.Row < end); foreach (var cell in sheet.Cells.Where(cell => cell.Row >= end)) cell.Row -= count; sheet.Normalize(sheet.Order); }
    public static void InsertColumns(DataSheet sheet, int index, int count = 1) { ArgumentNullException.ThrowIfNull(sheet); ValidateStructural(index, count); foreach (var cell in sheet.Cells.Where(cell => cell.Column >= index).OrderByDescending(cell => cell.Column)) cell.Column += count; sheet.Normalize(sheet.Order); }
    public static void DeleteColumns(DataSheet sheet, int index, int count = 1) { ArgumentNullException.ThrowIfNull(sheet); ValidateStructural(index, count); var end = checked(index + count); sheet.Cells.RemoveAll(cell => cell.Column >= index && cell.Column < end); foreach (var cell in sheet.Cells.Where(cell => cell.Column >= end)) cell.Column -= count; sheet.Normalize(sheet.Order); }

    public static void SortRange(DataSheet sheet, DataCellRange range, int keyColumn, bool descending = false, bool hasHeader = true)
    {
        ArgumentNullException.ThrowIfNull(sheet); ArgumentNullException.ThrowIfNull(range); range.Normalize(); if (keyColumn < range.StartColumn || keyColumn > range.EndColumn) throw new ArgumentOutOfRangeException(nameof(keyColumn));
        var firstDataRow = range.StartRow + (hasHeader ? 1 : 0); if (firstDataRow > range.EndRow) return;
        var rows = Enumerable.Range(firstDataRow, range.EndRow - firstDataRow + 1).Select(row => new RowSnapshot(row, Enumerable.Range(range.StartColumn, range.ColumnCount).Select(column => Clone(sheet.GetCell(row, column), row, column)).ToArray(), sheet.GetCell(row, keyColumn)?.Value ?? string.Empty)).ToList();
        rows.Sort((left, right) => { var result = Compare(left.Key, right.Key); if (result == 0) result = left.SourceRow.CompareTo(right.SourceRow); return descending ? -result : result; });
        sheet.Cells.RemoveAll(cell => cell.Row >= firstDataRow && cell.Row <= range.EndRow && cell.Column >= range.StartColumn && cell.Column <= range.EndColumn);
        for (var offset = 0; offset < rows.Count; offset++) { var targetRow = firstDataRow + offset; foreach (var cell in rows[offset].Cells.Where(cell => cell is not null)) { cell!.Row = targetRow; sheet.Cells.Add(cell); } }
        sheet.Normalize(sheet.Order);
    }

    public static IReadOnlyList<int> FilterRows(DataSheet sheet, DataCellRange range, IReadOnlyList<DataTableFilter> filters, bool hasHeader = true)
    {
        ArgumentNullException.ThrowIfNull(sheet); ArgumentNullException.ThrowIfNull(range); ArgumentNullException.ThrowIfNull(filters); range.Normalize(); var firstDataRow = range.StartRow + (hasHeader ? 1 : 0); if (firstDataRow > range.EndRow) return []; return Enumerable.Range(firstDataRow, range.EndRow - firstDataRow + 1).Where(row => filters.All(filter => Matches(sheet.GetCell(row, filter.Column)?.Value ?? string.Empty, filter))).ToArray();
    }

    public static DataValidationResult ValidateValue(IEnumerable<DataValidationRule> rules, Guid sheetId, int row, int column, string? value)
    {
        ArgumentNullException.ThrowIfNull(rules);
        foreach (var rule in rules.Where(rule => rule is not null && rule.SheetId == sheetId && rule.Range.Contains(row, column))) { if (IsValid(rule, value)) continue; var message = string.IsNullOrWhiteSpace(rule.ErrorMessage) ? $"Value does not satisfy {rule.Kind} validation for {rule.Range}." : rule.ErrorMessage; return new DataValidationResult(false, rule.Id, message); }
        return DataValidationResult.Valid;
    }

    public static bool IsValid(DataValidationRule rule, string? value)
    {
        ArgumentNullException.ThrowIfNull(rule); rule.Normalize(); value ??= string.Empty; if (value.Length == 0) return rule.AllowBlank || rule.Kind == DataValidationKind.Any;
        return rule.Kind switch { DataValidationKind.Any => true, DataValidationKind.List => rule.AllowedValues.Contains(value, StringComparer.OrdinalIgnoreCase), DataValidationKind.WholeNumber => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole) && InNumericBounds(whole, rule), DataValidationKind.Decimal => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && InNumericBounds(number, rule), DataValidationKind.Date => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date) && InDateBounds(date, rule), DataValidationKind.TextLength => InNumericBounds(value.Length, rule), _ => false };
    }

    private static void ValidateStructural(int index, int count) { if (index < 0) throw new ArgumentOutOfRangeException(nameof(index)); if (count < 1) throw new ArgumentOutOfRangeException(nameof(count)); }
    private static bool Matches(string value, DataTableFilter filter) { filter.Normalize(); var comparison = StringComparison.OrdinalIgnoreCase; return filter.Operator switch { DataFilterOperator.Contains => value.Contains(filter.Value, comparison), DataFilterOperator.Equals => value.Equals(filter.Value, comparison), DataFilterOperator.NotEquals => !value.Equals(filter.Value, comparison), DataFilterOperator.StartsWith => value.StartsWith(filter.Value, comparison), DataFilterOperator.EndsWith => value.EndsWith(filter.Value, comparison), DataFilterOperator.IsBlank => string.IsNullOrWhiteSpace(value), DataFilterOperator.IsNotBlank => !string.IsNullOrWhiteSpace(value), DataFilterOperator.GreaterThan => Compare(value, filter.Value) > 0, DataFilterOperator.GreaterThanOrEqual => Compare(value, filter.Value) >= 0, DataFilterOperator.LessThan => Compare(value, filter.Value) < 0, DataFilterOperator.LessThanOrEqual => Compare(value, filter.Value) <= 0, _ => false }; }
    private static int Compare(string left, string right) { if (double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber) && double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber)) return leftNumber.CompareTo(rightNumber); if (DateTimeOffset.TryParse(left, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var leftDate) && DateTimeOffset.TryParse(right, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var rightDate)) return leftDate.CompareTo(rightDate); return string.Compare(left, right, StringComparison.OrdinalIgnoreCase); }
    private static bool InNumericBounds(double value, DataValidationRule rule) { if (!string.IsNullOrWhiteSpace(rule.Minimum) && double.TryParse(rule.Minimum, NumberStyles.Float, CultureInfo.InvariantCulture, out var minimum) && value < minimum) return false; if (!string.IsNullOrWhiteSpace(rule.Maximum) && double.TryParse(rule.Maximum, NumberStyles.Float, CultureInfo.InvariantCulture, out var maximum) && value > maximum) return false; return true; }
    private static bool InDateBounds(DateTimeOffset value, DataValidationRule rule) { if (!string.IsNullOrWhiteSpace(rule.Minimum) && DateTimeOffset.TryParse(rule.Minimum, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var minimum) && value < minimum) return false; if (!string.IsNullOrWhiteSpace(rule.Maximum) && DateTimeOffset.TryParse(rule.Maximum, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var maximum) && value > maximum) return false; return true; }
    private static DataCell? Clone(DataCell? cell, int row, int column) => cell is null ? null : new DataCell { Row = row, Column = column, Kind = cell.Kind, Value = cell.Value, Formula = cell.Formula, Metadata = new Dictionary<string, string>(cell.Metadata, StringComparer.Ordinal) };
    private sealed record RowSnapshot(int SourceRow, DataCell?[] Cells, string Key);
}
