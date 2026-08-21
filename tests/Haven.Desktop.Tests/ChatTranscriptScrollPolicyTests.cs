using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Tests;

public sealed class ChatTranscriptScrollPolicyTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1000, 1000, true)]
    [InlineData(1000, 960, true)]
    [InlineData(1000, 952, true)]
    [InlineData(1000, 951, false)]
    [InlineData(1000, 600, false)]
    public void Tail_following_only_stays_enabled_near_the_end(double maxScrollY, double scrollY, bool expected)
    {
        Assert.Equal(expected, ChatTranscriptScrollPolicy.ShouldFollow(maxScrollY, scrollY));
    }

    [Fact]
    public void Policy_clamps_out_of_range_scroll_values_and_never_uses_negative_tolerance()
    {
        Assert.True(ChatTranscriptScrollPolicy.ShouldFollow(100, 500));
        Assert.False(ChatTranscriptScrollPolicy.ShouldFollow(100, 99, -1));
    }
}
