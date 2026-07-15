using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class RetrievalServicesTests : IDisposable
{
    private readonly TestPaths _paths = new();

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

    public void Dispose() => _paths.Dispose();

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

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
