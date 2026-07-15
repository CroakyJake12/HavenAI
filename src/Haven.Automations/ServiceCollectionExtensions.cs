using Microsoft.Extensions.DependencyInjection;

namespace Haven.Automations;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHavenAutomations(this IServiceCollection services)
    {
        services.AddSingleton<ScheduleCalculator>();
        services.AddSingleton<AutomationRunner>();
        services.AddSingleton<WindowsAutomationRegistrationService>();
        return services;
    }
}
