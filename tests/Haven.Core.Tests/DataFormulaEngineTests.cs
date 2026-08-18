using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DataFormulaEngineTests
{
    [Fact]
    public void Recalculation_resolves_operator_precedence_ranges_and_incremental_dependencies()
    {
        var workbook = DataWorkbook.Create("Formula workbook"); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "10", kind: DataCellKind.Number); sheet.SetCell(1, 0, "5", kind: DataCellKind.Number);
        sheet.SetCell(0, 1, string.Empty, "=A1+A2*2"); sheet.SetCell(1, 1, string.Empty, "=SUM(A1:A2)");
        sheet.SetCell(2, 1, string.Empty, "=IF(B1>15,\"yes\",\"no\")"); sheet.SetCell(0, 2, string.Empty, "=AVERAGE(A1:A2)");
        var engine = new DataFormulaEngine(new FixedTimeProvider());

        var initial = engine.Recalculate(workbook);

        Assert.Equal(4, initial.FormulaCells); Assert.Empty(initial.Issues);
        Assert.Equal("20", sheet.GetCell(0, 1)!.Value); Assert.Equal("15", sheet.GetCell(1, 1)!.Value); Assert.Equal("yes", sheet.GetCell(2, 1)!.Value); Assert.Equal("7.5", sheet.GetCell(0, 2)!.Value);
        var graph = engine.BuildDependencyGraph(workbook); var b1 = graph.Dependencies.Single(pair => pair.Key.A1 == "B1").Value; Assert.Contains(b1, address => address.A1 == "A1"); Assert.Contains(b1, address => address.A1 == "A2");

        sheet.SetCell(0, 0, "20", kind: DataCellKind.Number);
        var incremental = engine.Recalculate(workbook, [DataFormulaEngine.Address(sheet, 0, 0)]);

        Assert.True(incremental.EvaluatedCells >= 4); Assert.Equal("30", sheet.GetCell(0, 1)!.Value); Assert.Equal("25", sheet.GetCell(1, 1)!.Value); Assert.Equal("12.5", sheet.GetCell(0, 2)!.Value); Assert.Equal("yes", sheet.GetCell(2, 1)!.Value);
    }

    [Fact]
    public void Sheet_references_and_persistent_named_ranges_calculate()
    {
        var workbook = DataWorkbook.Create(); workbook.Sheets[0].Name = "Summary"; var rates = DataSheet.Create(1, "Rates 2026"); workbook.Sheets.Add(rates); rates.SetCell(0, 0, "0.2", kind: DataCellKind.Number);
        workbook.NamedRanges.Add(new DataNamedRange { Name = "TaxRate", RefersTo = "='Rates 2026'!$A$1" });
        workbook.Sheets[0].SetCell(0, 0, string.Empty, "=100*TaxRate"); workbook.Sheets[0].SetCell(0, 1, string.Empty, "='Rates 2026'!A1+1");

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Empty(report.Issues); Assert.Equal("20", workbook.Sheets[0].GetCell(0, 0)!.Value); Assert.Equal("1.2", workbook.Sheets[0].GetCell(0, 1)!.Value); Assert.Equal(2, workbook.SchemaVersion);
    }

    [Fact]
    public void Circular_references_and_formula_errors_are_visible_and_non_throwing()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, string.Empty, "=B1+1"); sheet.SetCell(0, 1, string.Empty, "=A1+1");
        sheet.SetCell(1, 0, string.Empty, "=1/0"); sheet.SetCell(1, 1, string.Empty, "=DOES_NOT_EXIST(1)"); sheet.SetCell(1, 2, string.Empty, "=SUM(");

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Equal("#CYCLE!", sheet.GetCell(0, 0)!.Value); Assert.Equal("#CYCLE!", sheet.GetCell(0, 1)!.Value);
        Assert.Equal("#DIV/0!", sheet.GetCell(1, 0)!.Value); Assert.Equal("#NAME?", sheet.GetCell(1, 1)!.Value); Assert.Equal("#ERROR!", sheet.GetCell(1, 2)!.Value);
        Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Cycle); Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.DivideByZero); Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Name); Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Parse);
    }

    [Fact]
    public void Core_text_date_lookup_and_statistical_functions_use_ranges()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Alice"); sheet.SetCell(1, 0, "Bob"); sheet.SetCell(0, 1, "10", kind: DataCellKind.Number); sheet.SetCell(1, 1, "20", kind: DataCellKind.Number);
        sheet.SetCell(0, 2, string.Empty, "=XLOOKUP(\"Bob\",A1:A2,B1:B2)"); sheet.SetCell(1, 2, string.Empty, "=INDEX(B1:B2,2)"); sheet.SetCell(2, 2, string.Empty, "=MATCH(\"Alice\",A1:A2,0)");
        sheet.SetCell(3, 2, string.Empty, "=CONCAT(UPPER(A1),\"-\",ROUND(1.234,2))"); sheet.SetCell(4, 2, string.Empty, "=DATE(2026,8,17)"); sheet.SetCell(5, 2, string.Empty, "=MEDIAN(B1:B2)");
        var engine = new DataFormulaEngine(new FixedTimeProvider());

        var report = engine.Recalculate(workbook);

        Assert.Empty(report.Issues); Assert.Equal("20", sheet.GetCell(0, 2)!.Value); Assert.Equal("20", sheet.GetCell(1, 2)!.Value); Assert.Equal("1", sheet.GetCell(2, 2)!.Value); Assert.Equal("ALICE-1.23", sheet.GetCell(3, 2)!.Value); Assert.Equal("2026-08-17", sheet.GetCell(4, 2)!.Value); Assert.Equal("15", sheet.GetCell(5, 2)!.Value);
    }

    [Fact]
    public void Copy_and_fill_respect_relative_absolute_and_mixed_A1_references()
    {
        Assert.Equal("=B2+$A2+B$1+$A$1", DataFormulaReferenceUtility.TranslateFormula("=A1+$A1+A$1+$A$1", 1, 1));
        Assert.Equal("=#REF!+$A$1", DataFormulaReferenceUtility.TranslateFormula("=A1+$A$1", 0, -1));
        Assert.Equal("=\"A1 is text\"&B2", DataFormulaReferenceUtility.TranslateFormula("=\"A1 is text\"&A1", 1, 1));

        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0]; sheet.SetCell(0, 0, "3", kind: DataCellKind.Number); sheet.SetCell(1, 1, string.Empty, "=A1*2");
        var engine = new DataFormulaEngine(new FixedTimeProvider()); Assert.True(engine.CopyFormula(workbook, sheet.Id, 1, 1, 2, 2));
        Assert.Equal("=B2*2", sheet.GetCell(2, 2)!.Formula); Assert.Equal("12", sheet.GetCell(2, 2)!.Value);
        sheet.SetCell(1, 1, "4", kind: DataCellKind.Number); _ = engine.Recalculate(workbook, [DataFormulaEngine.Address(sheet, 1, 1)]); Assert.Equal("8", sheet.GetCell(2, 2)!.Value);
    }

    [Fact]
    public void Sheet_reference_rename_rewrites_quoted_and_plain_references_without_touching_strings()
    {
        Assert.Equal("='Tax ''26'!A1+'Tax ''26'!$B$2+\"Rates 2026!A1\"", DataFormulaReferenceUtility.RenameSheetReferences("='Rates 2026'!A1+Rates 2026!$B$2+\"Rates 2026!A1\"", "Rates 2026", "Tax '26"));
        Assert.Equal("='Renamed'!A1", DataFormulaReferenceUtility.RenameSheetReferences("=Sheet1!A1", "Sheet1", "Renamed"));
    }

    [Fact]
    public void Imported_unsupported_formula_preserves_cached_result_and_reports_fallback()
    {
        var workbook = DataWorkbook.Create(); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 3, "42", "=UNSUPPORTED_EXCEL_FN(1)", DataCellKind.Formula);
        var cell = sheet.GetCell(0, 3)!; cell.Metadata["xlsxCachedValue"] = "42";

        var report = new DataFormulaEngine(new FixedTimeProvider()).Recalculate(workbook);

        Assert.Equal("42", cell.Value); Assert.Equal("xlsx", cell.Metadata["formulaCachedFallback"]);
        Assert.Contains("Unknown function", cell.Metadata["formulaError"], StringComparison.Ordinal);
        Assert.Contains(report.Issues, issue => issue.Code == DataFormulaErrorCode.Name && issue.CellAddress == "D1");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 17, 12, 30, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
