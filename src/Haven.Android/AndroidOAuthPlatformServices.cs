using System.Reflection;
using Android.Content;
using Haven.Application;
using Haven.Core;

namespace Haven.Android;

public sealed class AndroidOAuthBrowserLauncher : IOAuthBrowserLauncher
{
    public Task LaunchAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();
        var androidUri = global::Android.Net.Uri.Parse(uri.AbsoluteUri)
                         ?? throw new InvalidOperationException("Android could not parse the OAuth authorization URI.");
        using var intent = new Intent(Intent.ActionView, androidUri);
        intent.AddFlags(ActivityFlags.NewTask);
        global::Android.App.Application.Context.StartActivity(intent);
        return Task.CompletedTask;
    }
}

public sealed class AndroidCalendarOAuthClientIdProvider : ICalendarOAuthClientIdProvider
{
    public string? GetClientId(CalendarProviderKind provider)
    {
        var environment = provider switch
        {
            CalendarProviderKind.Google => Environment.GetEnvironmentVariable("HAVEN_GOOGLE_CALENDAR_CLIENT_ID")?.Trim(),
            CalendarProviderKind.Microsoft => Environment.GetEnvironmentVariable("HAVEN_MICROSOFT_CALENDAR_CLIENT_ID")?.Trim(),
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(environment)) return environment;
        var key = provider switch
        {
            CalendarProviderKind.Google => "HavenGoogleCalendarClientId",
            CalendarProviderKind.Microsoft => "HavenMicrosoftCalendarClientId",
            _ => null
        };
        if (key is null) return null;
        return typeof(AndroidCalendarOAuthClientIdProvider).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key.Equals(key, StringComparison.Ordinal))
            ?.Value?.Trim();
    }
}
