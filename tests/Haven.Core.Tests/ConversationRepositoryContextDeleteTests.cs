/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/ConversationRepositoryContextDeleteTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ConversationRepositoryContextDeleteTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Core.Tests;

/// <summary>
/// Verifies that per-conversation context rows can be deleted individually while protected compact summaries refuse deletion.
/// </summary>
public sealed class ConversationRepositoryContextDeleteTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Deletes a removable context entry through the real SQLite repository and keeps the remaining rows intact.
    /// </summary>
    [Fact]
    public async Task DeleteContextEntryRemovesRemovableRowAndReturnsTrue()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var conversation = ConversationAt(DateTimeOffset.UtcNow);
        await conversations.UpsertConversationAsync(conversation, CancellationToken.None);
        var registered = new ConversationContextEntry(Guid.NewGuid(), conversation.Id, ContextEntryKind.Registered,
            "Workspace facts", "The project builds with .NET 10.", "Observed in build logs", DateTimeOffset.UtcNow.AddMinutes(-2));
        var decision = new ConversationContextEntry(Guid.NewGuid(), conversation.Id, ContextEntryKind.Decision,
            "Use SQLite", "Persist context rows locally.", "Local-first privacy rule", DateTimeOffset.UtcNow.AddMinutes(-1));
        await conversations.AddContextEntryAsync(registered, CancellationToken.None);
        await conversations.AddContextEntryAsync(decision, CancellationToken.None);

        var removed = await conversations.DeleteContextEntryAsync(conversation.Id, registered.Id, CancellationToken.None);

        Assert.True(removed);
        var remaining = await conversations.GetContextEntriesAsync(conversation.Id, CancellationToken.None);
        var remainingIds = remaining.Select(entry => entry.Id).ToArray();
        Assert.DoesNotContain(registered.Id, remainingIds);
        Assert.Contains(decision.Id, remainingIds);
    }

    /// <summary>
    /// Refuses to delete a protected compact summary so continuity survives individual removal attempts.
    /// </summary>
    [Fact]
    public async Task DeleteContextEntryRefusesProtectedCompactSummaryAndKeepsRow()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var conversation = ConversationAt(DateTimeOffset.UtcNow);
        await conversations.UpsertConversationAsync(conversation, CancellationToken.None);
        var summary = new ConversationContextEntry(Guid.NewGuid(), conversation.Id, ContextEntryKind.CompactSummary,
            "Manual compact summary", "Earlier decisions preserved for future turns.", "Compacted 6 messages", DateTimeOffset.UtcNow);
        await conversations.AddContextEntryAsync(summary, CancellationToken.None);

        var removed = await conversations.DeleteContextEntryAsync(conversation.Id, summary.Id, CancellationToken.None);

        Assert.False(removed);
        var remaining = await conversations.GetContextEntriesAsync(conversation.Id, CancellationToken.None);
        Assert.Contains(remaining, entry => entry.Id == summary.Id && entry.Kind == ContextEntryKind.CompactSummary);
    }

    /// <summary>
    /// Keeps unknown ids honest by returning false without throwing.
    /// </summary>
    [Fact]
    public async Task DeleteContextEntryReturnsFalseForUnknownId()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var conversation = ConversationAt(DateTimeOffset.UtcNow);
        await conversations.UpsertConversationAsync(conversation, CancellationToken.None);

        Assert.False(await conversations.DeleteContextEntryAsync(conversation.Id, Guid.NewGuid(), CancellationToken.None));
    }

    /// <summary>
    /// Creates database async with the invariants required by its callers.
    /// </summary>
    private async Task<SqliteDatabase> CreateDatabaseAsync()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        return database;
    }

    /// <summary>
    /// Performs the conversation at step owned by this component.
    /// </summary>
    private static Conversation ConversationAt(DateTimeOffset now) => new(
        Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Context delete tests", null, null,
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
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-context-delete-tests-" + Guid.NewGuid().ToString("N"));
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
