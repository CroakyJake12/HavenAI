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
    private IStartupRecoveryCoordinator? _startupRecovery;
    private IProductionDiagnostics? _productionDiagnostics;
    private bool _exceptionHooksAttached;
    internal static IServiceProvider? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection();
        collection.AddHavenInfrastructure();
        collection.AddHavenPlannerInfrastructure();
        collection.AddHavenDesktopCallServices();
        collection.AddHavenAutomations();
        collection.AddSingleton<BrowserSessionService>();
        collection.AddSingleton<BrowserDataService>();
        collection.AddSingleton<BrowserNavigationPolicy>();
        collection.AddSingleton<IBrowserNavigationPolicy>(provider => provider.GetRequiredService<BrowserNavigationPolicy>());
        collection.AddSingleton<BrowserAutomationStore>();
        collection.AddSingleton<IBrowserAutomationStore>(provider => provider.GetRequiredService<BrowserAutomationStore>());
        collection.AddSingleton<BrowserDownloadTransport>();
        collection.AddSingleton<BrowserBackgroundPageLoader>();
        collection.AddSingleton(provider => new BrowserAutomationService(
            provider.GetRequiredService<BrowserSessionService>(),
            provider.GetRequiredService<IBrowserNavigationPolicy>(),
            provider.GetRequiredService<IBrowserAutomationStore>(),
            provider.GetRequiredService<BrowserDownloadTransport>(),
            provider.GetRequiredService<BrowserBackgroundPageLoader>()));
        collection.AddSingleton<SafeModeBrowserAutomationService>();
        collection.AddSingleton<IBrowserAutomationService>(provider => provider.GetRequiredService<SafeModeBrowserAutomationService>());
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
        collection.AddSingleton<AutomationDeliveryController>();
        collection.AddSingleton<GenerativeUiThemeRuntime>();
        collection.AddSingleton<IGenerativeUiRuntime>(provider => provider.GetRequiredService<GenerativeUiThemeRuntime>());
        collection.AddTransient<ProviderConnectionsViewModel>();
        collection.AddSingleton<MainWindowViewModel>();
        _services = collection.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        Services = _services;
        _startupRecovery = _services.GetRequiredService<IStartupRecoveryCoordinator>();
        _productionDiagnostics = _services.GetRequiredService<IProductionDiagnostics>();
        AttachExceptionHooks();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = _services.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await InitialiseAsync(vm);
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitialiseAsync(MainWindowViewModel vm)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        try
        {
            var services = _services ?? throw new InvalidOperationException("Haven services have not been initialized.");
            var recovery = _startupRecovery ?? services.GetRequiredService<IStartupRecoveryCoordinator>();
            var recoveryState = await recovery.BeginStartupAsync(CancellationToken.None);
            await services.GetRequiredService<GenerativeUiThemeRuntime>().InitializeAsync(CancellationToken.None);

            BrowserAutomationRegistry.Register(
                services.GetRequiredService<BrowserSessionService>(),
                services.GetRequiredService<IBrowserAutomationService>());

            var lifecycle = services.GetRequiredService<IApplicationLifecycle>();
            await lifecycle.CrashRecoveryAsync(CancellationToken.None);
            await lifecycle.StartupAsync(CancellationToken.None);
            await services.GetRequiredService<ModeSeedService>().SeedBuiltInModesAsync(CancellationToken.None);
            var migration = await services.GetRequiredService<ILegacyStateMigrator>().MigrateIfNeededAsync(CancellationToken.None);
            await vm.InitializeAsync(migration, CancellationToken.None);
            await services.GetRequiredService<AutomationDeliveryController>().StartAsync(CancellationToken.None);

            if (recoveryState.IsSafeMode)
            {
                services.GetRequiredService<NotificationService>().Show(
                    "Haven recovery safe mode",
                    recoveryState.Reason + " Local Ollama chat and read-only workspace inspection remain available.",
                    ToastKind.Warning,
                    TimeSpan.FromSeconds(30));
            }

            await recovery.MarkStartupCompletedAsync(CancellationToken.None);
            if (_productionDiagnostics is not null)
            {
                await _productionDiagnostics.WriteAsync(
                    ReliabilitySeverity.Information,
                    "desktop",
                    "startup-ready",
                    recoveryState.IsSafeMode ? "Haven started in recovery safe mode." : "Haven completed desktop startup.",
                    new Dictionary<string, string> { ["safeMode"] = recoveryState.IsSafeMode.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    correlationId,
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            await LogExceptionAsync("startup-failed", ex, correlationId);
            vm.SetStartupError(ex.Message);
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        var services = _services;
        var recovery = _startupRecovery;
        try
        {
            if (_productionDiagnostics is not null)
            {
                _productionDiagnostics.WriteAsync(
                    ReliabilitySeverity.Information,
                    "desktop",
                    "shutdown-begin",
                    "Haven began coordinated shutdown.",
                    cancellationToken: CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }

            if (services is not null)
                services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (recovery is not null)
                recovery.MarkCleanShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Haven shutdown] " + ex);
        }
        finally
        {
            _services = null;
            Services = null;
            _productionDiagnostics = null;
            _startupRecovery = null;
            DetachExceptionHooks();
        }
    }

    private void AttachExceptionHooks()
    {
        if (_exceptionHooksAttached) return;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        _exceptionHooksAttached = true;
    }

    private void DetachExceptionHooks()
    {
        if (!_exceptionHooksAttached) return;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _exceptionHooksAttached = false;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        var exception = eventArgs.ExceptionObject as Exception
                        ?? new InvalidOperationException("The runtime reported a non-Exception unhandled failure.");
        try
        {
            LogExceptionAsync(
                eventArgs.IsTerminating ? "unhandled-terminating" : "unhandled",
                exception,
                Guid.NewGuid().ToString("N")).GetAwaiter().GetResult();
        }
        catch { }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        try
        {
            LogExceptionAsync("unobserved-task", eventArgs.Exception, Guid.NewGuid().ToString("N")).GetAwaiter().GetResult();
        }
        catch { }
        finally
        {
            eventArgs.SetObserved();
        }
    }

    private async Task LogExceptionAsync(string eventName, Exception exception, string correlationId)
    {
        if (_productionDiagnostics is null) return;
        await _productionDiagnostics.WriteAsync(
            ReliabilitySeverity.Critical,
            "desktop",
            eventName,
            exception.ToString(),
            new Dictionary<string, string>
            {
                ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
                ["hResult"] = exception.HResult.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            correlationId,
            CancellationToken.None);
    }
}
