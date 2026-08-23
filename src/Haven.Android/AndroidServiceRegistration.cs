using Haven.Application;
using Haven.Application.Automations;
using Haven.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Haven.Android;

public static class AndroidServiceRegistration
{
    public static IServiceCollection AddHavenAndroidPlatformServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<IComputerToolService>();
        services.AddSingleton<IComputerToolService, AndroidComputerToolService>();
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType == typeof(IDeviceActionProvider)
                && descriptor.ImplementationType == typeof(WindowsComputerDeviceActionProvider))
                services.RemoveAt(index);
        }
        services.AddSingleton<IDeviceActionProvider, AndroidDeviceActionProvider>();
        services.AddSingleton<AndroidEncryptedPreferenceStore>();
        services.RemoveAll<IProviderSecretStore>();
        services.AddSingleton<IProviderSecretStore, AndroidProviderSecretStore>();
        services.RemoveAll<ICalendarTokenStore>();
        services.AddSingleton<ICalendarTokenStore, AndroidCalendarTokenStore>();
        services.RemoveAll<IOAuthBrowserLauncher>();
        services.AddSingleton<IOAuthBrowserLauncher, AndroidOAuthBrowserLauncher>();
        services.RemoveAll<ICalendarOAuthClientIdProvider>();
        services.AddSingleton<ICalendarOAuthClientIdProvider, AndroidCalendarOAuthClientIdProvider>();
        services.RemoveAll<IScreenShareService>();
        services.AddSingleton<IScreenShareService, WindowsGraphicsCaptureService>();
        services.AddSingleton<ISpeechInputService, AndroidSpeechInputService>();
        services.AddSingleton<AndroidNotificationBridge>();
        services.AddSingleton<AndroidAssistantOverlayCoordinator>();
        services.AddSingleton<IProjectorExperienceProvider, BuiltInProjectorExperienceProvider>();
        services.AddSingleton<AndroidProjectorApplicationService>();
        services.AddSingleton<IProjectorExperienceProvider>(provider =>
            provider.GetRequiredService<AndroidProjectorApplicationService>());
        services.AddSingleton<IProjectorExperienceCatalog, ProjectorExperienceCatalog>();
        services.AddSingleton<IProjectorActionPlanner, ProjectorActionPlanner>();
        services.AddSingleton<IProjectorDisplayRegistry, ProjectorDisplayRegistry>();
        services.AddSingleton<IProjectorSessionCoordinator, ProjectorSessionCoordinator>();
        services.AddSingleton<IProjectorSessionRecoveryService, ProjectorSessionRecoveryService>();
        services.AddSingleton<AndroidProjectorControllerActionDispatcher>();
        services.AddSingleton<AndroidProjectorPresentationHostService>();
        services.AddSingleton<AndroidProjectorDisplayService>(provider =>
        {
            _ = provider.GetRequiredService<AndroidProjectorPresentationHostService>();
            return new AndroidProjectorDisplayService(provider.GetRequiredService<IProjectorDisplayRegistry>());
        });
        return services;
    }
}
