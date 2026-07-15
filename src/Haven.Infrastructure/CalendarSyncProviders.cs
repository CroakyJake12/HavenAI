using Haven.Application;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Haven.Infrastructure;

public sealed class CalendarSyncProvider(
    CalendarProviderConfiguration configuration,
    ICalendarProviderTransport? transport = null) : ICalendarSyncProvider
{
    public CalendarProviderKind Kind => configuration.Provider;
    public bool IsConfigured => configuration.IsConfigured;
    public string ConfigurationStatus => !IsConfigured
        ? $"{Kind} Calendar is not configured. Add Haven's public OAuth client ID to enable sign-in."
        : transport is null
            ? $"{Kind} Calendar credentials are configured, but the live synchronization transport is unavailable in this build."
            : $"{Kind} Calendar is ready to connect.";

    public Task<CalendarAuthorizationResult> ConnectAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured) return Task.FromResult(new CalendarAuthorizationResult(false, CalendarSyncStatus.NotConfigured, ConfigurationStatus));
        if (transport is null) return Task.FromResult(new CalendarAuthorizationResult(false, CalendarSyncStatus.Error, ConfigurationStatus));
        return transport.ConnectAsync(configuration, cancellationToken);
    }

    public Task<CalendarSyncResult> SyncAsync(CalendarSyncRequest request, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return Task.FromResult(new CalendarSyncResult(false, CalendarSyncStatus.NotConfigured, 0, 0, 0, 0, ConfigurationStatus));
        if (transport is null) return Task.FromResult(new CalendarSyncResult(false, CalendarSyncStatus.Error, 0, 0, 0, 0, ConfigurationStatus));
        return transport.SyncAsync(request, cancellationToken);
    }

    public Task DisconnectAsync(Guid accountId, CancellationToken cancellationToken) =>
        transport?.DisconnectAsync(accountId, cancellationToken) ?? Task.CompletedTask;
}

public sealed class CalendarSyncProviderRegistry : ICalendarSyncProviderRegistry
{
    private readonly IReadOnlyDictionary<CalendarProviderKind, ICalendarSyncProvider> _providers;

    public CalendarSyncProviderRegistry(IEnumerable<ICalendarSyncProvider> providers)
    {
        _providers = providers.ToDictionary(provider => provider.Kind);
        Providers = _providers.Values.OrderBy(provider => provider.Kind).ToArray();
    }

    public IReadOnlyList<ICalendarSyncProvider> Providers { get; }

    public ICalendarSyncProvider Get(CalendarProviderKind kind) =>
        _providers.TryGetValue(kind, out var provider)
            ? provider
            : throw new KeyNotFoundException($"No calendar sync provider is registered for {kind}.");
}

public static class PlannerServiceCollectionExtensions
{
    public static IServiceCollection AddHavenPlannerInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<PlannerRepository>();
        services.TryAddSingleton<IPlannerRepository>(provider => provider.GetRequiredService<PlannerRepository>());
        services.TryAddSingleton<ICalendarSyncStore>(provider => provider.GetRequiredService<PlannerRepository>());
        services.TryAddSingleton<IPlannerProposalService, PlannerProposalService>();
        services.TryAddSingleton<ICalendarTokenStore, WindowsCalendarTokenStore>();
        services.AddHttpClient("HavenCalendarSync", client => client.Timeout = TimeSpan.FromSeconds(45));
        services.TryAddSingleton<GoogleCalendarProviderTransport>(provider => new GoogleCalendarProviderTransport(
            CreateGoogleConfiguration(), provider.GetRequiredService<IHttpClientFactory>(), provider.GetRequiredService<IPlannerRepository>(),
            provider.GetRequiredService<ICalendarSyncStore>(), provider.GetRequiredService<ICalendarTokenStore>()));
        services.TryAddSingleton<MicrosoftCalendarProviderTransport>(provider => new MicrosoftCalendarProviderTransport(
            CreateMicrosoftConfiguration(), provider.GetRequiredService<IHttpClientFactory>(), provider.GetRequiredService<IPlannerRepository>(),
            provider.GetRequiredService<ICalendarSyncStore>(), provider.GetRequiredService<ICalendarTokenStore>()));
        services.AddSingleton<ICalendarSyncProvider>(provider => new CalendarSyncProvider(CreateGoogleConfiguration(), provider.GetRequiredService<GoogleCalendarProviderTransport>()));
        services.AddSingleton<ICalendarSyncProvider>(provider => new CalendarSyncProvider(CreateMicrosoftConfiguration(), provider.GetRequiredService<MicrosoftCalendarProviderTransport>()));
        services.TryAddSingleton<ICalendarSyncProviderRegistry, CalendarSyncProviderRegistry>();
        return services;
    }

    private static CalendarProviderConfiguration CreateGoogleConfiguration() => new(
        CalendarProviderKind.Google,
        Environment.GetEnvironmentVariable("HAVEN_GOOGLE_CALENDAR_CLIENT_ID")?.Trim(),
        new Uri("http://127.0.0.1:53682/oauth/google/", UriKind.Absolute),
        ["openid", "email", "https://www.googleapis.com/auth/calendar"]);

    private static CalendarProviderConfiguration CreateMicrosoftConfiguration() => new(
        CalendarProviderKind.Microsoft,
        Environment.GetEnvironmentVariable("HAVEN_MICROSOFT_CALENDAR_CLIENT_ID")?.Trim(),
        new Uri("http://localhost:53683/oauth/microsoft/", UriKind.Absolute),
        ["openid", "offline_access", "User.Read", "Calendars.ReadWrite"]);
}
