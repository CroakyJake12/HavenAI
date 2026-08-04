using Android.App;
using Android.Runtime;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using Haven.Desktop;

namespace Haven.Android;

[Application]
public sealed class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();

        if (Avalonia.Application.Current?.ApplicationLifetime is IActivityApplicationLifetime lifetime)
        {
            lifetime.MainViewFactory = AndroidHavenBootstrap.CreateMainView;
        }
    }
}
