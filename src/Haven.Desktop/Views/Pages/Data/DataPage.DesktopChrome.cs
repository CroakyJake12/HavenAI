using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Views.Pages.Data;

public sealed partial class DataPage
{
    private void InitializeRecoveredDataUi()
    {
        var toolbar = _route.Root.DescendantsAndSelf().OfType<Container>().FirstOrDefault(element => element.Name == "Data.Grid.Toolbar");
        if (toolbar is not null && !toolbar.Children.Any(child => child.Name == "Data.Format.Bold"))
        {
            toolbar.Add(RecoveredButton("Data.Validation.WholeNumber", "Whole #", ApplyWholeNumberValidation));
            toolbar.Add(RecoveredButton("Data.Validation.Clear", "Clear validation", ClearValidation));
            toolbar.Add(RecoveredButton("Data.Format.Bold", "Bold", () => ApplySelectionFormat(DataCellFormatMetadata.FontWeight, "700", "Applied bold formatting.")));
            toolbar.Add(RecoveredButton("Data.Format.Underline", "Underline", () => ApplySelectionFormat(DataCellFormatMetadata.Underline, "true", "Applied underline formatting.")));
            toolbar.Add(RecoveredButton("Data.Format.Fill", "Fill", () => ApplySelectionFormat(DataCellFormatMetadata.Fill, "AccentSoft", "Applied cell fill.")));
            toolbar.Add(RecoveredButton("Data.Format.Border", "Border", () => ApplySelectionFormat(DataCellFormatMetadata.Border, "Accent", "Applied cell border.")));
            toolbar.Add(RecoveredButton("Data.Format.Center", "Center", () => ApplySelectionFormat(DataCellFormatMetadata.HorizontalAlignment, "center", "Centered selected cells.")));
            toolbar.Add(RecoveredButton("Data.Format.Decimal", "0.00", () => ApplySelectionFormat(DataCellFormatMetadata.NumberFormat, "0.00", "Applied decimal number format.")));
            toolbar.Add(RecoveredButton("Data.Format.Percent", "%", () => ApplySelectionFormat(DataCellFormatMetadata.NumberFormat, "percent", "Applied percentage number format.")));
        }
        InitializeCharting();
    }

    private void SyncRecoveredDataUi()
    {
        if (CurrentSheet is { } sheet && SpreadsheetSurface() is { } surface)
        {
            var table = Workbook?.Tables.FirstOrDefault(value => value.SheetId == sheet.Id && value.Range.Contains(surface.ActiveRow, surface.ActiveColumn))
                ?? Workbook?.Tables.FirstOrDefault(value => value.SheetId == sheet.Id);
            surface.ApplyTableDefinition(table);
        }
        SyncChartUi();
    }

    private DataTableDefinition? CurrentTableDefinition(DataSheet sheet, DataSpreadsheetSurface? surface = null)
    {
        if (Workbook is null) return null;
        surface ??= SpreadsheetSurface();
        return surface is null
            ? Workbook.Tables.FirstOrDefault(value => value.SheetId == sheet.Id)
            : Workbook.Tables.FirstOrDefault(value => value.SheetId == sheet.Id && value.Range.Contains(surface.ActiveRow, surface.ActiveColumn))
              ?? Workbook.Tables.FirstOrDefault(value => value.SheetId == sheet.Id);
    }

    private void ApplyWholeNumberValidation()
    {
        if (Workbook is null || CurrentSheet is not { } sheet || SpreadsheetSurface() is not { } surface) return;
        var selection = surface.Selection; if (!ValidateCommandRange(selection, "apply validation")) return;
        var activity = BeginDataActivity("Data validation", $"Applying whole-number validation to {Address(selection.StartRow, selection.StartColumn)}:{Address(selection.EndRow, selection.EndColumn)}.");
        CaptureSpreadsheetUndo();
        Workbook.Validations.RemoveAll(rule => rule.SheetId == sheet.Id && RangesOverlap(rule.Range, selection));
        Workbook.Validations.Add(new DataValidationRule
        {
            SheetId = sheet.Id, Range = ToCoreRange(selection), Kind = DataValidationKind.WholeNumber, AllowBlank = true,
            ErrorMessage = "Enter a whole number or leave the cell blank."
        });
        MarkDirty(); RenderCurrent(); var result = $"Whole-number validation applied to {Address(selection.StartRow, selection.StartColumn)}:{Address(selection.EndRow, selection.EndColumn)}."; _route.SetStatus(result); CompleteDataActivity(activity, result);
    }

    private void ClearValidation()
    {
        if (Workbook is null || CurrentSheet is not { } sheet || SpreadsheetSurface() is not { } surface) return;
        var selection = surface.Selection; var matches = Workbook.Validations.Where(rule => rule.SheetId == sheet.Id && RangesOverlap(rule.Range, selection)).ToArray(); if (matches.Length == 0) { _route.SetStatus("No validation rules overlap the selected range."); return; }
        CaptureSpreadsheetUndo(); foreach (var rule in matches) Workbook.Validations.Remove(rule); MarkDirty(); RenderCurrent(); _route.SetStatus($"Removed {matches.Length} validation rule(s).");
    }

    private void ApplySelectionFormat(string key, string value, string status)
    {
        if (CurrentSheet is not { } sheet || SpreadsheetSurface() is not { } surface) return; var selection = surface.Selection; if (!ValidateCommandRange(selection, "format this range")) return;
        var activity = BeginDataActivity("Format spreadsheet range", $"Formatting {Address(selection.StartRow, selection.StartColumn)}:{Address(selection.EndRow, selection.EndColumn)}.");
        CaptureSpreadsheetUndo(); DataSpreadsheetOperations.ApplyFormat(sheet, ToCoreRange(selection), new Dictionary<string, string?> { [key] = value }); MarkDirty(); RenderCurrent(); _route.SetStatus(status); CompleteDataActivity(activity, status);
    }

    private DataValidationResult ValidateCellValue(DataSheet sheet, int row, int column, string value) => Workbook is null ? DataValidationResult.Valid : DataSpreadsheetOperations.ValidateValue(Workbook.Validations, sheet.Id, row, column, value);
    private static DataCellRange ToCoreRange(DataSpreadsheetRange range) => new() { StartRow = range.StartRow, StartColumn = range.StartColumn, EndRow = range.EndRow, EndColumn = range.EndColumn };
    private static bool RangesOverlap(DataCellRange left, DataSpreadsheetRange right) => left.StartRow <= right.EndRow && left.EndRow >= right.StartRow && left.StartColumn <= right.EndColumn && left.EndColumn >= right.StartColumn;

    private static HavenButton RecoveredButton(string name, string content, Action action)
    {
        var button = new HavenButton { Name = name, Content = content, Variant = ButtonVariant.Tertiary }; button.Accessibility.AccessibleName = content; button.SetValue(HavenProperties.MinHeight, HavenLength.Px(38)); button.Invoked += (_, _) => action(); return button;
    }

    private static Container RecoveredCard(string name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Vertical }; card.SetValue(HavenProperties.Background, "SurfaceRaised"); card.SetValue(HavenProperties.BorderColor, "Border"); card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14))); card.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px")); card.SetValue(HavenProperties.Gap, HavenLength.Px(8)); return card;
    }

    private static Container RecoveredToolbar(string name) { var toolbar = new Container { Name = name, Layout = HavenLayout.Horizontal }; toolbar.SetValue(HavenProperties.Gap, HavenLength.Px(6)); toolbar.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); return toolbar; }
    private static Input RecoveredInput(string name, string accessibleName, string placeholder) { var input = new Input { Name = name, Placeholder = placeholder }; input.Accessibility.AccessibleName = accessibleName; input.SetValue(HavenProperties.MinHeight, HavenLength.Px(38)); return input; }
}
