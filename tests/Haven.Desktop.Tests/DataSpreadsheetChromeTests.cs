using Avalonia.Headless.XUnit;
using Haven.Core;
using Haven.Desktop.Views.Pages.Data;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class DataSpreadsheetChromeTests
{
    [AvaloniaFact]
    public void Spreadsheet_chrome_places_name_formula_and_sheet_tabs_around_the_retained_grid()
    {
        var workbook = DataWorkbook.Create("Chrome"); workbook.Sheets.Add(DataSheet.Create(1, "Second")); using var scene = new DataHavenScene(); var selectedSheet = 0; scene.CellSelected += (row, column) => scene.SetSelectedCell(workbook.Sheets[selectedSheet].GetCell(row, column), row, column); var chrome = new DataSpreadsheetChromeController(scene, () => workbook, () => selectedSheet, index => selectedSheet = index); scene.SetWorkbook(workbook, 0, 1, 0, 0, 0, 0, 0, 0, null);
        Assert.DoesNotContain(scene.Root.DescendantsAndSelf(), element => element.Name == "Data.Cell.Editor"); var children = scene.Editor.Children.ToArray(); var formulaIndex = Array.IndexOf(children, chrome.FormulaBar); var gridIndex = Array.IndexOf(children, scene.GridHost); var tabsIndex = Array.IndexOf(children, chrome.SheetTabs); Assert.True(formulaIndex >= 0 && formulaIndex < gridIndex && gridIndex < tabsIndex); Assert.Equal(2, chrome.SheetTabs.Children.Count);
        chrome.NameBox.Text = "C12"; var grid = Assert.Single(scene.GridHost.Children.OfType<DataSpreadsheetSurface>()); Assert.Equal(11, grid.ActiveRow); Assert.Equal(2, grid.ActiveColumn); Assert.Equal("C12", chrome.NameBox.Text);
        grid.SelectCell(2, 1); Assert.Equal("B3", chrome.NameBox.Text); chrome.SelectSheet(1); Assert.Equal(1, selectedSheet); scene.Root.ValidateUniqueNames();
    }

    [AvaloniaFact]
    public void Spreadsheet_layout_changes_persist_to_sheet_metadata_and_reload_with_freeze_controls()
    {
        var workbook = DataWorkbook.Create("Layout"); var dirty = false;
        using (var scene = new DataHavenScene())
        {
            _ = new DataSpreadsheetChromeController(scene, () => workbook, () => 0, _ => { }, () => dirty = true); scene.SetWorkbook(workbook, 0, 1, 0, 0, 0, 0, 0, 0, null);
            var grid = Assert.Single(scene.GridHost.Children.OfType<DataSpreadsheetSurface>()); grid.SetFrozenPanes(2, 1); grid.SetColumnWidth(0, 210);
            Assert.True(dirty); var state = DataSpreadsheetLayoutMetadata.Read(workbook.Sheets[0].Metadata); Assert.Equal(2, state.FrozenRows); Assert.Equal(1, state.FrozenColumns); Assert.Equal(210, state.ColumnWidths[0], 3);
            Assert.Contains(scene.Root.DescendantsAndSelf(), element => element.Name == "Data.Grid.Freeze"); Assert.Contains(scene.Root.DescendantsAndSelf(), element => element.Name == "Data.Grid.Unfreeze");
        }
        using var reloadedScene = new DataHavenScene(); _ = new DataSpreadsheetChromeController(reloadedScene, () => workbook, () => 0, _ => { }); reloadedScene.SetWorkbook(workbook, 0, 1, 0, 0, 0, 0, 0, 0, null);
        var reloaded = Assert.Single(reloadedScene.GridHost.Children.OfType<DataSpreadsheetSurface>()); Assert.Equal(2, reloaded.FrozenRows); Assert.Equal(1, reloaded.FrozenColumns); Assert.Equal(210, reloaded.ColumnWidthAt(0), 3);
    }
}
