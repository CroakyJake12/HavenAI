using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Host-owned runtime that registers plugin capabilities into Haven's authoritative registry
/// and executes them only through a permission-checked, cancellable process boundary.
/// </summary>
public sealed class NativePluginRuntime(
    ICapabilityRepository capabilities,
    ICatalogRepository catalog,
    INativePluginProcessFactory processFactory,
    IExecutionEventSink events) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, (InstalledExtensionPackage Package, INativePluginProcess Process)> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task LoadAsync(InstalledExtensionPackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        EnsurePermissionGrantValid(package);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded.ContainsKey(package.Manifest.PackageId)) return;
            var process = processFactory.Create(package);
            try
            {
                await process.StartAsync(cancellationToken).ConfigureAwait(false);
                await RegisterPackageAsync(package, cancellationToken).ConfigureAwait(false);
                if (!_loaded.TryAdd(package.Manifest.PackageId, (package, process)))
                    throw new InvalidOperationException("Plugin was loaded concurrently.");
            }
            catch
            {
                try { await DisablePackageRegistrationsAsync(package, CancellationToken.None).ConfigureAwait(false); } catch { }
                await process.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<string> InvokeAsync(
        string packageId,
        string capabilityId,
        string argumentsJson,
        ExtensionPermission authorisedPermissions,
        Guid executionId,
        Guid? parentActionId,
        CancellationToken cancellationToken)
    {
        (InstalledExtensionPackage Package, INativePluginProcess Process) loaded;
        if (!_loaded.TryGetValue(packageId, out loaded)) throw new InvalidOperationException("Plugin is not loaded.");
        var manifest = loaded.Package.Manifest.Capabilities.FirstOrDefault(value => value.Id.Equals(capabilityId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("Plugin capability was not declared.");
        if ((manifest.RequiredPermissions & ~authorisedPermissions) != 0 ||
            (manifest.RequiredPermissions & ~loaded.Package.GrantedPermissions) != 0)
            throw new UnauthorizedAccessException("The capability requires permissions that are not currently granted.");
        var actionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        events.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, parentActionId,
            ExecutionOrigin.NativePlugin, ExecutionActionType.PluginCall, ExecutionActionStatus.Running,
            manifest.DisplayName, "The selected plugin capability matches the requested action.", null,
            packageId, now, now));
        try
        {
            var result = await loaded.Process.InvokeAsync(capabilityId, SensitiveTextRedactor.Redact(argumentsJson, 16_000), cancellationToken).ConfigureAwait(false);
            events.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, parentActionId,
                ExecutionOrigin.NativePlugin, ExecutionActionType.PluginCall, ExecutionActionStatus.Completed,
                manifest.DisplayName, null, SensitiveTextRedactor.Redact(result, 8_000), packageId,
                DateTimeOffset.UtcNow, now, DateTimeOffset.UtcNow));
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            events.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, parentActionId,
                ExecutionOrigin.NativePlugin, ExecutionActionType.PluginCall, ExecutionActionStatus.Cancelled,
                manifest.DisplayName, null, "Plugin action was cancelled.", packageId, DateTimeOffset.UtcNow, now, DateTimeOffset.UtcNow));
            throw;
        }
        catch (Exception ex)
        {
            var failure = new ExecutionFailure("PLUGIN_CALL_FAILED", "Plugin call failed", SensitiveTextRedactor.Redact(ex.Message));
            events.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, parentActionId,
                ExecutionOrigin.NativePlugin, ExecutionActionType.PluginCall, ExecutionActionStatus.Failed,
                manifest.DisplayName, null, failure.Message, packageId, DateTimeOffset.UtcNow, now, DateTimeOffset.UtcNow, Failure: failure));
            throw;
        }
    }

    public async Task UnloadAsync(string packageId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_loaded.TryRemove(packageId, out var loaded)) return;
            Exception? stopFailure = null;
            try { await loaded.Process.StopAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) { stopFailure = ex; }
            try { await loaded.Process.DisposeAsync().ConfigureAwait(false); }
            finally { await DisablePackageRegistrationsAsync(loaded.Package, CancellationToken.None).ConfigureAwait(false); }
            if (stopFailure is not null) throw stopFailure;
        }
        finally { _gate.Release(); }
    }

    private async Task RegisterPackageAsync(InstalledExtensionPackage package, CancellationToken cancellationToken)
    {
        foreach (var item in package.Manifest.Capabilities)
        {
            var risk = item.RequiredPermissions.HasFlag(ExtensionPermission.ProcessExecution) || item.RequiredPermissions.HasFlag(ExtensionPermission.ProjectWrite)
                ? CapabilityRiskClass.Consequential : item.RequiredPermissions == ExtensionPermission.None ? CapabilityRiskClass.ReadOnly : CapabilityRiskClass.Low;
            await capabilities.UpsertCapabilityAsync(new CapabilityDefinition(
                StableId(package.Manifest.PackageId, "capability:" + item.Id), $"extension.{package.Manifest.PackageId}.{item.Id}",
                item.DisplayName, item.Description, "plugins", "plugin", string.Empty,
                $"native-plugin:{package.Manifest.PackageId}:{item.Id}", JsonSerializer.Serialize(item.SemanticActions),
                CapabilityPlatform.All, risk, CapabilityAvailability.Available, JsonSerializer.Serialize(package.Manifest.Dependencies),
                package.Manifest.PackageId, true, true, false, package.IsEnabled, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
        }
        foreach (var item in package.Manifest.Skills)
        {
            var path = Path.GetFullPath(Path.Combine(package.InstallPath, item.InstructionPath));
            var root = Path.GetFullPath(package.InstallPath) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Skill path escaped the installed package.");
            EnsureNoLinkedPath(package.InstallPath, path);
            if (new FileInfo(path).Length > 1_000_000) throw new InvalidDataException("Skill instructions exceed Haven's one-megabyte package limit.");
            var instructions = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            await catalog.UpsertPromptAsync(new PromptDefinition(
                StableId(package.Manifest.PackageId, "skill:" + item.Id), item.DisplayName, item.Description,
                "sparkles", instructions, true, false, package.IsEnabled && item.EnabledByDefault,
                DateTimeOffset.UtcNow, IsAgentic: true), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DisablePackageRegistrationsAsync(InstalledExtensionPackage package, CancellationToken cancellationToken)
    {
        foreach (var manifest in package.Manifest.Capabilities)
            await capabilities.SetCapabilityEnabledAsync(StableId(package.Manifest.PackageId, "capability:" + manifest.Id), false, cancellationToken).ConfigureAwait(false);
        foreach (var skill in package.Manifest.Skills)
            await catalog.SetPromptEnabledAsync(StableId(package.Manifest.PackageId, "skill:" + skill.Id), false, cancellationToken).ConfigureAwait(false);
    }

    private static void EnsurePermissionGrantValid(InstalledExtensionPackage package)
    {
        if ((package.GrantedPermissions & ~package.Manifest.RequestedPermissions) != 0)
            throw new InvalidOperationException("Granted permissions exceed the package request.");
        if ((package.Manifest.RequestedPermissions & ~package.GrantedPermissions) != 0)
            throw new UnauthorizedAccessException("Review and grant the package's requested permissions before loading it.");
    }

    private static Guid StableId(string packageId, string childId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(packageId + "\n" + childId));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static void EnsureNoLinkedPath(string root, string path)
    {
        var current = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Installed extension roots cannot be linked directories.");
        foreach (var segment in Path.GetRelativePath(current, path).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Installed extensions cannot execute or load linked content.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var key in _loaded.Keys.ToArray())
            try { await UnloadAsync(key, CancellationToken.None).ConfigureAwait(false); } catch { }
        _gate.Dispose();
    }
}
