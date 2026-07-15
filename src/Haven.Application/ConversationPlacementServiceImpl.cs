using Haven.Core;

namespace Haven.Application;

public sealed class ConversationPlacementService : IConversationPlacementService
{
    private readonly IConversationRepository _conversations;
    private readonly IConversationMoveRepository _moves;
    private readonly IModeRegistry _modes;
    private readonly IActivityLogRepository _activityLog;

    public ConversationPlacementService(
        IConversationRepository conversations,
        IConversationMoveRepository moves,
        IModeRegistry modes,
        IActivityLogRepository activityLog)
    {
        _conversations = conversations;
        _moves = moves;
        _modes = modes;
        _activityLog = activityLog;
    }

    public async Task<ConversationPlacementResult> MoveAsync(
        Guid conversationId,
        Guid? targetModeId,
        ConversationPlacement targetPlacement,
        string reason,
        CancellationToken cancellationToken)
    {
        var conversation = await _conversations.GetAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
            return new ConversationPlacementResult(false, "Conversation not found.", null, []);

        if (conversation.Kind is ConversationKind.Call or ConversationKind.AutomationRun or ConversationKind.Training)
            return new ConversationPlacementResult(false, $"Cannot move a {conversation.Kind} conversation.", null,
                [$"{conversation.Kind} conversations are system-managed and cannot be relocated."]);

        if (conversation.IsArchived)
            return new ConversationPlacementResult(false, "Cannot move an archived conversation.", null,
                ["Unarchive the conversation first."]);

        var fromModeId = await FindModeIdAsync(conversation.Mode, cancellationToken).ConfigureAwait(false);
        var warnings = new List<string>();

        var updatedConversation = conversation with
        {
            ContainerId = targetModeId,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _conversations.UpsertConversationAsync(updatedConversation, cancellationToken).ConfigureAwait(false);

        var move = new ConversationMove(
            Guid.NewGuid(),
            conversationId,
            fromModeId,
            targetModeId,
            ConversationPlacement.Auto,
            targetPlacement,
            reason,
            DateTimeOffset.UtcNow);
        await _moves.RecordMoveAsync(move, cancellationToken).ConfigureAwait(false);

        await _activityLog.AddEventAsync(new ActivityEvent(
            Guid.NewGuid(),
            ActivityEventKind.ConversationMove,
            conversationId,
            targetModeId,
            $"Moved conversation to {targetPlacement}",
            $"{{\"from\":\"{conversation.Mode}\",\"to\":\"{targetPlacement}\",\"reason\":\"{EscapeJson(reason)}\"}}",
            DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);

        return new ConversationPlacementResult(true, $"Conversation moved to {targetPlacement}.", updatedConversation, warnings);
    }

    public async Task<IReadOnlyList<ConversationMove>> GetHistoryAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        return await _moves.GetMovesAsync(conversationId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConversationPlacementResult> UndoAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var history = await _moves.GetMovesAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (history.Count == 0)
            return new ConversationPlacementResult(false, "No moves to undo.", null, []);

        var lastMove = history[0];
        var conversation = await _conversations.GetAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
            return new ConversationPlacementResult(false, "Conversation not found.", null, []);

        var restored = conversation with
        {
            ContainerId = lastMove.FromModeId,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _conversations.UpsertConversationAsync(restored, cancellationToken).ConfigureAwait(false);

        await _moves.RecordMoveAsync(new ConversationMove(
            Guid.NewGuid(),
            conversationId,
            lastMove.ToModeId,
            lastMove.FromModeId,
            lastMove.ToPlacement,
            lastMove.FromPlacement,
            "Undo",
            DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);

        await _activityLog.AddEventAsync(new ActivityEvent(
            Guid.NewGuid(),
            ActivityEventKind.ConversationMove,
            conversationId,
            lastMove.FromModeId,
            "Undid conversation move",
            $"{{\"undoOf\":\"{lastMove.Id}\"}}",
            DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);

        return new ConversationPlacementResult(true, "Move undone.", restored, []);
    }

    public async Task<IReadOnlyList<PlacementDestination>> GetValidDestinationsAsync(
        HavenMode currentMode,
        CancellationToken cancellationToken)
    {
        var modes = await _modes.GetModesAsync(cancellationToken).ConfigureAwait(false);
        var destinations = new List<PlacementDestination>();

        destinations.Add(new PlacementDestination("General Chat", "Move to general chat", null, ConversationPlacement.Auto, true, null));

        foreach (var mode in modes.Where(m => m.IsEnabled))
        {
            destinations.Add(new PlacementDestination(
                mode.Name,
                mode.Description,
                mode.Id,
                ConversationPlacement.Dock,
                true,
                null));
        }

        return destinations;
    }

    private async Task<Guid?> FindModeIdAsync(HavenMode mode, CancellationToken cancellationToken)
    {
        var key = mode switch
        {
            HavenMode.Chat => "chat",
            HavenMode.Teach => "teach",
            HavenMode.Do => "do",
            HavenMode.Studio => "studio",
            _ => "chat"
        };
        var modeDef = await _modes.GetModeByKeyAsync(key, cancellationToken).ConfigureAwait(false);
        return modeDef?.Id;
    }

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}
