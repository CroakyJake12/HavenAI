using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace Haven.Android;

[Activity(
    Label = "Haven",
    Theme = "@android:style/Theme.Material.Light.NoActionBar",
    Icon = "@drawable/haven_icon",
    MainLauncher = false,
    Exported = false,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode
        | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        try
        {
            base.OnCreate(savedInstanceState);
            AndroidRuntimeDiagnostics.Attach(this);
        }
        catch (Exception exception)
        {
            RedirectToNativeRecovery(exception);
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        AndroidRuntimeDiagnostics.Attach(this);
    }

    protected override void OnPause()
    {
        AndroidRuntimeDiagnostics.Detach(this);
        base.OnPause();
    }

    private void RedirectToNativeRecovery(Exception exception)
    {
        AndroidRuntimeDiagnostics.Record(
            exception,
            "Android Avalonia activity startup",
            showDialog: false);

        try
        {
            var intent = new Intent(this, typeof(AndroidBootstrapActivity));
            intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            StartActivity(intent);
        }
        catch (Exception redirectException)
        {
            AndroidRuntimeDiagnostics.Record(
                new AggregateException(exception, redirectException),
                "Redirecting to the native Android recovery activity",
                showDialog: false);
        }
        finally
        {
            Finish();
        }
    }
}
