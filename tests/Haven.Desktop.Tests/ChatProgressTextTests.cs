using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Tests;

public sealed class ChatProgressTextTests
{
    [Theory]
    [InlineData(true, 0, "Working…")]
    [InlineData(true, 1, "Working for 1 second")]
    [InlineData(true, 156, "Working for 156 seconds")]
    [InlineData(false, 1, "Worked for 1 second")]
    [InlineData(false, 156, "Worked for 156 seconds")]
    public void Format_uses_one_compact_timed_progress_label(bool isStreaming, long elapsedSeconds, string expected)
    {
        Assert.Equal(expected, ChatProgressText.Format(isStreaming, elapsedSeconds));
    }

    [Fact]
    public void Format_keeps_thinking_detail_under_the_single_progress_label()
    {
        Assert.Equal("Working for 12 seconds\nChecked the available context.", ChatProgressText.Format(true, 12, "  Checked the available context.  "));
    }
}
