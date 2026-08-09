using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class GenUiDestinationHandlerTests
{
    [Fact]
    public async Task AppEventHandlerRoutesToRegisteredApp()
    {
        var handler = new GenUiAppEventHandler();
        var called = false;
        handler.Register("write", (evt, binding, ct) =>
        {
            called = true;
            return Task.FromResult(GenerativeUiEventRouter.Result(
                evt, GenUiActionStatus.Completed, "Write handled the event."));
        });

        Assert.True(handler.CanHandle("write"));
        Assert.False(handler.CanHandle("nonexistent"));
        Assert.Equal(GenUiRouteKind.App, handler.RouteKind);

        var evt = CreateEvent();
        var binding = new GenUiActionBinding("edit", GenUiRouteKind.App, "write", CapabilityRiskClass.Low, false);
        var result = await handler.HandleAsync(evt, binding, CancellationToken.None);

        Assert.True(called);
        Assert.Equal(GenUiActionStatus.Completed, result.Status);
    }

    [Fact]
    public async Task AppEventHandlerReturnsUnavailableForUnregisteredApp()
    {
        var handler = new GenUiAppEventHandler();
        var evt = CreateEvent();
        var binding = new GenUiActionBinding("edit", GenUiRouteKind.App, "missing", CapabilityRiskClass.Low, false);

        var result = await handler.HandleAsync(evt, binding, CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Unavailable, result.Status);
    }

    [Fact]
    public void CapabilityEventHandlerRoutesToRegisteredCapability()
    {
        var handler = new GenUiCapabilityEventHandler();
        handler.Register("build", (evt, binding, ct) =>
            Task.FromResult(GenerativeUiEventRouter.Result(
                evt, GenUiActionStatus.Completed, "Build executed.")));

        Assert.True(handler.CanHandle("build"));
        Assert.Equal(GenUiRouteKind.Capability, handler.RouteKind);
    }

    [Fact]
    public void ExternalEventHandlerRoutesToRegisteredExternal()
    {
        var handler = new GenUiExternalEventHandler();
        handler.Register("spotify", (evt, binding, ct) =>
            Task.FromResult(GenerativeUiEventRouter.Result(
                evt, GenUiActionStatus.Completed, "Spotify action completed.")));

        Assert.True(handler.CanHandle("spotify"));
        Assert.Equal(GenUiRouteKind.External, handler.RouteKind);
    }

    [Fact]
    public void AgentEventHandlerDelegatesToFeedbackChannel()
    {
        var tracker = new GenUiAgentTaskTracker();
        var feedback = new DefaultGenUiAgentFeedbackChannel(tracker);
        var handler = new GenUiAgentEventHandler(feedback);

        Assert.True(handler.CanHandle("anything"));
        Assert.Equal(GenUiRouteKind.Agent, handler.RouteKind);
    }

    private static GenUiEvent CreateEvent() => new(
        Guid.NewGuid(),
        GenUiEventType.ActionInvoked,
        DateTimeOffset.UtcNow,
        new GenUiOrigin(Guid.NewGuid(), "chat", null, Guid.NewGuid()),
        "test.component",
        "test.action",
        null,
        null,
        null,
        JsonSerializer.SerializeToElement(new { }),
        GenUiEventSource.User,
        "Test interaction.");
}

public sealed class GenUiAgentFeedbackTests
{
    [Fact]
    public void AgentTaskTrackerUpdatesAndRetrievesState()
    {
        var tracker = new GenUiAgentTaskTracker();
        var taskId = Guid.NewGuid();
        var state = new GenUiAgentTaskState(
            taskId, Guid.NewGuid(), Guid.NewGuid(),
            GenUiAgentTaskPhase.Running, "Processing…", 45, null, DateTimeOffset.UtcNow);

        tracker.Update(state);

        Assert.Single(tracker.ActiveTasks);
        Assert.Equal(taskId, tracker.ActiveTasks[0].TaskId);
        Assert.Equal(GenUiAgentTaskPhase.Running, tracker.ActiveTasks[0].Phase);
    }

    [Fact]
    public void AgentTaskTrackerRemovesCompletedTasks()
    {
        var tracker = new GenUiAgentTaskTracker();
        var taskId = Guid.NewGuid();
        tracker.Update(new GenUiAgentTaskState(
            taskId, Guid.NewGuid(), Guid.NewGuid(),
            GenUiAgentTaskPhase.Completed, "Done", 100, null, DateTimeOffset.UtcNow));

        Assert.Empty(tracker.ActiveTasks);
    }

    [Fact]
    public void AgentTaskTrackerFiresStateChangedEvent()
    {
        var tracker = new GenUiAgentTaskTracker();
        GenUiAgentTaskState? received = null;
        tracker.TaskStateChanged += (_, s) => received = s;

        var state = new GenUiAgentTaskState(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            GenUiAgentTaskPhase.Preparing, "Starting…", 0, null, DateTimeOffset.UtcNow);
        tracker.Update(state);

        Assert.NotNull(received);
        Assert.Equal(GenUiAgentTaskPhase.Preparing, received!.Phase);
    }

    [Fact]
    public async Task FeedbackChannelQueuesAndCompletesEvents()
    {
        var tracker = new GenUiAgentTaskTracker();
        var channel = new DefaultGenUiAgentFeedbackChannel(tracker);
        var evt = CreateEvent();
        var binding = new GenUiActionBinding("explain", GenUiRouteKind.Agent, "agent", CapabilityRiskClass.Low, false);

        var tcs = new TaskCompletionSource<GenUiActionResult>();
        GenUiAgentFeedbackEntry? queued = null;
        channel.EventQueued += (_, entry) => queued = entry;

        var task = channel.SubmitEventAsync(evt, binding, CancellationToken.None);

        Assert.NotNull(queued);
        Assert.Single(tracker.ActiveTasks);

        var result = GenerativeUiEventRouter.Result(evt, GenUiActionStatus.Completed, "Explained.");
        queued!.CompletionSource!.SetResult(result);

        var actual = await task;
        Assert.Equal(GenUiActionStatus.Completed, actual.Status);
    }

    [Fact]
    public void FeedbackChannelDequeueReturnsNullWhenEmpty()
    {
        var tracker = new GenUiAgentTaskTracker();
        var channel = new DefaultGenUiAgentFeedbackChannel(tracker);

        Assert.Null(channel.TryDequeue());
    }

    private static GenUiEvent CreateEvent() => new(
        Guid.NewGuid(),
        GenUiEventType.ActionInvoked,
        DateTimeOffset.UtcNow,
        new GenUiOrigin(Guid.NewGuid(), "chat", null, Guid.NewGuid()),
        "test.component",
        "test.action",
        null,
        null,
        null,
        JsonSerializer.SerializeToElement(new { }),
        GenUiEventSource.User,
        "Test interaction.");
}

public sealed class GenUiIncrementalUpdaterTests
{
    [Fact]
    public void PatchStateUpdatesDocumentState()
    {
        var store = new GenUiInstanceStore();
        var document = CreateDocument();
        store.Register(document);

        var updater = new GenUiIncrementalUpdater(store);
        var change = new GenUiIncrementalChange(
            Guid.NewGuid(), document.Origin.InstanceId,
            GenUiIncrementalOperation.PatchState, null, null, null, null,
            "score", JsonSerializer.SerializeToElement(42), null, null, DateTimeOffset.UtcNow);

        var applied = updater.Apply(change);
        Assert.True(applied);
        Assert.Equal(42, store.TryGet(document.Origin.InstanceId)!.State["score"].GetInt32());
    }

    [Fact]
    public void UpdatePropertiesChangesComponentProperty()
    {
        var store = new GenUiInstanceStore();
        var document = CreateDocument();
        store.Register(document);

        var updater = new GenUiIncrementalUpdater(store);
        var change = new GenUiIncrementalChange(
            Guid.NewGuid(), document.Origin.InstanceId,
            GenUiIncrementalOperation.UpdateProperties, "test.button", null, null, null,
            "label", JsonSerializer.SerializeToElement("Updated"), null, null, DateTimeOffset.UtcNow);

        var applied = updater.Apply(change);
        Assert.True(applied);
    }

    [Fact]
    public void DismissSurfaceRemovesInstance()
    {
        var store = new GenUiInstanceStore();
        var document = CreateDocument();
        store.Register(document);

        var updater = new GenUiIncrementalUpdater(store);
        var change = new GenUiIncrementalChange(
            Guid.NewGuid(), document.Origin.InstanceId,
            GenUiIncrementalOperation.DismissSurface, null, null, null, null,
            null, null, null, null, DateTimeOffset.UtcNow);

        var applied = updater.Apply(change);
        Assert.True(applied);
        Assert.Null(store.TryGet(document.Origin.InstanceId));
    }

    [Fact]
    public void ApplyBatchProcessesMultipleChanges()
    {
        var store = new GenUiInstanceStore();
        var document = CreateDocument();
        store.Register(document);

        var updater = new GenUiIncrementalUpdater(store);
        var now = DateTimeOffset.UtcNow;
        var changes = new[]
        {
            new GenUiIncrementalChange(Guid.NewGuid(), document.Origin.InstanceId,
                GenUiIncrementalOperation.PatchState, null, null, null, null,
                "score", JsonSerializer.SerializeToElement(10), null, null, now),
            new GenUiIncrementalChange(Guid.NewGuid(), document.Origin.InstanceId,
                GenUiIncrementalOperation.PatchState, null, null, null, null,
                "level", JsonSerializer.SerializeToElement(2), null, null, now)
        };

        var results = updater.ApplyBatch(changes);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r));
    }

    [Fact]
    public void ChangeAppliedEventFires()
    {
        var store = new GenUiInstanceStore();
        var document = CreateDocument();
        store.Register(document);

        var updater = new GenUiIncrementalUpdater(store);
        GenUiIncrementalChange? received = null;
        updater.ChangeApplied += (_, c) => received = c;

        var change = new GenUiIncrementalChange(
            Guid.NewGuid(), document.Origin.InstanceId,
            GenUiIncrementalOperation.SetProgress, "test.progress", null, null, null,
            null, null, null, 75, DateTimeOffset.UtcNow);

        updater.Apply(change);
        Assert.NotNull(received);
    }

    private static GenUiDocument CreateDocument()
    {
        var origin = new GenUiOrigin(Guid.NewGuid(), "chat", null, Guid.NewGuid());
        var root = new GenUiComponent(
            "test.workspace", "HavenWorkspace",
            new Dictionary<string, JsonElement>(),
            [],
            [
                new GenUiComponent("test.button", "HavenButton",
                    new Dictionary<string, JsonElement>
                    {
                        ["label"] = JsonSerializer.SerializeToElement("Click")
                    },
                    [new GenUiActionBinding("click", GenUiRouteKind.Local, "local.click", CapabilityRiskClass.Low, false)],
                    []),
                new GenUiComponent("test.progress", "HavenProgress",
                    new Dictionary<string, JsonElement>
                    {
                        ["value"] = JsonSerializer.SerializeToElement(0)
                    },
                    [], [])
            ]);
        return new GenUiDocument(
            Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion,
            origin, "Test", "chat", root,
            new Dictionary<string, JsonElement>
            {
                ["score"] = JsonSerializer.SerializeToElement(0),
                ["level"] = JsonSerializer.SerializeToElement(1)
            },
            DateTimeOffset.UtcNow);
    }
}

public sealed class GenUiLiveActivityTests
{
    [Fact]
    public void LiveActivitySurfaceTracksPhaseAndProgress()
    {
        var store = new GenUiInstanceStore();
        var surface = new DefaultGenUiLiveActivitySurface(
            Guid.NewGuid(), Guid.NewGuid(), "write", "Editing document", store);

        Assert.Equal(GenUiLiveActivityPhase.Preparing, surface.Phase);
        Assert.Equal(0, surface.Progress);

        surface.Update(new GenUiLiveActivityUpdate(
            GenUiLiveActivityPhase.Operating, "Editing…", 50, null, null, DateTimeOffset.UtcNow));

        Assert.Equal(GenUiLiveActivityPhase.Operating, surface.Phase);
        Assert.Equal(50, surface.Progress);
        Assert.Equal("Editing…", surface.StatusMessage);
    }

    [Fact]
    public void LiveActivitySurfaceFiresStateChangedEvent()
    {
        var store = new GenUiInstanceStore();
        var surface = new DefaultGenUiLiveActivitySurface(
            Guid.NewGuid(), Guid.NewGuid(), "write", "Test", store);
        var fired = false;
        surface.StateChanged += (_, _) => fired = true;

        surface.Update(new GenUiLiveActivityUpdate(
            GenUiLiveActivityPhase.Operating, "Working…", 25, null, null, DateTimeOffset.UtcNow));

        Assert.True(fired);
    }

    [Fact]
    public void LiveActivityCancelSetsPhase()
    {
        var store = new GenUiInstanceStore();
        var surface = new DefaultGenUiLiveActivitySurface(
            Guid.NewGuid(), Guid.NewGuid(), "write", "Test", store);

        surface.Cancel();
        Assert.Equal(GenUiLiveActivityPhase.Cancelled, surface.Phase);
    }

    [Fact]
    public void LiveActivityDismissSetsPhase()
    {
        var store = new GenUiInstanceStore();
        var surface = new DefaultGenUiLiveActivitySurface(
            Guid.NewGuid(), Guid.NewGuid(), "write", "Test", store);

        surface.Dismiss();
        Assert.Equal(GenUiLiveActivityPhase.Dismissed, surface.Phase);
    }

    [Fact]
    public void LiveActivityTrackerTracksAndFilters()
    {
        var tracker = new GenUiLiveActivityTracker();
        var store = new GenUiInstanceStore();
        var threadId = Guid.NewGuid();

        var surface1 = new DefaultGenUiLiveActivitySurface(
            Guid.NewGuid(), threadId, "write", "Doc 1", store);
        var surface2 = new DefaultGenUiLiveActivitySurface(
            Guid.NewGuid(), threadId, "browse", "Research", store);

        tracker.Track(surface1);
        tracker.Track(surface2);

        Assert.Equal(2, tracker.ActiveActivities.Count);
        Assert.Equal(2, tracker.GetForThread(threadId).Count);
    }

    [Fact]
    public void LiveActivityTrackerFiresEvents()
    {
        var tracker = new GenUiLiveActivityTracker();
        var store = new GenUiInstanceStore();
        IGenUiLiveActivitySurface? created = null;
        tracker.ActivityCreated += (_, s) => created = s;

        var surface = new DefaultGenUiLiveActivitySurface(
            Guid.NewGuid(), Guid.NewGuid(), "write", "Test", store);
        tracker.Track(surface);

        Assert.NotNull(created);
        Assert.Equal(surface.ActivityId, created!.ActivityId);
    }
}
