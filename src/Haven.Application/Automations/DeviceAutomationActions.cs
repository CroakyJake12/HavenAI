using System.Globalization;
using Haven.Core;

namespace Haven.Application.Automations;

public static class DeviceAutomationNodeCategory { public const string Key = "DEVICE"; }
public enum DeviceTargetKind { CurrentDevice, MeshDevice, PlatformIntegration, PluginProvider }
public enum DeviceActionAvailability { Supported, PermissionRequired, AvailableThroughPlugin, Unsupported, Unknown }
public enum DeviceActionResultStatus { Success, Unsupported, PermissionRequired, DeviceUnavailable, ConnectionLost, ActionRejected, PlatformError }

public sealed record DeviceTargetDescriptor(string Id, string DisplayName, CapabilityPlatform Platform, DeviceTargetKind Kind, string? ProviderId = null);
public sealed record DeviceActionDescriptor(string Key, string Name, string Group, string CapabilityKey, string ImplementationKey, string ProviderId, DeviceActionAvailability Availability, IReadOnlyList<string> RequiredParameters);
public sealed record DeviceCapabilitySnapshot(DeviceTargetDescriptor Target, bool IsReachable, DateTimeOffset CapturedAt, IReadOnlyList<DeviceActionDescriptor> Actions);
public sealed record DeviceActionRequest(DeviceTargetDescriptor Target, string ActionKey, IReadOnlyDictionary<string, string>? Parameters = null, bool PermissionGranted = false);
public sealed record DeviceActionResult(DeviceActionResultStatus Status, string ActionKey, string TargetId, string Message, string? Output = null) { public bool Succeeded => Status == DeviceActionResultStatus.Success; }
public sealed record DeviceAutomationNodeDefinition(Guid Id, DeviceTargetDescriptor Target, string ActionKey, IReadOnlyDictionary<string, string> Parameters) { public string Category => DeviceAutomationNodeCategory.Key; }

public interface IDeviceActionProvider
{
    string ProviderId { get; }
    bool CanHandle(DeviceTargetDescriptor target);
    Task<DeviceCapabilitySnapshot> GetSnapshotAsync(DeviceTargetDescriptor target, CancellationToken cancellationToken);
    Task<DeviceActionResult> ExecuteAsync(DeviceActionRequest request, CancellationToken cancellationToken);
}
public sealed class DeviceActionRouter(IEnumerable<IDeviceActionProvider> providers)
{
    private readonly IReadOnlyList<IDeviceActionProvider> _providers = providers.ToArray();

    public async Task<DeviceCapabilitySnapshot> GetSnapshotAsync(DeviceTargetDescriptor target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var provider = Resolve(target);
        return provider is null ? new DeviceCapabilitySnapshot(target, false, DateTimeOffset.UtcNow, []) : await provider.GetSnapshotAsync(target, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DeviceActionResult> ExecuteAsync(DeviceActionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var provider = Resolve(request.Target);
        if (provider is null)
            return new(request.Target.Kind == DeviceTargetKind.MeshDevice ? DeviceActionResultStatus.DeviceUnavailable : DeviceActionResultStatus.Unsupported, request.ActionKey, request.Target.Id, $"No available device provider can handle {request.Target.DisplayName}.");
        try { return await provider.ExecuteAsync(request, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException ex) { return new(DeviceActionResultStatus.PermissionRequired, request.ActionKey, request.Target.Id, ex.Message); }
        catch (Exception ex) { return new(DeviceActionResultStatus.PlatformError, request.ActionKey, request.Target.Id, ex.Message); }
    }

    private IDeviceActionProvider? Resolve(DeviceTargetDescriptor target)
    {
        if (!string.IsNullOrWhiteSpace(target.ProviderId))
        {
            var exact = _providers.FirstOrDefault(p => string.Equals(p.ProviderId, target.ProviderId, StringComparison.OrdinalIgnoreCase) && p.CanHandle(target));
            if (exact is not null) return exact;
        }
        return _providers.FirstOrDefault(p => p.CanHandle(target));
    }
}

public sealed class DeviceAutomationNodeExecutor(DeviceActionRouter router)
{
    public Task<DeviceActionResult> ExecuteAsync(DeviceAutomationNodeDefinition node, bool permissionGranted, CancellationToken cancellationToken) => router.ExecuteAsync(new DeviceActionRequest(node.Target, node.ActionKey, node.Parameters, permissionGranted), cancellationToken);
}
public sealed class WindowsComputerDeviceActionProvider(IComputerToolService computer, CapabilityRegistryService capabilities) : IDeviceActionProvider
{
    public const string NativeProviderId = "haven.device";
    public const string CapabilityKey = "computer-device-use";
    public string ProviderId => NativeProviderId;

    public bool CanHandle(DeviceTargetDescriptor target) => target.Kind == DeviceTargetKind.CurrentDevice && target.Platform == CapabilityPlatform.Windows && (string.IsNullOrWhiteSpace(target.ProviderId) || string.Equals(target.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase));

    public async Task<DeviceCapabilitySnapshot> GetSnapshotAsync(DeviceTargetDescriptor target, CancellationToken cancellationToken)
    {
        var discovered = await capabilities.DiscoverAsync(CapabilityPlatform.Windows, cancellationToken).ConfigureAwait(false);
        var capability = discovered.FirstOrDefault(x => string.Equals(x.Key, CapabilityKey, StringComparison.OrdinalIgnoreCase));
        var availability = MapAvailability(capability?.Availability);
        var implementation = capability?.ImplementationKey ?? "device.control";
        var provider = capability?.ProviderId ?? NativeProviderId;
        DeviceActionDescriptor Native(string key,string name,string group,params string[] required) => new(key,name,group,CapabilityKey,implementation,provider,availability,required);
        DeviceActionDescriptor Unsupported(string key,string name,string group,params string[] required) => new(key,name,group,CapabilityKey,implementation,provider,DeviceActionAvailability.Unsupported,required);
        DeviceActionDescriptor[] actions =
        [
            Native("ui.snapshot","Inspect current desktop","Desktop"), Native("window.list","List windows","Windows"),
            Native("applications.launch","Launch application","Applications","name"), Native("window.focus","Focus window","Windows","title"),
            Native("ui.invoke","Invoke UI control","Desktop","windowTitle","name","automationId"), Native("ui.click","Click screen position","Desktop","windowTitle","x","y","button"),
            Native("ui.type","Type text","Desktop","windowTitle","text"), Native("ui.press","Press keys","Desktop","windowTitle","keys"), Native("window.close","Close window","Windows","title"),
            Unsupported("connectivity.wifi","Change Wi-Fi","Connectivity"), Unsupported("connectivity.bluetooth","Change Bluetooth","Connectivity"),
            Unsupported("display.brightness","Set brightness","Display","value"), Unsupported("display.orientation","Change orientation","Display","orientation"),
            Unsupported("audio.volume","Set volume","Audio","value"), Unsupported("audio.mute","Set mute","Audio","muted"),
            Unsupported("device.lock","Lock device","Device"), Unsupported("device.sleep","Sleep device","Device"), Unsupported("applications.open-uri","Open URI","Applications","uri")
        ];
        return new DeviceCapabilitySnapshot(target, capability is not null, DateTimeOffset.UtcNow, actions);
    }
    public async Task<DeviceActionResult> ExecuteAsync(DeviceActionRequest request, CancellationToken cancellationToken)
    {
        if (!CanHandle(request.Target)) return Result(request, DeviceActionResultStatus.DeviceUnavailable, "This provider does not own the selected target.");
        var snapshot = await GetSnapshotAsync(request.Target, cancellationToken).ConfigureAwait(false);
        var action = snapshot.Actions.FirstOrDefault(x => string.Equals(x.Key, request.ActionKey, StringComparison.OrdinalIgnoreCase));
        if (action is null || action.Availability == DeviceActionAvailability.Unsupported) return Result(request, DeviceActionResultStatus.Unsupported, $"{request.ActionKey} is not supported by the current Windows device provider.");
        if (action.Availability == DeviceActionAvailability.Unknown) return Result(request, DeviceActionResultStatus.ActionRejected, $"Availability for {request.ActionKey} could not be resolved.");
        if (action.Availability == DeviceActionAvailability.PermissionRequired && !request.PermissionGranted) return Result(request, DeviceActionResultStatus.PermissionRequired, "Computer / Device Use permission is required before this action can run.");
        foreach (var required in action.RequiredParameters) if (!TryParameter(request, required, out _)) return Result(request, DeviceActionResultStatus.ActionRejected, $"Missing required parameter '{required}'.");

        string output;
        switch (action.Key)
        {
            case "ui.snapshot": output = await computer.SnapshotAsync(cancellationToken).ConfigureAwait(false); break;
            case "window.list": output = await computer.ListWindowsAsync(cancellationToken).ConfigureAwait(false); break;
            case "applications.launch": output = await computer.LaunchAppAsync(Parameter(request, "name"), cancellationToken).ConfigureAwait(false); break;
            case "window.focus": output = await computer.FocusWindowAsync(Parameter(request, "title"), cancellationToken).ConfigureAwait(false); break;
            case "ui.invoke": output = await computer.InvokeAsync(Parameter(request, "windowTitle"), Parameter(request, "name"), Parameter(request, "automationId"), cancellationToken).ConfigureAwait(false); break;
            case "ui.click":
                if (!int.TryParse(Parameter(request, "x"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) || !int.TryParse(Parameter(request, "y"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)) return Result(request, DeviceActionResultStatus.ActionRejected, "Click coordinates must be integers.");
                output = await computer.ClickAsync(Parameter(request, "windowTitle"), x, y, Parameter(request, "button"), cancellationToken).ConfigureAwait(false); break;
            case "ui.type": output = await computer.TypeAsync(Parameter(request, "windowTitle"), Parameter(request, "text"), cancellationToken).ConfigureAwait(false); break;
            case "ui.press": output = await computer.PressAsync(Parameter(request, "windowTitle"), Parameter(request, "keys"), cancellationToken).ConfigureAwait(false); break;
            case "window.close": output = await computer.CloseWindowAsync(Parameter(request, "title"), cancellationToken).ConfigureAwait(false); break;
            default: return Result(request, DeviceActionResultStatus.Unsupported, $"{request.ActionKey} has no native Windows execution route.");
        }
        return Result(request, DeviceActionResultStatus.Success, "Device action completed.", output);
    }
    private static DeviceActionAvailability MapAvailability(CapabilityAvailability? value) => value switch
    {
        CapabilityAvailability.Available => DeviceActionAvailability.Supported,
        CapabilityAvailability.PermissionRequired => DeviceActionAvailability.PermissionRequired,
        CapabilityAvailability.DependencyRequired => DeviceActionAvailability.Unknown,
        CapabilityAvailability.Restricted or CapabilityAvailability.Unsupported => DeviceActionAvailability.Unsupported,
        _ => DeviceActionAvailability.Unknown
    };

    private static DeviceActionResult Result(DeviceActionRequest request, DeviceActionResultStatus status, string message, string? output = null) => new(status, request.ActionKey, request.Target.Id, message, output);

    private static bool TryParameter(DeviceActionRequest request, string key, out string value)
    {
        value = string.Empty;
        if (request.Parameters is null || !request.Parameters.TryGetValue(key, out var candidate) || string.IsNullOrWhiteSpace(candidate)) return false;
        value = candidate; return true;
    }

    private static string Parameter(DeviceActionRequest request, string key) => TryParameter(request, key, out var value) ? value : throw new InvalidOperationException($"Missing DEVICE action parameter '{key}'.");
}