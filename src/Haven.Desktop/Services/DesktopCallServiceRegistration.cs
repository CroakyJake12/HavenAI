using Haven.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

public static class DesktopCallServiceRegistration
{
    /// <summary>
    /// Replaces infrastructure fallbacks with Windows desktop implementations while
    /// preserving a single process-wide instance for Call, preview, Notes dictation
    /// and interruption.
    /// </summary>
    public static IServiceCollection AddHavenDesktopCallServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IScreenShareService, WindowsGraphicsCaptureService>();
        services.AddSingleton<WindowsNaturalSpeechOutputService>();
        services.AddSingleton<ISpeechOutputService>(provider =>
            provider.GetRequiredService<WindowsNaturalSpeechOutputService>());
        services.AddSingleton<CallVoicePreviewController>();
        services.AddSingleton<CallCompletionController>();
        services.AddSingleton<NotesDictationController>();
        return services;
    }
}
