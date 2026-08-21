using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Data;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Tests;

public sealed class DataPageTests
{
    [AvaloniaFact]
    public void Data_scene_uses_haven_grid_explorer_visual_sql_and_accessible_inputs()
    {
        using var scene = new DataHavenScene();
        var workbook = DataWorkbook.Create("Coursework data");
        workbook.Sheets[0].Name = "People";
        workbook.Sheets[0].SetCell(0, 0, "Ada");
        workbook.Sheets[0].SetCell(0, 1, "42", kind: DataCellKind.Number);
        workbook.Queries[0].Visual.Source = "People";
        workbook.Queries[0].Sql = "SELECT A, B FROM \"People\";";
        workbook.Schema.Tables.Add(new DataSchemaTable
        {
            Name = "ExternalResults",
            Columns = [new DataSchemaColumn { Name = "Score", DataType = "NUMBER" }]
        });

        scene.SetWorkbook(workbook, 0, 1, 0, 0, 0, 0, 0, 0, null);

        Assert.Equal("Coursework data", scene.WorkbookTitleInput.Text);
        Assert.Equal("People", scene.SheetNameInput.Text);
        Assert.Equal("Ada", scene.CellValueInput.Text);
        Assert.Equal(HavenAccessibleRole.Input, scene.WorkbookTitleInput.Accessibility.Role);
        Assert.Equal(HavenAccessibleRole.Input, scene.SqlInput.Accessibility.Role);
        Assert.Contains(scene.Root.DescendantsAndSelf(), element => element.Name?.StartsWith("Data.Cell.A1", StringComparison.Ordinal) == true);
        Assert.Contains(scene.Root.DescendantsAndSelf(), element => element.Name?.StartsWith("Data.Explorer.Sheet.", StringComparison.Ordinal) == true);
        Assert.Contains("Sheets are exposed as SQL tables", scene.ResultsText.Content, StringComparison.Ordinal);
        Assert.True(scene.RunQueryButton.GetValue(HavenProperties.Enabled));

        scene.SetQuerySafety("DELETE FROM \"People\"");
        Assert.False(scene.RunQueryButton.GetValue(HavenProperties.Enabled));
        Assert.Contains("Destructive", scene.SqlSafetyText.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(scene.Root.DescendantsAndSelf(), element => element is Video or Web);
        scene.Root.ValidateUniqueNames();
    }

    [AvaloniaFact]
    public async Task Data_page_edits_cells_builds_and_runs_visual_query_saves_and_exports_state()
    {
        var workbook = DataWorkbook.Create("Initial workbook");
        workbook.Sheets[0].SetCell(0, 0, "Ada");
        var repository = new FakeDataRepository(workbook);
        var formats = new FakeDataFormats();
        var queries = new FakeDataQueries(new DataQueryResult(["A"], [["Ada"]], false, "Focused test"));
        using var page = new DataPage(new HavenEventBus(), repository, formats, queries);
        await page.InitializeAsync();
        var window = new Window { Width = 1800, Height = 2400, Content = page };
        try
        {
            window.Show(); window.UpdateLayout();
            var router = new HavenInputRouter(page.SceneRoot);
            Assert.Same(page.SceneRoot, page.SceneHost.Root);
            Assert.Single(page.SceneHost.Children);

            page.Route.WorkbookTitleInput.Text = "Results workbook";
            page.Route.SheetNameInput.Text = "People";
            page.Route.CellValueInput.Text = "Grace";
            page.Route.QueryNameInput.Text = "Selected people";
            page.Route.VisualSourceInput.Text = "People";
            page.Route.VisualColumnsInput.Text = "A";
            page.Route.VisualFilterInput.Text = "A IS NOT NULL";
            page.Route.VisualLimitInput.Text = "20";
            Assert.True(page.IsDirty);
            Assert.Equal("Grace", page.Workbook!.Sheets[0].GetCell(0, 0)?.Value);

            window.UpdateLayout();
            Assert.True(page.Route.Editor.MaxScrollY > 0);
            page.Route.Editor.ScrollY = page.Route.Editor.MaxScrollY;
            window.UpdateLayout();
            Click(router, page.Route.BuildSqlButton);
            Assert.Equal("SELECT A FROM \"People\" WHERE A IS NOT NULL LIMIT 20;", page.Workbook.Queries[0].Sql);
            window.UpdateLayout();
            Click(router, page.Route.RunQueryButton);
            await WaitUntilAsync(() => queries.Calls == 1);
            Assert.Contains("Ada", page.Route.ResultsText.Content, StringComparison.Ordinal);
            Assert.Equal(page.Workbook.Queries[0].Sql, queries.LastSql);

            Assert.True(await page.SaveAsync("Focused test"));
            Assert.False(page.IsDirty);
            Assert.Equal(1, repository.SaveCalls);
            Assert.Equal("Results workbook", repository.LastSaved?.Title);
            Assert.Equal("People", repository.LastSaved?.Sheets[0].Name);

            var destination = Path.Combine(Path.GetTempPath(), "data-focused.xlsx");
            Assert.True(await page.ExportToPathAsync(destination));
            Assert.Equal(destination, formats.LastExportPath);
            Assert.Same(page.Workbook, formats.LastExportedWorkbook);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Data_page_recalculates_formulas_renders_results_and_updates_dependents_from_cell_edits()
    {
        var workbook = DataWorkbook.Create("Formula page");
        workbook.Sheets[0].SetCell(0, 0, "10", kind: DataCellKind.Number);
        workbook.Sheets[0].SetCell(0, 1, "0", "=A1*2", DataCellKind.Formula);
        var repository = new FakeDataRepository(workbook);
        using var page = new DataPage(new HavenEventBus(), repository, new FakeDataFormats(), new FakeDataQueries());
        await page.InitializeAsync();
        var window = new Window { Width = 1800, Height = 2400, Content = page };
        try
        {
            window.Show(); window.UpdateLayout();
            Assert.Equal("20", page.Workbook!.Sheets[0].GetCell(0, 1)!.Value);
            Assert.Equal(1, page.FormulaReport.FormulaCells);
            var spreadsheet = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>());
            spreadsheet.SelectCell(0, 1); window.UpdateLayout();
            Assert.Equal("=A1*2", page.Route.CellFormulaInput.Text); Assert.Equal("20", page.Route.CellValueInput.Text); Assert.False(page.Route.CellValueInput.GetValue(HavenProperties.Enabled)); Assert.Contains("Calculated locally", page.Route.FormulaStatusText.Content, StringComparison.Ordinal);
            spreadsheet = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); spreadsheet.SelectCell(0, 0); window.UpdateLayout(); page.Route.CellValueInput.Text = "7";
            Assert.Equal("14", page.Workbook.Sheets[0].GetCell(0, 1)!.Value); Assert.True(page.IsDirty); Assert.True(await page.SaveAsync("Formula interaction test")); Assert.Equal("14", repository.LastSaved!.Sheets[0].GetCell(0, 1)!.Value);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Spreadsheet_selection_updates_cell_chrome_without_rebuilding_or_normalizing_the_workbook()
    {
        var workbook = DataWorkbook.Create("Selection hot path");
        using var page = new DataPage(new HavenEventBus(), new FakeDataRepository(workbook), new FakeDataFormats(), new FakeDataQueries());
        await page.InitializeAsync();
        var window = new Window { Width = 1400, Height = 1000, Content = page };
        try
        {
            window.Show(); window.UpdateLayout();
            var sheet = page.Workbook!.Sheets[0];
            var late = new DataCell { Row = 50, Column = 0, Value = "late" };
            var early = new DataCell { Row = 3, Column = 0, Value = "early" };
            sheet.Cells.Clear(); sheet.Cells.Add(late); sheet.Cells.Add(early);
            var spreadsheet = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>());

            spreadsheet.SelectCell(3, 0);

            Assert.Same(spreadsheet, Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()));
            Assert.Same(late, sheet.Cells[0]); Assert.Same(early, sheet.Cells[1]);
            Assert.Equal("early", page.Route.CellValueInput.Text);
            Assert.Equal("Selected cell · A4", page.Route.SelectedCellText.Content);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Data_page_preserves_formula_references_on_sheet_rename_and_surfaces_ref_after_delete()
    {
        var workbook = DataWorkbook.Create("Structural formulas"); workbook.Sheets[0].Name = "Rates 2026"; workbook.Sheets[0].SetCell(0, 0, "2", kind: DataCellKind.Number);
        var summary = DataSheet.Create(1, "Summary"); summary.SetCell(0, 0, "0", "='Rates 2026'!A1*3", DataCellKind.Formula); workbook.Sheets.Add(summary);
        workbook.NamedRanges.Add(new DataNamedRange { Name = "Rate", RefersTo = "='Rates 2026'!$A$1" }); workbook.Queries[0].Visual.Source = "Rates 2026"; workbook.Queries[0].Sql = workbook.Queries[0].Visual.BuildSql();
        using var page = new DataPage(new HavenEventBus(), new FakeDataRepository(workbook), new FakeDataFormats(), new FakeDataQueries()); await page.InitializeAsync();
        var window = new Window { Width = 1800, Height = 2400, Content = page };
        try
        {
            window.Show(); window.UpdateLayout(); var router = new HavenInputRouter(page.SceneRoot);
            Assert.Equal("6", summary.GetCell(0, 0)!.Value); page.Route.SheetNameInput.Text = "Tax Rates";
            Assert.Equal("='Tax Rates'!A1*3", summary.GetCell(0, 0)!.Formula); Assert.Equal("'Tax Rates'!$A$1", workbook.NamedRanges[0].RefersTo); Assert.Equal("6", summary.GetCell(0, 0)!.Value); Assert.Equal("Tax Rates", workbook.Queries[0].Visual.Source); Assert.Contains("Tax Rates", workbook.Queries[0].Sql, StringComparison.Ordinal);
            window.UpdateLayout(); Click(router, page.Route.DeleteSheetButton);
            Assert.Single(workbook.Sheets); Assert.Equal("Summary", workbook.Sheets[0].Name); Assert.Equal("#REF!", workbook.Sheets[0].GetCell(0, 0)!.Value); Assert.Contains(page.FormulaReport.Issues, issue => issue.Code == DataFormulaErrorCode.Reference);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Data_page_creates_first_workbook_and_saves_dirty_state_on_detach()
    {
        var repository = new FakeDataRepository();
        using var page = new DataPage(new HavenEventBus(), repository, new FakeDataFormats(), new FakeDataQueries());
        await page.InitializeAsync();
        Assert.NotNull(page.Workbook);
        Assert.Equal(1, repository.SaveCalls);
        var window = new Window { Width = 1200, Height = 900, Content = page };
        try
        {
            window.Show(); window.UpdateLayout();
            page.Route.WorkbookTitleInput.Text = "Persist before leaving";
            page.Route.CellValueInput.Text = "Saved value";
            Assert.True(page.IsDirty);
            window.Content = null;
            await WaitUntilAsync(() => !page.IsDirty);
            Assert.Equal(2, repository.SaveCalls);
            Assert.Equal("Persist before leaving", repository.LastSaved?.Title);
            Assert.Equal("Saved value", repository.LastSaved?.Sheets[0].GetCell(0, 0)?.Value);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Data_page_keeps_dirty_state_when_save_fails_and_refuses_destructive_preview()
    {
        var workbook = DataWorkbook.Create("Failure test");
        var repository = new FakeDataRepository(workbook) { FailSaves = true };
        var queries = new FakeDataQueries();
        using var page = new DataPage(new HavenEventBus(), repository, new FakeDataFormats(), queries);
        await page.InitializeAsync();
        page.Route.WorkbookTitleInput.Text = "Unsaved edit";
        page.Route.SqlInput.Text = "DELETE FROM \"Sheet 1\"";

        var saved = await page.SaveAsync("Expected failure");

        Assert.False(saved);
        Assert.True(page.IsDirty);
        Assert.Equal("Unsaved edit", page.Workbook?.Title);
        Assert.Contains("Couldn’t save", page.Route.StatusText.Content, StringComparison.Ordinal);
        Assert.False(page.Route.RunQueryButton.GetValue(HavenProperties.Enabled));
        Assert.Equal(0, queries.Calls);
    }

    [AvaloniaFact]
    public async Task Spreadsheet_table_sort_filter_and_keyboard_undo_redo_round_trip_cells_and_metadata()
    {
        var workbook = DataWorkbook.Create("Spreadsheet commands");
        var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Name"); sheet.SetCell(0, 1, "Score");
        sheet.SetCell(1, 0, "beta"); sheet.SetCell(1, 1, "10", kind: DataCellKind.Number);
        sheet.SetCell(2, 0, "alpha"); sheet.SetCell(2, 1, "30", kind: DataCellKind.Number);
        sheet.SetCell(3, 0, "gamma"); sheet.SetCell(3, 1, "20", kind: DataCellKind.Number);
        using var page = new DataPage(new HavenEventBus(), new FakeDataRepository(workbook), new FakeDataFormats(), new FakeDataQueries());
        await page.InitializeAsync();
        var window = new Window { Width = 3200, Height = 1400, Content = page };
        try
        {
            window.Show(); window.UpdateLayout(); var router = new HavenInputRouter(page.SceneRoot);
            var surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); surface.SelectRange(0, 0, 3, 1);
            Click(router, Assert.IsType<HavenButton>(page.SceneRoot.DescendantsAndSelf().Single(element => element.Name == "Data.Grid.Table.Create")));
            var table = DataSpreadsheetTableMetadata.Read(sheet.Metadata); Assert.NotNull(table); Assert.Equal(0, table!.StartRow); Assert.Equal(3, table.EndRow); Assert.True(table.HasHeaders);

            surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); surface.SelectCell(1, 1); window.UpdateLayout();
            Click(router, Assert.IsType<HavenButton>(page.SceneRoot.DescendantsAndSelf().Single(element => element.Name == "Data.Grid.Sort.Ascending")));
            Assert.Equal("beta", sheet.GetCell(1, 0)?.Value); Assert.Equal("10", sheet.GetCell(1, 1)?.Value);
            Assert.Equal("gamma", sheet.GetCell(2, 0)?.Value); Assert.Equal("20", sheet.GetCell(2, 1)?.Value);
            Assert.Equal("alpha", sheet.GetCell(3, 0)?.Value); Assert.Equal("30", sheet.GetCell(3, 1)?.Value);

            var filter = Assert.IsType<Input>(page.SceneRoot.DescendantsAndSelf().Single(element => element.Name == "Data.Grid.Filter.Value")); filter.Text = "20"; window.UpdateLayout();
            Click(router, Assert.IsType<HavenButton>(page.SceneRoot.DescendantsAndSelf().Single(element => element.Name == "Data.Grid.Filter.Apply")));
            table = DataSpreadsheetTableMetadata.Read(sheet.Metadata); Assert.Equal(1, table?.FilterColumn); Assert.Equal("20", table?.FilterText);
            surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); Assert.Equal(2, surface.FilteredOutRowCount);
            Assert.Equal("beta", sheet.GetCell(1, 0)?.Value); Assert.Equal("gamma", sheet.GetCell(2, 0)?.Value); Assert.Equal("alpha", sheet.GetCell(3, 0)?.Value);

            Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.Z, HavenKeyModifiers.Control)));
            table = DataSpreadsheetTableMetadata.Read(sheet.Metadata); Assert.NotNull(table); Assert.Null(table!.FilterColumn); Assert.Equal(string.Empty, table.FilterText);
            surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); Assert.Equal(0, surface.FilteredOutRowCount); Assert.Equal("gamma", sheet.GetCell(2, 0)?.Value);

            Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.Z, HavenKeyModifiers.Control)));
            Assert.Equal("beta", sheet.GetCell(1, 0)?.Value); Assert.Equal("alpha", sheet.GetCell(2, 0)?.Value); Assert.Equal("gamma", sheet.GetCell(3, 0)?.Value);
            Assert.NotNull(DataSpreadsheetTableMetadata.Read(sheet.Metadata));

            surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.Y, HavenKeyModifiers.Control)));
            Assert.Equal("beta", sheet.GetCell(1, 0)?.Value); Assert.Equal("gamma", sheet.GetCell(2, 0)?.Value); Assert.Equal("alpha", sheet.GetCell(3, 0)?.Value);
            surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); Assert.True(surface.KeyDown(new HavenKeyInput(HavenKey.Y, HavenKeyModifiers.Control)));
            table = DataSpreadsheetTableMetadata.Read(sheet.Metadata); Assert.Equal(1, table?.FilterColumn); Assert.Equal("20", table?.FilterText); surface = Assert.Single(page.Route.GridHost.Children.OfType<DataSpreadsheetSurface>()); Assert.Equal(2, surface.FilteredOutRowCount);
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        var hit = router.HitTest(point);
        Assert.True(ReferenceEquals(element, hit), $"Expected pointer hit {element.Name}, but hit {hit?.Name ?? "<none>"}. Target bounds: {element.Bounds}. Parent {element.Parent?.Name} bounds: {element.Parent?.Bounds}. Hit bounds: {hit?.Bounds}.");
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("The focused Data page action did not complete.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeDataRepository(params DataWorkbook[] workbooks) : IDataWorkbookRepository
    {
        private readonly List<DataWorkbook> _workbooks = [.. workbooks];
        public int SaveCalls { get; private set; }
        public DataWorkbook? LastSaved { get; private set; }
        public bool FailSaves { get; set; }

        public Task<IReadOnlyList<DataWorkbookSummary>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<DataWorkbookSummary> result = _workbooks.OrderByDescending(workbook => workbook.UpdatedAt)
                .Select(workbook => new DataWorkbookSummary(workbook.Id, workbook.Title, workbook.UpdatedAt, workbook.Version, workbook.Sheets.Count, workbook.Queries.Count, workbook.Recovery.RecoveredFromBackup)).ToArray();
            return Task.FromResult(result);
        }

        public Task<DataWorkbook?> LoadAsync(Guid workbookId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_workbooks.FirstOrDefault(workbook => workbook.Id == workbookId));
        }

        public Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailSaves) throw new IOException("Synthetic save failure");
            SaveCalls++; workbook.Normalize(); workbook.Version++; LastSaved = workbook;
            var index = _workbooks.FindIndex(item => item.Id == workbook.Id);
            if (index < 0) _workbooks.Add(workbook); else _workbooks[index] = workbook;
            var root = Path.Combine(Path.GetTempPath(), "data-fake");
            return Task.FromResult(new DataSaveResult(workbook.Id, workbook.Version, DateTimeOffset.UtcNow, Path.Combine(root, "current.json"), Path.Combine(root, "previous.json")));
        }

        public Task DeleteAsync(Guid workbookId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); _workbooks.RemoveAll(workbook => workbook.Id == workbookId); return Task.CompletedTask;
        }
    }

    private sealed class FakeDataFormats : IDataWorkbookFormatService
    {
        public IReadOnlyList<string> ImportExtensions { get; } = [".xlsx"];
        public IReadOnlyList<string> ExportExtensions { get; } = [".xlsx"];
        public string? LastExportPath { get; private set; }
        public DataWorkbook? LastExportedWorkbook { get; private set; }
        public DataWorkbook ImportedWorkbook { get; set; } = DataWorkbook.Create("Imported workbook");
        public Task<DataWorkbook> ImportAsync(string sourcePath, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(ImportedWorkbook); }
        public Task<string> ExportAsync(DataWorkbook workbook, string destinationPath, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); LastExportedWorkbook = workbook; LastExportPath = destinationPath; return Task.FromResult(destinationPath); }
    }

    private sealed class FakeDataQueries(DataQueryResult? result = null) : IDataWorkbookQueryService
    {
        private readonly DataQueryResult _result = result ?? new DataQueryResult([], [], false, "Fake preview");
        public int Calls { get; private set; }
        public string? LastSql { get; private set; }
        public Task<DataQueryResult> ExecuteReadOnlyAsync(DataWorkbook workbook, string sql, int maxRows, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++; LastSql = sql; return Task.FromResult(_result);
        }
    }
}
