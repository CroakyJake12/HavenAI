namespace Haven.Desktop.Views.Pages.Chat;

internal readonly record struct ChatMentionQuery(int Start, int Length, string Query);

internal static class ChatMentionQueryParser
{
    public static bool TryParse(string? text, int caretIndex, out ChatMentionQuery mention)
    {
        var source = text ?? string.Empty;
        var caret = Math.Clamp(caretIndex, 0, source.Length);
        for (var at = caret - 1; at >= 0; at--)
        {
            var current = source[at];
            if (char.IsWhiteSpace(current)) break;
            if (current != '@') continue;

            if (at > 0 && IsIdentifierLike(source[at - 1])) break;
            var querySpan = source.AsSpan(at + 1, caret - at - 1);
            if (!IsValidQuery(querySpan)) break;
            mention = new ChatMentionQuery(at, caret - at, querySpan.ToString());
            return true;
        }

        mention = default;
        return false;
    }

    private static bool IsValidQuery(ReadOnlySpan<char> query)
    {
        foreach (var character in query)
            if (!(char.IsLetterOrDigit(character) || character is '-' or '_')) return false;
        return true;
    }

    private static bool IsIdentifierLike(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '.';
}
