using System.IO.Compression;
using System.Xml.Linq;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

public sealed class DataProductionTests : IDisposable
{
    private readonly DataTestPaths _paths = new();

    [Fact]
    public void Infrastructure_registers_data_repository_format_and_query_services()
    {
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<DataWorkbookRepository>(provider.GetRequiredService<IDataWorkbookRepository>());
        Assert.IsType<DataXlsxFormatService>(provider.GetRequiredService<IDataWorkbookFormatService>());
        Assert.IsType<DataWorkbookQueryService>(provider.GetRequiredService<IDataWorkbookQueryService>());
    }

    [Fact]
    public async Task Repository_round_trip_preserves_sparse_cells_queries_schema_and_metadata()
    {
        var repository = new DataWorkbookRepository(_paths);
        var workbook = DataWorkbook.Create("Results data");
        workbook.Sheets[0].Name = "People";
        workbook.Sheets[0].SetCell(0, 0, "Name");
        workbook.Sheets[0].SetCell(0, 1, "Score");
        workbook.Sheets[0].SetCell(1, 0, "Ada");
        workbook.Sheets[0].SetCell(1, 1, "42", kind: DataCellKind.Number);
        workbook.Sheets[0].SetCell(7, 4, "84", "=B2*2", DataCellKind.Formula);
        workbook.Queries[0].Name = "Top scores";
        workbook.Queries[0].Visual.Source = "People";
        workbook.Queries[0].Visual.Columns = "A, B";
        workbook.Queries[0].Visual.Filter = "B >= 40";
        workbook.Queries[0].Visual.OrderBy = "B DESC";
        workbook.Queries[0].Visual.Limit = 25;
        workbook.Queries[0].Sql = workbook.Queries[0].Visual.BuildSql();
        workbook.Schema.Tables.Add(new DataSchemaTable
        {
            Name = "People",
            Columns = [new DataSchemaColumn { Name = "Name", DataType = "TEXT" }, new DataSchemaColumn { Name = "Score", DataType = "NUMBER" }]
        });
        workbook.Metadata["havenBinding"] = "results-day";

        var saved = await repository.SaveAsync(workbook, "Initial workbook", CancellationToken.None);
        var loaded = await repository.LoadAsync(workbook.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(1, saved.Version);
        Assert.Equal("Results data", loaded!.Title);
        Assert.Equal("Ada", loaded.Sheets[0].GetCell(1, 0)?.Value);
        Assert.Equal(DataCellKind.Number, loaded.Sheets[0].GetCell(1, 1)?.Kind);
        Assert.Equal("=B2*2", loaded.Sheets[0].GetCell(7, 4)?.Formula);
        Assert.Equal("Top scores", loaded.Queries[0].Name);
        Assert.Equal(25, loaded.Queries[0].Visual.Limit);
        Assert.Equal("results-day", loaded.Metadata["havenBinding"]);
        Assert.Equal(2, loaded.Schema.Tables[0].Columns.Count);
        Assert.True(File.Exists(saved.CurrentPath));
    }

    [Fact]
    public async Task Repository_recovers_previous_valid_workbook_and_preserves_backup_when_recommitted()
    {
        var repository = new DataWorkbookRepository(_paths);
        var workbook = DataWorkbook.Create("Version one");
        workbook.Sheets[0].SetCell(0, 0, "one");
        _ = await repository.SaveAsync(workbook, "First", CancellationToken.None);
        workbook.Title = "Version two";
        workbook.Sheets[0].SetCell(0, 0, "two");
        var second = await repository.SaveAsync(workbook, "Second", CancellationToken.None);
        var backupBefore = await File.ReadAllTextAsync(second.BackupPath, CancellationToken.None);

        await File.WriteAllTextAsync(second.CurrentPath, "{ unreadable json", CancellationToken.None);
        var recovered = await repository.LoadAsync(workbook.Id, CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.True(recovered!.Recovery.RecoveredFromBackup);
        Assert.Equal("Version one", recovered.Title);
        Assert.Equal("one", recovered.Sheets[0].GetCell(0, 0)?.Value);

        recovered.Title = "Recovered edit";
        var saved = await repository.SaveAsync(recovered, "Recovery confirmed", CancellationToken.None);
        Assert.Equal(backupBefore, await File.ReadAllTextAsync(saved.BackupPath, CancellationToken.None));
        Assert.Contains(Directory.EnumerateFiles(Path.GetDirectoryName(saved.CurrentPath)!, "unreadable-current-*.json"), File.Exists);
        var reopened = await repository.LoadAsync(workbook.Id, CancellationToken.None);
        Assert.NotNull(reopened);
        Assert.Equal("Recovered edit", reopened!.Title);
        Assert.False(reopened.Recovery.RecoveredFromBackup);
    }

    [Fact]
    public async Task Xlsx_export_writes_real_package_and_import_round_trips_values_and_formulas()
    {
        var workbook = DataWorkbook.Create("Export workbook");
        workbook.Sheets[0].Name = "Results & Notes";
        workbook.Sheets[0].SetCell(0, 0, "Student");
        workbook.Sheets[0].SetCell(0, 1, "Score");
        workbook.Sheets[0].SetCell(1, 0, "Ada & Bob");
        workbook.Sheets[0].SetCell(1, 1, "42.5", kind: DataCellKind.Number);
        workbook.Sheets[0].SetCell(1, 2, "85", "=B2*2", DataCellKind.Formula);
        var service = new DataXlsxFormatService();
        var destination = Path.Combine(_paths.DataDirectory, "workbook.xlsx");

        var exported = await service.ExportAsync(workbook, destination, CancellationToken.None);

        Assert.Equal(destination, exported, ignoreCase: true);
        await using (var file = File.OpenRead(exported))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false))
        {
            foreach (var part in new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/worksheets/sheet1.xml" })
                Assert.NotNull(archive.GetEntry(part));
            foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.Ordinal) || entry.FullName.EndsWith(".rels", StringComparison.Ordinal)))
            {
                using var stream = entry.Open();
                _ = XDocument.Load(stream);
            }
        }

        var imported = await service.ImportAsync(exported, CancellationToken.None);
        Assert.Equal("Results & Notes", imported.Sheets[0].Name);
        Assert.Equal("Ada & Bob", imported.Sheets[0].GetCell(1, 0)?.Value);
        Assert.Equal("42.5", imported.Sheets[0].GetCell(1, 1)?.Value);
        Assert.Equal("=B2*2", imported.Sheets[0].GetCell(1, 2)?.Formula);
        Assert.Contains("Formatting, charts, drawings, macros and external links are not imported", imported.Metadata["compatibilityNote"], StringComparison.Ordinal);
    }

    [Fact]
    public void Sql_safety_is_conservative_for_mutation_multiple_statements_and_with()
    {
        Assert.True(DataSqlSafety.Analyze("-- comment\nSELECT * FROM \"Sheet 1\";").IsReadOnly);
        Assert.Equal(DataSqlRisk.Destructive, DataSqlSafety.Analyze("DROP TABLE users").Risk);
        Assert.Equal(DataSqlRisk.Destructive, DataSqlSafety.Analyze("DELETE FROM users").Risk);
        Assert.Equal(DataSqlRisk.Mutating, DataSqlSafety.Analyze("UPDATE users SET name='x'").Risk);
        Assert.Equal(DataSqlRisk.MultipleStatements, DataSqlSafety.Analyze("SELECT 1; SELECT 2;").Risk);
        Assert.Equal(DataSqlRisk.Unknown, DataSqlSafety.Analyze("WITH x AS (SELECT 1) SELECT * FROM x").Risk);
    }

    [Fact]
    public async Task Query_service_runs_real_read_only_select_over_workbook_and_refuses_delete()
    {
        var workbook = DataWorkbook.Create("Query workbook");
        workbook.Sheets[0].Name = "People";
        workbook.Sheets[0].SetCell(0, 0, "Ada");
        workbook.Sheets[0].SetCell(0, 1, "42", kind: DataCellKind.Number);
        workbook.Sheets[0].SetCell(1, 0, "Bob");
        workbook.Sheets[0].SetCell(1, 1, "7", kind: DataCellKind.Number);
        var service = new DataWorkbookQueryService();

        var result = await service.ExecuteReadOnlyAsync(workbook, "SELECT A, B FROM \"People\" WHERE B > 10 ORDER BY _row", 20, CancellationToken.None);

        Assert.Equal(["A", "B"], result.Columns);
        var row = Assert.Single(result.Rows);
        Assert.Equal(["Ada", "42"], row);
        Assert.False(result.Truncated);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteReadOnlyAsync(workbook, "DELETE FROM \"People\"", 20, CancellationToken.None));
    }

    public void Dispose() => _paths.Dispose();

    private sealed class DataTestPaths : IAppPaths, IDisposable
    {
        public DataTestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-data-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");

        public void Dispose()
        {
            try { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
