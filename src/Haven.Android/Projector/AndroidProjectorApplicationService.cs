using Android.App;
using Android.Content;
using Android.Content.PM;
using Haven.Application;

namespace Haven.Android;

public sealed record AndroidProjectorLaunchResult(bool Started, string? Error);

public sealed class AndroidProjectorApplicationService : IProjectorExperienceProvider
{
    private const string ExperiencePrefix = "android-app:";
    private const string RuntimePrefix = "android-display:";

    private readonly object _gate = new();
    private readonly Dictionary<string, AndroidInstalledApp> _apps = new(StringComparer.Ordinal);

    public ValueTask<IReadOnlyList<ProjectorExperience>> GetExperiencesAsync(
        ProjectorSessionSnapshot? session,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var apps = RefreshApps();
        IReadOnlyList<ProjectorExperience> experiences = apps
            .Select(app => new ProjectorExperience(
                ExperienceId(app),
                app.Label,
                "Open " + app.Label + " on this Projector display.",
                "apps",
                ArtworkKey: null,
                ProjectorExperienceSource.Application,
                ProjectorLaunchStrategy.AndroidApplication,
                ProjectorInteractionProfile.Mixed,
                ProjectorExperiencePersistence.Session,
                [ProjectorCapability.LaunchAndroidActivity]))
            .ToArray();
        return new ValueTask<IReadOnlyList<ProjectorExperience>>(experiences);
    }

    public AndroidProjectorLaunchResult TryLaunch(ProjectorExperience experience, ProjectorDisplay display)
    {
        ArgumentNullException.ThrowIfNull(experience);
        ArgumentNullException.ThrowIfNull(display);

        if (experience.Source != ProjectorExperienceSource.Application
            || experience.LaunchStrategy != ProjectorLaunchStrategy.AndroidApplication
            || !experience.Id.StartsWith(ExperiencePrefix, StringComparison.Ordinal))
        {
            return new(false, "This Projector experience is not an Android application.");
        }

        if (!experience.IsAllowedOn(display.Trust))
        {
            return new(false, $"{experience.Name} is not allowed on a {display.Trust.ToString().ToLowerInvariant()} Projector display. Change display trust on the phone first.");
        }

        if (display.Capabilities.LaunchAndroidActivity != ProjectorCapabilityState.Available)
            return new(false, "Android has not proven secondary-display app launching for this Projector target.");

        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            return new(false, "Per-app secondary-display launch permission cannot be proven on this Android version.");

        if (!TryGetDisplayId(display.RuntimeId, out var displayId))
            return new(false, "The Projector target does not expose a valid Android display id.");

        var context = global::Android.App.Application.Context;
        var packageManager = context.PackageManager;
        if (packageManager is null
            || !packageManager.HasSystemFeature(PackageManager.FeatureActivitiesOnSecondaryDisplays))
        {
            return new(false, "This Android device does not advertise activity support on secondary displays.");
        }

        var app = ResolveApp(experience.Id);
        if (app is null)
            return new(false, "That Android application is no longer available.");

        using var intent = new Intent(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryLauncher);
        intent.SetClassName(app.PackageName, app.ActivityName);
        intent.AddFlags(ActivityFlags.NewTask);

        var activityManager = context.GetSystemService(Context.ActivityService) as ActivityManager;
        if (activityManager is null)
            return new(false, "Android ActivityManager is unavailable.");

        if (!activityManager.IsActivityStartAllowedOnDisplay(context, displayId, intent))
            return new(false, app.Label + " is not allowed to open on this display.");

        try
        {
            using var options = ActivityOptions.MakeBasic();
            if (options is null)
                return new(false, "Android could not create launch options for the Projector display.");
            options.SetLaunchDisplayId(displayId);
            context.StartActivity(intent, options.ToBundle());
            return new(true, null);
        }
        catch (Exception exception)
        {
            return new(false, "Android rejected the Projector app launch: " + exception.Message);
        }
    }

    public static ProjectorCapabilityState SecondaryDisplayLaunchCapability(bool isPresentationDisplay)
    {
        if (!isPresentationDisplay)
            return ProjectorCapabilityState.Unavailable;
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            return ProjectorCapabilityState.Unknown;

        var packageManager = global::Android.App.Application.Context.PackageManager;
        if (packageManager is null)
            return ProjectorCapabilityState.Unknown;
        return packageManager.HasSystemFeature(PackageManager.FeatureActivitiesOnSecondaryDisplays)
            ? ProjectorCapabilityState.Available
            : ProjectorCapabilityState.Unavailable;
    }

    private IReadOnlyList<AndroidInstalledApp> RefreshApps()
    {
        var context = global::Android.App.Application.Context;
        var apps = AndroidInstalledAppCatalog.Query(
            excludePackageName: context.PackageName,
            loadIcons: false);
        lock (_gate)
        {
            _apps.Clear();
            foreach (var app in apps)
                _apps[ExperienceId(app)] = app;
        }
        return apps;
    }

    private AndroidInstalledApp? ResolveApp(string experienceId)
    {
        lock (_gate)
        {
            if (_apps.TryGetValue(experienceId, out var cached))
                return cached;
        }

        RefreshApps();
        lock (_gate)
            return _apps.TryGetValue(experienceId, out var refreshed) ? refreshed : null;
    }

    private static string ExperienceId(AndroidInstalledApp app) => ExperiencePrefix + app.Key;

    private static bool TryGetDisplayId(string runtimeId, out int displayId)
    {
        displayId = -1;
        return runtimeId.StartsWith(RuntimePrefix, StringComparison.Ordinal)
            && int.TryParse(runtimeId.AsSpan(RuntimePrefix.Length), out displayId)
            && displayId >= 0;
    }
}
