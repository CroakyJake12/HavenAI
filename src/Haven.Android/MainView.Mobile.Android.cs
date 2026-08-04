using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;

#if ANDROID
using Android.Content;
using Android.Content.PM;
#endif

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
#if ANDROID

    private sealed record AndroidLauncherApp(string Label, string PackageName, string ActivityName);

    private static IReadOnlyList<AndroidLauncherApp> GetInstalledAndroidApps()
    {
        var context = global::Android.App.Application.Context;
        var packageManager = context.PackageManager;
        if (packageManager is null)
            return [];

        var query = new Intent(Intent.ActionMain);
        query.AddCategory(Intent.CategoryLauncher);

#pragma warning disable CA1422
        var activities = packageManager.QueryIntentActivities(query, PackageInfoFlags.MatchAll);
#pragma warning restore CA1422

        return activities
            .Where(item => item.ActivityInfo is not null)
            .Select(item => new AndroidLauncherApp(
                item.LoadLabel(packageManager)?.ToString()
                    ?? item.ActivityInfo!.PackageName,
                item.ActivityInfo!.PackageName,
                item.ActivityInfo.Name))
            .Where(item => !string.Equals(
                item.PackageName,
                context.PackageName,
                StringComparison.OrdinalIgnoreCase))
            .DistinctBy(item => item.PackageName)
            .OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static void LaunchAndroidApp(AndroidLauncherApp app)
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryLauncher);
        intent.SetClassName(app.PackageName, app.ActivityName);
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

#endif
}
