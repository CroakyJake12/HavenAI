using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

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
public sealed class AndroidBootstrapActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var application = Application;
        if (application is not null)
        {
            AndroidRuntimeDiagnostics.Initialize(application);
        }

        ShowRecoveryOrLaunchHaven();
    }

    private void ShowRecoveryOrLaunchHaven()
    {
        if (AndroidRuntimeDiagnostics.TryReadReport(out var report))
        {
            AndroidRuntimeDiagnostics.ShowNativeRecoveryDialog(
                this,
                report,
                RetryHaven);
            return;
        }

        LaunchHaven();
    }

    private void RetryHaven()
    {
        LaunchHaven();
    }

    private void LaunchHaven()
    {
        try
        {
            var intent = new Intent(this, typeof(MainActivity));
            StartActivity(intent);
            Finish();
        }
        catch (Exception exception)
        {
            AndroidRuntimeDiagnostics.Record(
                exception,
                "Launching Haven from the native Android bootstrap activity",
                showDialog: false);

            if (AndroidRuntimeDiagnostics.TryReadReport(out var report))
            {
                AndroidRuntimeDiagnostics.ShowNativeRecoveryDialog(
                    this,
                    report,
                    RetryHaven);
            }
        }
    }
}
