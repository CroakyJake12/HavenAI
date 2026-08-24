using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Structured feedback channel connecting GenUI events back to the active
/// Haven agent. Events that require reasoning, generation, adaptation or
/// interpretation flow through this interface rather than being flattened
/// into synthetic user chat messages.
/// </summary>
public interface IGenUiAgentFeedbackChannel
{
    /// <summary>
    /// Submits a semantic event that requires agent reasoning.
    /// Returns a result once the agent has processed the event.
    /// </summary>
    Task<GenUiActionResult> SubmitEventAsync(
        GenUiEvent semanticEvent,
        GenUiActionBinding binding,
        CancellationToken cancellationToken);

    /// <summary>
    /// Submits a semantic event for background processing without blocking
    /// the UI. The result arrives asynchronously through the registered callback.
    /// </summary>
    void SubmitEventBackground(
        GenUiEvent semanticEvent,
        GenUiActionBinding binding,
        Action<GenUiActionResult>? callback = null);
}

/// <summary>
/// Represents the current state of an active agent task that originated
/// from a GenUI interaction. Provides progress, status and result feedback
/// to the originating generated surface.
/// </summary>
public sealed record GenUiAgentTaskState(
    Guid TaskId,
    Guid EventId,
    Guid InstanceId,
    GenUiAgentTaskPhase Phase,
    string StatusMessage,
    double ProgressPercentage,
    JsonElement? IntermediateResult,
    DateTimeOffset UpdatedAt);

public enum GenUiAgentTaskPhase
{
    Queued,
    Preparing,
    Running,
    WaitingForPermission,
    WaitingForInput,
    Paused,
    Delegated,
    Retrying,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Tracks active agent tasks originating from GenUI interactions so
/// surfaces can display real progress/state rather than appearing frozen.
/// </summary>
public sealed class GenUiAgentTaskTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, GenUiAgentTaskState> _tasks = new();

    public event EventHandler<GenUiAgentTaskState>? TaskStateChanged;

    public IReadOnlyList<GenUiAgentTaskState> ActiveTasks
    {
        get
        {
            lock (_gate) return _tasks.Values.Where(t => t.Phase is not (GenUiAgentTaskPhase.Completed or GenUiAgentTaskPhase.Failed or GenUiAgentTaskPhase.Cancelled)).ToArray();
        }
    }

    public GenUiAgentTaskState? TryGet(Guid taskId)
    {
        lock (_gate) return _tasks.TryGetValue(taskId, out var state) ? state : null;
    }

    public void Update(GenUiAgentTaskState state)
    {
        lock (_gate) _tasks[state.TaskId] = state;
        TaskStateChanged?.Invoke(this, state);
    }

    public void Remove(Guid taskId)
    {
        lock (_gate) _tasks.Remove(taskId);
    }
}

/// <summary>
/// Default implementation that queues agent events and tracks their lifecycle.
/// The active agent loop consumes queued events through its normal context/tool
/// cycle rather than receiving raw UI messages.
/// </summary>
public sealed class DefaultGenUiAgentFeedbackChannel : IGenUiAgentFeedbackChannel
{
    private readonly GenUiAgentTaskTracker _tracker;
    private readonly ConcurrentQueue<GenUiAgentFeedbackEntry> _queue = new();

    public DefaultGenUiAgentFeedbackChannel(GenUiAgentTaskTracker tracker) => _tracker = tracker;

    public event EventHandler<GenUiAgentFeedbackEntry>? EventQueued;

    public async Task<GenUiActionResult> SubmitEventAsync(
        GenUiEvent semanticEvent,
        GenUiActionBinding binding,
        CancellationToken cancellationToken)
    {
        var taskId = Guid.NewGuid();
        var entry = new GenUiAgentFeedbackEntry(taskId, semanticEvent, binding, null);
        _queue.Enqueue(entry);
        EventQueued?.Invoke(this, entry);

        _tracker.Update(new GenUiAgentTaskState(
            taskId, semanticEvent.EventId, semanticEvent.Origin.InstanceId,
            GenUiAgentTaskPhase.Queued, "Waiting for agent…", 0, null, DateTimeOffset.UtcNow));

        var tcs = new TaskCompletionSource<GenUiActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        entry.CompletionSource = tcs;

        using var registration = cancellationToken.Register(() =>
        {
            tcs.TrySetResult(GenerativeUiEventRouter.Result(
                semanticEvent, GenUiActionStatus.Cancelled, "The agent request was cancelled."));
            _tracker.Update(new GenUiAgentTaskState(
                taskId, semanticEvent.EventId, semanticEvent.Origin.InstanceId,
                GenUiAgentTaskPhase.Cancelled, "Cancelled", 0, null, DateTimeOffset.UtcNow));
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    public void SubmitEventBackground(
        GenUiEvent semanticEvent,
        GenUiActionBinding binding,
        Action<GenUiActionResult>? callback = null)
    {
        var taskId = Guid.NewGuid();
        var entry = new GenUiAgentFeedbackEntry(taskId, semanticEvent, binding, callback);
        _queue.Enqueue(entry);
        EventQueued?.Invoke(this, entry);

        _tracker.Update(new GenUiAgentTaskState(
            taskId, semanticEvent.EventId, semanticEvent.Origin.InstanceId,
            GenUiAgentTaskPhase.Queued, "Queued for agent…", 0, null, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Called by the agent loop to consume the next pending GenUI event.
    /// Returns null if the queue is empty.
    /// </summary>
    public GenUiAgentFeedbackEntry? TryDequeue()
    {
        _queue.TryDequeue(out var entry);
        return entry;
    }

    /// <summary>
    /// Completes a pending event with the agent's result.
    /// </summary>
    public void Complete(Guid taskId, GenUiActionResult result)
    {
        if (result.Status is GenUiActionStatus.Completed)
            _tracker.Update(new GenUiAgentTaskState(
                taskId, result.EventId, result.Origin.InstanceId,
                GenUiAgentTaskPhase.Completed, result.Summary, 100, result.StructuredResult, DateTimeOffset.UtcNow));
        else
            _tracker.Update(new GenUiAgentTaskState(
                taskId, result.EventId, result.Origin.InstanceId,
                GenUiAgentTaskPhase.Failed, result.Summary, 0, result.StructuredResult, DateTimeOffset.UtcNow));
    }
}

public sealed class GenUiAgentFeedbackEntry(
    Guid TaskId,
    GenUiEvent SemanticEvent,
    GenUiActionBinding Binding,
    Action<GenUiActionResult>? Callback)
{
    public Guid TaskId { get; } = TaskId;
    public GenUiEvent SemanticEvent { get; } = SemanticEvent;
    public GenUiActionBinding Binding { get; } = Binding;
    public Action<GenUiActionResult>? Callback { get; } = Callback;
    public TaskCompletionSource<GenUiActionResult>? CompletionSource { get; set; }
}
