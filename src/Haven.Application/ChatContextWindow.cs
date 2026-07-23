using Haven.Core;

namespace Haven.Application;

/// <summary>Builds a bounded recent conversation window for a model request.</summary>
public static class ChatContextWindow
{
    public const int DefaultCharacterBudget = 24_000;

    public static IReadOnlyList<ChatMessage> Build(
        IReadOnlyList<ChatMessage> messages,
        int characterBudget = DefaultCharacterBudget)
    {
        if (characterBudget < 1)
            throw new ArgumentOutOfRangeException(nameof(characterBudget));

        var eligible = messages
            .Where(message => !message.IsCompacted &&
                message.Role is MessageRole.User or MessageRole.Assistant)
            .OrderBy(message => message.CreatedAt)
            .ToArray();

        var result = new List<ChatMessage>();
        var used = 0;

        for (var index = eligible.Length - 1; index >= 0; index--)
        {
            var message = eligible[index];
            var cost = Math.Max(1, message.Content.Length);
            if (result.Count > 0 && used + cost > characterBudget)
                break;

            result.Add(message);
            used += cost;
        }

        result.Reverse();
        return result;
    }
}
