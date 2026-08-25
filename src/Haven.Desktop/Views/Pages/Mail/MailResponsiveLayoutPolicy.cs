namespace Haven.Desktop.Views.Pages.Mail;

public enum MailResponsiveMode
{
    Narrow,
    Compact,
    Wide
}

public static class MailResponsiveLayoutPolicy
{
    public static MailResponsiveMode Resolve(double width) => width switch
    {
        < 680 => MailResponsiveMode.Narrow,
        < 1000 => MailResponsiveMode.Compact,
        _ => MailResponsiveMode.Wide
    };
}
