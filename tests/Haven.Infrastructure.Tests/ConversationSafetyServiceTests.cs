using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure.Tests;

public sealed class ConversationSafetyServiceTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task ThreeDistinctConfirmedFlagsLockAtomicallyAndDuplicatesDoNotCount()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var repository = new ConversationRepository(database);
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Safety test", null, null, false, false, now, now);
        await repository.UpsertConversationAsync(conversation, CancellationToken.None);
        var service = new ConversationSafetyService(database);
        var eventIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await Task.WhenAll(Enumerable.Range(0, 30).Select(index =>
            service.RecordConfirmedFlagAsync(
                conversation.Id,
                new ConfirmedSafetyFlag(eventIds[index % 3], "test-classifier", "confirmed-harm", new string((char)('a' + index % 3), 64), now.AddMilliseconds(index)),
                CancellationToken.None)));

        var snapshot = await service.GetSnapshotAsync(conversation.Id, CancellationToken.None);
        Assert.Equal(ConversationSafetyState.Locked, snapshot.State);
        Assert.Equal(3, snapshot.ConfirmedCount);
        Assert.Equal(3, snapshot.Version);
        Assert.NotNull(snapshot.LockedAt);

        var duplicate = await service.RecordConfirmedFlagAsync(
            conversation.Id,
            new ConfirmedSafetyFlag(eventIds[0], "test-classifier", "confirmed-harm", new string('a', 64), now),
            CancellationToken.None);
        Assert.False(duplicate.Added);
        Assert.False(duplicate.LockedNow);
        Assert.Equal(3, duplicate.Snapshot.ConfirmedCount);
        Assert.Equal(3, duplicate.Snapshot.Version);

        var restarted = new ConversationSafetyService(database);
        Assert.Equal(snapshot, await restarted.GetSnapshotAsync(conversation.Id, CancellationToken.None));
        await Assert.ThrowsAsync<ConversationSafetyLockException>(
            () => restarted.EnsureMayActAsync(conversation.Id, "chat.send", CancellationToken.None));
    }

    [Fact]
    public async Task LockedConversationBlocksRepositoryAndDirectSqlMutationButAllowsErasure()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var repository = new ConversationRepository(database);
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Safety test", null, null, false, false, now, now);
        await repository.UpsertConversationAsync(conversation, CancellationToken.None);
        var service = new ConversationSafetyService(database);
        for (var index = 0; index < 3; index++)
            await service.RecordConfirmedFlagAsync(
                conversation.Id,
                new ConfirmedSafetyFlag(Guid.NewGuid(), "test", "confirmed", new string((char)('d' + index), 64), now),
                CancellationToken.None);

        var repositoryFailure = await Assert.ThrowsAsync<SqliteException>(() =>
            repository.AddMessageAsync(
                new ChatMessage(Guid.NewGuid(), conversation.Id, MessageRole.User, "blocked", null, null, null, now),
                CancellationToken.None));
        Assert.Contains("CONVERSATION_SAFETY_LOCKED", repositoryFailure.Message, StringComparison.Ordinal);

        await using (var connection = await database.OpenAsync(CancellationToken.None))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE conversations SET title='bypass' WHERE id=$id;";
            command.Parameters.AddWithValue("$id", conversation.Id.ToString());
            var directFailure = await Assert.ThrowsAsync<SqliteException>(
                () => command.ExecuteNonQueryAsync(CancellationToken.None));
            Assert.Contains("CONVERSATION_SAFETY_LOCKED", directFailure.Message, StringComparison.Ordinal);
        }

        await repository.DeleteConversationAsync(conversation.Id, CancellationToken.None);
        Assert.Null(await repository.GetAsync(conversation.Id, CancellationToken.None));
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-safety-tests-" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
            Directory.CreateDirectory(DataDirectory);
        }
        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
