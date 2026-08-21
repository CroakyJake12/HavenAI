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

public sealed class AndroidDeviceActionProvider(IComputerToolService computer) : IDeviceActionProvider
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
        IReadOnlyList<DeviceActionDescriptor> actions =
        [
            new("applications.launch", "Launch application", "Applications", "computer-device-use", "android.intent.launch", ProviderId, DeviceActionAvailability.PermissionRequired, ["name"]),
            new("ui.snapshot", "Inspect current screen", "Screen", "computer-device-use", "android.media-projection", ProviderId, DeviceActionAvailability.Unsupported, []),
            new("ui.invoke", "Invoke another app control", "Accessibility", "computer-device-use", "android.accessibility", ProviderId, DeviceActionAvailability.Unsupported, ["name"])
        ];
        return Task.FromResult(new DeviceCapabilitySnapshot(target, true, DateTimeOffset.UtcNow, actions));
    }

    public async Task<DeviceActionResult> ExecuteAsync(DeviceActionRequest request, CancellationToken cancellationToken)
    {
        if (!CanHandle(request.Target))
            return Result(request, DeviceActionResultStatus.DeviceUnavailable, "This provider does not own the selected Android target.");
        if (!request.PermissionGranted)
            return Result(request, DeviceActionResultStatus.PermissionRequired, "Computer / Device Use permission is required before this Android action can run.");
        if (!request.ActionKey.Equals("applications.launch", StringComparison.OrdinalIgnoreCase))
            return Result(request, DeviceActionResultStatus.Unsupported, "This action needs a dedicated Android consent flow and is not enabled in this build.");
        if (request.Parameters is null || !request.Parameters.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
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

    private static DeviceActionResult Result(DeviceActionRequest request, DeviceActionResultStatus status, string message) =>
        new(status, request.ActionKey, request.Target.Id, message);
}
