using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Correlates Haven's ordered assistant tool calls and tool results. Cloud
/// providers require the exact model-issued call identifier to be repeated by
/// the corresponding result; local Ollama and legacy turns can fall back to a
/// deterministic request-local identifier when no provider ID exists.
/// </summary>
internal static class ProviderToolTurnCorrelation
{
    private const int MaximumCallIdLength = 512;

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
        var knownIds = new HashSet<string>(StringComparer.Ordinal);

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

                    var id = string.IsNullOrWhiteSpace(call.Id)
                        ? $"{idPrefix}_{turnIndex:D4}_{callIndex:D4}"
                        : call.Id.Trim();
                    if (id.Length > MaximumCallIdLength)
                        throw new InvalidDataException(
                            $"Tool call {callIndex} in turn {turnIndex} has an identifier longer than {MaximumCallIdLength} characters.");
                    if (!knownIds.Add(id))
                        throw new InvalidDataException(
                            $"Tool call identifier '{id}' appears more than once in the request history.");

                    var correlated = new CorrelatedCall(call, id);
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
