using Android.App;
using Android.Runtime;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using Haven.Desktop;

namespace Haven.Android;

[Application]
public sealed class AndroidApp : AvaloniaAndroidApplication<App>
{
    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        AndroidRuntimeDiagnostics.Initialize(this);

        try
        {
            base.OnCreate();

            if (Avalonia.Application.Current?.ApplicationLifetime is IActivityApplicationLifetime lifetime)
            {
                lifetime.MainViewFactory = AndroidHavenBootstrap.CreateMainView;
            }
        }
        catch (Exception exception)
        {
            // Do not rethrow here. Android creates the native bootstrap activity after
            // Application.OnCreate, and that activity must remain available to display
            // the saved report even when Avalonia could not initialize.
            AndroidRuntimeDiagnostics.Record(
                exception,
                "Android application startup",
                showDialog: false);
            AndroidRuntimeDiagnostics.ShowStartupToast(this);
        }
    }
}
