using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Data;
using Haven.UI;
using Haven.UI.Components;

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

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
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
