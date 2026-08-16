/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/CalendarSyncProviders.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns CalendarSyncProvider, CalendarSyncProviderRegistry, PlannerServiceCollectionExtensions. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Haven.Infrastructure;

/// <summary>
/// Represents calendar sync provider and keeps its related state and behavior together.
/// </summary>
public sealed class CalendarSyncProvider(
    CalendarProviderConfiguration configuration,
    ICalendarProviderTransport? transport = null) : ICalendarSyncProvider
{
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public CalendarProviderKind Kind => configuration.Provider;
    /// <summary>
    /// Reports whether configured applies to the current state.
    /// </summary>
    public bool IsConfigured => configuration.IsConfigured;
    /// <summary>
    /// Gets or updates configuration status, the bindable or domain state represented by this property.
    /// </summary>
    public string ConfigurationStatus => !IsConfigured
        ? $"{Kind} Calendar is not configured. Add Haven's public OAuth client ID to enable sign-in."
        : transport is null
            ? $"{Kind} Calendar credentials are configured, but the live synchronization transport is unavailable in this build."
            : $"{Kind} Calendar is ready to connect.";

    /// <summary>
    /// Performs connect asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<CalendarAuthorizationResult> ConnectAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured) return Task.FromResult(new CalendarAuthorizationResult(false, CalendarSyncStatus.NotConfigured, ConfigurationStatus));
        if (transport is null) return Task.FromResult(new CalendarAuthorizationResult(false, CalendarSyncStatus.Error, ConfigurationStatus));
        return transport.ConnectAsync(configuration, cancellationToken);
    }

    /// <summary>
    /// Performs sync asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<CalendarSyncResult> SyncAsync(CalendarSyncRequest request, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return Task.FromResult(new CalendarSyncResult(false, CalendarSyncStatus.NotConfigured, 0, 0, 0, 0, ConfigurationStatus));
        if (transport is null) return Task.FromResult(new CalendarSyncResult(false, CalendarSyncStatus.Error, 0, 0, 0, 0, ConfigurationStatus));
        return transport.SyncAsync(request, cancellationToken);
    }

    /// <summary>
    /// Performs disconnect asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DisconnectAsync(Guid accountId, CancellationToken cancellationToken) =>
        transport?.DisconnectAsync(accountId, cancellationToken) ?? Task.CompletedTask;
}

/// <summary>
/// Represents calendar sync provider registry and keeps its related state and behavior together.
/// </summary>
public sealed class CalendarSyncProviderRegistry : ICalendarSyncProviderRegistry
{
    /// <summary>
    /// Stores providers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IReadOnlyDictionary<CalendarProviderKind, ICalendarSyncProvider> _providers;

    public CalendarSyncProviderRegistry(IEnumerable<ICalendarSyncProvider> providers)
    {
        _providers = providers.ToDictionary(provider => provider.Kind);
        Providers = _providers.Values.OrderBy(provider => provider.Kind).ToArray();
    }

    /// <summary>
    /// Gets or updates providers, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<ICalendarSyncProvider> Providers { get; }

    /// <summary>
    /// Retrieves this member for the current operation.
    /// </summary>
    public ICalendarSyncProvider Get(CalendarProviderKind kind) =>
        _providers.TryGetValue(kind, out var provider)
            ? provider
            : throw new KeyNotFoundException($"No calendar sync provider is registered for {kind}.");
}

/// <summary>
/// Represents planner service collection extensions and keeps its related state and behavior together.
/// </summary>
public static class PlannerServiceCollectionExtensions
{
    /// <summary>
    /// Performs the add haven planner infrastructure step owned by this component.
    /// </summary>
    public static IServiceCollection AddHavenPlannerInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<PlannerRepository>();
        services.TryAddSingleton<IPlannerRepository>(provider => provider.GetRequiredService<PlannerRepository>());
        services.TryAddSingleton<ICalendarSyncStore>(provider => provider.GetRequiredService<PlannerRepository>());
        services.TryAddSingleton<IPlannerProposalService, PlannerProposalService>();
        services.TryAddSingleton<IPlannerDayService, PlannerDayService>();
        services.TryAddSingleton<IPlannerCountdownService, PlannerCountdownService>();
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

    /// <summary>
    /// Creates google configuration with the invariants required by its callers.
    /// </summary>
    private static CalendarProviderConfiguration CreateGoogleConfiguration() => new(
        CalendarProviderKind.Google,
        Environment.GetEnvironmentVariable("HAVEN_GOOGLE_CALENDAR_CLIENT_ID")?.Trim(),
        new Uri("http://127.0.0.1:53682/oauth/google/", UriKind.Absolute),
        ["openid", "email", "https://www.googleapis.com/auth/calendar"]);

    /// <summary>
    /// Creates microsoft configuration with the invariants required by its callers.
    /// </summary>
    private static CalendarProviderConfiguration CreateMicrosoftConfiguration() => new(
        CalendarProviderKind.Microsoft,
        Environment.GetEnvironmentVariable("HAVEN_MICROSOFT_CALENDAR_CLIENT_ID")?.Trim(),
        new Uri("http://localhost:53683/oauth/microsoft/", UriKind.Absolute),
        ["openid", "offline_access", "User.Read", "Calendars.ReadWrite"]);
}
