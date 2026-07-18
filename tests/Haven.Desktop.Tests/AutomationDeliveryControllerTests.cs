/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/AutomationDeliveryControllerTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns AutomationDeliveryControllerTests, MemoryOutbox. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents automation delivery controller tests and keeps its related state and behavior together.
/// </summary>
public sealed class AutomationDeliveryControllerTests
{
    /// <summary>
    /// Performs the notification failure requeues drained delivery step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Reports whether cancelled initial start can be retried is true for the current state.
    /// </summary>
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

    /// <summary>
    /// Performs the delivery step owned by this component.
    /// </summary>
    private static AutomationDelivery Delivery(string message) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Release watch",
        AutomationDeliveryKind.ConditionMet,
        "Condition met: Release watch",
        message,
        DateTimeOffset.UtcNow);

    /// <summary>
    /// Represents memory outbox and keeps its related state and behavior together.
    /// </summary>
    private sealed class MemoryOutbox(IEnumerable<AutomationDelivery> initial) : IAutomationDeliveryOutbox
    {
        /// <summary>
        /// Gets or updates items, the bindable or domain state represented by this property.
        /// </summary>
        public List<AutomationDelivery> Items { get; } = [.. initial];
        /// <summary>
        /// Gets or updates drain calls, the bindable or domain state represented by this property.
        /// </summary>
        public int DrainCalls { get; private set; }

        /// <summary>
        /// Performs enqueue asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task EnqueueAsync(AutomationDelivery delivery, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Items.RemoveAll(item => item.Id == delivery.Id);
            Items.Add(delivery);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Performs drain asynchronously so I/O does not block the caller's thread.
        /// </summary>
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
