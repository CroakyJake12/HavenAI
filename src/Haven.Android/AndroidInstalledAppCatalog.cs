using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;

namespace Haven.Android;

internal sealed record AndroidInstalledApp(
    string Label,
    string PackageName,
    string ActivityName,
    Drawable? Icon)
{
    public string Key => PackageName + "/" + ActivityName;
}

internal static class AndroidInstalledAppCatalog
{
    public static IReadOnlyList<AndroidInstalledApp> Query(
        PackageManager? packageManager = null,
        string? excludePackageName = null,
        bool loadIcons = false)
    {
        var context = global::Android.App.Application.Context;
        var manager = packageManager ?? context.PackageManager;
        if (manager is null)
            return [];

        using var query = new Intent(Intent.ActionMain);
        query.AddCategory(Intent.CategoryLauncher);

#pragma warning disable CA1422
        var activities = manager.QueryIntentActivities(query, PackageInfoFlags.MatchAll);
#pragma warning restore CA1422

        return activities
            .Where(item => item.ActivityInfo is
            {
                PackageName: { Length: > 0 },
                Name: { Length: > 0 }
            })
            .Select(item =>
            {
                var info = item.ActivityInfo!;
                var packageName = info.PackageName!;
                var activityName = info.Name!;
                var loadedLabel = item.LoadLabel(manager)?.ToString();
                var label = string.IsNullOrWhiteSpace(loadedLabel) ? packageName : loadedLabel;
                var icon = loadIcons ? item.LoadIcon(manager) : null;
                return new AndroidInstalledApp(label, packageName, activityName, icon);
            })
            .Where(item => string.IsNullOrWhiteSpace(excludePackageName)
                || !string.Equals(item.PackageName, excludePackageName, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(item => item.Key)
            .OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.PackageName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ActivityName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
