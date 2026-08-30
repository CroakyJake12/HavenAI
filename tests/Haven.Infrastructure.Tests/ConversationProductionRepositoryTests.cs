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
    public async Task RecentAttachmentsAreReturnedAcrossChatsNewestFirstAndBounded()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var production = CreateProduction(database, conversations);
        var now = DateTimeOffset.UtcNow;
        var firstConversation = ConversationAt(now) with { Title = "First" };
        var secondConversation = ConversationAt(now.AddMinutes(1)) with { Title = "Second" };
        await conversations.UpsertConversationAsync(firstConversation, CancellationToken.None);
        await conversations.UpsertConversationAsync(secondConversation, CancellationToken.None);

        var oldest = new MessageAttachment(Guid.NewGuid(), firstConversation.Id, null, null, "old.txt", "old.txt", "text/plain", MessageAttachmentKind.PlainText, 3, "old", AttachmentProcessingState.Ready, AttachmentAnalysisMethod.TextExtracted, "old", "{}", now, now);
        var middle = new MessageAttachment(Guid.NewGuid(), secondConversation.Id, null, null, "middle.pdf", "middle.pdf", "application/pdf", MessageAttachmentKind.Pdf, 4, "middle", AttachmentProcessingState.Ready, AttachmentAnalysisMethod.None, string.Empty, "{}", now.AddMinutes(1), now.AddMinutes(1));
        var newest = new MessageAttachment(Guid.NewGuid(), firstConversation.Id, null, null, "new.cs", "new.cs", "text/plain", MessageAttachmentKind.SourceCode, 5, "new", AttachmentProcessingState.Ready, AttachmentAnalysisMethod.TextExtracted, "code", "{}", now.AddMinutes(2), now.AddMinutes(2));
        await production.UpsertAttachmentAsync(oldest, CancellationToken.None);
        await production.UpsertAttachmentAsync(middle, CancellationToken.None);
        await production.UpsertAttachmentAsync(newest, CancellationToken.None);

        var recent = await production.GetRecentAttachmentsAsync(2, CancellationToken.None);

        Assert.Collection(recent,
            item => Assert.Equal(newest.Id, item.Id),
            item => Assert.Equal(middle.Id, item.Id));
        Assert.Contains(recent, item => item.ConversationId == firstConversation.Id);
        Assert.Contains(recent, item => item.ConversationId == secondConversation.Id);
        Assert.Empty(await production.GetRecentAttachmentsAsync(0, CancellationToken.None));
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

    /// <summary>
    /// Creates database async with the invariants required by its callers.
    /// </summary>
    [Fact]
    public async Task SpaceMembershipRoundTripsAndQueriesOnlyMatchingSpace()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ConversationRepository(database);
        var now = DateTimeOffset.UtcNow;
        var targetSpace = Guid.NewGuid();
        var otherSpace = Guid.NewGuid();
        var target = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Target", null, null, false, false, now, now, SpaceId: targetSpace);
        var other = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Other", null, null, false, false, now, now.AddMinutes(1), SpaceId: otherSpace);
        var unscoped = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Unscoped", null, null, false, false, now, now.AddMinutes(2));

        await repository.UpsertConversationAsync(target, CancellationToken.None);
        await repository.UpsertConversationAsync(other, CancellationToken.None);
        await repository.UpsertConversationAsync(unscoped, CancellationToken.None);

        var reopenedDatabase = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(reopenedDatabase).InitializeAsync(CancellationToken.None);
        var reopened = new ConversationRepository(reopenedDatabase);
        var loaded = await reopened.GetAsync(target.Id, CancellationToken.None);
        var rows = await reopened.GetBySpaceAsync(targetSpace, 50, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(targetSpace, loaded!.SpaceId);
        Assert.Single(rows);
        Assert.Equal(target.Id, rows[0].Id);
    }

    [Fact]
    public async Task DetachSpacePreservesConversationAndClearsMembership()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ConversationRepository(database);
        var now = DateTimeOffset.UtcNow;
        var spaceId = Guid.NewGuid();
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Keep me", null, null, false, false, now, now, SpaceId: spaceId);
        await repository.UpsertConversationAsync(conversation, CancellationToken.None);

        await repository.DetachSpaceAsync(spaceId, CancellationToken.None);

        var loaded = await repository.GetAsync(conversation.Id, CancellationToken.None);
        var rows = await repository.GetBySpaceAsync(spaceId, 50, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(conversation.Id, loaded!.Id);
        Assert.Equal("Keep me", loaded.Title);
        Assert.Null(loaded.SpaceId);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task ExistingUnscopedConversationRemainsUnscoped()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ConversationRepository(database);
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Normal chat", null, null, false, false, now, now);
        await repository.UpsertConversationAsync(conversation, CancellationToken.None);

        var loaded = await repository.GetAsync(conversation.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Null(loaded!.SpaceId);
    }
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
