using System.Collections.Concurrent;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Coordinates live steering and durable follow-up work for any Haven execution surface.
/// Persisted state is authoritative; cancellation delegates are intentionally process-local.
/// </summary>
public sealed class TaskExecutionCoordinator(
    ITaskExecutionRepository repository,
    IExecutionEventSink events,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<(Guid TaskId, Guid ActionId), CancellationTokenSource> _cancellableActions = new();

    public TaskFollowUpMode InferMode(string instruction)
    {
        var value = instruction.Trim().ToLowerInvariant();
        if (value.StartsWith("after that", StringComparison.Ordinal) ||
            value.StartsWith("afterwards", StringComparison.Ordinal) ||
            value.StartsWith("when you're done", StringComparison.Ordinal) ||
            value.StartsWith("when you are done", StringComparison.Ordinal) ||
            value.StartsWith("once you're done", StringComparison.Ordinal) ||
            value.StartsWith("then ", StringComparison.Ordinal)) return TaskFollowUpMode.Queue;

        if (value.StartsWith("actually", StringComparison.Ordinal) ||
            value.StartsWith("instead", StringComparison.Ordinal) ||
            value.StartsWith("don't ", StringComparison.Ordinal) ||
            value.StartsWith("do not ", StringComparison.Ordinal) ||
            value.StartsWith("skip ", StringComparison.Ordinal) ||
            value.StartsWith("stop doing", StringComparison.Ordinal) ||
            value.StartsWith("use the existing", StringComparison.Ordinal) ||
            value.StartsWith("make it ", StringComparison.Ordinal) ||
            value.StartsWith("change ", StringComparison.Ordinal) ||
            value.StartsWith("replace ", StringComparison.Ordinal)) return TaskFollowUpMode.Steer;

        // Queue is the conservative default: ambiguous text must not mutate an active action.
        return TaskFollowUpMode.Queue;
    }

    public async Task<TaskExecutionSnapshot> BeginAsync(
        Guid contextId, Guid executionId, string promptSummary, TaskExecutionDurability durability,
        IReadOnlyCollection<string>? approvedPermissionScopes, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var snapshot = new TaskExecutionSnapshot(
            Guid.NewGuid(), contextId, executionId, SensitiveTextRedactor.Redact(promptSummary, 240),
            TaskExecutionLifecycle.Running, durability, 1, [], [], [], NormalizeScopes(approvedPermissionScopes),
            null, now, now);
        await repository.UpsertAsync(snapshot, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public Task<TaskExecutionSnapshot?> GetAsync(Guid taskId, CancellationToken cancellationToken) =>
        repository.GetAsync(taskId, cancellationToken);

    public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid contextId, CancellationToken cancellationToken) =>
        repository.GetByContextAsync(contextId, cancellationToken);

    public async Task<TaskExecutionSnapshot> RegisterActionAsync(
        Guid taskId, Guid actionId, Guid? parentActionId, string summary,
        TaskActionInterruptionPolicy interruptionPolicy, CancellationTokenSource? cancellationSource,
        IReadOnlyCollection<string>? requiredPermissionScopes, CancellationToken cancellationToken)
    {
        var snapshot = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        var nodes = snapshot.Plan.ToList();
        var node = new TaskPlanNode(actionId, parentActionId, SensitiveTextRedactor.Redact(summary, 256),
            TaskPlanNodeState.Running, interruptionPolicy, snapshot.PlanVersion,
            RequiredPermissionScopes: NormalizeScopes(requiredPermissionScopes));
        var index = nodes.FindIndex(item => item.ActionId == actionId);
        if (index >= 0) nodes[index] = node; else nodes.Add(node);
        if (cancellationSource is not null && interruptionPolicy == TaskActionInterruptionPolicy.ReadOnlyCancellable)
            _cancellableActions[(taskId, actionId)] = cancellationSource;
        var updated = snapshot with { Plan = nodes, State = TaskExecutionLifecycle.Running, UpdatedAt = _time.GetUtcNow() };
        await repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<TaskExecutionSnapshot> CompleteActionAsync(Guid taskId, Guid actionId, bool succeeded, CancellationToken cancellationToken)
    {
        _cancellableActions.TryRemove((taskId, actionId), out _);
        var snapshot = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        var nodes = snapshot.Plan.Select(item => item.ActionId == actionId && item.State != TaskPlanNodeState.Superseded
            ? item with { State = succeeded ? TaskPlanNodeState.Completed : TaskPlanNodeState.Failed } : item).ToArray();
        var updated = snapshot with { Plan = nodes, LastCheckpointActionId = actionId, UpdatedAt = _time.GetUtcNow() };
        await repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    public async Task<FollowUpDecision> SubmitFollowUpAsync(
        Guid taskId, string instruction, TaskFollowUpMode? explicitMode, IReadOnlyCollection<Guid>? affectedActionIds,
        IReadOnlyCollection<string>? requiredPermissionScopes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instruction)) throw new ArgumentException("A follow-up instruction is required.", nameof(instruction));
        var mode = explicitMode ?? InferMode(instruction);
        var inference = explicitMode is null ? TaskFollowUpInference.Inferred : TaskFollowUpInference.Explicit;
        return mode == TaskFollowUpMode.Queue
            ? await QueueAsync(taskId, instruction, inference, cancellationToken).ConfigureAwait(false)
            : await SteerAsync(taskId, instruction, inference, affectedActionIds, requiredPermissionScopes, cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueuedFollowUpTask> EditQueuedAsync(Guid ownerTaskId, Guid queuedTaskId, string summary,
        Guid? dependencyTaskId, CancellationToken cancellationToken)
    {
        var snapshot = await RequireAsync(ownerTaskId, cancellationToken).ConfigureAwait(false);
        var queue = snapshot.Queue.ToList();
        var index = queue.FindIndex(item => item.TaskId == queuedTaskId);
        if (index < 0) throw new KeyNotFoundException("Queued task was not found.");
        if (queue[index].State is not (QueuedFollowUpState.Queued or QueuedFollowUpState.Ready or QueuedFollowUpState.Blocked))
            throw new InvalidOperationException("Only pending queued work can be edited.");
        EnsureDependency(queuedTaskId, dependencyTaskId, queue);
        queue[index] = queue[index] with { Summary = SensitiveTextRedactor.Redact(summary, 500), DependencyTaskId = dependencyTaskId, UpdatedAt = _time.GetUtcNow() };
        await PersistQueueAsync(snapshot, queue, cancellationToken).ConfigureAwait(false);
        return queue[index];
    }

    public async Task RemoveQueuedAsync(Guid ownerTaskId, Guid queuedTaskId, CancellationToken cancellationToken)
    {
        var snapshot = await RequireAsync(ownerTaskId, cancellationToken).ConfigureAwait(false);
        var queue = snapshot.Queue.ToList();
        var item = queue.FirstOrDefault(entry => entry.TaskId == queuedTaskId);
        if (item is null) return;
        if (item.State is QueuedFollowUpState.Running or QueuedFollowUpState.Completed)
            throw new InvalidOperationException("Running or completed queued work cannot be removed.");
        queue.Remove(item);
        Reposition(queue);
        await PersistQueueAsync(snapshot, queue, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<QueuedFollowUpTask>> ReorderQueueAsync(Guid ownerTaskId, IReadOnlyList<Guid> orderedTaskIds, CancellationToken cancellationToken)
    {
        var snapshot = await RequireAsync(ownerTaskId, cancellationToken).ConfigureAwait(false);
        if (orderedTaskIds.Count != snapshot.Queue.Count || orderedTaskIds.Distinct().Count() != snapshot.Queue.Count ||
            snapshot.Queue.Any(item => !orderedTaskIds.Contains(item.TaskId)))
            throw new ArgumentException("Reorder must contain every queued task exactly once.", nameof(orderedTaskIds));
        var byId = snapshot.Queue.ToDictionary(item => item.TaskId);
        var queue = orderedTaskIds.Select((id, position) => byId[id] with { Position = position, UpdatedAt = _time.GetUtcNow() }).ToList();
        await PersistQueueAsync(snapshot, queue, cancellationToken).ConfigureAwait(false);
        return queue;
    }

    public async Task<FollowUpDecision> PromoteQueuedToSteerAsync(Guid ownerTaskId, Guid queuedTaskId,
        IReadOnlyCollection<Guid>? affectedActionIds, CancellationToken cancellationToken)
    {
        var snapshot = await RequireAsync(ownerTaskId, cancellationToken).ConfigureAwait(false);
        var queue = snapshot.Queue.ToList();
        var index = queue.FindIndex(item => item.TaskId == queuedTaskId);
        if (index < 0) throw new KeyNotFoundException("Queued task was not found.");
        if (queue[index].State is not (QueuedFollowUpState.Queued or QueuedFollowUpState.Ready or QueuedFollowUpState.Blocked))
            throw new InvalidOperationException("Only pending queued work can be promoted.");
        var text = queue[index].Summary;
        queue[index] = queue[index] with { State = QueuedFollowUpState.PromotedToSteer, UpdatedAt = _time.GetUtcNow() };
        await PersistQueueAsync(snapshot, queue, cancellationToken).ConfigureAwait(false);
        return await SteerAsync(ownerTaskId, text, TaskFollowUpInference.Explicit, affectedActionIds, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<QueueCheckpointResult> ReachCheckpointAsync(Guid ownerTaskId, Guid? completedActionId, bool ownerCompleted, CancellationToken cancellationToken)
    {
        var snapshot = await RequireAsync(ownerTaskId, cancellationToken).ConfigureAwait(false);
        if (completedActionId is { } completed) snapshot = await CompleteActionAsync(ownerTaskId, completed, true, cancellationToken).ConfigureAwait(false);
        if (ownerCompleted) snapshot = snapshot with { State = TaskExecutionLifecycle.Completed, UpdatedAt = _time.GetUtcNow() };
        var queue = snapshot.Queue.ToList();
        var completedIds = queue.Where(item => item.State == QueuedFollowUpState.Completed).Select(item => item.TaskId).ToHashSet();
        QueuedFollowUpTask? ready = null;
        foreach (var candidate in queue.OrderBy(item => item.Position))
        {
            if (candidate.State != QueuedFollowUpState.Queued) continue;
            if (candidate.DependencyTaskId is { } dependency && !completedIds.Contains(dependency)) continue;
            if (!ownerCompleted && snapshot.LastCheckpointActionId is null) continue;
            ready = candidate with { State = QueuedFollowUpState.Ready, UpdatedAt = _time.GetUtcNow() };
            queue[queue.FindIndex(item => item.TaskId == candidate.TaskId)] = ready;
            break;
        }
        snapshot = snapshot with { Queue = queue, UpdatedAt = _time.GetUtcNow() };
        await repository.UpsertAsync(snapshot, cancellationToken).ConfigureAwait(false);
        if (ready is not null) PublishQueue(snapshot, ready, ExecutionActionStatus.Queued, "Queued follow-up ready");
        return new QueueCheckpointResult(snapshot, ready);
    }

    public async Task<QueueCheckpointResult> ReachSafeBoundaryAsync(Guid taskId, Guid actionId, CancellationToken cancellationToken)
    {
        _cancellableActions.TryRemove((taskId, actionId), out _);
        var snapshot = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        var pending = snapshot.Steers.Where(item => item.State == SteerInstructionState.WaitingSafeBoundary && item.AffectedActionIds.Contains(actionId))
            .OrderBy(item => item.Sequence).LastOrDefault();
        if (pending is null)
        {
            snapshot = snapshot with { LastCheckpointActionId = actionId, UpdatedAt = _time.GetUtcNow() };
            await repository.UpsertAsync(snapshot, cancellationToken).ConfigureAwait(false);
            return await ReachCheckpointAsync(taskId, null, false, cancellationToken).ConfigureAwait(false);
        }

        // The action has already crossed its commit boundary. Preserve its terminal state rather than
        // pretending it was undone or scheduling a duplicate replacement. The next pending work is
        // what the steer replans.
        var version = snapshot.PlanVersion + 1;
        var steers = snapshot.Steers.Select(item => item.Id == pending.Id ? item with { State = SteerInstructionState.Applied } : item).ToArray();
        snapshot = snapshot with { Steers = steers, PlanVersion = version, State = TaskExecutionLifecycle.Running, LastCheckpointActionId = actionId, UpdatedAt = _time.GetUtcNow() };
        await repository.UpsertAsync(snapshot, cancellationToken).ConfigureAwait(false);
        PublishSteer(snapshot, pending with { State = SteerInstructionState.Applied }, ExecutionActionStatus.Completed, "Steer applied at safe boundary", actionId);
        return await ReachCheckpointAsync(taskId, null, false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TaskExecutionSnapshot> MarkQueuedTaskStateAsync(Guid ownerTaskId, Guid queuedTaskId, QueuedFollowUpState state,
        string? idempotencyKey, CancellationToken cancellationToken)
    {
        var snapshot = await RequireAsync(ownerTaskId, cancellationToken).ConfigureAwait(false);
        var queue = snapshot.Queue.ToList();
        var index = queue.FindIndex(item => item.TaskId == queuedTaskId);
        if (index < 0) throw new KeyNotFoundException("Queued task was not found.");
        var current = queue[index];
        if (current.State == QueuedFollowUpState.Completed)
        {
            if (state == QueuedFollowUpState.Completed && !string.IsNullOrWhiteSpace(idempotencyKey) && current.IdempotencyKey == idempotencyKey) return snapshot;
            throw new InvalidOperationException("Completed queued work cannot execute twice.");
        }
        queue[index] = current with { State = state, IdempotencyKey = idempotencyKey ?? current.IdempotencyKey, UpdatedAt = _time.GetUtcNow() };
        snapshot = snapshot with { Queue = queue, UpdatedAt = _time.GetUtcNow() };
        await repository.UpsertAsync(snapshot, cancellationToken).ConfigureAwait(false);
        PublishQueue(snapshot, queue[index], MapQueueStatus(state), "Queued follow-up state changed");
        return snapshot;
    }

    public async Task<TaskExecutionSnapshot> RestoreAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var snapshot = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (snapshot.Durability != TaskExecutionDurability.InMemoryContinuation ||
            snapshot.State is not (TaskExecutionLifecycle.Running or TaskExecutionLifecycle.WaitingSafeBoundary)) return snapshot;
        var restored = snapshot with
        {
            State = TaskExecutionLifecycle.Suspended,
            Plan = snapshot.Plan.Select(item => item.State is TaskPlanNodeState.Running or TaskPlanNodeState.WaitingSafeBoundary
                ? item with { State = TaskPlanNodeState.RequiresReexecution } : item).ToArray(),
            Queue = snapshot.Queue.Select(item => item.State == QueuedFollowUpState.Running
                ? item with { State = QueuedFollowUpState.Ready, UpdatedAt = _time.GetUtcNow() } : item).ToArray(),
            UpdatedAt = _time.GetUtcNow()
        };
        await repository.UpsertAsync(restored, cancellationToken).ConfigureAwait(false);
        events.TryPublish(new ExecutionEvent(Guid.NewGuid(), restored.ExecutionId, Guid.NewGuid(), restored.LastCheckpointActionId,
            ExecutionOrigin.Haven, ExecutionActionType.Warning, ExecutionActionStatus.Suspended, "Execution continuation requires re-run", null,
            "Persisted plan and queue state were restored, but the prior in-memory continuation was not. The unfinished action is marked for re-execution.",
            "task-coordination", _time.GetUtcNow(), TaskId: restored.TaskId));
        return restored;
    }

    private async Task<FollowUpDecision> QueueAsync(Guid taskId, string instruction, TaskFollowUpInference inference, CancellationToken cancellationToken)
    {
        var snapshot = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        var queue = snapshot.Queue.OrderBy(item => item.Position).ToList();
        var queued = new QueuedFollowUpTask(Guid.NewGuid(), taskId, snapshot.ExecutionId, SensitiveTextRedactor.Redact(instruction, 500),
            queue.Count == 0 ? 1 : queue.Max(item => item.CreationOrder) + 1, queue.Count, QueuedFollowUpState.Queued, null, now, now);
        queue.Add(queued);
        var updated = snapshot with { Queue = queue, UpdatedAt = now };
        await repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
        PublishQueue(updated, queued, ExecutionActionStatus.Queued, "Follow-up queued");
        return new FollowUpDecision(TaskFollowUpMode.Queue, inference, updated, QueuedTask: queued);
    }

    private async Task<FollowUpDecision> SteerAsync(Guid taskId, string instruction, TaskFollowUpInference inference,
        IReadOnlyCollection<Guid>? affectedActionIds, IReadOnlyCollection<string>? requiredPermissionScopes, CancellationToken cancellationToken)
    {
        var snapshot = await RequireAsync(taskId, cancellationToken).ConfigureAwait(false);
        var scopes = NormalizeScopes(requiredPermissionScopes);
        var requiresApproval = scopes.Any(scope => !snapshot.ApprovedPermissionScopes.Contains(scope, StringComparer.OrdinalIgnoreCase));
        var affected = (affectedActionIds is { Count: > 0 } ? affectedActionIds : snapshot.Plan
            .Where(item => item.State is TaskPlanNodeState.Running or TaskPlanNodeState.Pending).Select(item => item.ActionId).TakeLast(1)).Distinct().ToArray();
        var now = _time.GetUtcNow();
        var sequence = snapshot.Steers.Count == 0 ? 1 : snapshot.Steers.Max(item => item.Sequence) + 1;
        var steerId = Guid.NewGuid();
        var steers = snapshot.Steers.ToList();
        for (var i = 0; i < steers.Count; i++)
        {
            var older = steers[i];
            if (older.State is SteerInstructionState.Pending or SteerInstructionState.WaitingSafeBoundary or SteerInstructionState.Applied && older.AffectedActionIds.Intersect(affected).Any())
            {
                steers[i] = older with { State = SteerInstructionState.Superseded, SupersededById = steerId };
                PublishSteer(snapshot, steers[i], ExecutionActionStatus.Superseded, "Earlier steer superseded");
            }
        }
        if (requiresApproval)
        {
            var blocked = new SteerInstruction(steerId, taskId, snapshot.ExecutionId, sequence, SensitiveTextRedactor.Redact(instruction, 500),
                inference, SteerInstructionState.Blocked, affected, now, RequiredPermissionScopes: scopes);
            steers.Add(blocked);
            var blockedSnapshot = snapshot with { Steers = steers, State = TaskExecutionLifecycle.Blocked, UpdatedAt = now };
            await repository.UpsertAsync(blockedSnapshot, cancellationToken).ConfigureAwait(false);
            PublishSteer(blockedSnapshot, blocked, ExecutionActionStatus.UserActionRequired, "Steer needs existing approval flow");
            return new FollowUpDecision(TaskFollowUpMode.Steer, inference, blockedSnapshot, blocked, RequiresApproval: true);
        }

        var nodes = snapshot.Plan.ToList();
        var cancellationRequested = false;
        var waiting = false;
        var version = snapshot.PlanVersion;
        foreach (var actionId in affected)
        {
            var index = nodes.FindIndex(item => item.ActionId == actionId);
            if (index < 0 || nodes[index].State == TaskPlanNodeState.Completed) continue;
            var node = nodes[index];
            if (node.State == TaskPlanNodeState.Running && node.InterruptionPolicy is TaskActionInterruptionPolicy.SafeBoundary or TaskActionInterruptionPolicy.AtomicCommit)
            {
                waiting = true;
                nodes[index] = node with { State = TaskPlanNodeState.WaitingSafeBoundary };
                continue;
            }
            if (node.State == TaskPlanNodeState.Running && node.InterruptionPolicy == TaskActionInterruptionPolicy.ReadOnlyCancellable &&
                _cancellableActions.TryGetValue((taskId, actionId), out var source))
            {
                cancellationRequested = true;
                try { source.Cancel(); } catch (ObjectDisposedException) { }
            }
            nodes[index] = node with { State = TaskPlanNodeState.Superseded };
            version++;
            nodes.Add(node with { ActionId = Guid.NewGuid(), State = TaskPlanNodeState.Pending, PlanVersion = version, SupersedesActionId = actionId });
        }
        var steerState = waiting ? SteerInstructionState.WaitingSafeBoundary : SteerInstructionState.Applied;
        var steer = new SteerInstruction(steerId, taskId, snapshot.ExecutionId, sequence, SensitiveTextRedactor.Redact(instruction, 500),
            inference, steerState, affected, now, RequiredPermissionScopes: scopes);
        steers.Add(steer);
        var updated = snapshot with { Steers = steers, Plan = nodes, PlanVersion = version,
            State = waiting ? TaskExecutionLifecycle.WaitingSafeBoundary : TaskExecutionLifecycle.Running, UpdatedAt = now };
        await repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
        PublishSteer(updated, steer, waiting ? ExecutionActionStatus.PendingSafeBoundary : ExecutionActionStatus.Completed,
            waiting ? "Steer waiting for safe boundary" : "Steer applied");
        return new FollowUpDecision(TaskFollowUpMode.Steer, inference, updated, steer, CancellationRequested: cancellationRequested, WaitingForSafeBoundary: waiting);
    }

    private async Task<TaskExecutionSnapshot> RequireAsync(Guid taskId, CancellationToken cancellationToken) =>
        await repository.GetAsync(taskId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("Task execution was not found.");

    private async Task PersistQueueAsync(TaskExecutionSnapshot snapshot, List<QueuedFollowUpTask> queue, CancellationToken cancellationToken)
    {
        var updated = snapshot with { Queue = queue, UpdatedAt = _time.GetUtcNow() };
        await repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    private void PublishSteer(TaskExecutionSnapshot snapshot, SteerInstruction steer, ExecutionActionStatus status, string name, Guid? oldAction = null, Guid? newAction = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["steerId"] = steer.Id.ToString(), ["sequence"] = steer.Sequence.ToString(),
            ["affectedActionIds"] = string.Join(",", steer.AffectedActionIds), ["planVersion"] = snapshot.PlanVersion.ToString()
        };
        if (steer.SupersededById is { } supersededBy) metadata["supersededBySteerId"] = supersededBy.ToString();
        if (oldAction is { } oldId) metadata["supersededActionId"] = oldId.ToString();
        if (newAction is { } newId) metadata["replacementActionId"] = newId.ToString();
        events.TryPublish(new ExecutionEvent(Guid.NewGuid(), snapshot.ExecutionId, steer.Id, oldAction, ExecutionOrigin.Haven, ExecutionActionType.Steer, status, name,
            "The active plan was updated only where the follow-up instruction affected pending work.", steer.Summary, "task-coordination", _time.GetUtcNow(),
            TaskId: snapshot.TaskId, SafeMetadata: metadata));
    }

    private void PublishQueue(TaskExecutionSnapshot snapshot, QueuedFollowUpTask queued, ExecutionActionStatus status, string name)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["queuedTaskId"] = queued.TaskId.ToString(), ["queuePosition"] = queued.Position.ToString(), ["creationOrder"] = queued.CreationOrder.ToString()
        };
        if (queued.DependencyTaskId is { } dependency) metadata["dependencyTaskId"] = dependency.ToString();
        events.TryPublish(new ExecutionEvent(Guid.NewGuid(), snapshot.ExecutionId, queued.TaskId, null, ExecutionOrigin.Haven, ExecutionActionType.Queue, status, name,
            null, queued.Summary, "task-coordination", _time.GetUtcNow(), TaskId: queued.TaskId, SafeMetadata: metadata));
    }

    private static ExecutionActionStatus MapQueueStatus(QueuedFollowUpState state) => state switch
    {
        QueuedFollowUpState.Queued or QueuedFollowUpState.Ready => ExecutionActionStatus.Queued,
        QueuedFollowUpState.Running => ExecutionActionStatus.Running,
        QueuedFollowUpState.Blocked => ExecutionActionStatus.Blocked,
        QueuedFollowUpState.Completed => ExecutionActionStatus.Completed,
        QueuedFollowUpState.Failed => ExecutionActionStatus.Failed,
        QueuedFollowUpState.Cancelled or QueuedFollowUpState.PromotedToSteer => ExecutionActionStatus.Cancelled,
        _ => ExecutionActionStatus.Warning
    };

    private static IReadOnlyList<string> NormalizeScopes(IReadOnlyCollection<string>? scopes) => scopes is null ? [] : scopes
        .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

    private static void Reposition(List<QueuedFollowUpTask> queue)
    {
        for (var i = 0; i < queue.Count; i++) queue[i] = queue[i] with { Position = i };
    }

    private static void EnsureDependency(Guid taskId, Guid? dependencyTaskId, IReadOnlyCollection<QueuedFollowUpTask> queue)
    {
        if (dependencyTaskId is null) return;
        if (dependencyTaskId == taskId) throw new InvalidOperationException("A queued task cannot depend on itself.");
        if (queue.All(item => item.TaskId != dependencyTaskId)) throw new InvalidOperationException("Queue dependencies must reference an explicit queued task.");
    }
}
