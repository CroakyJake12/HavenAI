/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/WorkspaceRetrievalIndexerTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns WorkspaceRetrievalIndexerTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents workspace retrieval indexer tests and keeps its related state and behavior together.
/// </summary>
public sealed class WorkspaceRetrievalIndexerTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the project indexer stays inside root skips build folders and removes stale files step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the subject indexer keeps lessons inside one subject scope step owned by this component.
    /// </summary>
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
            "OCR law revision context", "Use the current specification", now, now, false);
        var lessons = new[]
        {
            new Lesson(Guid.NewGuid(), subject.Id, "Causation", "Criminal Law", "{\"topics\":[\"but for\",\"legal causation\"]}", 0, now, now)
        };

        await indexer.IndexSubjectAsync(subject, lessons, CancellationToken.None);
        var result = await retrieval.SearchAsync(new RetrievalQuery(
            "but for causation", [new RetrievalScope(RetrievalScopeKind.Subject, subject.Id)]), CancellationToken.None);

        Assert.Contains(result.Citations, item => item.Title == "Causation");
        Assert.All(result.Citations, item => Assert.Equal(subject.Id, item.ScopeId));
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
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-workspace-retrieval-tests-" + Guid.NewGuid().ToString("N"));
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
