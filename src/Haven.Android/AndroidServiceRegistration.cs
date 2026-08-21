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
        services.RemoveAll<IDeviceActionProvider>();
        services.AddSingleton<IDeviceActionProvider, AndroidDeviceActionProvider>();
        services.AddSingleton<AndroidEncryptedPreferenceStore>();
        services.RemoveAll<IProviderSecretStore>();
        services.AddSingleton<IProviderSecretStore, AndroidProviderSecretStore>();
        services.RemoveAll<ICalendarTokenStore>();
        services.AddSingleton<ICalendarTokenStore, AndroidCalendarTokenStore>();
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
