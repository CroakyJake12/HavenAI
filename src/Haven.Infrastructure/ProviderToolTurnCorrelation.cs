using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Adds deterministic request-local identifiers to Haven's provider-neutral tool
/// turns. Ollama correlates results by tool name, while OpenAI-compatible,
/// Anthropic and current Gemini protocols require an identifier that is repeated
/// by the corresponding result. Haven stores the ordered calls and results, so
/// the exact relationship can be reconstructed without changing persisted data.
/// </summary>
internal static class ProviderToolTurnCorrelation
{
    internal sealed record CorrelatedCall(OllamaToolCall Call, string Id);

    internal sealed record CorrelatedTurn(
        OllamaToolTurn Turn,
        IReadOnlyList<CorrelatedCall> Calls,
        string? ResultCallId);

    public static IReadOnlyList<CorrelatedTurn> Correlate(
        IReadOnlyList<OllamaToolTurn> turns,
        string idPrefix)
    {
        ArgumentNullException.ThrowIfNull(turns);
        if (string.IsNullOrWhiteSpace(idPrefix))
            throw new ArgumentException("A provider tool-call ID prefix is required.", nameof(idPrefix));

        var result = new List<CorrelatedTurn>(turns.Count);
        var pending = new List<CorrelatedCall>();

        for (var turnIndex = 0; turnIndex < turns.Count; turnIndex++)
        {
            var turn = turns[turnIndex]
                       ?? throw new InvalidDataException($"Tool turn {turnIndex} is null.");

            if (turn.ToolCalls is { Count: > 0 })
            {
                var calls = new List<CorrelatedCall>(turn.ToolCalls.Count);
                for (var callIndex = 0; callIndex < turn.ToolCalls.Count; callIndex++)
                {
                    var call = turn.ToolCalls[callIndex]
                               ?? throw new InvalidDataException(
                                   $"Tool call {callIndex} in turn {turnIndex} is null.");
                    if (string.IsNullOrWhiteSpace(call.Name))
                        throw new InvalidDataException(
                            $"Tool call {callIndex} in turn {turnIndex} has no name.");

                    var correlated = new CorrelatedCall(
                        call,
                        $"{idPrefix}_{turnIndex:D4}_{callIndex:D4}");
                    calls.Add(correlated);
                    pending.Add(correlated);
                }

                result.Add(new CorrelatedTurn(turn, calls, null));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(turn.ToolName))
            {
                var pendingIndex = pending.FindIndex(value =>
                    value.Call.Name.Equals(turn.ToolName, StringComparison.OrdinalIgnoreCase));
                if (pendingIndex < 0)
                {
                    throw new InvalidDataException(
                        $"Tool result '{turn.ToolName}' has no preceding unmatched tool call.");
                }

                var callId = pending[pendingIndex].Id;
                pending.RemoveAt(pendingIndex);
                result.Add(new CorrelatedTurn(turn, [], callId));
                continue;
            }

            result.Add(new CorrelatedTurn(turn, [], null));
        }

        return result;
    }
}
