using System.Diagnostics;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class EnvironmentCalendarOAuthClientIdProvider : ICalendarOAuthClientIdProvider
{
    public string? GetClientId(CalendarProviderKind provider) => provider switch
    {
        CalendarProviderKind.Google => Environment.GetEnvironmentVariable("HAVEN_GOOGLE_CALENDAR_CLIENT_ID")?.Trim(),
        CalendarProviderKind.Microsoft => Environment.GetEnvironmentVariable("HAVEN_MICROSOFT_CALENDAR_CLIENT_ID")?.Trim(),
        _ => null
    };
}

public sealed class SystemOAuthBrowserLauncher : IOAuthBrowserLauncher
{
    public Task LaunchAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}
