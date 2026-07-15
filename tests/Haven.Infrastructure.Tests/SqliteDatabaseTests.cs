using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class SqliteDatabaseTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task InitialisationIsRepeatableAndConversationRoundTrips()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        await database.InitializeAsync(CancellationToken.None);
        var repository = new ConversationRepository(database);
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Studio, ConversationKind.StudioChat, "Native test", null, null, true, false, now, now);
        await repository.UpsertConversationAsync(conversation, CancellationToken.None);
        await repository.AddMessageAsync(new ChatMessage(Guid.NewGuid(), conversation.Id, MessageRole.User, "Hello", null, null, null, now), CancellationToken.None);

        var loaded = await repository.GetAsync(conversation.Id, CancellationToken.None);
        var messages = await repository.GetMessagesAsync(conversation.Id, CancellationToken.None);
        Assert.Equal(conversation.Title, loaded?.Title);
        Assert.Single(messages);
        Assert.Equal("Hello", messages[0].Content);
    }

    [Fact]
    public async Task ExtendedWorkspaceStateAndArchiveRoundTrip()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var conversations = new ConversationRepository(database);
        var containers = new ContainerRepository(database);
        var catalog = new CatalogRepository(database);
        var workspace = new WorkspaceStateRepository(database);
        var now = DateTimeOffset.UtcNow;
        var container = new ContainerDefinition(Guid.NewGuid(), HavenMode.Studio, "Test project", _paths.DataDirectory, "context", "rules", now, now);
        await containers.UpsertAsync(container, CancellationToken.None);
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Studio, ConversationKind.StudioChat, "Archived branch", container.Id, null, false, false, now, now, true, Guid.NewGuid(), now);
        await conversations.UpsertConversationAsync(conversation, CancellationToken.None);
        await conversations.AddMessageAsync(new ChatMessage(Guid.NewGuid(), conversation.Id, MessageRole.User, "Old turn", null, null, null, now, true), CancellationToken.None);
        await conversations.AddContextEntryAsync(new ConversationContextEntry(Guid.NewGuid(), conversation.Id, ContextEntryKind.CompactSummary, "Summary", "Preserved context", "test evidence", now), CancellationToken.None);

        await workspace.UpsertMacroAsync(new MacroDefinition(Guid.NewGuid(), "Timestamp", "Add timestamp", "Insert the current timestamp", container.Id, true, now, now), CancellationToken.None);
        await workspace.AddVersionAsync(new WorkspaceVersion(Guid.NewGuid(), conversation.Id, container.Id, _paths.DataDirectory, "note.txt", WorkspaceVersionKind.Edit, "a", "b", "Changed note", 1, 1, now), CancellationToken.None);
        await workspace.UpsertDecisionAsync(new DecisionRecord(Guid.NewGuid(), container.Id, "Storage", "Use SQLite", "JSON", "Atomic local queries", "Migration test", "Maintain migrations", now, now), CancellationToken.None);

        Assert.DoesNotContain((await conversations.GetRecentAsync(HavenMode.Studio, 20, CancellationToken.None)), item => item.Id == conversation.Id);
        Assert.Contains((await conversations.GetArchivedAsync(HavenMode.Studio, 20, CancellationToken.None)), item => item.Id == conversation.Id);
        Assert.Single(await conversations.GetContextEntriesAsync(conversation.Id, CancellationToken.None));
        Assert.Single(await workspace.GetMacrosAsync(container.Id, CancellationToken.None));
        Assert.Single(await workspace.GetVersionsAsync(container.Id, "note.txt", 10, CancellationToken.None));
        Assert.Single(await workspace.GetDecisionsAsync(container.Id, CancellationToken.None));

        var prompts = await catalog.GetPromptsAsync(CancellationToken.None);
        Assert.Contains(prompts, item => item.Name == "Handoff");
        Assert.Contains(prompts, item => item.Name == "Context");
        Assert.DoesNotContain(await catalog.GetPluginsAsync(CancellationToken.None), item => item.Name == "Parameter" && item.IsEnabled);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-db-tests-" + Guid.NewGuid().ToString("N"));
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
