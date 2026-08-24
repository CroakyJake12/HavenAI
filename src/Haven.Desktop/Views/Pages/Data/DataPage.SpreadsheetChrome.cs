using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Views.Pages.Data;

public sealed partial class DataPage
{
    private DataSpreadsheetChromeController? _spreadsheetChrome;
    internal DataSpreadsheetChromeController SpreadsheetChrome => _spreadsheetChrome ??= new(_route, () => Workbook, () => _sheetIndex, OnSheetSelected, MarkDirty);
}

internal sealed class DataSpreadsheetChromeController
{
    private readonly DataHavenScene _route;
    private readonly Func<DataWorkbook?> _workbook;
    private readonly Func<int> _selectedSheet;
    private readonly Action<int> _selectSheet;
    private readonly Action _markDirty;
    private DataSpreadsheetSurface? _boundSurface;
    private readonly Input _nameBox = new() { Name = "Data.Grid.NameBox", Placeholder = "A1" };
    private readonly Container _formulaBar = new() { Name = "Data.Grid.FormulaBar", Layout = HavenLayout.Vertical };
    private readonly Container _sheetTabs = new() { Name = "Data.Sheet.Tabs", Layout = HavenLayout.Horizontal };
    private bool _suppressNameBox;

    public DataSpreadsheetChromeController(DataHavenScene route, Func<DataWorkbook?> workbook, Func<int> selectedSheet, Action<int> selectSheet, Action? markDirty = null)
    {
        _route = route; _workbook = workbook; _selectedSheet = selectedSheet; _selectSheet = selectSheet; _markDirty = markDirty ?? (() => { }); ConfigureChrome();
        _nameBox.Invalidated += (_, _) => NavigateFromNameBox(); _route.SelectedCellText.Invalidated += (_, _) => SyncNameBox(); _route.GridHost.Invalidated += (_, _) => { EnsureSurfaceBindings(); SyncNameBox(); }; _route.Explorer.Invalidated += (_, _) => RefreshSheetTabs(); _route.SheetNameInput.Invalidated += (_, _) => RefreshSheetTabs();
        EnsureSurfaceBindings(); SyncNameBox(); RefreshSheetTabs();
    }

    internal Input NameBox => _nameBox; internal Container FormulaBar => _formulaBar; internal Container SheetTabs => _sheetTabs;
    internal void SelectSheet(int index) { _selectSheet(index); RefreshSheetTabs(); }

    private void ConfigureChrome()
    {
        _nameBox.Accessibility.AccessibleName = "Name box"; _nameBox.SetValue(HavenProperties.Width, HavenLength.Px(100)); _nameBox.SetValue(HavenProperties.MinWidth, HavenLength.Px(90));
        _formulaBar.SetValue(HavenProperties.Background, "SurfaceRaised"); _formulaBar.SetValue(HavenProperties.BorderColor, "Border"); _formulaBar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); _formulaBar.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12))); _formulaBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px")); _formulaBar.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        foreach (var element in new HavenElement[] { _route.SelectedCellText, _route.CellValueInput, _route.CellFormulaInput, _route.FormulaStatusText }) if (element.Parent is not null) element.Parent.Remove(element);
        _formulaBar.Add(_route.SelectedCellText);
        var inputs = new Container { Name = "Data.Grid.FormulaInputs", Layout = HavenLayout.Grid, Columns = "100px 1fr 2fr", Rows = "Auto" }; inputs.SetValue(HavenProperties.Gap, HavenLength.Px(8)); _nameBox.SetValue(HavenProperties.Column, 0); _route.CellValueInput.SetValue(HavenProperties.Column, 1); _route.CellFormulaInput.SetValue(HavenProperties.Column, 2); inputs.Add(_nameBox); inputs.Add(_route.CellValueInput); inputs.Add(_route.CellFormulaInput); _formulaBar.Add(inputs); _formulaBar.Add(_route.FormulaStatusText);
        _sheetTabs.SetValue(HavenProperties.Gap, HavenLength.Px(6)); _sheetTabs.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); _sheetTabs.SetValue(HavenProperties.MinHeight, HavenLength.Px(42));

        var oldCellEditor = _route.Editor.Children.FirstOrDefault(child => child.Name == "Data.Cell.Editor"); var current = _route.Editor.Children.ToArray(); foreach (var child in current) _route.Editor.Remove(child);
        foreach (var child in current)
        {
            if (ReferenceEquals(child, oldCellEditor)) continue;
            if (ReferenceEquals(child, _route.GridHost)) { _route.Editor.Add(_formulaBar); _route.Editor.Add(child); _route.Editor.Add(_sheetTabs); } else _route.Editor.Add(child);
        }
    }

    private void NavigateFromNameBox()
    {
        if (_suppressNameBox || !TryParseAddress(_nameBox.Text, out var row, out var column)) return; var surface = MainSurface(); if (surface is null || surface.ActiveRow == row && surface.ActiveColumn == column) return; surface.SelectCell(row, column);
    }

    private void SyncNameBox()
    {
        var address = MainSurface()?.ActiveAddress; if (string.IsNullOrWhiteSpace(address) || string.Equals(_nameBox.Text, address, StringComparison.OrdinalIgnoreCase)) return; _suppressNameBox = true; try { _nameBox.Text = address; } finally { _suppressNameBox = false; }
    }

    private void RefreshSheetTabs()
    {
        _sheetTabs.Children.ToList().ForEach(child => _sheetTabs.Remove(child)); var workbook = _workbook(); if (workbook is null) return; var selected = Math.Clamp(_selectedSheet(), 0, Math.Max(0, workbook.Sheets.Count - 1));
        for (var index = 0; index < workbook.Sheets.Count; index++) { var captured = index; var sheet = workbook.Sheets[index]; var button = new HavenButton { Name = $"Data.Sheet.Tab.{sheet.Id:N}", Content = sheet.Name, Variant = index == selected ? ButtonVariant.Primary : ButtonVariant.Tertiary }; button.Accessibility.AccessibleName = $"Sheet {sheet.Name}"; button.SetValue(HavenProperties.MinHeight, HavenLength.Px(36)); button.Invoked += (_, _) => SelectSheet(captured); _sheetTabs.Add(button); }
    }

    private void EnsureSurfaceBindings()
    {
        var surface = MainSurface(); if (surface is null) return;
        if (!ReferenceEquals(_boundSurface, surface))
        {
            if (_boundSurface is not null) _boundSurface.LayoutChanged -= OnLayoutChanged;
            _boundSurface = surface; _boundSurface.LayoutChanged += OnLayoutChanged; EnsureGridTools();
        }
        var workbook = _workbook(); if (workbook is null || workbook.Sheets.Count == 0) return;
        var selected = Math.Clamp(_selectedSheet(), 0, workbook.Sheets.Count - 1);
        surface.ApplyLayoutState(DataSpreadsheetLayoutMetadata.Read(workbook.Sheets[selected].Metadata));
    }

    private void OnLayoutChanged(DataSpreadsheetLayoutState state)
    {
        var workbook = _workbook(); if (workbook is null || workbook.Sheets.Count == 0) return;
        var selected = Math.Clamp(_selectedSheet(), 0, workbook.Sheets.Count - 1);
        DataSpreadsheetLayoutMetadata.Write(workbook.Sheets[selected].Metadata, state); _markDirty();
    }

    private void EnsureGridTools()
    {
        var toolbar = _route.Root.DescendantsAndSelf().OfType<Container>().FirstOrDefault(element => element.Name == "Data.Grid.Toolbar");
        if (toolbar is null || toolbar.Children.Any(child => child.Name == "Data.Grid.Freeze")) return;
        var freeze = new HavenButton { Name = "Data.Grid.Freeze", Content = "Freeze panes", Variant = ButtonVariant.Tertiary }; freeze.Accessibility.AccessibleName = "Freeze panes above and left of the selected cell"; freeze.SetValue(HavenProperties.MinHeight, HavenLength.Px(38));
        freeze.Invoked += (_, _) => { var surface = MainSurface(); if (surface is null) return; var rows = surface.ActiveRow; var columns = surface.ActiveColumn; if (rows == 0 && columns == 0) rows = 1; surface.SetFrozenPanes(rows, columns); };
        var unfreeze = new HavenButton { Name = "Data.Grid.Unfreeze", Content = "Unfreeze", Variant = ButtonVariant.Tertiary }; unfreeze.Accessibility.AccessibleName = "Unfreeze spreadsheet panes"; unfreeze.SetValue(HavenProperties.MinHeight, HavenLength.Px(38)); unfreeze.Invoked += (_, _) => MainSurface()?.SetFrozenPanes(0, 0);
        toolbar.Add(freeze); toolbar.Add(unfreeze);
    }

    private DataSpreadsheetSurface? MainSurface() => _route.GridHost.Children.OfType<DataSpreadsheetSurface>().FirstOrDefault();

    private static bool TryParseAddress(string? text, out int row, out int column)
    {
        row = column = 0; var value = (text ?? string.Empty).Trim().Replace("$", string.Empty, StringComparison.Ordinal); if (value.Length < 2) return false; var split = 0; while (split < value.Length && char.IsLetter(value[split])) split++; if (split == 0 || split == value.Length || !int.TryParse(value[split..], out var oneBasedRow) || oneBasedRow < 1 || oneBasedRow > DataSpreadsheetSurface.MaximumRows) return false;
        long oneBasedColumn = 0; for (var index = 0; index < split; index++) { var letter = char.ToUpperInvariant(value[index]); if (letter is < 'A' or > 'Z') return false; oneBasedColumn = oneBasedColumn * 26 + letter - 'A' + 1; if (oneBasedColumn > DataSpreadsheetSurface.MaximumColumns) return false; } row = oneBasedRow - 1; column = (int)oneBasedColumn - 1; return column >= 0;
    }
}
