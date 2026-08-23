using System.Text.Json;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DataSpreadsheetOperationsTests
{
    [Fact]
    public void Cell_range_parses_A1_notation_and_normalizes_reversed_ranges()
    {
        Assert.True(DataCellRange.TryParse("$C$5:A2", out var range));
        Assert.Equal(1, range.StartRow); Assert.Equal(0, range.StartColumn); Assert.Equal(4, range.EndRow); Assert.Equal(2, range.EndColumn); Assert.Equal("A2:C5", range.ToString());
    }

    [Fact]
    public void Structural_operations_preserve_cell_metadata()
    {
        var sheet = DataSheet.Create(0, "Data"); sheet.SetCell(1, 1, "42", kind: DataCellKind.Number); sheet.GetCell(1, 1)!.Metadata["note"] = "keep";
        DataSpreadsheetOperations.InsertRows(sheet, 1, 2); DataSpreadsheetOperations.InsertColumns(sheet, 1, 1);
        var shifted = sheet.GetCell(3, 2); Assert.NotNull(shifted); Assert.Equal("42", shifted!.Value); Assert.Equal("keep", shifted.Metadata["note"]);
        DataSpreadsheetOperations.DeleteRows(sheet, 0, 1); DataSpreadsheetOperations.DeleteColumns(sheet, 0, 1);
        Assert.Equal("42", sheet.GetCell(2, 1)?.Value);
    }

    [Fact]
    public void Sort_moves_complete_logical_rows_in_stable_order()
    {
        var sheet = DataSheet.Create(0, "Scores");
        sheet.SetCell(0, 0, "Name"); sheet.SetCell(0, 1, "Score"); sheet.SetCell(0, 2, "Formula");
        sheet.SetCell(1, 0, "Ada"); sheet.SetCell(1, 1, "8", kind: DataCellKind.Number); sheet.SetCell(1, 2, "8", "=B2");
        sheet.SetCell(2, 0, "Grace"); sheet.SetCell(2, 1, "10", kind: DataCellKind.Number); sheet.SetCell(2, 2, "10", "=B3");
        DataSpreadsheetOperations.SortRange(sheet, new DataCellRange { StartRow = 0, EndRow = 2, StartColumn = 0, EndColumn = 2 }, 1, descending: true, hasHeader: true);
        Assert.Equal("Grace", sheet.GetCell(1, 0)?.Value); Assert.Equal("10", sheet.GetCell(1, 1)?.Value); Assert.Equal("=B3", sheet.GetCell(1, 2)?.Formula);
        Assert.Equal("Ada", sheet.GetCell(2, 0)?.Value); Assert.Equal("=B2", sheet.GetCell(2, 2)?.Formula);
    }

    [Fact]
    public void Multiple_filters_compose_without_mutating_underlying_rows()
    {
        var sheet = DataSheet.Create(0, "Scores");
        sheet.SetCell(0, 0, "Name"); sheet.SetCell(0, 1, "Score");
        sheet.SetCell(1, 0, "Ada"); sheet.SetCell(1, 1, "8");
        sheet.SetCell(2, 0, "Grace"); sheet.SetCell(2, 1, "10");
        sheet.SetCell(3, 0, "Linus"); sheet.SetCell(3, 1, "7");
        var before = sheet.Cells.Select(cell => (cell.Row, cell.Column, cell.Value)).ToArray();
        var visible = DataSpreadsheetOperations.FilterRows(sheet, new DataCellRange { StartRow = 0, EndRow = 3, StartColumn = 0, EndColumn = 1 }, [new DataTableFilter { Column = 0, Operator = DataFilterOperator.Contains, Value = "a" }, new DataTableFilter { Column = 1, Operator = DataFilterOperator.GreaterThanOrEqual, Value = "9" }]);
        Assert.Equal(new[] { 2 }, visible);
        Assert.Equal(before, sheet.Cells.Select(cell => (cell.Row, cell.Column, cell.Value)).ToArray());
    }

    [Fact]
    public void Validation_returns_semantic_failure_with_rule_identity()
    {
        var sheet = DataSheet.Create(0, "Input");
        var rule = new DataValidationRule { SheetId = sheet.Id, Range = new DataCellRange { StartRow = 1, EndRow = 4, StartColumn = 0, EndColumn = 0 }, Kind = DataValidationKind.WholeNumber, Minimum = "0", Maximum = "10", AllowBlank = false, ErrorMessage = "Enter a whole number from 0 to 10." };
        var valid = DataSpreadsheetOperations.ValidateValue([rule], sheet.Id, 2, 0, "9");
        var invalid = DataSpreadsheetOperations.ValidateValue([rule], sheet.Id, 2, 0, "11");
        Assert.True(valid.IsValid); Assert.False(invalid.IsValid); Assert.Equal(rule.Id, invalid.RuleId); Assert.Equal(rule.ErrorMessage, invalid.Message);
    }

    [Fact]
    public void Formatting_is_canonical_cell_metadata_and_survives_normalize()
    {
        var sheet = DataSheet.Create(0, "Format");
        DataSpreadsheetOperations.ApplyFormat(sheet, new DataCellRange { StartRow = 1, EndRow = 2, StartColumn = 1, EndColumn = 2 }, new Dictionary<string, string?> { [DataCellFormatMetadata.FontWeight] = "700", [DataCellFormatMetadata.Fill] = "AccentSoft", [DataCellFormatMetadata.HorizontalAlignment] = "center" });
        sheet.Normalize(0);
        Assert.Equal("700", sheet.GetCell(1, 1)?.Metadata[DataCellFormatMetadata.FontWeight]); Assert.Equal("AccentSoft", sheet.GetCell(2, 2)?.Metadata[DataCellFormatMetadata.Fill]);
    }

    [Fact]
    public void Workbook_schema_round_trips_tables_validations_and_charts()
    {
        var workbook = DataWorkbook.Create("Semantic"); var sheet = workbook.Sheets[0];
        workbook.Tables.Add(new DataTableDefinition { SheetId = sheet.Id, Name = "Scores", Range = new DataCellRange { StartRow = 0, EndRow = 3, StartColumn = 0, EndColumn = 1 }, Filters = [new DataTableFilter { Column = 1, Operator = DataFilterOperator.GreaterThan, Value = "5" }] });
        workbook.Validations.Add(new DataValidationRule { SheetId = sheet.Id, Range = new DataCellRange { StartRow = 1, EndRow = 3, StartColumn = 1, EndColumn = 1 }, Kind = DataValidationKind.WholeNumber });
        workbook.Charts.Add(new DataChartDefinition { SheetId = sheet.Id, Title = "Scores", SourceRange = new DataCellRange { StartRow = 0, EndRow = 3, StartColumn = 0, EndColumn = 1 }, SeriesColumns = [1] });
        workbook.Normalize(); var json = JsonSerializer.Serialize(workbook); var loaded = JsonSerializer.Deserialize<DataWorkbook>(json)!; loaded.Normalize();
        Assert.Equal(3, loaded.SchemaVersion); Assert.Single(loaded.Tables); Assert.Single(loaded.Validations); Assert.Single(loaded.Charts); Assert.Equal("Scores", loaded.Charts[0].Title); Assert.Equal(DataFilterOperator.GreaterThan, loaded.Tables[0].Filters[0].Operator);
    }

    [Fact]
    public void Large_sparse_sheet_lookup_remains_indexed_after_recovered_operations()
    {
        var sheet = DataSheet.Create(0, "Large");
        for (var row = 0; row < 50_000; row += 5) sheet.SetCell(row, 3, row.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("49995", sheet.GetCell(49_995, 3)?.Value);
        DataSpreadsheetOperations.ApplyFormat(sheet, new DataCellRange { StartRow = 49_995, EndRow = 49_995, StartColumn = 3, EndColumn = 3 }, new Dictionary<string, string?> { [DataCellFormatMetadata.FontWeight] = "700" });
        Assert.Equal("700", sheet.GetCell(49_995, 3)?.Metadata[DataCellFormatMetadata.FontWeight]);
    }
}
