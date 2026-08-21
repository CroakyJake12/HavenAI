using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Data;

public sealed partial class DataPage
{
    private readonly DataFormulaEngine _formulaEngine = new();
    private DataFormulaRecalculationReport _formulaReport = new(0, 0, 0, []);

    internal DataFormulaRecalculationReport FormulaReport => _formulaReport;

    private void RecalculateWorkbook()
    {
        if (Workbook is null) { _formulaReport = new(0, 0, 0, []); return; }
        _formulaReport = _formulaEngine.Recalculate(Workbook);
    }

    private void RecalculateFrom(DataSheet sheet, int row, int column)
    {
        if (Workbook is null) return;
        _formulaReport = _formulaEngine.Recalculate(Workbook, [DataFormulaEngine.Address(sheet, row, column)]);
    }

    private void RewriteSheetReferences(string oldName, string newName)
    {
        if (Workbook is null) return;
        foreach (var cell in Workbook.Sheets.SelectMany(sheet => sheet.Cells).Where(cell => !string.IsNullOrWhiteSpace(cell.Formula))) cell.Formula = DataFormulaReferenceUtility.RenameSheetReferences(cell.Formula, oldName, newName);
        foreach (var range in Workbook.NamedRanges) range.RefersTo = DataFormulaReferenceUtility.RenameSheetReferences(range.RefersTo, oldName, newName);
    }

    private void RenderFormulaState()
    {
        var cell = CurrentSheet?.GetCell(_selectedRow, _selectedColumn);
        _route.SetFormulaState(_formulaReport, cell);
    }
}
