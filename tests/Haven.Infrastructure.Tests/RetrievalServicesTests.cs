/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/RetrievalServicesTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns RetrievalServicesTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents retrieval services tests and keeps its related state and behavior together.
/// </summary>
public sealed class RetrievalServicesTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the indexing same content is incremental and does not duplicate chunks step owned by this component.
    /// </summary>
    [Fact]
    public async Task IndexingSameContentIsIncrementalAndDoesNotDuplicateChunks()
    {
        var (database, service) = await CreateAsync();
        var scope = new RetrievalScope(RetrievalScopeKind.Project, Guid.NewGuid());
        var text = string.Join("\n\n", Enumerable.Repeat("The launch controller validates every deployment before release.", 80));

        var first = await service.IndexTextAsync(scope, "file", "src/controller.cs", "Controller", text, CancellationToken.None);
        var second = await service.IndexTextAsync(scope, "file", "src/controller.cs", "Controller", text, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        await using var connection = await database.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM retrieval_chunks WHERE document_id=$id;";
        command.Parameters.AddWithValue("$id", first.Id.ToString());
        var count = (long)(await command.ExecuteScalarAsync(CancellationToken.None))!;
        Assert.InRange(count, 2, 20);
    }

    /// <summary>
    /// Performs the hybrid search finds semantically related and keyword specific chunks with citations step owned by this component.
    /// </summary>
    [Fact]
    public async Task HybridSearchFindsSemanticallyRelatedAndKeywordSpecificChunksWithCitations()
    {
        var (_, service) = await CreateAsync();
        var scope = new RetrievalScope(RetrievalScopeKind.Conversation, Guid.NewGuid());
        await service.IndexTextAsync(scope, "message", "one", "Database notes",
            "The persistence layer uses SQLite transactions and atomic file replacement to protect drafts.", CancellationToken.None);
        await service.IndexTextAsync(scope, "attachment", "two", "Garden notes",
            "Tomatoes prefer warm soil and regular watering.", CancellationToken.None);

        var result = await service.SearchAsync(new RetrievalQuery(
            "How are saved drafts protected by database writes?", [scope], MaximumResults: 4, TokenBudget: 500), CancellationToken.None);

        Assert.NotEmpty(result.Citations);
        Assert.Contains(result.Citations, citation => citation.Title == "Database notes");
        Assert.Contains("[source 1]", result.Context);
        Assert.InRange(result.EstimatedTokens, 1, 500);
        Assert.Contains("Hybrid", result.Method);
    }

    /// <summary>
    /// Performs the search never leaks across unselected scopes step owned by this component.
    /// </summary>
    [Fact]
    public async Task SearchNeverLeaksAcrossUnselectedScopes()
    {
        var (_, service) = await CreateAsync();
        var allowed = new RetrievalScope(RetrievalScopeKind.Subject, Guid.NewGuid());
        var privateScope = new RetrievalScope(RetrievalScopeKind.Project, Guid.NewGuid());
        await service.IndexTextAsync(allowed, "note", "allowed", "Allowed", "Public revision note about negligence.", CancellationToken.None);
        await service.IndexTextAsync(privateScope, "secret", "private", "Private", "The hidden phrase is cobalt-marzipan-771.", CancellationToken.None);

        var result = await service.SearchAsync(new RetrievalQuery("cobalt marzipan", [allowed]), CancellationToken.None);

        Assert.Empty(result.Citations);
        Assert.DoesNotContain("cobalt", result.Context, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the token budget and per document diversity are enforced step owned by this component.
    /// </summary>
    [Fact]
    public async Task TokenBudgetAndPerDocumentDiversityAreEnforced()
    {
        var (_, service) = await CreateAsync();
        var scope = new RetrievalScope(RetrievalScopeKind.Collection, Guid.NewGuid());
        var longText = string.Join("\n\n", Enumerable.Range(1, 40).Select(index => $"Section {index}: authentication tokens are rotated and audited after deployment."));
        await service.IndexTextAsync(scope, "manual", "security", "Security manual", longText, CancellationToken.None);
        await service.IndexTextAsync(scope, "manual", "operations", "Operations manual", "Deployment audit records include authentication events and rollback status.", CancellationToken.None);

        var result = await service.SearchAsync(new RetrievalQuery("authentication deployment audit", [scope], MaximumResults: 10, TokenBudget: 220), CancellationToken.None);

        Assert.InRange(result.EstimatedTokens, 1, 220);
        Assert.True(result.Citations.GroupBy(item => item.DocumentId).All(group => group.Count() <= 3));
    }

    private async Task<(SqliteDatabase Database, RetrievalIndexService Service)> CreateAsync()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var service = new RetrievalIndexService(database, new LocalHashEmbeddingService());
        return (database, service);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-retrieval-tests-" + Guid.NewGuid().ToString("N"));
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
