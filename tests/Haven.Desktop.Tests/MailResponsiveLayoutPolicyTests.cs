using Haven.Desktop.Views.Pages.Mail;

namespace Haven.Desktop.Tests;

public sealed class MailResponsiveLayoutPolicyTests
{
    [Theory]
    [InlineData(320, MailResponsiveMode.Narrow)]
    [InlineData(679, MailResponsiveMode.Narrow)]
    [InlineData(680, MailResponsiveMode.Compact)]
    [InlineData(999, MailResponsiveMode.Compact)]
    [InlineData(1000, MailResponsiveMode.Wide)]
    [InlineData(1600, MailResponsiveMode.Wide)]
    public void ResolvesMailboxLayoutAtDocumentedBreakpoints(double width, MailResponsiveMode expected)
    {
        Assert.Equal(expected, MailResponsiveLayoutPolicy.Resolve(width));
    }
}
