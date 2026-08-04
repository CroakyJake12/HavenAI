using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
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

[Activity(
    Label = "Haven",
    Theme = "@android:style/Theme.Material.Light.NoActionBar",
    Icon = "@drawable/haven_icon",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode
        | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : AvaloniaMainActivity<App>
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Dispatcher.UIThread.Post(() => _ = InitialiseHavenAsync());
    }

    private static async Task InitialiseHavenAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not ISingleViewApplicationLifetime lifetime)
            return;

        var services = App.Services;
        if (services is null)
            return;

        var preferences = services.GetRequiredService<UserPreferencesService>();
        preferences.ApplyTheme("new-haven", save: false);

        var mainView = services.GetRequiredService<MainView>();
        mainView.ApplyEdition(HavenShellEdition.New);
        mainView.ApplyMobileLayout();
        lifetime.MainView = mainView;

        var recovery = services.GetRequiredService<IStartupRecoveryCoordinator>();

        try
        {
            var recoveryState = await recovery.BeginStartupAsync(CancellationToken.None);

            BrowserAutomationRegistry.Register(
                services.GetRequiredService<BrowserSessionService>(),
                services.GetRequiredService<IBrowserAutomationService>());

            var lifecycle = services.GetRequiredService<IApplicationLifecycle>();
            await lifecycle.CrashRecoveryAsync(CancellationToken.None);
            await lifecycle.StartupAsync(CancellationToken.None);
            await services.GetRequiredService<ModeSeedService>()
                .SeedBuiltInModesAsync(CancellationToken.None);

            var migration = await services.GetRequiredService<ILegacyStateMigrator>()
                .MigrateIfNeededAsync(CancellationToken.None);

            await mainView.InitializeAsync(migration, CancellationToken.None);
            await services.GetRequiredService<AutomationDeliveryController>()
                .StartAsync(CancellationToken.None);

            if (recoveryState.IsSafeMode)
            {
                services.GetRequiredService<NotificationService>().Show(
                    "Haven recovery safe mode",
                    recoveryState.Reason
                    + " Local chat and read-only workspace inspection remain available.",
                    ToastKind.Warning,
                    TimeSpan.FromSeconds(30));
            }

            await recovery.MarkStartupCompletedAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("[Haven Android startup] " + exception);
            services.GetRequiredService<NotificationService>().Show(
                "Haven could not finish starting",
                exception.Message,
                ToastKind.Error,
                TimeSpan.FromSeconds(30));
        }
    }
}
