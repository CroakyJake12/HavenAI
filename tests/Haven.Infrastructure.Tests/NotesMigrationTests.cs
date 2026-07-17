using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class NotesMigrationTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task SchemaZeroFileMigratesAndRepairsDuplicateIdentifiers()
    {
        var duplicate = Guid.NewGuid();
        var path = Path.Combine(_paths.DataDirectory, "legacy.haven-notes.json");
        await File.WriteAllTextAsync(path,
            $$"""
            {
              "id": "{{Guid.NewGuid()}}",
              "title": "Legacy",
              "sections": [
                {
                  "id": "{{duplicate}}",
                  "title": "Section",
                  "pages": [
                    {
                      "id": "{{duplicate}}",
                      "title": "Page",
                      "blocks": [
                        { "id": "00000000-0000-0000-0000-000000000000", "kind": 0, "plainText": "Legacy text" }
                      ]
                    }
                  ]
                }
              ]
            }
            """);
        var migrator = new NotesDocumentMigrator();

        var result = await migrator.ReadAndMigrateAsync(path, CancellationToken.None);
        var validation = new NotesDocumentValidator().Validate(result.Document);

        Assert.Equal(0, result.SourceSchemaVersion);
        Assert.Equal(NotesDocument.CurrentSchemaVersion, result.TargetSchemaVersion);
        Assert.True(validation.IsValid, string.Join(" | ", validation.Issues.Select(issue => issue.Message)));
        var identifiers = AllIds(result.Document).ToArray();
        Assert.DoesNotContain(Guid.Empty, identifiers);
        Assert.Equal(identifiers.Length, identifiers.Distinct().Count());
        Assert.Contains(result.Changes, change => change.Contains("schema 0", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Changes, change => change.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ManagedLegacyDocumentIsMigratedBeforeLibraryListingAndPreservesOldCurrent()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var validator = new NotesDocumentValidator();
        var baseRepository = new NotesRepository(_paths, validator, diagnostics);
        var verified = new VerifiedNotesRepository(baseRepository, _paths, diagnostics);
        var repository = new MigratingNotesRepository(
            verified,
            new NotesDocumentMigrator(),
            validator,
            _paths,
            diagnostics);
        var id = Guid.NewGuid();
        var directory = Path.Combine(_paths.DataDirectory, "Notes", "Documents", id.ToString("D"));
        Directory.CreateDirectory(directory);
        var current = Path.Combine(directory, "current.haven-notes.json");
        await File.WriteAllTextAsync(current,
            $$"""
            {
              "schemaVersion": 0,
              "id": "{{id}}",
              "title": "Managed legacy",
              "sections": [
                {
                  "title": "Section",
                  "pages": [
                    {
                      "title": "Page",
                      "blocks": [ { "kind": 0, "plainText": "Migrated content" } ]
                    }
                  ]
                }
              ]
            }
            """);

        var list = await repository.ListAsync(CancellationToken.None);
        var loaded = await repository.LoadAsync(id, CancellationToken.None);
        var versions = await repository.GetVersionsAsync(id, CancellationToken.None);

        Assert.Contains(list, item => item.Id == id && item.Title == "Managed legacy");
        Assert.NotNull(loaded);
        Assert.Equal(NotesDocument.CurrentSchemaVersion, loaded!.SchemaVersion);
        Assert.Contains("Migrated content", loaded.Sections[0].Pages[0].Blocks[0].PlainText);
        Assert.NotEmpty(versions);
        Assert.True(File.Exists(Path.Combine(directory, "current.integrity.json")));
        Assert.True(File.Exists(Path.Combine(directory, "backup.haven-notes.json")));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(current));
        Assert.Equal(NotesDocument.CurrentSchemaVersion, json.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public async Task NativeImportMigratesBeforeAssigningNewDocumentIdentity()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var validator = new NotesDocumentValidator();
        var inner = new NotesImportExportService(validator, diagnostics);
        var service = new MigratingNotesImportExportService(
            inner,
            new NotesDocumentMigrator(),
            validator,
            diagnostics);
        var sourceId = Guid.NewGuid();
        var path = Path.Combine(_paths.DataDirectory, "import.haven-notes.json");
        await File.WriteAllTextAsync(path,
            $$"""
            {
              "schemaVersion": 0,
              "id": "{{sourceId}}",
              "title": "Imported legacy",
              "sections": [
                {
                  "title": "Section",
                  "pages": [
                    {
                      "title": "Page",
                      "blocks": [ { "kind": 0, "plainText": "Imported text" } ]
                    }
                  ]
                }
              ]
            }
            """);

        var document = await service.ImportAsync(path, CancellationToken.None);

        Assert.NotEqual(sourceId, document.Id);
        Assert.Equal(NotesDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.Equal(0, document.Version);
        Assert.Contains(document.Revisions, revision => revision.Kind == NotesRevisionKind.Imported);
        Assert.True(validator.Validate(document).IsValid);
    }

    [Fact]
    public async Task NewerSchemaFailsClosedWithoutOverwritingInput()
    {
        var path = Path.Combine(_paths.DataDirectory, "future.haven-notes.json");
        var content = $$"""
                      {
                        "schemaVersion": {{NotesDocument.CurrentSchemaVersion + 1}},
                        "id": "{{Guid.NewGuid()}}",
                        "title": "Future"
                      }
                      """;
        await File.WriteAllTextAsync(path, content);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new NotesDocumentMigrator().ReadAndMigrateAsync(path, CancellationToken.None));

        Assert.Equal(content, await File.ReadAllTextAsync(path));
    }

    private static IEnumerable<Guid> AllIds(NotesDocument document)
    {
        yield return document.Id;
        foreach (var section in document.Sections)
        {
            yield return section.Id;
            foreach (var page in section.Pages)
            {
                yield return page.Id;
                foreach (var block in page.Blocks)
                {
                    yield return block.Id;
                    foreach (var run in block.Runs) yield return run.Id;
                    if (block.List is not null) foreach (var item in block.List.Items) yield return item.Id;
                    if (block.Table is not null)
                    foreach (var row in block.Table.Rows)
                    {
                        yield return row.Id;
                        foreach (var cell in row.Cells) yield return cell.Id;
                    }
                }
            }
        }
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-notes-migration-tests-" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
