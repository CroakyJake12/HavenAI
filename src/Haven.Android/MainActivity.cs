using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

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
            AndroidRuntimeDiagnostics.Record(
                exception,
                "Android main activity startup",
                showDialog: false);
            AndroidRuntimeDiagnostics.ShowStartupToast(this);
            throw;
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
}
