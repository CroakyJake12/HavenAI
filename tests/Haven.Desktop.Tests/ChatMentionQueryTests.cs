using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Tests;

public sealed class ChatMentionQueryTests
{
    [Theory]
    [InlineData("@resear", 0, 7, "resear")]
    [InlineData("Please use @resear", 11, 7, "resear")]
    [InlineData("Compare this with @file", 18, 5, "file")]
    [InlineData("(@compres", 1, 8, "compres")]
    public void Parses_valid_inline_mentions(string text, int start, int length, string query)
    {
        Assert.True(ChatMentionQueryParser.TryParse(text, text.Length, out var mention));
        Assert.Equal(new ChatMentionQuery(start, length, query), mention);
    }

    [Theory]
    [InlineData("test@example.com")]
    [InlineData("foo@bar")]
    [InlineData("prefix.name@host")]
    public void Does_not_trigger_inside_email_like_tokens(string text)
    {
        Assert.False(ChatMentionQueryParser.TryParse(text, text.Length, out _));
    }

    [Fact]
    public void Uses_the_caret_token_not_text_after_it()
    {
        const string text = "Use @file then continue";
        Assert.True(ChatMentionQueryParser.TryParse(text, 9, out var mention));
        Assert.Equal(new ChatMentionQuery(4, 5, "file"), mention);
    }
}
