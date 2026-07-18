/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/AutomationDeliveryTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns AutomationDeliveryTests, RecordingOutbox, FakeModelClient, MemoryAutomationRepository, TemporaryPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Automations;
using Haven.Core;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents automation delivery tests and keeps its related state and behavior together.
/// </summary>
public sealed class AutomationDeliveryTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TemporaryPaths _paths = new();

    /// <summary>
    /// Performs the durable outbox survives new instance and drains exactly once step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the selected condition run persists one run and queues condition met delivery step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the failed selected run retries three times and queues one failure delivery step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the definition step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents recording outbox and keeps its related state and behavior together.
    /// </summary>
    private sealed class RecordingOutbox : IAutomationDeliveryOutbox
    {
        /// <summary>
        /// Gets or updates items, the bindable or domain state represented by this property.
        /// </summary>
        public List<AutomationDelivery> Items { get; } = [];
        /// <summary>
        /// Performs enqueue async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task EnqueueAsync(AutomationDelivery delivery, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Items.Add(delivery);
            return Task.CompletedTask;
        }
        /// <summary>
        /// Performs drain async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<IReadOnlyList<AutomationDelivery>> DrainAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomationDelivery>>([]);
    }

    /// <summary>
    /// Represents fake model client and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeModelClient(string response) : IOllamaClient
    {
        /// <summary>
        /// Gets or updates failure, the bindable or domain state represented by this property.
        /// </summary>
        public Exception? Failure { get; init; }
        /// <summary>
        /// Gets or updates complete count, the bindable or domain state represented by this property.
        /// </summary>
        public int CompleteCount { get; private set; }
        /// <summary>
        /// Reports whether is available async is true for the current state.
        /// </summary>
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        /// <summary>
        /// Retrieves models async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([new ModelDescriptor(
                "qwen-test", 1, "qwen", "test", "test",
                new HashSet<ToolCapability> { ToolCapability.Text },
                DateTimeOffset.UtcNow)]);
        /// <summary>
        /// Performs stream chat async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return response;
        }
        /// <summary>
        /// Performs complete async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            CompleteCount++;
            return Failure is null ? Task.FromResult(response) : Task.FromException<string>(Failure);
        }
        /// <summary>
        /// Performs chat with tools async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(response, []));
    }

    /// <summary>
    /// Represents memory automation repository and keeps its related state and behavior together.
    /// </summary>
    private sealed class MemoryAutomationRepository : IAutomationRepository
    {
        /// <summary>
        /// Stores leases locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly HashSet<Guid> _leases = [];
        /// <summary>
        /// Gets or updates definitions, the bindable or domain state represented by this property.
        /// </summary>
        public Dictionary<Guid, AutomationDefinition> Definitions { get; } = [];
        /// <summary>
        /// Runs runs while preserving the surrounding cancellation and error-handling contract.
        /// </summary>
        public List<AutomationRun> Runs { get; } = [];

        /// <summary>
        /// Retrieves all async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomationDefinition>>(Definitions.Values.ToArray());
        /// <summary>
        /// Retrieves due async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomationDefinition>>(Definitions.Values
                .Where(item => item.IsEnabled && item.NextRunAt <= now)
                .ToArray());
        /// <summary>
        /// Performs upsert async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task UpsertAsync(AutomationDefinition automation, CancellationToken cancellationToken)
        {
            Definitions[automation.Id] = automation;
            return Task.CompletedTask;
        }
        /// <summary>
        /// Performs delete async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Definitions.Remove(id);
            return Task.CompletedTask;
        }
        /// <summary>
        /// Attempts to acquire lease async and reports the result without using failure for normal control flow.
        /// </summary>
        public Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken cancellationToken) =>
            Task.FromResult(_leases.Add(automationId));
        /// <summary>
        /// Performs complete run async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken)
        {
            Runs.Add(run);
            _leases.Remove(run.AutomationId);
            if (Definitions.TryGetValue(run.AutomationId, out var definition))
                Definitions[run.AutomationId] = definition with { NextRunAt = nextRunAt };
            return Task.CompletedTask;
        }
        /// <summary>
        /// Retrieves runs async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid automationId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AutomationRun>>(Runs.Where(item => item.AutomationId == automationId).Take(limit).ToArray());
    }

    /// <summary>
    /// Represents temporary paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class TemporaryPaths : IAppPaths, IDisposable
    {
        public TemporaryPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-delivery-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }
        /// <summary>
        /// Gets or updates data directory, the bindable or domain state represented by this property.
        /// </summary>
        public string DataDirectory { get; }
        /// <summary>
        /// Gets or updates database path, the bindable or domain state represented by this property.
        /// </summary>
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        /// <summary>
        /// Gets or updates browser profile directory, the bindable or domain state represented by this property.
        /// </summary>
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        /// <summary>
        /// Gets or updates attachments directory, the bindable or domain state represented by this property.
        /// </summary>
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        /// <summary>
        /// Gets or updates logs directory, the bindable or domain state represented by this property.
        /// </summary>
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        /// <summary>
        /// Gets or updates legacy state path, the bindable or domain state represented by this property.
        /// </summary>
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
