using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class ConversationProductionRepositoryTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task NewBranchEditPreservesOriginalBranchAndProjectsEditedContent()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var production = CreateProduction(database, conversations);
        var versioning = new ConversationVersioningService(conversations, production);
        var now = DateTimeOffset.UtcNow;
        var conversation = ConversationAt(now);
        var user = new ChatMessage(Guid.NewGuid(), conversation.Id, MessageRole.User, "Original question", null, null, null, now);
        var assistant = new ChatMessage(Guid.NewGuid(), conversation.Id, MessageRole.Assistant, "Original answer", "Haven", "qwen", null, now.AddSeconds(1));

        await conversations.UpsertConversationAsync(conversation, CancellationToken.None);
        await conversations.AddMessageAsync(user, CancellationToken.None);
        await conversations.AddMessageAsync(assistant, CancellationToken.None);
        var root = await production.EnsureRootBranchAsync(conversation.Id, CancellationToken.None);

        var editedBranch = await versioning.EditUserMessageAsync(
            conversation.Id, user.Id, "Edited question", MessageEditMode.NewBranch, CancellationToken.None);

        Assert.NotEqual(root.Id, editedBranch.Id);
        Assert.Equal("Edited question", Assert.Single(await conversations.GetMessagesAsync(conversation.Id, CancellationToken.None)).Content);

        await production.SetCurrentBranchAsync(conversation.Id, root.Id, CancellationToken.None);
        var originalMessages = await conversations.GetMessagesAsync(conversation.Id, CancellationToken.None);
        Assert.Collection(originalMessages,
            item => Assert.Equal("Original question", item.Content),
            item => Assert.Equal("Original answer", item.Content));

        await production.SetCurrentBranchAsync(conversation.Id, editedBranch.Id, CancellationToken.None);
        Assert.Equal("Edited question", Assert.Single(await conversations.GetMessagesAsync(conversation.Id, CancellationToken.None)).Content);
    }

    [Fact]
    public async Task OverwriteEditStoresRecoverySnapshotBeforeMakingNewVersionCurrent()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var production = CreateProduction(database, conversations);
        var versioning = new ConversationVersioningService(conversations, production);
        var now = DateTimeOffset.UtcNow;
        var conversation = ConversationAt(now);
        var user = new ChatMessage(Guid.NewGuid(), conversation.Id, MessageRole.User, "Before", null, null, null, now);

        await conversations.UpsertConversationAsync(conversation, CancellationToken.None);
        await conversations.AddMessageAsync(user, CancellationToken.None);
        var branch = await production.EnsureRootBranchAsync(conversation.Id, CancellationToken.None);

        await versioning.EditUserMessageAsync(conversation.Id, user.Id, "After", MessageEditMode.OverwriteCurrentBranch, CancellationToken.None);

        var versions = await production.GetVersionsAsync(user.Id, branch.Id, CancellationToken.None);
        Assert.Contains(versions, item => item.Kind == MessageVersionKind.RecoverySnapshot && item.Content == "Before");
        Assert.Contains(versions, item => item.Kind == MessageVersionKind.UserEdit && item.Content == "After" && item.IsCurrent);
        Assert.Equal("After", Assert.Single(await conversations.GetMessagesAsync(conversation.Id, CancellationToken.None)).Content);
    }

    [Fact]
    public async Task DraftBookmarkSearchAndExportsRoundTrip()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var production = CreateProduction(database, conversations);
        var exports = new ConversationExportService(production);
        var now = DateTimeOffset.UtcNow;
        var conversation = ConversationAt(now) with { Title = "Production conversation" };
        var message = new ChatMessage(Guid.NewGuid(), conversation.Id, MessageRole.User, "Find the sapphire phrase", null, null, null, now);

        await conversations.UpsertConversationAsync(conversation, CancellationToken.None);
        await conversations.AddMessageAsync(message, CancellationToken.None);
        var branch = await production.EnsureRootBranchAsync(conversation.Id, CancellationToken.None);
        var draft = new ConversationDraft(conversation.Id, branch.Id, "unfinished thought", "[]", now.AddMinutes(1));
        await production.SaveDraftAsync(draft, CancellationToken.None);
        await production.UpsertBookmarkAsync(new MessageBookmark(Guid.NewGuid(), conversation.Id, message.Id, "Important", "Use later", now), CancellationToken.None);

        Assert.Equal("unfinished thought", (await production.GetDraftAsync(conversation.Id, branch.Id, CancellationToken.None))?.Content);
        Assert.Single(await production.GetBookmarksAsync(conversation.Id, CancellationToken.None));
        Assert.Contains(await production.SearchAsync("sapphire", null, 20, CancellationToken.None), item => item.MessageId == message.Id);

        var markdown = await exports.ExportMarkdownAsync(conversation.Id, CancellationToken.None);
        var plainText = await exports.ExportPlainTextAsync(conversation.Id, CancellationToken.None);
        var json = await exports.ExportJsonAsync(conversation.Id, CancellationToken.None);
        Assert.Contains("Production conversation", markdown);
        Assert.Contains("sapphire phrase", plainText);
        Assert.Contains("production conversation", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductionSchemaIsCreatedForEveryDatabaseInstance()
    {
        var firstPaths = new TestPaths();
        var secondPaths = new TestPaths();
        try
        {
            foreach (var paths in new[] { firstPaths, secondPaths })
            {
                var database = new SqliteDatabase(paths);
                await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
                await using var connection = await database.OpenAsync(CancellationToken.None);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('conversation_branches','message_versions','message_attachments','conversation_drafts','shared_sessions');";
                Assert.Equal(5L, (long)(await command.ExecuteScalarAsync(CancellationToken.None))!);
            }
        }
        finally
        {
            firstPaths.Dispose();
            secondPaths.Dispose();
        }
    }

    private async Task<SqliteDatabase> CreateDatabaseAsync()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        return database;
    }

    private static IConversationProductionRepository CreateProduction(SqliteDatabase database, ConversationRepository conversations)
    {
        var inner = new ConversationProductionRepository(database, conversations);
        return new SafeConversationProductionRepository(database, conversations, inner);
    }

    private static Conversation ConversationAt(DateTimeOffset now) => new(
        Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Conversation", null, null,
        false, false, now, now);

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-conversation-production-tests-" + Guid.NewGuid().ToString("N"));
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
