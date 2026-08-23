using Android.Content;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Android;

public sealed class AndroidComputerToolService : IComputerToolService
{
    public Task<string> SnapshotAsync(CancellationToken cancellationToken) =>
        Unsupported("Android screen inspection requires an explicit MediaProjection or accessibility-service consent flow.", cancellationToken);

    public Task<string> ListWindowsAsync(CancellationToken cancellationToken) =>
        Unsupported("Android does not expose another app's window list to ordinary applications.", cancellationToken);

    public Task<string> LaunchAppAsync(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requested = name?.Trim();
        if (string.IsNullOrWhiteSpace(requested)) throw new ArgumentException("An application name or package is required.", nameof(name));
        var app = AndroidInstalledAppCatalog.Query().FirstOrDefault(item =>
            item.PackageName.Equals(requested, StringComparison.OrdinalIgnoreCase)
            || item.Label.Equals(requested, StringComparison.CurrentCultureIgnoreCase));
        if (app is null) throw new InvalidOperationException($"No launchable Android application named '{requested}' was found.");

        using var intent = new Intent(Intent.ActionMain);
        intent.SetClassName(app.PackageName, app.ActivityName);
        intent.AddCategory(Intent.CategoryLauncher);
        intent.AddFlags(ActivityFlags.NewTask);
        global::Android.App.Application.Context.StartActivity(intent);
        return Task.FromResult($"Launched {app.Label} ({app.PackageName}).");
    }

    public Task<string> FocusWindowAsync(string title, CancellationToken cancellationToken) =>
        Unsupported("Android does not allow Haven to focus another app window without launching an activity.", cancellationToken);
    public Task<string> InvokeAsync(string windowTitle, string name, string automationId, CancellationToken cancellationToken) =>
        Unsupported("Android UI invocation requires an explicitly enabled accessibility service and is not silently enabled.", cancellationToken);
    public Task<string> ClickAsync(string windowTitle, int x, int y, string button, CancellationToken cancellationToken) =>
        Unsupported("Android coordinate injection is not available to ordinary applications.", cancellationToken);
    public Task<string> TypeAsync(string windowTitle, string text, CancellationToken cancellationToken) =>
        Unsupported("Android text injection requires an explicitly enabled accessibility service and is not silently enabled.", cancellationToken);
    public Task<string> PressAsync(string windowTitle, string keys, CancellationToken cancellationToken) =>
        Unsupported("Android key injection is not available to ordinary applications.", cancellationToken);
    public Task<string> CloseWindowAsync(string title, CancellationToken cancellationToken) =>
        Unsupported("Android does not allow Haven to close another application's task.", cancellationToken);

    private static Task<string> Unsupported(string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<string>(new PlatformNotSupportedException(message));
    }
}

public sealed class AndroidDeviceActionProvider(
    IComputerToolService computer,
    AndroidProjectorPresentationHostService projector) : IDeviceActionProvider
{
    public const string NativeProviderId = "haven.device.android";
    public string ProviderId => NativeProviderId;

    public bool CanHandle(DeviceTargetDescriptor target) =>
        target.Kind == DeviceTargetKind.CurrentDevice
        && target.Platform == CapabilityPlatform.Android
        && (string.IsNullOrWhiteSpace(target.ProviderId) || target.ProviderId.Equals(ProviderId, StringComparison.OrdinalIgnoreCase));

    public Task<DeviceCapabilitySnapshot> GetSnapshotAsync(DeviceTargetDescriptor target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var projectorAvailability = projector.GetRoutableRemoteTargets().Count > 0
            ? DeviceActionAvailability.PermissionRequired
            : DeviceActionAvailability.Unsupported;
        IReadOnlyList<DeviceActionDescriptor> actions =
        [
            new("applications.launch", "Launch application", "Applications", AndroidProjectorRemoteExperienceService.CapabilityKey, "android.intent.launch", ProviderId, DeviceActionAvailability.PermissionRequired, ["name"]),
            new(AndroidProjectorRemoteExperienceService.EnumerateTargetsActionKey, "List Projector targets", "Projector", AndroidProjectorRemoteExperienceService.CapabilityKey, "android.projector.targets", ProviderId, projectorAvailability, []),
            new(AndroidProjectorRemoteExperienceService.RouteExperienceActionKey, "Route Projector experience", "Projector", AndroidProjectorRemoteExperienceService.CapabilityKey, "android.projector.route", ProviderId, projectorAvailability, ["runtimeId", "experienceId"]),
            new("ui.snapshot", "Inspect current screen", "Screen", AndroidProjectorRemoteExperienceService.CapabilityKey, "android.media-projection", ProviderId, DeviceActionAvailability.Unsupported, []),
            new("ui.invoke", "Invoke another app control", "Accessibility", AndroidProjectorRemoteExperienceService.CapabilityKey, "android.accessibility", ProviderId, DeviceActionAvailability.Unsupported, ["name"])
        ];
        return Task.FromResult(new DeviceCapabilitySnapshot(target, true, DateTimeOffset.UtcNow, actions));
    }

    public async Task<DeviceActionResult> ExecuteAsync(DeviceActionRequest request, CancellationToken cancellationToken)
    {
        if (!CanHandle(request.Target))
            return Result(request, DeviceActionResultStatus.DeviceUnavailable, "This provider does not own the selected Android target.");
        if (!request.PermissionGranted)
            return Result(request, DeviceActionResultStatus.PermissionRequired, "Computer / Device Use permission is required before this Android action can run.");

        if (request.ActionKey.Equals("applications.launch", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParameter(request, "name", out var name))
                return Result(request, DeviceActionResultStatus.ActionRejected, "The application name parameter is required.");
            try
            {
                var output = await computer.LaunchAppAsync(name, cancellationToken).ConfigureAwait(false);
                return new DeviceActionResult(DeviceActionResultStatus.Success, request.ActionKey, request.Target.Id, "Android application launched.", output);
            }
            catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
            {
                return Result(request, DeviceActionResultStatus.PlatformError, ex.Message);
            }
        }

        if (request.ActionKey.Equals(AndroidProjectorRemoteExperienceService.EnumerateTargetsActionKey, StringComparison.OrdinalIgnoreCase))
        {
            var targets = projector.GetRoutableRemoteTargets();
            if (targets.Count == 0)
                return Result(request, DeviceActionResultStatus.DeviceUnavailable, "This Android device has no currently hosted Projector target.");
            return new DeviceActionResult(
                DeviceActionResultStatus.Success,
                request.ActionKey,
                request.Target.Id,
                "Projector targets acknowledged.",
                System.Text.Json.JsonSerializer.Serialize(targets));
        }

        if (request.ActionKey.Equals(AndroidProjectorRemoteExperienceService.RouteExperienceActionKey, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParameter(request, "runtimeId", out var runtimeId)
                || !TryParameter(request, "experienceId", out var experienceId))
                return Result(request, DeviceActionResultStatus.ActionRejected, "Projector runtimeId and experienceId parameters are required.");

            var routed = await projector.RouteRemoteCommandAsync(runtimeId, experienceId, cancellationToken).ConfigureAwait(false);
            return routed.Succeeded
                ? Result(request, DeviceActionResultStatus.Success, routed.Message)
                : Result(request, DeviceActionResultStatus.ActionRejected, routed.Message);
        }

        return Result(request, DeviceActionResultStatus.Unsupported, "This action needs a dedicated Android consent flow and is not enabled in this build.");
    }

    private static bool TryParameter(DeviceActionRequest request, string key, out string value)
    {
        value = string.Empty;
        if (request.Parameters is null
            || !request.Parameters.TryGetValue(key, out var candidate)
            || string.IsNullOrWhiteSpace(candidate))
            return false;
        value = candidate.Trim();
        return true;
    }

    private static DeviceActionResult Result(DeviceActionRequest request, DeviceActionResultStatus status, string message) =>
        new(status, request.ActionKey, request.Target.Id, message);
}
