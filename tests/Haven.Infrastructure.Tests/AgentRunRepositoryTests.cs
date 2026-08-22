using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class AgentRunRepositoryTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task Runs_round_trip_update_and_filter_by_agent()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var catalog = new CatalogRepository(database);
        var agent = new AgentDefinition(
            Guid.NewGuid(), "Run test", "Tests run persistence", "Do the task.", "agent-test",
            "model-a", null, string.Empty, """{"capabilities":["web-search"]}""",
            false, true, DateTimeOffset.UtcNow);
        await catalog.UpsertAgentAsync(agent, CancellationToken.None);

        var repository = new AgentRunRepository(database);
        var now = DateTimeOffset.UtcNow;
        var run = new AgentRun(
            Guid.NewGuid(), agent.Id, agent.Name, "Check sources", AgentRunStatus.Running, "model-a",
            "partial", string.Empty, """["web-search"]""", """[{"title":"Search"}]""",
            now, now, null, null, "https://example.test/source", 35);

        await repository.UpsertAsync(run, CancellationToken.None);
        var stored = Assert.IsType<AgentRun>(await repository.GetAsync(run.Id, CancellationToken.None));
        Assert.Equal(AgentRunStatus.Running, stored.Status);
        Assert.Equal("partial", stored.Result);
        Assert.Equal(agent.Id, stored.AgentId);
        Assert.Equal("https://example.test/source", stored.ResourceReference);
        Assert.Equal(35, stored.ProgressPercent);

        var completed = run with
        {
            Status = AgentRunStatus.Completed,
            Result = "done",
            CompletedAt = now.AddSeconds(2),
            ProgressPercent = 100
        };
        await repository.UpsertAsync(completed, CancellationToken.None);

        var recent = await repository.GetRecentAsync(10, CancellationToken.None);
        Assert.Equal("done", Assert.Single(recent).Result);
        Assert.Equal(100, Assert.Single(recent).ProgressPercent);
        var byAgent = await repository.GetByAgentAsync(agent.Id, 10, CancellationToken.None);
        Assert.Equal(run.Id, Assert.Single(byAgent).Id);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : Haven.Application.IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-agent-run-tests-" + Guid.NewGuid().ToString("N"));
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
