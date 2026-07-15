using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class ContinuationMigrationTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task ProductionAndRetrievalSchemasApplyRepeatedlyWithoutChangingEarlierVersions()
    {
        var database = new SqliteDatabase(_paths);
        var production = new ConversationProductionDatabase(database);
        await production.InitializeAsync(CancellationToken.None);
        await production.InitializeAsync(CancellationToken.None);

        var retrieval = new RetrievalIndexService(database, new LocalHashEmbeddingService());
        var scope = new RetrievalScope(RetrievalScopeKind.Collection, Guid.NewGuid());
        await retrieval.IndexTextAsync(scope, "test", "one", "One", "first indexed document", CancellationToken.None);
        await retrieval.IndexTextAsync(scope, "test", "one", "One", "first indexed document", CancellationToken.None);

        await using var connection = await database.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_migrations ORDER BY version;";
        var versions = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None)) versions.Add(reader.GetInt64(0));

        Assert.Contains(9, versions);
        Assert.Contains(10, versions);
        Assert.Equal(versions.Count, versions.Distinct().Count());
        Assert.Equal(10, versions.Max());
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : Haven.Application.IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-continuation-migration-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
