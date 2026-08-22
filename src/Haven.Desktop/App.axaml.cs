using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App : Avalonia.Application
{
#if DEBUG
    private static int _developerToolsAttached;
#endif
    private ServiceProvider? _services;
    private IStartupRecoveryCoordinator? _startupRecovery;
    private IProductionDiagnostics? _productionDiagnostics;
    private bool _exceptionHooksAttached;
    internal static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        #if DEBUG
            // Avalonia's developer-tools service is process-wide. Headless tests create
            // multiple isolated App instances in one process, so attaching per instance
            // causes cleanup failures after otherwise successful tests.



            if (Interlocked.Exchange(ref _developerToolsAttached, 1) == 0)
                this.AttachDeveloperTools();
        #endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection();
        collection.AddHavenInfrastructure();
        collection.AddHavenPlannerInfrastructure();
        collection.AddHavenDesktopCallServices();
#if ANDROID
        global::Haven.Android.AndroidServiceRegistration.AddHavenAndroidPlatformServices(collection);
#endif
        collection.AddHavenMesh();
        collection.AddSingleton<ScheduledTaskScheduleCalculator>();
        collection.AddSingleton<ScheduledTaskRunner>();
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
        collection.AddSingleton(provider => new SafeModeBrowserAutomationService(
            provider.GetRequiredService<BrowserAutomationService>(),
            provider.GetRequiredService<IProductionDiagnostics>()));
        collection.AddSingleton(provider => new BrowserNativeDownloadAutomationService(
            provider.GetRequiredService<SafeModeBrowserAutomationService>(),
            provider.GetRequiredService<IBrowserNavigationPolicy>(),
            provider.GetRequiredService<IBrowserAutomationStore>()));
        collection.AddSingleton<IBrowserAutomationService>(provider => provider.GetRequiredService<BrowserNativeDownloadAutomationService>());
        collection.AddSingleton<IBrowserNativeDownloadService>(provider => provider.GetRequiredService<BrowserNativeDownloadAutomationService>());
        collection.AddSingleton<IBrowserToolService>(provider => provider.GetRequiredService<BrowserSessionService>());
        collection.AddSingleton<BrowserCompletionService>();
        collection.AddSingleton<BrowserToolRuntime>();
        collection.AddSingleton<AutomationToolRuntime>();
        collection.AddSingleton<CapabilityPreflightService>();
        collection.AddSingleton<TerminalCommandActivityHub>();
        collection.AddSingleton<WorkspaceToolRuntime>();
        collection.AddSingleton<ComputerToolRuntime>();
        collection.AddSingleton<ChatSessionService>(provider => new ChatSessionService(
            provider.GetRequiredService<IConversationRepository>(),
            provider.GetRequiredService<ProviderRoutingModelClient>(),
            provider.GetRequiredService<CapabilityPreflightService>(),
            provider.GetRequiredService<IConversationSafetyService>(),
            provider.GetRequiredService<WorkspaceToolRuntime>(),
            provider.GetRequiredService<ComputerToolRuntime>(),
            provider.GetRequiredService<BrowserToolRuntime>(),
            provider.GetRequiredService<AutomationToolRuntime>(),
            mcpTools: provider.GetRequiredService<McpToolRuntime>(),
            calendarTools: provider.GetRequiredService<CalendarConnectionToolRuntime>(),
            executionEvents: provider.GetRequiredService<IExecutionEventSink>()));
        collection.AddSingleton<UserPreferencesService>();
        collection.AddSingleton<Services.OllamaWakeService>();
        collection.AddSingleton<ProjectCreationService>();
        collection.AddSingleton<NotificationService>();
        collection.AddSingleton<ComputerUseOverlayCoordinator>();
#if !ANDROID
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayWorkspaceRegistry>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayContextActionCandidateService>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayForegroundContextCaptureService>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayVisualContextCaptureService>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayChatSessionFactory>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayGoSessionFactory>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayGlobalHotkey>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayWorkspaceController>();
#endif
        // Legacy automation delivery polling retired; Tasks owns execution state.
        
        collection.AddSingleton<FloatingActivityStateStore>();
        collection.AddSingleton<Haven.Desktop.Views.Pages.Imagine.VisionWorkspaceStateStore>();
        collection.AddSingleton<AgentTaskRuntimeService>();
#if ANDROID
        collection.AddSingleton<IFloatingActivityHost, global::Haven.Android.Compatibility.AndroidFloatingActivityHost>();
#else
        collection.AddSingleton<IFloatingActivityHost, DesktopFloatingActivityHost>();
#endif
        
        collection.AddSingleton<HavenEventBus>();
        collection.AddSingleton<Haven.Desktop.ViewModels.ProviderConnectionsViewModel>();
        collection.AddTransient<MainView>();
        collection.AddSingleton<WorkspaceSessionCoordinator>();
        collection.AddSingleton<WorkspaceWindowService>();
        _services = collection.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        Services = _services;
        _services.GetRequiredService<ComputerUseOverlayCoordinator>();
        Subscribe.EventBus = _services.GetRequiredService<HavenEventBus>();
        _startupRecovery = _services.GetRequiredService<IStartupRecoveryCoordinator>();
        _productionDiagnostics = _services.GetRequiredService<IProductionDiagnostics>();
        AttachExceptionHooks();

#if !ANDROID
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += OnDesktopExit;

            var preferences = _services.GetRequiredService<UserPreferencesService>();
            preferences.ApplyAppearance(preferences.Appearance, save: false);
            var mainView = _services.GetRequiredService<MainView>();
            mainView.ApplyEdition(HavenStartupExperiencePolicy.Edition);
            _services.GetRequiredService<WorkspaceSessionCoordinator>().Register(mainView, WorkspaceWindowKind.Main, queueSave: false);
            var window = new MainWindow(preferences) { DataContext = mainView, PreserveWorkspaceSessionOnClose = true };
            window.Opened += async (_, _) => await InitialiseHaven(mainView);
            window.Closed += (_, _) => desktop.Shutdown();
            desktop.MainWindow = window;
        }
#endif

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitialiseHaven(MainView shell)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        try
        {
            var services = _services ?? throw new InvalidOperationException("Haven services have not been initialized.");
            var recovery = _startupRecovery ?? services.GetRequiredService<IStartupRecoveryCoordinator>();
            var recoveryState = await recovery.BeginStartupAsync(CancellationToken.None);
            BrowserAutomationRegistry.Register(
                services.GetRequiredService<BrowserSessionService>(),
                services.GetRequiredService<IBrowserAutomationService>());

            var lifecycle = services.GetRequiredService<IApplicationLifecycle>();
            await lifecycle.CrashRecoveryAsync(CancellationToken.None);
            await lifecycle.StartupAsync(CancellationToken.None);
            await services.GetRequiredService<ModeSeedService>().SeedBuiltInModesAsync(CancellationToken.None);
            var migration = await services.GetRequiredService<ILegacyStateMigrator>().MigrateIfNeededAsync(CancellationToken.None);
            await shell.InitializeAsync(migration, CancellationToken.None);
            await shell.RestoreWorkspaceSessionAsync(CancellationToken.None);

            if (!recoveryState.IsSafeMode)
            {
                try
                {
                    await services.GetRequiredService<MeshCoordinator>().InitialiseAsync(CancellationToken.None);
                }
                catch (Exception meshException)
                {
                    try
                    {
                        await (_productionDiagnostics ?? services.GetRequiredService<IProductionDiagnostics>()).WriteAsync(
                            ReliabilitySeverity.Warning,
                            "mesh",
                            "startup-unavailable",
                            meshException.ToString(),
                            cancellationToken: CancellationToken.None);
                    }
                    catch
                    {
                        // Mesh is optional at startup; diagnostics must not turn it into an app-start failure.
                    }
                }
            }
#if !ANDROID
            await services.GetRequiredService<Haven.Desktop.Overlay.OverlayWorkspaceController>().InitializeAsync(CancellationToken.None);
#endif
            // Scheduled Tasks have no parallel automation delivery loop.

            if (recoveryState.IsSafeMode)
            {
                services.GetRequiredService<NotificationService>().Show(
                    "Haven recovery safe mode",
                    recoveryState.Reason + " Local Ollama chat and read-only workspace inspection remain available.",
                    ToastKind.Warning,
                    TimeSpan.FromSeconds(30));
            }

            await recovery.MarkStartupCompletedAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            try
            {
                await LogExceptionAsync("startup-failed", ex, correlationId);
            }
            catch
            {
                // The diagnostics sink must not hide the primary startup failure.
            }

            var userMessage = $"Haven could not finish starting. Diagnostic reference: {correlationId}.";
            shell.SetStartupError(userMessage);
            try
            {
                _services?.GetService<NotificationService>()?.Show(
                    "Haven startup problem",
                    userMessage,
                    ToastKind.Error,
                    TimeSpan.FromSeconds(30));
            }
            catch
            {
                // The persistent shell status remains available if toast delivery fails.
            }
        }
    }

    private async void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        var services = _services;
        var recovery = _startupRecovery;
        try
        {
            if (_productionDiagnostics is not null)
            {
                await _productionDiagnostics.WriteAsync(
                    ReliabilitySeverity.Information,
                    "desktop",
                    "shutdown-begin",
                    "Haven began coordinated shutdown.",
                    cancellationToken: CancellationToken.None);
            }

            if (recovery is not null)
                await recovery.MarkCleanShutdownAsync(CancellationToken.None);
            if (services is not null)
                await services.DisposeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Haven shutdown] " + ex);
        }
        finally
        {
            Subscribe.EventBus?.Dispose();
            Subscribe.EventBus = null;
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
        try { _ = LogExceptionAsync(eventArgs.IsTerminating ? "unhandled-terminating" : "unhandled", exception, Guid.NewGuid().ToString("N")); }
        catch { }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        try { _ = LogExceptionAsync("unobserved-task", eventArgs.Exception, Guid.NewGuid().ToString("N")); }
        catch { }
        finally { eventArgs.SetObserved(); }
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
