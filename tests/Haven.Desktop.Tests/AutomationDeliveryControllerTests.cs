using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class AutomationDeliveryControllerTests
{
    [AvaloniaFact]
    public async Task NotificationFailureRequeuesDrainedDelivery()
    {
        var delivery = Delivery("Release available");
        var outbox = new MemoryOutbox([delivery]);
        var notifications = new NotificationService();
        notifications.Dispose();
        await using var controller = new AutomationDeliveryController(outbox, notifications);

        await controller.DrainAsync(CancellationToken.None);

        Assert.Equal([delivery], outbox.Items);
    }

    [AvaloniaFact]
    public async Task CancelledInitialStartCanBeRetried()
    {
        var outbox = new MemoryOutbox([]);
        using var notifications = new NotificationService();
        await using var controller = new AutomationDeliveryController(outbox, notifications);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.StartAsync(cancelled.Token));
        await controller.StartAsync(CancellationToken.None);

        Assert.True(outbox.DrainCalls >= 1);
    }

    private static AutomationDelivery Delivery(string message) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Release watch",
        AutomationDeliveryKind.ConditionMet,
        "Condition met: Release watch",
        message,
        DateTimeOffset.UtcNow);

    private sealed class MemoryOutbox(IEnumerable<AutomationDelivery> initial) : IAutomationDeliveryOutbox
    {
        public List<AutomationDelivery> Items { get; } = [.. initial];
        public int DrainCalls { get; private set; }

        public Task EnqueueAsync(AutomationDelivery delivery, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Items.RemoveAll(item => item.Id == delivery.Id);
            Items.Add(delivery);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AutomationDelivery>> DrainAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DrainCalls++;
            IReadOnlyList<AutomationDelivery> result = Items.ToArray();
            Items.Clear();
            return Task.FromResult(result);
        }
    }
}
