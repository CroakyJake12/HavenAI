using Haven.Core;

namespace Haven.Application;

/// <summary>Resolves the public OAuth client ID for a connected Google or Microsoft account.</summary>
public interface ICalendarOAuthClientIdProvider
{
    string? GetClientId(CalendarProviderKind provider);
}

/// <summary>Launches the system browser for PKCE OAuth without coupling Infrastructure to a UI platform.</summary>
public interface IOAuthBrowserLauncher
{
    Task LaunchAsync(Uri uri, CancellationToken cancellationToken);
}
