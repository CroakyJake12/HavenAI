using Android.Content;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Android;

internal static class AndroidHavenBootstrap
{
    private static readonly SemaphoreSlim StartupGate = new(1, 1);
    private static bool _applicationStarted;
    private static string? _pendingSurface;
    private static string? _pendingPrompt;
    private static WeakReference<MainView>? _activeMainView;
    private static bool _activeMainViewReady;

    public static void SetLaunchRequest(Intent? intent)
    {
        _pendingSurface = intent?.GetStringExtra("haven_surface");
        _pendingPrompt = intent?.GetStringExtra("haven_prompt");
    }

    public static void ApplyLaunchRequest(Intent? intent)
    {
        var surface = intent?.GetStringExtra("haven_surface");
        var prompt = intent?.GetStringExtra("haven_prompt");
        if (string.IsNullOrWhiteSpace(surface) && string.IsNullOrWhiteSpace(prompt))
            return;

        if (_activeMainViewReady
            && _activeMainView is not null
            && _activeMainView.TryGetTarget(out var mainView))
        {
            Dispatcher.UIThread.Post(() => _ = ApplyLaunchRequestToMainViewAsync(mainView, surface, prompt));
            return;
        }

        _pendingSurface = surface;
        _pendingPrompt = prompt;
    }

    private static async Task ApplyLaunchRequestToMainViewAsync(
        MainView mainView,
        string? surface,
        string? prompt)
    {
        try
        {
            await mainView.ApplyMobileLaunchRequestAsync(surface, prompt);
        }
        catch (Exception exception)
        {
            AndroidRuntimeDiagnostics.Record(
                exception,
                "Applying an Android launcher request to the active Haven surface",
                showDialog: false);
        }
    }

    private static (string? Surface, string? Prompt) TakeLaunchRequest()
    {
        var request = (_pendingSurface, _pendingPrompt);
        _pendingSurface = null;
        _pendingPrompt = null;
        return request;
    }

    public static Control CreateMainView()
    {
        try
        {
            var services = App.Services
                ?? throw new InvalidOperationException(
                    "Haven services were not created before Android requested its main view.");

            var preferences = services.GetRequiredService<UserPreferencesService>();
            preferences.ApplyAppearance(preferences.Appearance, save: false);
            _ = services.GetRequiredService<AndroidNotificationBridge>();

            // Android can recreate an Activity. Create a fresh Avalonia control graph while
            // reusing Haven's application and infrastructure services.
            var mainView = ActivatorUtilities.CreateInstance<MainView>(services);
            _activeMainView = new WeakReference<MainView>(mainView);
            _activeMainViewReady = false;

            // Keep the uninitialised desktop shell hidden. New Haven creates repository-backed
            // pages, so it must only be applied after the database lifecycle has completed.
            mainView.IsVisible = false;

            Dispatcher.UIThread.Post(() => _ = InitializeMainViewAsync(mainView, services));
            return mainView;
        }
        catch (Exception exception)
        {
            AndroidRuntimeDiagnostics.Record(
                exception,
                "Haven main-view creation",
                showDialog: true);
            throw;
        }
    }

    private static async Task InitializeMainViewAsync(
        MainView mainView,
        IServiceProvider services)
    {
        try
        {
            await StartupGate.WaitAsync().ConfigureAwait(true);
            try
            {
                var recovery = services.GetRequiredService<IStartupRecoveryCoordinator>();
                StartupRecoveryState? recoveryState = null;

                if (!_applicationStarted)
                {
                    recoveryState = await recovery.BeginStartupAsync(CancellationToken.None);

                    BrowserAutomationRegistry.Register(
                        services.GetRequiredService<BrowserSessionService>(),
                        services.GetRequiredService<IBrowserAutomationService>());

                    var lifecycle = services.GetRequiredService<IApplicationLifecycle>();
                    await lifecycle.CrashRecoveryAsync(CancellationToken.None);
                    await lifecycle.StartupAsync(CancellationToken.None);
                    await services.GetRequiredService<ModeSeedService>()
                        .SeedBuiltInModesAsync(CancellationToken.None);

                    _applicationStarted = true;
                }

                // Apply the Android shell only after StartupAsync has created and migrated the
                // SQLite schema. Activity recreation still receives a fresh MainView instance.
                mainView.ApplyEdition(HavenShellEdition.New);
                mainView.ApplyMobileLayout();

                var migration = await services.GetRequiredService<ILegacyStateMigrator>()
                    .MigrateIfNeededAsync(CancellationToken.None);

                await mainView.InitializeAsync(migration, CancellationToken.None);

                var launchRequest = TakeLaunchRequest();
                await mainView.ApplyMobileLaunchRequestAsync(
                    launchRequest.Surface,
                    launchRequest.Prompt);
                mainView.IsVisible = true;
                _activeMainViewReady = true;

                var deferredLaunchRequest = TakeLaunchRequest();
                if (!string.IsNullOrWhiteSpace(deferredLaunchRequest.Surface)
                    || !string.IsNullOrWhiteSpace(deferredLaunchRequest.Prompt))
                {
                    await mainView.ApplyMobileLaunchRequestAsync(
                        deferredLaunchRequest.Surface,
                        deferredLaunchRequest.Prompt);
                }

                if (recoveryState?.IsSafeMode == true)
                {
                    services.GetRequiredService<NotificationService>().Show(
                        "Haven recovery safe mode",
                        recoveryState.Reason
                            + " Local chat and read-only workspace inspection remain available.",
                        ToastKind.Warning,
                        TimeSpan.FromSeconds(30));
                }

                if (recoveryState is not null)
                {
                    await recovery.MarkStartupCompletedAsync(CancellationToken.None);
                }
            }
            finally
            {
                StartupGate.Release();
            }
        }
        catch (Exception exception)
        {
            _activeMainViewReady = false;
            mainView.IsVisible = true;
            mainView.SetStartupError(exception.Message);
            AndroidRuntimeDiagnostics.Record(
                exception,
                "Haven service and main-view startup",
                showDialog: true);
            System.Diagnostics.Debug.WriteLine("[Haven Android startup] " + exception);

            services.GetService<NotificationService>()?.Show(
                "Haven could not finish starting",
                exception.Message,
                ToastKind.Error,
                TimeSpan.FromSeconds(30));
        }
    }
}
