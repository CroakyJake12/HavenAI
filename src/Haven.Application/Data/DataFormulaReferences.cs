using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Haven.Application;

public enum DataFormulaSegmentOperator { Add, Subtract, Multiply, Divide, Power, Concat, Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }
public enum DataFormulaUnaryOperator { Positive, Negative, Percent }
public sealed record DataFormulaReference(string? SheetName, int Row, int Column, bool AbsoluteRow = false, bool AbsoluteColumn = false)
{
    public string ToA1() => (AbsoluteColumn ? "$" : string.Empty) + DataFormulaReferenceUtility.ColumnName(Column) + (AbsoluteRow ? "$" : string.Empty) + (Row + 1).ToString(CultureInfo.InvariantCulture);
}
public sealed record DataFormulaRangeReference(DataFormulaReference Start, DataFormulaReference End);
public abstract record DataFormulaExpression;
public sealed record DataFormulaLiteralExpression(object? Value) : DataFormulaExpression;
public sealed record DataFormulaReferenceExpression(DataFormulaReference Reference) : DataFormulaExpression;
public sealed record DataFormulaRangeExpression(DataFormulaRangeReference Range) : DataFormulaExpression;
public sealed record DataFormulaNameExpression(string Name) : DataFormulaExpression;
public sealed record DataFormulaErrorExpression(DataFormulaErrorCode Code) : DataFormulaExpression;
public sealed record DataFormulaUnaryExpression(DataFormulaUnaryOperator Operator, DataFormulaExpression Operand) : DataFormulaExpression;
public sealed record DataFormulaBinaryExpression(DataFormulaSegmentOperator Operator, DataFormulaExpression Left, DataFormulaExpression Right) : DataFormulaExpression;
public sealed record DataFormulaFunctionExpression(string Name, IReadOnlyList<DataFormulaExpression> Arguments) : DataFormulaExpression;

public sealed class DataFormulaParseException(string message, int position) : FormatException($"{message} at formula position {position + 1}.")
{
    public int Position { get; } = position;
}

public static partial class DataFormulaReferenceUtility
{
    public const int MaximumColumn = 16_383; public const int MaximumRow = 1_048_575;
    public static bool TryParse(string? text, out DataFormulaReference reference, string? sheetName = null)
    {
        reference = new DataFormulaReference(sheetName, 0, 0); if (string.IsNullOrWhiteSpace(text)) return false; var match = CellReferencePattern().Match(text.Trim()); if (!match.Success) return false;
        var column = ColumnIndex(match.Groups["column"].Value); if (column is < 0 or > MaximumColumn || !int.TryParse(match.Groups["row"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var rowOneBased) || rowOneBased is < 1 or > MaximumRow + 1) return false;
        reference = new DataFormulaReference(sheetName, rowOneBased - 1, column, match.Groups["rowAbs"].Value.Length > 0, match.Groups["columnAbs"].Value.Length > 0); return true;
    }
    public static int ColumnIndex(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return -1; var result = 0; foreach (var character in columnName.Trim().ToUpperInvariant()) { if (character is < 'A' or > 'Z') return -1; checked { result = result * 26 + (character - 'A' + 1); } } return result - 1;
    }
    public static string ColumnName(int column)
    {
        if (column < 0) throw new ArgumentOutOfRangeException(nameof(column)); var value = column + 1; var builder = new StringBuilder(); while (value > 0) { value--; builder.Insert(0, (char)('A' + value % 26)); value /= 26; } return builder.ToString();
    }
    public static string TranslateFormula(string formula, int rowDelta, int columnDelta)
    {
        if (string.IsNullOrWhiteSpace(formula) || (rowDelta == 0 && columnDelta == 0)) return formula ?? string.Empty; var builder = new StringBuilder(formula.Length + 16);
        for (var index = 0; index < formula.Length;)
        {
            if (formula[index] == '"') { var start = index++; while (index < formula.Length) { if (formula[index] != '"') { index++; continue; } if (index + 1 < formula.Length && formula[index + 1] == '"') { index += 2; continue; } index++; break; } builder.Append(formula, start, index - start); continue; }
            var match = TranslatableReferencePattern().Match(formula, index); if (match.Success && match.Index == index)
            {
                var before = index > 0 ? formula[index - 1] : (char)0; var afterIndex = index + match.Length; var after = afterIndex < formula.Length ? formula[afterIndex] : (char)0;
                if (!char.IsLetterOrDigit(before) && before != '_' && !char.IsLetterOrDigit(after) && after != '_')
                {
                    var columnAbsolute = match.Groups["columnAbs"].Value.Length > 0; var rowAbsolute = match.Groups["rowAbs"].Value.Length > 0; var column = ColumnIndex(match.Groups["column"].Value); var row = int.Parse(match.Groups["row"].Value, CultureInfo.InvariantCulture) - 1; if (!columnAbsolute) column += columnDelta; if (!rowAbsolute) row += rowDelta;
                    if (column < 0 || row < 0 || column > MaximumColumn || row > MaximumRow) builder.Append("#REF!"); else { builder.Append(match.Groups["sheet"].Value); if (columnAbsolute) builder.Append('$'); builder.Append(ColumnName(column)); if (rowAbsolute) builder.Append('$'); builder.Append(row + 1); } index += match.Length; continue;
                }
            }
            builder.Append(formula[index++]);
        }
        return builder.ToString();
    }
    public static string RenameSheetReferences(string formula, string oldSheetName, string newSheetName)
    {
        if (string.IsNullOrWhiteSpace(formula) || oldSheetName.Equals(newSheetName, StringComparison.OrdinalIgnoreCase)) return formula ?? string.Empty;
        var replacement = SheetPrefix(newSheetName);
        return RewriteSheetPrefix(formula, SheetPrefix(oldSheetName), oldSheetName + "!", replacement);
    }

    private static string SheetPrefix(string name) => "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'!";

    private static string RewriteSheetPrefix(string formula, string quotedPrefix, string plainPrefix, string replacement)
    {
        var builder = new StringBuilder(formula.Length); var inString = false;
        for (var index = 0; index < formula.Length;)
        {
            if (formula[index] == '"')
            {
                if (inString && index + 1 < formula.Length && formula[index + 1] == '"') { builder.Append("\"\""); index += 2; continue; }
                inString = !inString; builder.Append(formula[index++]); continue;
            }
            if (!inString && formula.AsSpan(index).StartsWith(quotedPrefix, StringComparison.OrdinalIgnoreCase)) { builder.Append(replacement); index += quotedPrefix.Length; continue; }
            var boundary = index == 0 || (!char.IsLetterOrDigit(formula[index - 1]) && formula[index - 1] != '_');
            if (!inString && boundary && formula.AsSpan(index).StartsWith(plainPrefix, StringComparison.OrdinalIgnoreCase)) { builder.Append(replacement); index += plainPrefix.Length; continue; }
            builder.Append(formula[index++]);
        }
        return builder.ToString();
    }
    [GeneratedRegex(@"^(?<columnAbs>\$?)(?<column>[A-Za-z]{1,4})(?<rowAbs>\$?)(?<row>[1-9][0-9]*)$", RegexOptions.CultureInvariant)] private static partial Regex CellReferencePattern();
    [GeneratedRegex(@"(?<sheet>(?:'(?:[^']|'')+'|[A-Za-z_][A-Za-z0-9_ .]*)!)?(?<columnAbs>\$?)(?<column>[A-Za-z]{1,4})(?<rowAbs>\$?)(?<row>[1-9][0-9]*)", RegexOptions.CultureInvariant)] private static partial Regex TranslatableReferencePattern();
}
