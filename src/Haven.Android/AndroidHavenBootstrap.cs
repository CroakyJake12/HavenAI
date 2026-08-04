using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Automations;
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

    public static Control CreateMainView()
    {
        try
        {
            var services = App.Services
                ?? throw new InvalidOperationException(
                    "Haven services were not created before Android requested its main view.");

            var preferences = services.GetRequiredService<UserPreferencesService>();
            preferences.ApplyTheme("new-haven", save: false);

            // Android can recreate an Activity. Create a fresh Avalonia control graph while
            // reusing Haven's application and infrastructure services.
            var mainView = ActivatorUtilities.CreateInstance<MainView>(services);
            mainView.ApplyEdition(HavenShellEdition.New);
            mainView.ApplyMobileLayout();

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
                    await services.GetRequiredService<AutomationDeliveryController>()
                        .StartAsync(CancellationToken.None);

                    _applicationStarted = true;
                }

                var migration = await services.GetRequiredService<ILegacyStateMigrator>()
                    .MigrateIfNeededAsync(CancellationToken.None);

                await mainView.InitializeAsync(migration, CancellationToken.None);

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
                    await recovery.MarkStartupCompletedAsync(CancellationToken.None);
            }
            finally
            {
                StartupGate.Release();
            }
        }
        catch (Exception exception)
        {
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
