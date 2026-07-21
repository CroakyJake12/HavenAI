/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/DesktopCallServiceRegistration.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns DesktopCallServiceRegistration. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>
/// Represents desktop call service registration and keeps its related state and behavior together.
/// </summary>
public static class DesktopCallServiceRegistration
{
    /// <summary>
    /// Replaces infrastructure fallbacks with Windows desktop implementations while
    /// preserving a single process-wide instance for Call, preview, Notes dictation,
    /// Notes read aloud and interruption.
    /// </summary>
    public static IServiceCollection AddHavenDesktopCallServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IScreenShareService, WindowsGraphicsCaptureService>();
        services.AddSingleton<WindowsNaturalSpeechOutputService>();
        services.AddSingleton<HybridNaturalSpeechOutputService>();
        services.AddSingleton<ISpeechOutputService>(provider =>
            provider.GetRequiredService<HybridNaturalSpeechOutputService>());
        services.AddSingleton<ISpeechOutputWarmup>(provider =>
            provider.GetRequiredService<HybridNaturalSpeechOutputService>());

        // Call gets a deliberately smaller context and narrow instant social replies.
        // Re-registering these after Infrastructure keeps ordinary Chat/Studio on the
        // full IOllamaClient while the live voice surface uses the optimized wrapper.
        services.AddSingleton<CallOptimizedOllamaClient>();
        services.AddSingleton<CallCoordinator>(provider => new CallCoordinator(
            provider.GetRequiredService<ICallRepository>(),
            provider.GetRequiredService<IConversationRepository>(),
            provider.GetRequiredService<CallOptimizedOllamaClient>(),
            provider.GetRequiredService<ISpeechInputService>(),
            provider.GetRequiredService<ISpeechOutputService>(),
            provider.GetRequiredService<IScreenShareService>()));
        services.AddSingleton<ResponsiveCallCoordinator>();
        services.AddSingleton<ICallCoordinator>(provider =>
        {
            var models = provider.GetRequiredService<CallOptimizedOllamaClient>();
            _ = models.WarmDefaultSafelyAsync();
            return provider.GetRequiredService<ResponsiveCallCoordinator>();
        });

        services.AddSingleton<CallVoicePreviewController>();
        services.AddSingleton<CallCompletionController>();
        services.AddSingleton<NotesDictationController>();
        services.AddSingleton<NotesReadAloudController>();
        return services;
    }
}
