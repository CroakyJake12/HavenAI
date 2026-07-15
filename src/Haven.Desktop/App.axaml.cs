using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Haven.Application;
using Haven.Automations;
using Haven.Browser;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App : Avalonia.Application
{
    private ServiceProvider? _services;
    internal static IServiceProvider? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection();
        collection.AddHavenInfrastructure();
        collection.AddHavenPlannerInfrastructure();
        collection.AddSingleton<IScreenShareService, WindowsGraphicsCaptureService>();
        collection.AddHavenAutomations();
        collection.AddSingleton<BrowserSessionService>();
        collection.AddSingleton<BrowserDataService>();
        collection.AddSingleton<BrowserNavigationPolicy>();
        collection.AddSingleton<IBrowserNavigationPolicy>(provider => provider.GetRequiredService<BrowserNavigationPolicy>());
        collection.AddSingleton<BrowserAutomationStore>();
        collection.AddSingleton<IBrowserAutomationStore>(provider => provider.GetRequiredService<BrowserAutomationStore>());
        collection.AddSingleton<BrowserDownloadTransport>();
        collection.AddSingleton<BrowserBackgroundPageLoader>();
        collection.AddSingleton<BrowserAutomationService>();
        collection.AddSingleton<IBrowserAutomationService>(provider => provider.GetRequiredService<BrowserAutomationService>());
        collection.AddSingleton<IBrowserToolService>(provider => provider.GetRequiredService<BrowserSessionService>());
        collection.AddSingleton<BrowserToolRuntime>();
        collection.AddSingleton<AutomationToolRuntime>();
        collection.AddSingleton<CapabilityPreflightService>();
        collection.AddSingleton<WorkspaceToolRuntime>();
        collection.AddSingleton<ComputerToolRuntime>();
        collection.AddSingleton<ChatSessionService>(provider => new ChatSessionService(
            provider.GetRequiredService<IConversationRepository>(),
            provider.GetRequiredService<ProviderRoutingModelClient>(),
            provider.GetRequiredService<CapabilityPreflightService>(),
            provider.GetRequiredService<WorkspaceToolRuntime>(),
            provider.GetRequiredService<ComputerToolRuntime>(),
            provider.GetRequiredService<BrowserToolRuntime>(),
            provider.GetRequiredService<AutomationToolRuntime>()));
        collection.AddSingleton<UserPreferencesService>();
        collection.AddSingleton<ProjectCreationService>();
        collection.AddSingleton<NotificationService>();
        collection.AddTransient<ProviderConnectionsViewModel>();
        collection.AddSingleton<MainWindowViewModel>();
        _services = collection.BuildServiceProvider();
        Services = _services;
        BrowserAutomationRegistry.Register(
            _services.GetRequiredService<BrowserSessionService>(),
            _services.GetRequiredService<IBrowserAutomationService>());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = _services.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await InitialiseAsync(vm);
            desktop.Exit += async (_, _) =>
            {
                if (_services is not null)
                    await _services.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitialiseAsync(MainWindowViewModel vm)
    {
        try
        {
            var services = _services ?? throw new InvalidOperationException("Haven services have not been initialized.");
            var lifecycle = services.GetRequiredService<IApplicationLifecycle>();
            await lifecycle.CrashRecoveryAsync(CancellationToken.None).ConfigureAwait(false);
            await lifecycle.StartupAsync(CancellationToken.None).ConfigureAwait(false);
            await services.GetRequiredService<ModeSeedService>().SeedBuiltInModesAsync(CancellationToken.None).ConfigureAwait(false);
            var migration = await services.GetRequiredService<ILegacyStateMigrator>().MigrateIfNeededAsync(CancellationToken.None);
            await vm.InitializeAsync(migration, CancellationToken.None);
        }
        catch (Exception ex)
        {
            vm.SetStartupError(ex.Message);
        }
    }
}
