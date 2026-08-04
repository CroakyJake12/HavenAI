using Android.App;
using Android.Content.PM;
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
}
