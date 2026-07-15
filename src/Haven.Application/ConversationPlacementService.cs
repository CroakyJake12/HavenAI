using Haven.Core;

namespace Haven.Application;

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

public sealed record ConversationPlacementResult(
    bool Succeeded,
    string Message,
    Conversation? Conversation,
    IReadOnlyList<string> Warnings);

public sealed record PlacementDestination(
    string Label,
    string Description,
    Guid? ModeId,
    ConversationPlacement Placement,
    bool IsValid,
    string? InvalidReason);
