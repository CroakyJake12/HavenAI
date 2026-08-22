using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed class AgentTaskRuntimeServiceTests
{
    [Fact]
    public async Task Recent_runs_recover_stale_inflight_work_after_restart()
    {
        var stale = new AgentRun(
            Guid.NewGuid(), Guid.NewGuid(), "Research Agent", "Check source",
            AgentRunStatus.Running, "model", string.Empty, string.Empty, "[]", "[]",
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(-5), null,
            null, "haven://document/123", 40);
        var repository = new FakeAgentRunRepository(stale);
        var runtime = new AgentTaskRuntimeService(null!, repository, null!, null!, null!, null!);

        var recent = await runtime.GetRecentAsync(8, CancellationToken.None);

        var recovered = Assert.Single(recent);
        Assert.Equal(AgentRunStatus.Failed, recovered.Status);
        Assert.Equal(40, recovered.ProgressPercent);
        Assert.Equal("haven://document/123", recovered.ResourceReference);
        Assert.Contains("Interrupted", recovered.Error, StringComparison.Ordinal);
        Assert.NotNull(recovered.CompletedAt);
    }

    private sealed class FakeAgentRunRepository(params AgentRun[] seed) : IAgentRunRepository
    {
        private readonly Dictionary<Guid, AgentRun> _runs = seed.ToDictionary(run => run.Id);

        public Task UpsertAsync(AgentRun run, CancellationToken cancellationToken)
        {
            _runs[run.Id] = run;
            return Task.CompletedTask;
        }

        public Task<AgentRun?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_runs.GetValueOrDefault(id));

        public Task<IReadOnlyList<AgentRun>> GetRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentRun>>(_runs.Values.OrderByDescending(run => run.CreatedAt).Take(limit).ToArray());

        public Task<IReadOnlyList<AgentRun>> GetByAgentAsync(Guid agentId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentRun>>(_runs.Values.Where(run => run.AgentId == agentId).Take(limit).ToArray());
    }
}
