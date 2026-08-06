using Android.Content;
using Android.Content.PM;
using Haven.Core;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private sealed record AndroidLauncherApp(string Label, string PackageName, string ActivityName);

    private IReadOnlyList<AndroidLauncherApp>? _installedAndroidApps;

    private async Task<IReadOnlyList<AndroidLauncherApp>> GetInstalledAndroidAppsAsync()
    {
        if (_installedAndroidApps is not null)
            return _installedAndroidApps;

        _installedAndroidApps = await Task.Run(QueryInstalledAndroidApps);
        return _installedAndroidApps;
    }

    private static IReadOnlyList<AndroidLauncherApp> QueryInstalledAndroidApps()
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
                var loadedLabel = item.LoadLabel(packageManager)?.ToString();
                var label = string.IsNullOrWhiteSpace(loadedLabel) ? packageName : loadedLabel;
                return new AndroidLauncherApp(label, packageName, activityName);
            })
            .Where(item => !string.Equals(
                item.PackageName,
                context.PackageName,
                StringComparison.OrdinalIgnoreCase))
            .DistinctBy(item => item.PackageName)
            .OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<ModeDefinition>> GetInstalledAndroidAppDefinitionsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        return (await GetInstalledAndroidAppsAsync())
            .Select(app => new ModeDefinition(
                Guid.NewGuid(),
                "android-app:" + app.PackageName,
                app.Label,
                "Connected Android app: " + app.PackageName,
                "apps",
                HavenMode.Chat,
                "[]",
                "[]",
                "[]",
                "[]",
                $"The user connected {app.Label} ({app.PackageName}) to this chat. " +
                "Use supported Android intents, content providers, or a documented web API. " +
                "Do not launch the app merely because it is connected. Explain limitations when no safe integration is available.",
                ModeSource.Created,
                ModeInstallState.InstalledByUser,
                "Android",
                "1",
                "[\"android\",\"connected-app\"]",
                now,
                now))
            .ToArray();
    }

    private static bool IsAndroidAppDefinition(ModeDefinition mode)
        => mode.Key.StartsWith("android-app:", StringComparison.OrdinalIgnoreCase);

    private async Task ConnectAndroidAppDefinitionAsync(ModeDefinition mode)
    {
        var packageName = mode.Key["android-app:".Length..];
        var app = (await GetInstalledAndroidAppsAsync())
            .FirstOrDefault(item => string.Equals(
                item.PackageName,
                packageName,
                StringComparison.OrdinalIgnoreCase));

        if (app is not null)
            await ConnectAndroidAppToChatAsync(app);
    }

    private Task ConnectAndroidAppToChatAsync(AndroidLauncherApp app)
        => OpenNewChatAsync(
            $"Connected Android app: {app.Label} ({app.PackageName}). " +
            "Treat the next instructions as involving this app. Prefer supported Android intents, content providers, " +
            "or a documented web API; do not launch the app merely because it is connected. " +
            "When no supported integration exists, explain the limitation and offer a draft the user can paste.");

    private static void LaunchAndroidHomeChooser()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(Haven.Android.HavenLauncherActivity));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    private static void LaunchAndroidModelImporter()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(Haven.Android.ModelImportActivity));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
