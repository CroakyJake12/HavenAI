using System.Globalization;

namespace Haven.Application;

public enum DataFormulaErrorCode { DivideByZero, Reference, Value, Name, NotAvailable, Cycle, Number, Parse }
public sealed record DataFormulaIssue(Guid SheetId, string SheetName, string CellAddress, DataFormulaErrorCode Code, string Message);
public sealed record DataFormulaCellAddress(Guid SheetId, string SheetName, int Row, int Column)
{
    public string A1 => DataFormulaReferenceUtility.ColumnName(Column) + (Row + 1).ToString(CultureInfo.InvariantCulture);
}
public sealed record DataFormulaRecalculationReport(int FormulaCells, int EvaluatedCells, int ChangedCells, IReadOnlyList<DataFormulaIssue> Issues);
public sealed record DataFormulaDependencyGraph(IReadOnlyDictionary<DataFormulaCellAddress, IReadOnlySet<DataFormulaCellAddress>> Dependencies);

internal sealed record DataFormulaValue(object? Scalar, IReadOnlyList<DataFormulaValue>? Items = null, int Rows = 1, int Columns = 1, DataFormulaErrorCode? ErrorCode = null, string ErrorMessage = "")
{
    public bool IsError => ErrorCode is not null;
    public bool IsRange => Items is not null;
    public static DataFormulaValue Empty { get; } = new((object?)null);
    public static DataFormulaValue Number(double value) => double.IsFinite(value) ? new(value) : Error(DataFormulaErrorCode.Number, "The formula produced a non-finite number.");
    public static DataFormulaValue Text(string? value) => new(value ?? string.Empty);
    public static DataFormulaValue Boolean(bool value) => new(value);
    public static DataFormulaValue Date(DateTimeOffset value) => new(value);
    public static DataFormulaValue Range(IReadOnlyList<DataFormulaValue> values, int rows, int columns) => new(null, values, Math.Max(0, rows), Math.Max(0, columns));
    public static DataFormulaValue Error(DataFormulaErrorCode code, string message) => new(null, null, 1, 1, code, message);

    public string Display()
    {
        if (ErrorCode is { } code) return code switch
        {
            DataFormulaErrorCode.DivideByZero => "#DIV/0!", DataFormulaErrorCode.Reference => "#REF!", DataFormulaErrorCode.Value => "#VALUE!",
            DataFormulaErrorCode.Name => "#NAME?", DataFormulaErrorCode.NotAvailable => "#N/A", DataFormulaErrorCode.Cycle => "#CYCLE!",
            DataFormulaErrorCode.Number => "#NUM!", _ => "#ERROR!"
        };
        if (IsRange) return Items!.Count == 1 ? Items[0].Display() : "#VALUE!";
        return Scalar switch
        {
            null => string.Empty, bool value => value ? "TRUE" : "FALSE", double value => value.ToString("G15", CultureInfo.InvariantCulture),
            DateTimeOffset value => value.TimeOfDay == TimeSpan.Zero ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            _ => Convert.ToString(Scalar, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }
}

internal static class DataFormulaConversions
{
    public static DataFormulaValue First(DataFormulaValue value) => value.IsRange ? value.Items!.FirstOrDefault() ?? DataFormulaValue.Empty : value;

    public static bool TryNumber(DataFormulaValue source, out double value)
    {
        source = First(source); value = 0; if (source.IsError) return false;
        switch (source.Scalar)
        {
            case null: return true;
            case double number: value = number; return true;
            case bool boolean: value = boolean ? 1 : 0; return true;
            case DateTimeOffset date: value = date.UtcDateTime.ToOADate(); return true;
            case string text when string.IsNullOrWhiteSpace(text): return true;
            case string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed): value = parsed; return true;
            default: return false;
        }
    }

    public static bool TryBoolean(DataFormulaValue source, out bool value)
    {
        source = First(source); value = false; if (source.IsError) return false;
        switch (source.Scalar)
        {
            case null: return true; case bool boolean: value = boolean; return true; case double number: value = Math.Abs(number) > double.Epsilon; return true;
            case string text when bool.TryParse(text, out var parsed): value = parsed; return true; case string text when string.IsNullOrWhiteSpace(text): return true; default: return false;
        }
    }

    public static string Text(DataFormulaValue source) => First(source).Display();
    public static IEnumerable<DataFormulaValue> Flatten(IEnumerable<DataFormulaValue> values) => values.SelectMany(value => value.IsRange ? value.Items! : [value]);
    public static DataFormulaValue? FirstError(IEnumerable<DataFormulaValue> values) => Flatten(values).FirstOrDefault(value => value.IsError);
}

internal static class DataFormulaFunctions
{
    public static DataFormulaValue Evaluate(string name, IReadOnlyList<DataFormulaValue> arguments, DateTimeOffset now)
    {
        var upper = name.ToUpperInvariant();
        var error = DataFormulaConversions.FirstError(arguments);
        if (error is not null && upper is not "IFERROR") return error;
        return upper switch
        {
            "SUM" => Aggregate(arguments, values => values.Sum()),
            "AVERAGE" or "AVG" => Aggregate(arguments, values => values.Count == 0 ? double.NaN : values.Average()),
            "MIN" => Aggregate(arguments, values => values.Count == 0 ? 0 : values.Min()),
            "MAX" => Aggregate(arguments, values => values.Count == 0 ? 0 : values.Max()),
            "COUNT" => DataFormulaValue.Number(Numbers(arguments).Count),
            "COUNTA" => DataFormulaValue.Number(DataFormulaConversions.Flatten(arguments).Count(value => value.Scalar is not null && value.Display().Length > 0)),
            "MEDIAN" => Median(arguments),
            "STDEV.S" or "STDEV" => SampleStatistic(arguments, standardDeviation: true),
            "VAR.S" or "VAR" => SampleStatistic(arguments, standardDeviation: false),
            "ABS" => UnaryNumber(arguments, Math.Abs),
            "SQRT" => UnaryNumber(arguments, value => value < 0 ? double.NaN : Math.Sqrt(value)),
            "INT" => UnaryNumber(arguments, Math.Floor),
            "EXP" => UnaryNumber(arguments, Math.Exp),
            "LN" => UnaryNumber(arguments, value => value <= 0 ? double.NaN : Math.Log(value)),
            "LOG10" => UnaryNumber(arguments, value => value <= 0 ? double.NaN : Math.Log10(value)),
            "ROUND" => Round(arguments, MidpointRounding.AwayFromZero),
            "ROUNDUP" => RoundDirectional(arguments, up: true),
            "ROUNDDOWN" => RoundDirectional(arguments, up: false),
            "MOD" => Mod(arguments),
            "POWER" => Power(arguments),
            "PI" => arguments.Count == 0 ? DataFormulaValue.Number(Math.PI) : ValueError("PI takes no arguments."),
            "AND" => Logical(arguments, and: true),
            "OR" => Logical(arguments, and: false),
            "NOT" => Not(arguments),
            "LEN" => arguments.Count == 1 ? DataFormulaValue.Number(DataFormulaConversions.Text(arguments[0]).Length) : ValueError("LEN expects one argument."),
            "LEFT" => LeftRight(arguments, left: true),
            "RIGHT" => LeftRight(arguments, left: false),
            "MID" => Mid(arguments),
            "LOWER" => arguments.Count == 1 ? DataFormulaValue.Text(DataFormulaConversions.Text(arguments[0]).ToLowerInvariant()) : ValueError("LOWER expects one argument."),
            "UPPER" => arguments.Count == 1 ? DataFormulaValue.Text(DataFormulaConversions.Text(arguments[0]).ToUpperInvariant()) : ValueError("UPPER expects one argument."),
            "TRIM" => arguments.Count == 1 ? DataFormulaValue.Text(string.Join(' ', DataFormulaConversions.Text(arguments[0]).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))) : ValueError("TRIM expects one argument."),
            "CONCAT" or "CONCATENATE" => DataFormulaValue.Text(string.Concat(DataFormulaConversions.Flatten(arguments).Select(DataFormulaConversions.Text))),
            "SUBSTITUTE" => Substitute(arguments),
            "DATE" => Date(arguments),
            "TODAY" => arguments.Count == 0 ? DataFormulaValue.Date(new DateTimeOffset(now.Date, now.Offset)) : ValueError("TODAY takes no arguments."),
            "NOW" => arguments.Count == 0 ? DataFormulaValue.Date(now) : ValueError("NOW takes no arguments."),
            "YEAR" => DatePart(arguments, value => value.Year),
            "MONTH" => DatePart(arguments, value => value.Month),
            "DAY" => DatePart(arguments, value => value.Day),
            "MATCH" => Match(arguments),
            "INDEX" => Index(arguments),
            "XLOOKUP" => XLookup(arguments),
            "VLOOKUP" => VLookup(arguments),
            _ => DataFormulaValue.Error(DataFormulaErrorCode.Name, $"Unknown function '{name}'.")
        };
    }

    private static List<double> Numbers(IEnumerable<DataFormulaValue> arguments)
    {
        var result = new List<double>(); foreach (var item in DataFormulaConversions.Flatten(arguments)) if (DataFormulaConversions.TryNumber(item, out var number) && item.Scalar is not null) result.Add(number); return result;
    }
    private static DataFormulaValue Aggregate(IReadOnlyList<DataFormulaValue> args, Func<List<double>, double> operation) { var values = Numbers(args); var result = operation(values); return double.IsFinite(result) ? DataFormulaValue.Number(result) : DataFormulaValue.Error(DataFormulaErrorCode.Number, "The aggregate produced an invalid number."); }
    private static DataFormulaValue UnaryNumber(IReadOnlyList<DataFormulaValue> args, Func<double, double> operation) { if (args.Count != 1 || !DataFormulaConversions.TryNumber(args[0], out var value)) return ValueError("This function expects one numeric argument."); var result = operation(value); return double.IsFinite(result) ? DataFormulaValue.Number(result) : DataFormulaValue.Error(DataFormulaErrorCode.Number, "The numeric result is outside the supported domain."); }
    private static DataFormulaValue Median(IReadOnlyList<DataFormulaValue> args) { var values = Numbers(args); if (values.Count == 0) return DataFormulaValue.Number(0); values.Sort(); var middle = values.Count / 2; return DataFormulaValue.Number(values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2); }
    private static DataFormulaValue SampleStatistic(IReadOnlyList<DataFormulaValue> args, bool standardDeviation) { var values = Numbers(args); if (values.Count < 2) return DataFormulaValue.Error(DataFormulaErrorCode.DivideByZero, "Sample statistics require at least two numeric values."); var average = values.Average(); var variance = values.Sum(value => Math.Pow(value - average, 2)) / (values.Count - 1); return DataFormulaValue.Number(standardDeviation ? Math.Sqrt(variance) : variance); }
    private static DataFormulaValue Round(IReadOnlyList<DataFormulaValue> args, MidpointRounding mode) { if (args.Count is < 1 or > 2 || !DataFormulaConversions.TryNumber(args[0], out var value) || (args.Count == 2 && !DataFormulaConversions.TryNumber(args[1], out _))) return ValueError("ROUND expects a number and optional digits."); var digits = args.Count == 2 && DataFormulaConversions.TryNumber(args[1], out var d) ? Math.Clamp((int)d, -15, 15) : 0; if (digits >= 0) return DataFormulaValue.Number(Math.Round(value, digits, mode)); var scale = Math.Pow(10, -digits); return DataFormulaValue.Number(Math.Round(value / scale, 0, mode) * scale); }
    private static DataFormulaValue RoundDirectional(IReadOnlyList<DataFormulaValue> args, bool up) { if (args.Count is < 1 or > 2 || !DataFormulaConversions.TryNumber(args[0], out var value)) return ValueError("ROUNDUP/ROUNDDOWN expect a number and optional digits."); var digits = args.Count == 2 && DataFormulaConversions.TryNumber(args[1], out var d) ? Math.Clamp((int)d, -15, 15) : 0; var scale = Math.Pow(10, digits); var scaled = value * scale; var rounded = up ? (value >= 0 ? Math.Ceiling(scaled) : Math.Floor(scaled)) : (value >= 0 ? Math.Floor(scaled) : Math.Ceiling(scaled)); return DataFormulaValue.Number(rounded / scale); }
    private static DataFormulaValue Mod(IReadOnlyList<DataFormulaValue> args) { if (args.Count != 2 || !DataFormulaConversions.TryNumber(args[0], out var left) || !DataFormulaConversions.TryNumber(args[1], out var right)) return ValueError("MOD expects two numbers."); if (Math.Abs(right) < double.Epsilon) return DataFormulaValue.Error(DataFormulaErrorCode.DivideByZero, "MOD divisor cannot be zero."); return DataFormulaValue.Number(left - right * Math.Floor(left / right)); }
    private static DataFormulaValue Power(IReadOnlyList<DataFormulaValue> args) { if (args.Count != 2 || !DataFormulaConversions.TryNumber(args[0], out var left) || !DataFormulaConversions.TryNumber(args[1], out var right)) return ValueError("POWER expects two numbers."); return DataFormulaValue.Number(Math.Pow(left, right)); }
    private static DataFormulaValue Logical(IReadOnlyList<DataFormulaValue> args, bool and) { if (args.Count == 0) return ValueError("Logical functions require arguments."); var values = new List<bool>(); foreach (var item in DataFormulaConversions.Flatten(args)) { if (!DataFormulaConversions.TryBoolean(item, out var value)) return ValueError("Logical arguments must be boolean-compatible."); values.Add(value); } return DataFormulaValue.Boolean(and ? values.All(value => value) : values.Any(value => value)); }
    private static DataFormulaValue Not(IReadOnlyList<DataFormulaValue> args) => args.Count == 1 && DataFormulaConversions.TryBoolean(args[0], out var value) ? DataFormulaValue.Boolean(!value) : ValueError("NOT expects one boolean-compatible argument.");
    private static DataFormulaValue LeftRight(IReadOnlyList<DataFormulaValue> args, bool left) { if (args.Count is < 1 or > 2) return ValueError("LEFT/RIGHT expect text and optional character count."); var text = DataFormulaConversions.Text(args[0]); var count = args.Count == 2 && DataFormulaConversions.TryNumber(args[1], out var value) ? (int)value : 1; if (count < 0) return ValueError("Character count cannot be negative."); count = Math.Min(count, text.Length); return DataFormulaValue.Text(left ? text[..count] : text[(text.Length - count)..]); }
    private static DataFormulaValue Mid(IReadOnlyList<DataFormulaValue> args) { if (args.Count != 3 || !DataFormulaConversions.TryNumber(args[1], out var start) || !DataFormulaConversions.TryNumber(args[2], out var length)) return ValueError("MID expects text, start and length."); var text = DataFormulaConversions.Text(args[0]); var offset = Math.Max(0, (int)start - 1); var count = Math.Max(0, (int)length); return DataFormulaValue.Text(offset >= text.Length ? string.Empty : text.Substring(offset, Math.Min(count, text.Length - offset))); }
    private static DataFormulaValue Substitute(IReadOnlyList<DataFormulaValue> args) { if (args.Count != 3) return ValueError("SUBSTITUTE expects text, old text and new text."); return DataFormulaValue.Text(DataFormulaConversions.Text(args[0]).Replace(DataFormulaConversions.Text(args[1]), DataFormulaConversions.Text(args[2]), StringComparison.Ordinal)); }
    private static DataFormulaValue Date(IReadOnlyList<DataFormulaValue> args) { if (args.Count != 3 || !DataFormulaConversions.TryNumber(args[0], out var year) || !DataFormulaConversions.TryNumber(args[1], out var month) || !DataFormulaConversions.TryNumber(args[2], out var day)) return ValueError("DATE expects year, month and day."); try { return DataFormulaValue.Date(new DateTimeOffset((int)year, (int)month, (int)day, 0, 0, 0, TimeSpan.Zero)); } catch (ArgumentOutOfRangeException) { return DataFormulaValue.Error(DataFormulaErrorCode.Number, "DATE arguments are outside the supported calendar."); } }
    private static DataFormulaValue DatePart(IReadOnlyList<DataFormulaValue> args, Func<DateTimeOffset, int> selector) { if (args.Count != 1) return ValueError("Date-part functions expect one value."); var value = DataFormulaConversions.First(args[0]); if (value.Scalar is DateTimeOffset date) return DataFormulaValue.Number(selector(date)); if (value.Scalar is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)) return DataFormulaValue.Number(selector(date)); if (DataFormulaConversions.TryNumber(value, out var serial)) { try { return DataFormulaValue.Number(selector(new DateTimeOffset(DateTime.FromOADate(serial), TimeSpan.Zero))); } catch (ArgumentException) { } } return ValueError("The value is not a recognised date."); }
    private static DataFormulaValue Match(IReadOnlyList<DataFormulaValue> args) { if (args.Count is < 2 or > 3 || !args[1].IsRange) return ValueError("MATCH expects a lookup value and one-dimensional range."); var target = DataFormulaConversions.Text(args[0]); var items = args[1].Items!; var index = items.ToList().FindIndex(item => string.Equals(DataFormulaConversions.Text(item), target, StringComparison.OrdinalIgnoreCase)); return index >= 0 ? DataFormulaValue.Number(index + 1) : DataFormulaValue.Error(DataFormulaErrorCode.NotAvailable, "MATCH did not find the lookup value."); }
    private static DataFormulaValue Index(IReadOnlyList<DataFormulaValue> args) { if (args.Count is < 2 or > 3 || !args[0].IsRange || !DataFormulaConversions.TryNumber(args[1], out var rowValue)) return ValueError("INDEX expects a range and row number."); var range = args[0]; var row = (int)rowValue - 1; var column = args.Count == 3 && DataFormulaConversions.TryNumber(args[2], out var columnValue) ? (int)columnValue - 1 : 0; if (row < 0 || column < 0 || row >= range.Rows || column >= range.Columns) return DataFormulaValue.Error(DataFormulaErrorCode.Reference, "INDEX points outside the supplied range."); return range.Items![row * range.Columns + column]; }
    private static DataFormulaValue XLookup(IReadOnlyList<DataFormulaValue> args) { if (args.Count is < 3 or > 4 || !args[1].IsRange || !args[2].IsRange) return ValueError("XLOOKUP expects lookup value, lookup range and return range."); var lookup = args[1].Items!; var returns = args[2].Items!; if (lookup.Count != returns.Count) return ValueError("XLOOKUP ranges must contain the same number of cells."); var target = DataFormulaConversions.Text(args[0]); for (var i = 0; i < lookup.Count; i++) if (string.Equals(DataFormulaConversions.Text(lookup[i]), target, StringComparison.OrdinalIgnoreCase)) return returns[i]; return args.Count == 4 ? DataFormulaConversions.First(args[3]) : DataFormulaValue.Error(DataFormulaErrorCode.NotAvailable, "XLOOKUP did not find the lookup value."); }
    private static DataFormulaValue VLookup(IReadOnlyList<DataFormulaValue> args) { if (args.Count is < 3 or > 4 || !args[1].IsRange || !DataFormulaConversions.TryNumber(args[2], out var columnValue)) return ValueError("VLOOKUP expects lookup value, table range and column index."); var table = args[1]; var column = (int)columnValue - 1; if (column < 0 || column >= table.Columns) return DataFormulaValue.Error(DataFormulaErrorCode.Reference, "VLOOKUP column index is outside the table."); var target = DataFormulaConversions.Text(args[0]); for (var row = 0; row < table.Rows; row++) if (string.Equals(DataFormulaConversions.Text(table.Items![row * table.Columns]), target, StringComparison.OrdinalIgnoreCase)) return table.Items[row * table.Columns + column]; return DataFormulaValue.Error(DataFormulaErrorCode.NotAvailable, "VLOOKUP did not find the lookup value."); }
    private static DataFormulaValue ValueError(string message) => DataFormulaValue.Error(DataFormulaErrorCode.Value, message);
}
