using System.Collections.Concurrent;
using Haven.Core;

namespace Haven.Application;

/// <summary>One cross-window live-activity and retained-notification service.</summary>
public sealed class HavenNotificationService : IDisposable
{
    private readonly IHavenNotificationRepository _repository;
    private readonly ExecutionEventHub _events;
    private readonly ConcurrentDictionary<string, HavenNotification> _live = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _sourceRate = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, (Guid ExecutionId, CancellationTokenSource Cancellation)> _pendingFailures = new();
    private int _disposed;

    public HavenNotificationService(IHavenNotificationRepository repository, ExecutionEventHub events)
    {
        _repository = repository;
        _events = events;
        _events.Published += OnExecutionEvent;
    }

    public event EventHandler<HavenNotification>? Changed;
    public IReadOnlyList<HavenNotification> Live => _live.Values.OrderByDescending(item => item.UpdatedAt).ToArray();

    public Task<IReadOnlyList<HavenNotification>> GetRecentAsync(int limit, bool includeDismissed, CancellationToken cancellationToken) =>
        _repository.GetRecentAsync(Math.Clamp(limit, 1, 500), includeDismissed, cancellationToken);

    public Task MarkReadAsync(Guid id, bool read, CancellationToken cancellationToken) =>
        _repository.SetReadAsync(id, read, cancellationToken);

    public Task DismissAsync(Guid id, CancellationToken cancellationToken) =>
        _repository.DismissAsync(id, cancellationToken);

    public async Task PublishAsync(HavenNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (!AllowSource(notification.SourceId)) return;
        var safe = Sanitize(notification);
        if (safe.IsLive)
        {
            var key = safe.CoalescingKey ?? safe.Id.ToString("N");
            _live[key] = safe;
        }
        else
        {
            await _repository.UpsertAsync(safe, cancellationToken).ConfigureAwait(false);
        }
        NotifyChanged(safe);
    }

    private void OnExecutionEvent(object? sender, ExecutionEvent value)
    {
        if (Volatile.Read(ref _disposed) == 1) return;
        if (value.RecoveryOfActionId is { } recovered) CancelPendingFailure(recovered);
        if (value.RetryOfActionId is { } retried) CancelPendingFailure(retried);
        if (value.ActionType is ExecutionActionType.AutomaticDiagnosis or ExecutionActionType.AutomaticRepair or ExecutionActionType.Retry &&
            value.Status == ExecutionActionStatus.Completed)
        {
            _live.TryRemove($"execution:{value.ExecutionId:N}", out _);
            NotifyChanged(CreateFromExecution(value, terminal: true));
            return;
        }

        var retain = value.ActionType is ExecutionActionType.FinalResponse or ExecutionActionType.ExternalAgent || value.Status is
            ExecutionActionStatus.Cancelled or ExecutionActionStatus.Suspended or ExecutionActionStatus.UserActionRequired or ExecutionActionStatus.Blocked;
        if (value.ActionType == ExecutionActionType.FinalResponse && value.Status == ExecutionActionStatus.Completed)
            CancelPendingFailures(value.ExecutionId);
        if (value.Status == ExecutionActionStatus.Failed && value.ActionType != ExecutionActionType.FinalResponse)
        {
            var failure = CreateFromExecution(value, terminal: true);
            _live[$"execution:{value.ExecutionId:N}"] = failure with { IsLive = true, CompletedAt = null };
            NotifyChanged(failure);
            ScheduleFailurePersistence(value, failure);
            return;
        }
        if (value.Status == ExecutionActionStatus.Completed && !retain)
        {
            _live.TryRemove($"execution:{value.ExecutionId:N}", out _);
            NotifyChanged(CreateFromExecution(value, terminal: true));
            return;
        }

        var notification = CreateFromExecution(value, retain);
        if (retain)
        {
            _live.TryRemove($"execution:{value.ExecutionId:N}", out _);
            _ = PersistSafelyAsync(notification);
        }
        else
        {
            _live[$"execution:{value.ExecutionId:N}"] = notification;
            NotifyChanged(notification);
        }
    }

    private void ScheduleFailurePersistence(ExecutionEvent value, HavenNotification notification)
    {
        CancelPendingFailure(value.ActionId);
        var cancellation = new CancellationTokenSource();
        _pendingFailures[value.ActionId] = (value.ExecutionId, cancellation);
        _ = PersistFailureAfterDelayAsync(value.ActionId, notification, cancellation.Token);
    }

    private async Task PersistFailureAfterDelayAsync(Guid actionId, HavenNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            if (_pendingFailures.TryRemove(actionId, out var pending))
                pending.Cancellation.Dispose();
            await PersistSafelyAsync(notification).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void CancelPendingFailure(Guid actionId)
    {
        if (!_pendingFailures.TryRemove(actionId, out var pending)) return;
        pending.Cancellation.Cancel();
        pending.Cancellation.Dispose();
    }

    private void CancelPendingFailures(Guid executionId)
    {
        foreach (var pair in _pendingFailures.Where(pair => pair.Value.ExecutionId == executionId).ToArray())
            CancelPendingFailure(pair.Key);
    }

    private async Task PersistSafelyAsync(HavenNotification notification)
    {
        try
        {
            await _repository.UpsertAsync(notification, CancellationToken.None).ConfigureAwait(false);
            NotifyChanged(notification);
        }
        catch
        {
            // A notification write must never delay or fail the user operation.
        }
    }

    private static HavenNotification CreateFromExecution(ExecutionEvent value, bool terminal)
    {
        var attention = value.Status is ExecutionActionStatus.UserActionRequired or ExecutionActionStatus.Blocked;
        var priority = value.Status switch
        {
            ExecutionActionStatus.Failed => HavenNotificationPriority.Error,
            ExecutionActionStatus.UserActionRequired or ExecutionActionStatus.Blocked => HavenNotificationPriority.AttentionRequired,
            ExecutionActionStatus.Warning or ExecutionActionStatus.Suspended => HavenNotificationPriority.Warning,
            ExecutionActionStatus.Completed => HavenNotificationPriority.Success,
            _ => HavenNotificationPriority.Information
        };
        var kind = attention ? HavenNotificationKind.UserActionRequired
            : value.ActionType == ExecutionActionType.ExternalAgent ? HavenNotificationKind.Task
            : value.Status == ExecutionActionStatus.Failed ? HavenNotificationKind.Failure
            : value.ActionType == ExecutionActionType.FinalResponse ? HavenNotificationKind.ResponseReady
            : HavenNotificationKind.LiveActivity;
        var target = new HavenNavigationTarget(value.TabId, TaskId: value.TaskId, ActionId: value.ActionId,
            ExecutionId: value.ExecutionId, ProjectId: value.ProjectId, PluginId: value.ComponentId,
            RemediationId: value.RemediationId);
        var action = new HavenNotificationAction(
            value.ActionType == ExecutionActionType.FinalResponse ? "navigation.open-response" : "navigation.open-action-graph",
            value.ActionType == ExecutionActionType.FinalResponse ? "Open" : "Review", target);
        return new HavenNotification(
            DeterministicNotificationId(value.ExecutionId, value.ActionId, terminal), kind, priority,
            value.ComponentId ?? "haven", value.ComponentId ?? "Haven", value.Name,
            value.Failure?.Message ?? value.SafeDetail ?? value.SafeReasoningSummary ?? value.Name,
            !terminal, false, false, attention, $"execution:{value.ExecutionId:N}", target, [action],
            value.StartedAt ?? value.Timestamp, value.Timestamp, terminal ? value.Timestamp : null);
    }

    private bool AllowSource(string sourceId)
    {
        var now = DateTimeOffset.UtcNow;
        var queue = _sourceRate.GetOrAdd(sourceId, static _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.TryPeek(out var oldest) && now - oldest > TimeSpan.FromMinutes(1)) queue.Dequeue();
            if (queue.Count >= 60) return false;
            queue.Enqueue(now);
            return true;
        }
    }

    private static HavenNotification Sanitize(HavenNotification value) => value with
    {
        SourceId = SensitiveTextRedactor.Redact(value.SourceId, 200),
        SourceName = SensitiveTextRedactor.Redact(value.SourceName, 200),
        Title = SensitiveTextRedactor.Redact(value.Title, 240),
        Message = SensitiveTextRedactor.Redact(value.Message, 2_000)
    };

    private void NotifyChanged(HavenNotification notification)
    {
        var handlers = Changed;
        if (handlers is null) return;
        foreach (EventHandler<HavenNotification> handler in handlers.GetInvocationList())
        {
            try { handler(this, notification); }
            catch { /* A notification observer must not fail or delay the originating action. */ }
        }
    }

    private static Guid DeterministicNotificationId(Guid executionId, Guid actionId, bool terminal)
    {
        Span<byte> bytes = stackalloc byte[16];
        executionId.TryWriteBytes(bytes);
        Span<byte> action = stackalloc byte[16];
        actionId.TryWriteBytes(action);
        for (var index = 0; index < bytes.Length; index++) bytes[index] ^= action[index];
        if (terminal) bytes[15] ^= 0xA5;
        return new Guid(bytes);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _events.Published -= OnExecutionEvent;
        foreach (var actionId in _pendingFailures.Keys.ToArray()) CancelPendingFailure(actionId);
        _live.Clear();
    }
}
