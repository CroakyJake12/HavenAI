using Haven.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Android;

public static class AndroidServiceRegistration
{
    public static IServiceCollection AddHavenAndroidPlatformServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ISpeechInputService, AndroidSpeechInputService>();
        services.AddSingleton<AndroidNotificationBridge>();
        services.AddSingleton<AndroidAssistantOverlayCoordinator>();
        return services;
    }
}
