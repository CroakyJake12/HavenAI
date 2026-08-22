/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/ContinuationMigrationTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ContinuationMigrationTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents continuation migration tests and keeps its related state and behavior together.
/// </summary>
public sealed class ContinuationMigrationTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the production and retrieval schemas apply repeatedly without changing earlier versions step owned by this component.
    /// </summary>
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
        Assert.Contains(11, versions);
        Assert.Contains(12, versions);
        Assert.Contains(13, versions);
        Assert.Contains(14, versions);
        Assert.Contains(15, versions);
        Assert.Contains(16, versions);
        Assert.Contains(17, versions);
        Assert.Contains(18, versions);
        Assert.Contains(19, versions);
        Assert.Contains(20, versions);
        Assert.Contains(21, versions);
        Assert.Equal(Migrations.All.Max(item => item.Version), versions.Max());
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
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

        /// <summary>
        /// Gets or updates data directory, the bindable or domain state represented by this property.
        /// </summary>
        public string DataDirectory { get; }
        /// <summary>
        /// Gets or updates database path, the bindable or domain state represented by this property.
        /// </summary>
        public string DatabasePath { get; }
        /// <summary>
        /// Gets or updates browser profile directory, the bindable or domain state represented by this property.
        /// </summary>
        public string BrowserProfileDirectory { get; }
        /// <summary>
        /// Gets or updates attachments directory, the bindable or domain state represented by this property.
        /// </summary>
        public string AttachmentsDirectory { get; }
        /// <summary>
        /// Gets or updates logs directory, the bindable or domain state represented by this property.
        /// </summary>
        public string LogsDirectory { get; }
        /// <summary>
        /// Gets or updates legacy state path, the bindable or domain state represented by this property.
        /// </summary>
        public string LegacyStatePath { get; }
        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
