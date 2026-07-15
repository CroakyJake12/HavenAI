using Haven.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHavenInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<SqliteDatabase>();
        services.AddSingleton<IAppDatabase>(provider => provider.GetRequiredService<SqliteDatabase>());
        services.AddSingleton<ISqliteConnectionFactory>(provider => provider.GetRequiredService<SqliteDatabase>());
        services.AddSingleton<IConversationRepository, ConversationRepository>();
        services.AddSingleton<IContainerRepository, ContainerRepository>();
        services.AddSingleton<IContainerResourceRepository, ContainerResourceRepository>();
        services.AddSingleton<ICatalogRepository, CatalogRepository>();
        services.AddSingleton<IWorkspaceStateRepository, WorkspaceStateRepository>();
        services.AddSingleton<IProjectIntelligenceService, ProjectIntelligenceService>();
        services.AddSingleton<IAutomationRepository, AutomationRepository>();
        services.AddSingleton<ITrainingRepository, TrainingRepository>();
        services.AddSingleton<IDashboardRepository, DashboardRepository>();
        services.AddSingleton<IDashboardLayoutRepository, DashboardLayoutRepository>();
        services.AddSingleton<IDashboardTileProviderRegistry, DashboardTileProviderRegistry>();
        services.AddSingleton<ICallRepository, CallRepository>();
        services.AddSingleton<ISpeechInputService, WindowsSpeechInputService>();
        services.AddSingleton<ISpeechOutputService, SystemSpeechOutputService>();
        services.AddSingleton<IScreenShareService, UnsupportedScreenShareService>();
        services.AddHttpClient<ISpeechModelManager, WhisperModelManager>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<ICallCoordinator, CallCoordinator>();
        services.AddSingleton<ILegacyStateMigrator, LegacyStateMigrator>();
        services.AddSingleton<IWorkspaceToolService, WorkspaceToolService>();
        services.AddSingleton<IComputerToolService, WindowsComputerToolService>();
        services.AddHttpClient<IOllamaClient, OllamaClient>(client =>
        {
            var endpoint = Environment.GetEnvironmentVariable("OLLAMA_HOST")?.Trim();
            if (string.IsNullOrWhiteSpace(endpoint)) endpoint = "http://127.0.0.1:11434/";
            if (!endpoint.EndsWith("/", StringComparison.Ordinal)) endpoint += "/";
            client.BaseAddress = new Uri(endpoint, UriKind.Absolute);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.AddHttpClient("Haven.ModelProvider.openai");
        services.AddHttpClient("Haven.ModelProvider.openrouter");
        services.AddHttpClient("Haven.ModelProvider.openai-compatible");
        services.AddHttpClient("Haven.ModelProvider.anthropic");
        services.AddHttpClient("Haven.ModelProvider.gemini");
        services.AddSingleton<IProviderConfigurationStore, ProviderConfigurationStore>();
        services.AddSingleton<IProviderSecretStore, WindowsProviderSecretStore>();
        services.AddSingleton<IModelProvider, OllamaModelProvider>();
        services.AddSingleton<IModelProvider, OpenAiModelProvider>();
        services.AddSingleton<IModelProvider, OpenRouterModelProvider>();
        services.AddSingleton<IModelProvider, CustomOpenAiCompatibleModelProvider>();
        services.AddSingleton<IModelProvider, AnthropicModelProvider>();
        services.AddSingleton<IModelProvider, GeminiModelProvider>();
        services.AddSingleton<IModelProviderRegistry, ModelProviderRegistry>();
        services.AddSingleton<IModelRouter, ModelRouter>();

        services.AddSingleton<IModeRegistry, ModeRegistry>();
        services.AddSingleton<IModeUsageRepository, ModeUsageRepository>();
        services.AddSingleton<IPinRepository, PinRepository>();
        services.AddSingleton<ISurfaceRouter, SurfaceRouter>();
        services.AddSingleton<IModeIntentRouter, IntentRouter>();
        services.AddSingleton<IActivityLogRepository, ActivityLogRepository>();
        services.AddSingleton<IConversationMoveRepository, ConversationMoveRepository>();
        services.AddSingleton<ICompanionDockService, CompanionDockService>();
        services.AddSingleton<IBrowserTabHostManager, BrowserTabHostManager>();
        services.AddSingleton<IPlatformShellService, PlatformShellService>();
        services.AddSingleton<IAppDiagnostics, AppDiagnostics>();
        services.AddSingleton<IAppCommandRegistry, AppCommandRegistry>();
        services.AddSingleton<SurfaceOrchestrationService>();
        services.AddSingleton<ModeSeedService>();
        services.AddSingleton<ModeManifestValidator>();
        services.AddSingleton<ModeCreationService>();
        services.AddSingleton<BrowserCompletionService>();
        services.AddSingleton<FilesystemActionService>();
        services.AddSingleton<SettingsEncryptionService>();
        services.AddSingleton<SettingsExportService>();
        services.AddSingleton<KeybindingService>();
        services.AddSingleton<AccessibilityService>();
        services.AddSingleton<DiagnosticsService>();
        services.AddSingleton<IConversationPlacementService, ConversationPlacementService>();
        services.AddSingleton<IModePackageValidator, ModePackageValidator>();
        services.AddSingleton<IModePackageInstaller, ModePackageInstaller>();
        services.AddSingleton<IVersionedSettingsStore, VersionedAtomicSettingsStore>();
        services.AddSingleton<IApplicationLifecycle, ApplicationLifecycleService>();
        services.AddSingleton<ISingleInstanceService, SingleInstanceService>();
        return services;
    }
}
