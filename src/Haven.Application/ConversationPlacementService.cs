/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ConversationPlacementService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns IConversationPlacementService, ConversationPlacementResult, PlacementDestination. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Defines the i conversation placement service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IConversationPlacementService
{
    Task<ConversationPlacementResult> MoveAsync(
        Guid conversationId,
        Guid? targetModeId,
        ConversationPlacement targetPlacement,
        string reason,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationMove>> GetHistoryAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<ConversationPlacementResult> UndoAsync(Guid conversationId, CancellationToken cancellationToken);
}

/// <summary>
/// Represents conversation placement result and keeps its related state and behavior together.
/// </summary>
public sealed record ConversationPlacementResult(
    bool Succeeded,
    string Message,
    Conversation? Conversation,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Represents placement destination and keeps its related state and behavior together.
/// </summary>
public sealed record PlacementDestination(
    string Label,
    string Description,
    Guid? ModeId,
    ConversationPlacement Placement,
    bool IsValid,
    string? InvalidReason);
