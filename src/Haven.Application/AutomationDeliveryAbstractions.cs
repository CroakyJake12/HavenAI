/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/AutomationDeliveryAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns IAutomationDeliveryOutbox. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Defines the automation delivery outbox contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IAutomationDeliveryOutbox
{
    Task EnqueueAsync(AutomationDelivery delivery, CancellationToken cancellationToken);
    Task<IReadOnlyList<AutomationDelivery>> DrainAsync(CancellationToken cancellationToken);
}
