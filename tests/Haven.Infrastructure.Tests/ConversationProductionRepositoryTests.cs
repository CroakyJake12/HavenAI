/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/ConversationProductionRepositoryTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ConversationProductionRepositoryTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents conversation production repository tests and keeps its related state and behavior together.
/// </summary>
public sealed class ConversationProductionRepositoryTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the new branch edit preserves original branch and projects edited content step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the overwrite edit stores recovery snapshot before making new version current step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the draft bookmark search and exports round trip step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the production schema is created for every database instance step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Creates database async with the invariants required by its callers.
    /// </summary>
    private async Task<SqliteDatabase> CreateDatabaseAsync()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        return database;
    }

    /// <summary>
    /// Creates production with the invariants required by its callers.
    /// </summary>
    private static IConversationProductionRepository CreateProduction(SqliteDatabase database, ConversationRepository conversations)
    {
        var inner = new ConversationProductionRepository(database, conversations);
        return new SafeConversationProductionRepository(database, conversations, inner);
    }

    /// <summary>
    /// Performs the conversation at step owned by this component.
    /// </summary>
    private static Conversation ConversationAt(DateTimeOffset now) => new(
        Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Conversation", null, null,
        false, false, now, now);

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
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-conversation-production-tests-" + Guid.NewGuid().ToString("N"));
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
