using System.Globalization;
using Haven.Core;

namespace Haven.Application;

public sealed class DataFormulaEngine(TimeProvider? timeProvider = null)
{
    private const int MaximumEvaluatedRangeCells = 100_000;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public DataFormulaRecalculationReport Recalculate(DataWorkbook workbook, IEnumerable<DataFormulaCellAddress>? changedCells = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        workbook.Normalize();
        var graph = BuildDependencyGraph(workbook);
        var formulas = FormulaAddresses(workbook).ToArray();
        var targets = changedCells is null ? formulas.ToHashSet() : AffectedFormulaCells(graph, formulas, changedCells);
        var context = new EvaluationContext(workbook, _timeProvider.GetLocalNow());
        var changed = 0;
        foreach (var target in targets.OrderBy(value => value.SheetName, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.Row).ThenBy(value => value.Column))
        {
            var sheet = workbook.Sheets.FirstOrDefault(value => value.Id == target.SheetId);
            if (sheet is null) continue;
            var cell = sheet.GetCell(target.Row, target.Column);
            if (cell is null || string.IsNullOrWhiteSpace(cell.Formula)) continue;
            var before = cell.Value;
            _ = EvaluateCell(context, sheet, target.Row, target.Column);
            if (!string.Equals(before, cell.Value, StringComparison.Ordinal)) changed++;
        }
        return new DataFormulaRecalculationReport(formulas.Length, context.EvaluatedFormulaCells, changed, context.Issues.ToArray());
    }

    public DataFormulaDependencyGraph BuildDependencyGraph(DataWorkbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook); workbook.Normalize();
        var result = new Dictionary<DataFormulaCellAddress, IReadOnlySet<DataFormulaCellAddress>>();
        foreach (var sheet in workbook.Sheets)
        foreach (var cell in sheet.Cells.Where(value => !string.IsNullOrWhiteSpace(value.Formula)))
        {
            var address = Address(sheet, cell.Row, cell.Column);
            var dependencies = new HashSet<DataFormulaCellAddress>();
            try
            {
                var expression = DataFormulaParser.Parse(cell.Formula);
                CollectDependencies(workbook, sheet, expression, dependencies, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
            catch (DataFormulaParseException) { }
            result[address] = dependencies;
        }
        return new DataFormulaDependencyGraph(result);
    }

    public bool CopyFormula(DataWorkbook workbook, Guid sheetId, int sourceRow, int sourceColumn, int targetRow, int targetColumn)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var sheet = workbook.Sheets.FirstOrDefault(value => value.Id == sheetId);
        var source = sheet?.GetCell(sourceRow, sourceColumn);
        if (sheet is null || source is null || string.IsNullOrWhiteSpace(source.Formula)) return false;
        var translated = DataFormulaReferenceUtility.TranslateFormula(source.Formula, targetRow - sourceRow, targetColumn - sourceColumn);
        sheet.SetCell(targetRow, targetColumn, string.Empty, translated, DataCellKind.Formula);
        _ = Recalculate(workbook, [Address(sheet, targetRow, targetColumn)]);
        return true;
    }

    public int FillFormula(DataWorkbook workbook, Guid sheetId, int sourceRow, int sourceColumn, int fromRow, int fromColumn, int toRow, int toColumn)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var sheet = workbook.Sheets.FirstOrDefault(value => value.Id == sheetId);
        var source = sheet?.GetCell(sourceRow, sourceColumn);
        if (sheet is null || source is null || string.IsNullOrWhiteSpace(source.Formula)) return 0;
        var minRow = Math.Min(fromRow, toRow); var maxRow = Math.Max(fromRow, toRow); var minColumn = Math.Min(fromColumn, toColumn); var maxColumn = Math.Max(fromColumn, toColumn);
        var changed = new List<DataFormulaCellAddress>();
        for (var row = minRow; row <= maxRow; row++)
        for (var column = minColumn; column <= maxColumn; column++)
        {
            if (row == sourceRow && column == sourceColumn) continue;
            var translated = DataFormulaReferenceUtility.TranslateFormula(source.Formula, row - sourceRow, column - sourceColumn);
            sheet.SetCell(row, column, string.Empty, translated, DataCellKind.Formula); changed.Add(Address(sheet, row, column));
        }
        if (changed.Count > 0) _ = Recalculate(workbook, changed);
        return changed.Count;
    }

    public static DataFormulaCellAddress Address(DataSheet sheet, int row, int column) => new(sheet.Id, sheet.Name, row, column);

    private static HashSet<DataFormulaCellAddress> AffectedFormulaCells(DataFormulaDependencyGraph graph, IReadOnlyCollection<DataFormulaCellAddress> formulas, IEnumerable<DataFormulaCellAddress> changedCells)
    {
        var reverse = new Dictionary<DataFormulaCellAddress, HashSet<DataFormulaCellAddress>>();
        foreach (var pair in graph.Dependencies)
        foreach (var dependency in pair.Value)
        {
            if (!reverse.TryGetValue(dependency, out var dependents)) reverse[dependency] = dependents = [];
            dependents.Add(pair.Key);
        }
        var formulaSet = formulas.ToHashSet(); var result = new HashSet<DataFormulaCellAddress>(); var queue = new Queue<DataFormulaCellAddress>(); var seen = new HashSet<DataFormulaCellAddress>();
        foreach (var changed in changedCells) { queue.Enqueue(changed); seen.Add(changed); if (formulaSet.Contains(changed)) result.Add(changed); }
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!reverse.TryGetValue(current, out var dependents)) continue;
            foreach (var dependent in dependents) if (seen.Add(dependent)) { result.Add(dependent); queue.Enqueue(dependent); }
        }
        return result;
    }

    private static IEnumerable<DataFormulaCellAddress> FormulaAddresses(DataWorkbook workbook) => workbook.Sheets.SelectMany(sheet => sheet.Cells.Where(cell => !string.IsNullOrWhiteSpace(cell.Formula)).Select(cell => Address(sheet, cell.Row, cell.Column)));

    private static void CollectDependencies(DataWorkbook workbook, DataSheet currentSheet, DataFormulaExpression expression, ISet<DataFormulaCellAddress> output, ISet<string> names)
    {
        switch (expression)
        {
            case DataFormulaReferenceExpression reference:
                if (ResolveReferenceSheet(workbook, currentSheet, reference.Reference.SheetName) is { } sheet) output.Add(Address(sheet, reference.Reference.Row, reference.Reference.Column));
                break;
            case DataFormulaRangeExpression range:
                if (ResolveRange(workbook, currentSheet, range.Range) is { } resolved)
                {
                    var count = (long)(resolved.MaxRow - resolved.MinRow + 1) * (resolved.MaxColumn - resolved.MinColumn + 1);
                    if (count <= MaximumEvaluatedRangeCells) for (var row = resolved.MinRow; row <= resolved.MaxRow; row++) for (var column = resolved.MinColumn; column <= resolved.MaxColumn; column++) output.Add(Address(resolved.Sheet, row, column));
                }
                break;
            case DataFormulaUnaryExpression unary: CollectDependencies(workbook, currentSheet, unary.Operand, output, names); break;
            case DataFormulaBinaryExpression binary: CollectDependencies(workbook, currentSheet, binary.Left, output, names); CollectDependencies(workbook, currentSheet, binary.Right, output, names); break;
            case DataFormulaFunctionExpression function: foreach (var argument in function.Arguments) CollectDependencies(workbook, currentSheet, argument, output, names); break;
            case DataFormulaNameExpression name:
                if (!names.Add(name.Name)) break;
                var named = workbook.NamedRanges.FirstOrDefault(value => value.Name.Equals(name.Name, StringComparison.OrdinalIgnoreCase));
                if (named is not null && !string.IsNullOrWhiteSpace(named.RefersTo)) try { CollectDependencies(workbook, currentSheet, DataFormulaParser.Parse(named.RefersTo), output, names); } catch (DataFormulaParseException) { }
                names.Remove(name.Name);
                break;
        }
    }

    private static DataFormulaValue EvaluateCell(EvaluationContext context, DataSheet sheet, int row, int column)
    {
        var key = new CellKey(sheet.Id, row, column);
        if (context.Cache.TryGetValue(key, out var cached)) return cached;
        var cell = sheet.GetCell(row, column);
        if (cell is null) return DataFormulaValue.Empty;
        if (string.IsNullOrWhiteSpace(cell.Formula)) return context.Cache[key] = FromStoredCell(cell);
        if (!context.Visiting.Add(key)) return DataFormulaValue.Error(DataFormulaErrorCode.Cycle, "Circular reference detected.");
        context.EvaluatedFormulaCells++;
        DataFormulaValue value;
        try
        {
            var expression = DataFormulaParser.Parse(cell.Formula);
            value = EvaluateExpression(context, sheet, expression);
            if (value.IsRange) value = value.Items!.Count == 1 ? value.Items[0] : DataFormulaValue.Error(DataFormulaErrorCode.Value, "A formula cannot return a multi-cell range directly.");
        }
        catch (DataFormulaParseException ex) { value = DataFormulaValue.Error(DataFormulaErrorCode.Parse, ex.Message); }
        catch (OverflowException) { value = DataFormulaValue.Error(DataFormulaErrorCode.Number, "The formula exceeded supported numeric bounds."); }
        finally { context.Visiting.Remove(key); }
        var importedCachedValue = cell.Metadata.GetValueOrDefault("xlsxCachedValue");
        var useImportedCache = value.IsError
            && value.ErrorCode is DataFormulaErrorCode.Name or DataFormulaErrorCode.Parse
            && !string.IsNullOrWhiteSpace(importedCachedValue);
        if (useImportedCache)
        {
            var fallbackCell = new DataCell { Value = importedCachedValue!, Kind = DataCell.InferKind(importedCachedValue!) };
            var fallback = FromStoredCell(fallbackCell);
            context.Cache[key] = fallback;
            cell.Value = importedCachedValue!; cell.Kind = DataCellKind.Formula;
            cell.Metadata["formulaError"] = value.ErrorMessage; cell.Metadata["formulaCachedFallback"] = "xlsx";
            context.AddIssue(sheet, row, column, value.ErrorCode!.Value, value.ErrorMessage);
            return fallback;
        }
        context.Cache[key] = value;
        cell.Value = value.Display(); cell.Kind = DataCellKind.Formula; cell.Metadata.Remove("formulaCachedFallback");
        if (value.IsError)
        {
            cell.Metadata["formulaError"] = value.ErrorMessage;
            context.AddIssue(sheet, row, column, value.ErrorCode!.Value, value.ErrorMessage);
        }
        else cell.Metadata.Remove("formulaError");
        return value;
    }

    private static DataFormulaValue EvaluateExpression(EvaluationContext context, DataSheet currentSheet, DataFormulaExpression expression)
    {
        switch (expression)
        {
            case DataFormulaLiteralExpression literal: return literal.Value switch { null => DataFormulaValue.Empty, double number => DataFormulaValue.Number(number), bool boolean => DataFormulaValue.Boolean(boolean), string text => DataFormulaValue.Text(text), DateTimeOffset date => DataFormulaValue.Date(date), _ => DataFormulaValue.Text(Convert.ToString(literal.Value, CultureInfo.InvariantCulture)) };
            case DataFormulaReferenceExpression reference: return EvaluateReference(context, currentSheet, reference.Reference);
            case DataFormulaRangeExpression range: return EvaluateRange(context, currentSheet, range.Range);
            case DataFormulaNameExpression name: return EvaluateName(context, currentSheet, name.Name);
            case DataFormulaErrorExpression error: return DataFormulaValue.Error(error.Code, error.Code == DataFormulaErrorCode.Reference ? "Invalid cell reference." : "Spreadsheet error literal.");
            case DataFormulaUnaryExpression unary: return EvaluateUnary(context, currentSheet, unary);
            case DataFormulaBinaryExpression binary: return EvaluateBinary(context, currentSheet, binary);
            case DataFormulaFunctionExpression function: return EvaluateFunction(context, currentSheet, function);
            default: return DataFormulaValue.Error(DataFormulaErrorCode.Value, "Unsupported formula expression.");
        }
    }

    private static DataFormulaValue EvaluateReference(EvaluationContext context, DataSheet currentSheet, DataFormulaReference reference)
    {
        var sheet = ResolveReferenceSheet(context.Workbook, currentSheet, reference.SheetName);
        if (sheet is null || reference.Row < 0 || reference.Column < 0) return DataFormulaValue.Error(DataFormulaErrorCode.Reference, "Cell reference does not resolve to a workbook cell.");
        return EvaluateCell(context, sheet, reference.Row, reference.Column);
    }

    private static DataFormulaValue EvaluateRange(EvaluationContext context, DataSheet currentSheet, DataFormulaRangeReference range)
    {
        var resolved = ResolveRange(context.Workbook, currentSheet, range);
        if (resolved is null) return DataFormulaValue.Error(DataFormulaErrorCode.Reference, "Range reference does not resolve to one sheet.");
        var rows = resolved.MaxRow - resolved.MinRow + 1; var columns = resolved.MaxColumn - resolved.MinColumn + 1; var count = (long)rows * columns;
        if (count > MaximumEvaluatedRangeCells) return DataFormulaValue.Error(DataFormulaErrorCode.Value, $"Formula ranges are limited to {MaximumEvaluatedRangeCells:N0} cells per evaluation.");
        var values = new List<DataFormulaValue>((int)count);
        for (var row = resolved.MinRow; row <= resolved.MaxRow; row++) for (var column = resolved.MinColumn; column <= resolved.MaxColumn; column++) values.Add(EvaluateCell(context, resolved.Sheet, row, column));
        return DataFormulaValue.Range(values, rows, columns);
    }

    private static DataFormulaValue EvaluateName(EvaluationContext context, DataSheet currentSheet, string name)
    {
        var named = context.Workbook.NamedRanges.FirstOrDefault(value => value.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (named is null) return DataFormulaValue.Error(DataFormulaErrorCode.Name, $"Unknown named range '{name}'.");
        if (!context.EvaluatingNames.Add(named.Name)) return DataFormulaValue.Error(DataFormulaErrorCode.Cycle, $"Named range '{name}' refers to itself.");
        try { return EvaluateExpression(context, currentSheet, DataFormulaParser.Parse(named.RefersTo)); }
        catch (DataFormulaParseException ex) { return DataFormulaValue.Error(DataFormulaErrorCode.Parse, ex.Message); }
        finally { context.EvaluatingNames.Remove(named.Name); }
    }

    private static DataFormulaValue EvaluateUnary(EvaluationContext context, DataSheet sheet, DataFormulaUnaryExpression unary)
    {
        var value = EvaluateExpression(context, sheet, unary.Operand); if (value.IsError) return value;
        if (!DataFormulaConversions.TryNumber(value, out var number)) return DataFormulaValue.Error(DataFormulaErrorCode.Value, "Unary numeric operator requires a number.");
        return unary.Operator switch { DataFormulaUnaryOperator.Positive => DataFormulaValue.Number(number), DataFormulaUnaryOperator.Negative => DataFormulaValue.Number(-number), _ => DataFormulaValue.Number(number / 100d) };
    }

    private static DataFormulaValue EvaluateBinary(EvaluationContext context, DataSheet sheet, DataFormulaBinaryExpression binary)
    {
        var left = EvaluateExpression(context, sheet, binary.Left); if (left.IsError) return left; var right = EvaluateExpression(context, sheet, binary.Right); if (right.IsError) return right;
        if ((left.IsRange && left.Items!.Count != 1) || (right.IsRange && right.Items!.Count != 1)) return DataFormulaValue.Error(DataFormulaErrorCode.Value, "Operators require scalar values, not multi-cell ranges.");
        if (binary.Operator == DataFormulaSegmentOperator.Concat) return DataFormulaValue.Text(DataFormulaConversions.Text(left) + DataFormulaConversions.Text(right));
        if (binary.Operator is DataFormulaSegmentOperator.Equal or DataFormulaSegmentOperator.NotEqual or DataFormulaSegmentOperator.Less or DataFormulaSegmentOperator.LessOrEqual or DataFormulaSegmentOperator.Greater or DataFormulaSegmentOperator.GreaterOrEqual) return Compare(binary.Operator, left, right);
        if (!DataFormulaConversions.TryNumber(left, out var l) || !DataFormulaConversions.TryNumber(right, out var r)) return DataFormulaValue.Error(DataFormulaErrorCode.Value, "Arithmetic operators require numeric values.");
        return binary.Operator switch
        {
            DataFormulaSegmentOperator.Add => DataFormulaValue.Number(l + r), DataFormulaSegmentOperator.Subtract => DataFormulaValue.Number(l - r), DataFormulaSegmentOperator.Multiply => DataFormulaValue.Number(l * r),
            DataFormulaSegmentOperator.Divide => Math.Abs(r) < double.Epsilon ? DataFormulaValue.Error(DataFormulaErrorCode.DivideByZero, "Division by zero.") : DataFormulaValue.Number(l / r),
            DataFormulaSegmentOperator.Power => DataFormulaValue.Number(Math.Pow(l, r)), _ => DataFormulaValue.Error(DataFormulaErrorCode.Value, "Unsupported arithmetic operator.")
        };
    }

    private static DataFormulaValue Compare(DataFormulaSegmentOperator operation, DataFormulaValue left, DataFormulaValue right)
    {
        int comparison;
        if (DataFormulaConversions.TryNumber(left, out var l) && DataFormulaConversions.TryNumber(right, out var r)) comparison = l.CompareTo(r);
        else comparison = StringComparer.OrdinalIgnoreCase.Compare(DataFormulaConversions.Text(left), DataFormulaConversions.Text(right));
        return DataFormulaValue.Boolean(operation switch { DataFormulaSegmentOperator.Equal => comparison == 0, DataFormulaSegmentOperator.NotEqual => comparison != 0, DataFormulaSegmentOperator.Less => comparison < 0, DataFormulaSegmentOperator.LessOrEqual => comparison <= 0, DataFormulaSegmentOperator.Greater => comparison > 0, _ => comparison >= 0 });
    }

    private static DataFormulaValue EvaluateFunction(EvaluationContext context, DataSheet sheet, DataFormulaFunctionExpression function)
    {
        if (function.Name == "IF")
        {
            if (function.Arguments.Count is < 2 or > 3) return DataFormulaValue.Error(DataFormulaErrorCode.Value, "IF expects condition, true value and optional false value.");
            var condition = EvaluateExpression(context, sheet, function.Arguments[0]); if (condition.IsError) return condition; if (!DataFormulaConversions.TryBoolean(condition, out var value)) return DataFormulaValue.Error(DataFormulaErrorCode.Value, "IF condition is not boolean-compatible.");
            return value ? EvaluateExpression(context, sheet, function.Arguments[1]) : function.Arguments.Count == 3 ? EvaluateExpression(context, sheet, function.Arguments[2]) : DataFormulaValue.Boolean(false);
        }
        if (function.Name is "IFERROR" or "IFNA")
        {
            if (function.Arguments.Count != 2) return DataFormulaValue.Error(DataFormulaErrorCode.Value, $"{function.Name} expects two arguments.");
            var first = EvaluateExpression(context, sheet, function.Arguments[0]); var replace = first.IsError && (function.Name == "IFERROR" || first.ErrorCode == DataFormulaErrorCode.NotAvailable); return replace ? EvaluateExpression(context, sheet, function.Arguments[1]) : first;
        }
        var arguments = function.Arguments.Select(argument => EvaluateExpression(context, sheet, argument)).ToArray();
        return DataFormulaFunctions.Evaluate(function.Name, arguments, context.Now);
    }

    private static DataFormulaValue FromStoredCell(DataCell cell)
    {
        if (string.IsNullOrEmpty(cell.Value)) return DataFormulaValue.Empty;
        return cell.Kind switch
        {
            DataCellKind.Number when double.TryParse(cell.Value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number) => DataFormulaValue.Number(number),
            DataCellKind.Boolean when bool.TryParse(cell.Value, out var boolean) => DataFormulaValue.Boolean(boolean),
            DataCellKind.Date when DateTimeOffset.TryParse(cell.Value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out var date) => DataFormulaValue.Date(date),
            _ => DataFormulaValue.Text(cell.Value)
        };
    }

    private static DataSheet? ResolveReferenceSheet(DataWorkbook workbook, DataSheet currentSheet, string? sheetName) => string.IsNullOrWhiteSpace(sheetName) ? currentSheet : workbook.Sheets.FirstOrDefault(value => value.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
    private static ResolvedRange? ResolveRange(DataWorkbook workbook, DataSheet currentSheet, DataFormulaRangeReference range)
    {
        var startSheet = ResolveReferenceSheet(workbook, currentSheet, range.Start.SheetName); var endSheet = ResolveReferenceSheet(workbook, currentSheet, range.End.SheetName ?? range.Start.SheetName); if (startSheet is null || endSheet is null || startSheet.Id != endSheet.Id) return null;
        return new ResolvedRange(startSheet, Math.Min(range.Start.Row, range.End.Row), Math.Max(range.Start.Row, range.End.Row), Math.Min(range.Start.Column, range.End.Column), Math.Max(range.Start.Column, range.End.Column));
    }

    private sealed class EvaluationContext(DataWorkbook workbook, DateTimeOffset now)
    {
        public DataWorkbook Workbook { get; } = workbook; public DateTimeOffset Now { get; } = now; public Dictionary<CellKey, DataFormulaValue> Cache { get; } = []; public HashSet<CellKey> Visiting { get; } = []; public HashSet<string> EvaluatingNames { get; } = new(StringComparer.OrdinalIgnoreCase); public List<DataFormulaIssue> Issues { get; } = []; public int EvaluatedFormulaCells { get; set; }
        public void AddIssue(DataSheet sheet, int row, int column, DataFormulaErrorCode code, string message) { var address = Address(sheet, row, column); if (!Issues.Any(issue => issue.SheetId == sheet.Id && issue.CellAddress == address.A1 && issue.Code == code)) Issues.Add(new(sheet.Id, sheet.Name, address.A1, code, message)); }
    }
    private readonly record struct CellKey(Guid SheetId, int Row, int Column);
    private sealed record ResolvedRange(DataSheet Sheet, int MinRow, int MaxRow, int MinColumn, int MaxColumn);
}
