using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Avalonia.Android;

namespace Haven.Android;

[Activity(
    Label = "Haven",
    Theme = "@style/Theme.AppCompat.Light.NoActionBar",
    Icon = "@drawable/haven_icon",
    MainLauncher = false,
    LaunchMode = global::Android.Content.PM.LaunchMode.SingleTop,
    Exported = false,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges =
        global::Android.Content.PM.ConfigChanges.Orientation
        | global::Android.Content.PM.ConfigChanges.ScreenSize
        | global::Android.Content.PM.ConfigChanges.SmallestScreenSize
        | global::Android.Content.PM.ConfigChanges.ScreenLayout
        | global::Android.Content.PM.ConfigChanges.UiMode
        | global::Android.Content.PM.ConfigChanges.Density
        | global::Android.Content.PM.ConfigChanges.FontScale
        | global::Android.Content.PM.ConfigChanges.Keyboard
        | global::Android.Content.PM.ConfigChanges.KeyboardHidden
        | global::Android.Content.PM.ConfigChanges.LayoutDirection)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        try
        {
            AndroidHavenBootstrap.SetLaunchRequest(Intent);
            base.OnCreate(savedInstanceState);
            Window?.SetSoftInputMode(SoftInput.AdjustResize);
            AndroidRuntimePermissions.Attach(this, isForeground: true);
            AndroidRuntimeDiagnostics.Attach(this);
        }
        catch (Exception exception)
        {
            RedirectToNativeRecovery(exception);
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        AndroidHavenBootstrap.ApplyLaunchRequest(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        Window?.SetSoftInputMode(SoftInput.AdjustResize);
        AndroidRuntimePermissions.SetForeground(this, isForeground: true);
        AndroidRuntimeDiagnostics.Attach(this);
    }

    protected override void OnPause()
    {
        AndroidRuntimePermissions.SetForeground(this, isForeground: false);
        AndroidRuntimeDiagnostics.Detach(this);
        base.OnPause();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, global::Android.Content.PM.Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        _ = AndroidRuntimePermissions.HandlePermissionResult(requestCode, grantResults);
    }

    private void RedirectToNativeRecovery(Exception exception)
    {
        AndroidRuntimePermissions.SetForeground(this, isForeground: false);
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
