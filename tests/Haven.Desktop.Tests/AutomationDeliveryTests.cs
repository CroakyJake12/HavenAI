using Haven.Application;
using Haven.Automations;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed class AutomationDeliveryTests : IDisposable
{
    private readonly TemporaryPaths _paths = new();

    [Fact]
    public async Task DurableOutboxSurvivesNewInstanceAndDrainsExactlyOnce()
    {
        var delivery = new AutomationDelivery(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Release watch",
            AutomationDeliveryKind.ConditionMet,
            "Condition met: Release watch",
            "The release is available.",
            DateTimeOffset.UtcNow);

        await new AutomationDeliveryOutbox(_paths).EnqueueAsync(delivery, CancellationToken.None);
        var reopened = new AutomationDeliveryOutbox(_paths);
        var first = await reopened.DrainAsync(CancellationToken.None);
        var second = await reopened.DrainAsync(CancellationToken.None);

        Assert.Single(first);
        Assert.Equal(delivery, first[0]);
        Assert.Empty(second);
    }

    [Fact]
    public async Task SelectedConditionRunPersistsOneRunAndQueuesConditionMetDelivery()
    {
        var repository = new MemoryAutomationRepository();
        var outbox = new RecordingOutbox();
        var model = new FakeModelClient("{\"conditionMet\":true,\"report\":\"The release is available.\"}");
        var runner = new AutomationRunner(repository, model, new ScheduleCalculator(), outbox);
        var automation = Definition(AutomationScheduleKind.ConditionWatch, enabled: true);
        await repository.UpsertAsync(automation, CancellationToken.None);

        var run = await runner.RunOneAsync(automation, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(AutomationRunStatus.Succeeded, run.Status);
        Assert.Single(repository.Runs);
        var delivery = Assert.Single(outbox.Items);
        Assert.Equal(AutomationDeliveryKind.ConditionMet, delivery.Kind);
        Assert.Contains("release", delivery.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedSelectedRunRetriesThreeTimesAndQueuesOneFailureDelivery()
    {
        var repository = new MemoryAutomationRepository();
        var outbox = new RecordingOutbox();
        var model = new FakeModelClient("unused") { Failure = new IOException("Provider unavailable.") };
        var runner = new AutomationRunner(repository, model, new ScheduleCalculator(), outbox);
        var automation = Definition(AutomationScheduleKind.Daily, enabled: false);
        await repository.UpsertAsync(automation, CancellationToken.None);

        var run = await runner.RunOneAsync(automation, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(3, model.CompleteCount);
        Assert.Equal(AutomationRunStatus.Failed, run.Status);
        Assert.Null(repository.Definitions[automation.Id].NextRunAt);
        var delivery = Assert.Single(outbox.Items);
        Assert.Equal(AutomationDeliveryKind.Failed, delivery.Kind);
        Assert.Contains("Provider unavailable", delivery.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AutomationDefinition Definition(AutomationScheduleKind kind, bool enabled)
    {
        var now = DateTimeOffset.UtcNow;
        var schedule = AutomationScheduleComposer.Compose(
            kind,
            new AutomationScheduleDraft(now.AddHours(1), new TimeOnly(8, 0), DayOfWeek.Monday, 1, 60));
        return new AutomationDefinition(
            Guid.NewGuid(),
            "Release watch",
            HavenMode.Chat,
            "Check whether the release is available.",
            kind,
            schedule,
            enabled ? now.AddHours(1) : null,
            null,
            enabled,
            now,
            now);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class RecordingOutbox : IAutomationDeliveryOutbox
    {
        public List<AutomationDelivery> Items { get; } = [];
        public Task EnqueueAsync(AutomationDelivery delivery, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Items.Add(delivery);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<AutomationDelivery>> DrainAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomationDelivery>>([]);
    }

    private sealed class FakeModelClient(string response) : IOllamaClient
    {
        public Exception? Failure { get; init; }
        public int CompleteCount { get; private set; }
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([new ModelDescriptor(
                "qwen-test", 1, "qwen", "test", "test",
                new HashSet<ToolCapability> { ToolCapability.Text },
                DateTimeOffset.UtcNow)]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return response;
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            CompleteCount++;
            return Failure is null ? Task.FromResult(response) : Task.FromException<string>(Failure);
        }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(response, []));
    }

    private sealed class MemoryAutomationRepository : IAutomationRepository
    {
        private readonly HashSet<Guid> _leases = [];
        public Dictionary<Guid, AutomationDefinition> Definitions { get; } = [];
        public List<AutomationRun> Runs { get; } = [];

        public Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomationDefinition>>(Definitions.Values.ToArray());
        public Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomationDefinition>>(Definitions.Values
                .Where(item => item.IsEnabled && item.NextRunAt <= now)
                .ToArray());
        public Task UpsertAsync(AutomationDefinition automation, CancellationToken cancellationToken)
        {
            Definitions[automation.Id] = automation;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Definitions.Remove(id);
            return Task.CompletedTask;
        }
        public Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken cancellationToken) =>
            Task.FromResult(_leases.Add(automationId));
        public Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken)
        {
            Runs.Add(run);
            _leases.Remove(run.AutomationId);
            if (Definitions.TryGetValue(run.AutomationId, out var definition))
                Definitions[run.AutomationId] = definition with { NextRunAt = nextRunAt };
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid automationId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomationRun>>(Runs.Where(item => item.AutomationId == automationId).Take(limit).ToArray());
    }

    private sealed class TemporaryPaths : IAppPaths, IDisposable
    {
        public TemporaryPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-delivery-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
