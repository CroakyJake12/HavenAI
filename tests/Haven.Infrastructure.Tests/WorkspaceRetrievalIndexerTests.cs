using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class WorkspaceRetrievalIndexerTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task ProjectIndexerStaysInsideRootSkipsBuildFoldersAndRemovesStaleFiles()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var retrieval = new RetrievalIndexService(database, new LocalHashEmbeddingService());
        var indexer = new WorkspaceRetrievalIndexer(retrieval);
        var projectId = Guid.NewGuid();
        var root = Path.Combine(_paths.DataDirectory, "project");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        await File.WriteAllTextAsync(Path.Combine(root, "src", "Program.cs"), "class Program { static void Main() { } }");
        await File.WriteAllTextAsync(Path.Combine(root, "bin", "generated.cs"), "secret generated output");
        await File.WriteAllTextAsync(Path.Combine(root, "large.txt"), new string('x', 2 * 1024 * 1024 + 1));

        var first = await indexer.IndexProjectAsync(projectId, root, CancellationToken.None);
        var documents = await retrieval.GetDocumentsAsync(new RetrievalScope(RetrievalScopeKind.Project, projectId), CancellationToken.None);

        Assert.Equal(1, first.Indexed);
        Assert.Contains(documents, item => item.SourceId == "src/Program.cs");
        Assert.DoesNotContain(documents, item => item.SourceId.Contains("bin", StringComparison.OrdinalIgnoreCase));
        Assert.True(first.Skipped >= 1);

        File.Delete(Path.Combine(root, "src", "Program.cs"));
        var second = await indexer.IndexProjectAsync(projectId, root, CancellationToken.None);
        Assert.Equal(1, second.Removed);
        Assert.Empty(await retrieval.GetDocumentsAsync(new RetrievalScope(RetrievalScopeKind.Project, projectId), CancellationToken.None));
    }

    [Fact]
    public async Task SubjectIndexerKeepsLessonsInsideOneSubjectScope()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var retrieval = new RetrievalIndexService(database, new LocalHashEmbeddingService());
        var indexer = new WorkspaceRetrievalIndexer(retrieval);
        var now = DateTimeOffset.UtcNow;
        var subject = new ContainerDefinition(
            Guid.NewGuid(), HavenMode.Teach, "A-Level Law", null,
            "OCR law revision context", "Use the current specification", false, now, now);
        var lessons = new[]
        {
            new LessonDefinition(Guid.NewGuid(), subject.Id, "Causation", "Criminal Law", "{\"topics\":[\"but for\",\"legal causation\"]}", now, now)
        };

        await indexer.IndexSubjectAsync(subject, lessons, CancellationToken.None);
        var result = await retrieval.SearchAsync(new RetrievalQuery(
            "but for causation", [new RetrievalScope(RetrievalScopeKind.Subject, subject.Id)]), CancellationToken.None);

        Assert.Contains(result.Citations, item => item.Title == "Causation");
        Assert.All(result.Citations, item => Assert.Equal(subject.Id, item.ScopeId));
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-workspace-retrieval-tests-" + Guid.NewGuid().ToString("N"));
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
