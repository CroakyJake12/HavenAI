using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class UnifiedNotificationTests
{
    [Fact]
    public async Task Routine_actions_coalesce_and_automatic_recovery_does_not_create_retained_spam()
    {
        var eventRepository = new EventRepository();
        await using var hub = new ExecutionEventHub(eventRepository);
        var notificationRepository = new NotificationRepository();
        using var notifications = new HavenNotificationService(notificationRepository, hub);
        var executionId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;

        hub.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, null, ExecutionOrigin.Haven,
            ExecutionActionType.ToolCall, ExecutionActionStatus.Running, "Run tests", null, "Running", "dotnet", started, started));
        Assert.Single(notifications.Live);

        hub.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, null, ExecutionOrigin.Haven,
            ExecutionActionType.ToolCall, ExecutionActionStatus.Completed, "Run tests", null, "Passed", "dotnet",
            DateTimeOffset.UtcNow, started, DateTimeOffset.UtcNow));
        Assert.Empty(notifications.Live);
        Assert.Empty(notificationRepository.Values);

        var failedAction = Guid.NewGuid();
        hub.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, failedAction, actionId, ExecutionOrigin.Haven,
            ExecutionActionType.ToolCall, ExecutionActionStatus.Failed, "Build", null, "Compiler error", "dotnet", DateTimeOffset.UtcNow,
            Failure: new ExecutionFailure("CS0103", "Build failed", "Name does not exist")));
        Assert.Single(notifications.Live);
        hub.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, Guid.NewGuid(), failedAction, ExecutionOrigin.Haven,
            ExecutionActionType.AutomaticRepair, ExecutionActionStatus.Completed, "Repair code", null, "Corrected generated code", "haven",
            DateTimeOffset.UtcNow, RecoveryOfActionId: failedAction));

        await Task.Delay(25);
        Assert.Empty(notifications.Live);
        Assert.Empty(notificationRepository.Values);
    }

    [Fact]
    public async Task Final_response_is_retained_and_deep_links_to_the_execution()
    {
        await using var hub = new ExecutionEventHub(new EventRepository());
        var repository = new NotificationRepository();
        using var notifications = new HavenNotificationService(repository, hub);
        var executionId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        hub.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, null, ExecutionOrigin.Haven,
            ExecutionActionType.FinalResponse, ExecutionActionStatus.Completed, "Response ready", null, "Finished", "haven", DateTimeOffset.UtcNow));

        for (var attempt = 0; attempt < 20 && repository.Values.Count == 0; attempt++) await Task.Delay(10);
        var retained = Assert.Single(repository.Values);
        Assert.Equal(HavenNotificationKind.ResponseReady, retained.Kind);
        Assert.Equal(executionId, retained.Target.ExecutionId);
    }

    [Fact]
    public async Task A_faulting_notification_observer_does_not_block_other_observers_or_persistence()
    {
        await using var hub = new ExecutionEventHub(new EventRepository());
        var repository = new NotificationRepository();
        using var notifications = new HavenNotificationService(repository, hub);
        var observed = 0;
        notifications.Changed += (_, _) => throw new InvalidOperationException("observer failure");
        notifications.Changed += (_, _) => observed++;
        var value = new HavenNotification(Guid.NewGuid(), HavenNotificationKind.Plugin,
            HavenNotificationPriority.Information, "test", "Test", "Saved", "Safe message",
            false, false, false, false, null, new HavenNavigationTarget(), [],
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        await notifications.PublishAsync(value, CancellationToken.None);

        Assert.Equal(1, observed);
        Assert.Single(repository.Values);
    }

    private sealed class EventRepository : IExecutionEventRepository
    {
        public Task AppendAsync(IReadOnlyList<ExecutionEvent> events, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ExecutionEvent>> GetExecutionAsync(Guid executionId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExecutionEvent>>([]);
        public Task<IReadOnlyList<ExecutionSummary>> SearchExecutionsAsync(string? query, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExecutionSummary>>([]);
    }

    private sealed class NotificationRepository : IHavenNotificationRepository
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, HavenNotification> _values = [];
        public IReadOnlyList<HavenNotification> Values { get { lock (_gate) return _values.Values.ToArray(); } }
        public Task UpsertAsync(HavenNotification notification, CancellationToken cancellationToken) { lock (_gate) _values[notification.Id] = notification; return Task.CompletedTask; }
        public Task<IReadOnlyList<HavenNotification>> GetRecentAsync(int limit, bool includeDismissed, CancellationToken cancellationToken) => Task.FromResult(Values);
        public Task SetReadAsync(Guid id, bool isRead, CancellationToken cancellationToken) { lock (_gate) if (_values.TryGetValue(id, out var value)) _values[id] = value with { IsRead = isRead }; return Task.CompletedTask; }
        public Task DismissAsync(Guid id, CancellationToken cancellationToken) { lock (_gate) if (_values.TryGetValue(id, out var value)) _values[id] = value with { IsDismissed = true }; return Task.CompletedTask; }
    }
}
