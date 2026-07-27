/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/LocalConversationShareAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns LocalShareHandle, ILocalConversationShareService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents local share handle and keeps its related state and behavior together.
/// </summary>
public sealed record LocalShareHandle(
    Guid SessionId,
    Uri Address,
    DateTimeOffset ExpiresAt,
    string Notice);

/// <summary>
/// Defines the local conversation share service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface ILocalConversationShareService : IAsyncDisposable
{
    Task<LocalShareHandle> StartAsync(Guid conversationId, TimeSpan duration, CancellationToken cancellationToken);
    Task StopAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<SharedSession?> GetActiveAsync(Guid conversationId, CancellationToken cancellationToken);
}
