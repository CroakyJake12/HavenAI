using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed class ScheduledGraphRuntimeTests
{
    [Fact]
    public async Task Scheduled_pure_graph_executes_without_legacy_model_instruction_path()
    {
        var schedule = Node("Schedule", "Recurrence", ("recurrence", "hourly"));
        var condition = Node("Condition", "Condition", ("expression", "true"));
        var graph = new AutomationGraphDefinition(1, [schedule, condition], [new AutomationGraphEdgeDefinition(schedule.Id, condition.Id)]);
        var repository = new MemoryAutomationRepository();
        var model = new FakeModelClient("legacy path must not run");
        var runner = new ScheduledTaskRunner(repository, model, new ScheduledTaskScheduleCalculator());
        var task = Definition(
            AutomationScheduleKind.Hourly,
            ScheduledGraphAutomationPayloadCodec.Serialize(Guid.NewGuid(), schedule.Id, "Graph workflow", AutomationGraphCodec.Serialize(graph), null));
        await repository.UpsertAsync(task, CancellationToken.None);

        var run = await runner.RunOneAsync(task, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(AutomationRunStatus.Succeeded, run.Status);
        Assert.Equal(0, model.CompleteCount);
        Assert.Contains("graphExecuted\":true", run.Result ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tracedNodes\":2", run.Result ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task False_condition_watch_does_not_enter_downstream_graph()
    {
        var watch = Node("ConditionWatch", "Condition watch", ("watch", "release exists"));
        var unsupported = Node("App", "Would fail if executed");
        var graph = new AutomationGraphDefinition(1, [watch, unsupported], [new AutomationGraphEdgeDefinition(watch.Id, unsupported.Id)]);
        var repository = new MemoryAutomationRepository();
        var model = new FakeModelClient("{\"conditionMet\":false,\"report\":\"No release yet.\"}");
        var runner = new ScheduledTaskRunner(repository, model, new ScheduledTaskScheduleCalculator());
        var task = Definition(
            AutomationScheduleKind.ConditionWatch,
            ScheduledGraphAutomationPayloadCodec.Serialize(Guid.NewGuid(), watch.Id, "Release watch", AutomationGraphCodec.Serialize(graph), "release exists"));
        await repository.UpsertAsync(task, CancellationToken.None);

        var run = await runner.RunOneAsync(task, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(AutomationRunStatus.Succeeded, run.Status);
        Assert.Equal(1, model.CompleteCount);
        Assert.Contains("graphExecuted\":false", run.Result ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No release yet", run.Result ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Malformed_scheduled_graph_payload_fails_once_without_instruction_fallback()
    {
        var repository = new MemoryAutomationRepository();
        var model = new FakeModelClient("legacy path must not run");
        var runner = new ScheduledTaskRunner(repository, model, new ScheduledTaskScheduleCalculator());
        var task = Definition(AutomationScheduleKind.Daily, "haven:scheduled-graph:v1:{not-json");
        await repository.UpsertAsync(task, CancellationToken.None);

        var run = await runner.RunOneAsync(task, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(AutomationRunStatus.Failed, run.Status);
        Assert.Equal(0, model.CompleteCount);
        Assert.Contains("1 attempt", run.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid", run.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static AutomationDefinition Definition(AutomationScheduleKind kind, string instruction)
    {
        var now = DateTimeOffset.UtcNow;
        var schedule = ScheduledTaskScheduleComposer.Compose(kind, new ScheduledTaskScheduleDraft(now.AddHours(1), new TimeOnly(8, 0), DayOfWeek.Monday, 1, 60));
        return new AutomationDefinition(Guid.NewGuid(), "Scheduled graph", HavenMode.Tasks, instruction, kind, schedule, null, null, false, now, now);
    }

    private static AutomationGraphNodeDefinition Node(string category, string title, params (string Key, string Value)[] parameters) =>
        new(Guid.NewGuid(), category, null, null, parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase)) { Title = title };

    private sealed class FakeModelClient(string response) : IOllamaClient
    {
        public int CompleteCount { get; private set; }
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([new ModelDescriptor(
            "qwen-test", 1, "qwen", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow)]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return response;
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            CompleteCount++;
            return Task.FromResult(response);
        }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) => Task.FromResult(new OllamaToolResponse(response, []));
    }

    private sealed class MemoryAutomationRepository : IAutomationRepository
    {
        private readonly HashSet<Guid> _leases = [];
        private readonly Dictionary<Guid, AutomationDefinition> _definitions = [];
        public Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AutomationDefinition>>(_definitions.Values.ToArray());
        public Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AutomationDefinition>>([]);
        public Task UpsertAsync(AutomationDefinition automation, CancellationToken cancellationToken) { _definitions[automation.Id] = automation; return Task.CompletedTask; }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) { _definitions.Remove(id); return Task.CompletedTask; }
        public Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken cancellationToken) => Task.FromResult(_leases.Add(automationId));
        public Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken) { _leases.Remove(run.AutomationId); return Task.CompletedTask; }
        public Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid automationId, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AutomationRun>>([]);
    }
}
