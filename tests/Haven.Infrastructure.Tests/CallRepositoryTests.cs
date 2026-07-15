using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class CallRepositoryTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task CallMetadataRoundTripsWithoutMediaColumns()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        await using (var connection = await database.OpenAsync(CancellationToken.None))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS call_sessions(
                  id TEXT PRIMARY KEY,
                  conversation_id TEXT NOT NULL UNIQUE REFERENCES conversations(id) ON DELETE CASCADE,
                  model_name TEXT NOT NULL,
                  input_device_id TEXT NULL,
                  output_device_id TEXT NULL,
                  voice_name TEXT NULL,
                  input_mode INTEGER NOT NULL DEFAULT 0 CHECK(input_mode IN (0,1)),
                  used_screen_share INTEGER NOT NULL DEFAULT 0 CHECK(used_screen_share IN (0,1)),
                  status INTEGER NOT NULL DEFAULT 0 CHECK(status IN (0,1,2,3)),
                  started_at TEXT NOT NULL,
                  ended_at TEXT NULL,
                  error TEXT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(
            Guid.NewGuid(), HavenMode.Chat, ConversationKind.Call, "Test call",
            null, null, false, false, now, now);
        await new ConversationRepository(database).UpsertConversationAsync(conversation, CancellationToken.None);
        var repository = new CallRepository(database);
        var session = new CallSession(
            Guid.NewGuid(), conversation.Id, "qwen-test", "mic", "speaker", "voice",
            CallInputMode.PushToTalk, true, CallSessionStatus.Active, now);
        await repository.UpsertAsync(session, CancellationToken.None);
        var completed = session with
        {
            Status = CallSessionStatus.Completed,
            EndedAt = now.AddMinutes(3)
        };
        await repository.UpsertAsync(completed, CancellationToken.None);

        Assert.Equal(completed, await repository.GetAsync(session.Id, CancellationToken.None));
        Assert.Equal(session.Id, Assert.Single(await repository.GetRecentAsync(10, CancellationToken.None)).Id);

        await using var verifyConnection = await database.OpenAsync(CancellationToken.None);
        await using var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = "PRAGMA table_info(call_sessions);";
        var columns = new List<string>();
        await using var reader = await verifyCommand.ExecuteReaderAsync(CancellationToken.None);
        while (await reader.ReadAsync(CancellationToken.None)) columns.Add(reader.GetString(1));
        Assert.DoesNotContain("audio", columns);
        Assert.DoesNotContain("frame", columns);
        Assert.DoesNotContain("transcript", columns);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-call-db-tests-" + Guid.NewGuid().ToString("N"));
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
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (IOException) { }
        }
    }
}
