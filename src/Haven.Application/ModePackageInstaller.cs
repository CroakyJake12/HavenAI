/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ModePackageInstaller.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ModePackageInstaller. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents mode package installer and keeps its related state and behavior together.
/// </summary>
public sealed class ModePackageInstaller : IModePackageInstaller
{
    /// <summary>
    /// Stores registry locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModeRegistry _registry;
    /// <summary>
    /// Stores usage locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModeUsageRepository _usage;
    /// <summary>
    /// Stores pins locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IPinRepository _pins;
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAppPaths _paths;

    public ModePackageInstaller(
        IModeRegistry registry,
        IModeUsageRepository usage,
        IPinRepository pins,
        IAppPaths paths)
    {
        _registry = registry;
        _usage = usage;
        _pins = pins;
        _paths = paths;
    }

    /// <summary>
    /// Performs install async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<ModePackageInstallResult> InstallAsync(ModePackageManifest manifest, CancellationToken cancellationToken)
    {
        var existing = await _registry.GetModeByKeyAsync(manifest.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return new ModePackageInstallResult { Succeeded = false, Message = $"Mode '{manifest.Id}' already installed." };

        var def = manifest.Definition;
        var baseMode = def.Surfaces.Chat ? HavenMode.Chat : def.Surfaces.Do ? HavenMode.Do : HavenMode.Chat;
        var mode = new ModeDefinition(
            Guid.NewGuid(),
            def.Key,
            def.Name,
            def.Description,
            def.IconKey,
            baseMode,
            JsonSerializer.Serialize(new { def.Surfaces.Chat, def.Surfaces.Do, def.Surfaces.Studio, def.Surfaces.Browse, def.Surfaces.Plan }),
            "[]",
            "[]",
            "[]",
            string.Empty,
            manifest.Source,
            ModeInstallState.InstalledByUser,
            def.Author ?? "Custom",
            manifest.Version,
            JsonSerializer.Serialize(def.Tags),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            true);

        await _registry.UpsertModeAsync(mode, cancellationToken).ConfigureAwait(false);

        var version = new ModeVersion(
            Guid.NewGuid(),
            mode.Id,
            ParseMajor(manifest.Version),
            ParseMinor(manifest.Version),
            ParsePatch(manifest.Version),
            JsonSerializer.Serialize(manifest),
            manifest.Source.ToString(),
            DateTimeOffset.UtcNow);
        await _registry.AddVersionAsync(version, cancellationToken).ConfigureAwait(false);

        var permission = new ModePermissionGrant(
            Guid.NewGuid(),
            mode.Id,
            Enum.TryParse<PermissionMode>(def.Permissions.FilePermission, true, out var fp) ? fp : PermissionMode.Ask,
            Enum.TryParse<PermissionMode>(def.Permissions.CommandPermission, true, out var cp) ? cp : PermissionMode.Ask,
            Enum.TryParse<PermissionMode>(def.Permissions.BrowserPermission, true, out var bp) ? bp : PermissionMode.Ask,
            def.Permissions.AllowDesktopTools,
            def.Permissions.AllowFileSystemWrites,
            DateTimeOffset.UtcNow);
        await _registry.UpsertGrantAsync(permission, cancellationToken).ConfigureAwait(false);

        await SaveManifestAsync(mode.Id, manifest, cancellationToken).ConfigureAwait(false);

        return new ModePackageInstallResult
        {
            Succeeded = true,
            ModeId = mode.Id,
            Message = $"Mode '{def.Name}' installed successfully.",
            Warnings = []
        };
    }

    /// <summary>
    /// Performs update async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<ModePackageInstallResult> UpdateAsync(Guid modeId, ModePackageManifest manifest, CancellationToken cancellationToken)
    {
        var existing = await _registry.GetModeByIdAsync(modeId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return new ModePackageInstallResult { Succeeded = false, Message = "Mode not found." };

        var updated = existing with
        {
            Name = manifest.Name,
            Description = manifest.Definition.Description,
            Version = manifest.Version,
            IconKey = manifest.Definition.IconKey,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _registry.UpsertModeAsync(updated, cancellationToken).ConfigureAwait(false);

        var version = new ModeVersion(
            Guid.NewGuid(),
            modeId,
            ParseMajor(manifest.Version),
            ParseMinor(manifest.Version),
            ParsePatch(manifest.Version),
            JsonSerializer.Serialize(manifest),
            $"Updated from {existing.Version}",
            DateTimeOffset.UtcNow);
        await _registry.AddVersionAsync(version, cancellationToken).ConfigureAwait(false);

        await SaveManifestAsync(modeId, manifest, cancellationToken).ConfigureAwait(false);

        return new ModePackageInstallResult
        {
            Succeeded = true,
            ModeId = modeId,
            Message = $"Mode updated to {manifest.Version}."
        };
    }

    /// <summary>
    /// Performs rollback async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<bool> RollbackAsync(Guid modeId, CancellationToken cancellationToken)
    {
        var versions = await _registry.GetVersionsAsync(modeId, cancellationToken).ConfigureAwait(false);
        if (versions.Count < 2) return false;

        var previous = versions[^2];
        if (JsonSerializer.Deserialize<ModePackageManifest>(previous.ManifestJson) is not { } manifest)
            return false;

        var mode = await _registry.GetModeByIdAsync(modeId, cancellationToken).ConfigureAwait(false);
        if (mode is null) return false;

        var updated = mode with
        {
            Name = manifest.Name,
            Version = previous.Major + "." + previous.Minor + "." + previous.Patch,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _registry.UpsertModeAsync(updated, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Performs uninstall async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<bool> UninstallAsync(Guid modeId, CancellationToken cancellationToken)
    {
        var mode = await _registry.GetModeByIdAsync(modeId, cancellationToken).ConfigureAwait(false);
        if (mode is null || mode.Source == ModeSource.BuiltIn) return false;

        var updated = mode with { IsEnabled = false, UpdatedAt = DateTimeOffset.UtcNow };
        await _registry.UpsertModeAsync(updated, cancellationToken).ConfigureAwait(false);

        var manifestPath = GetManifestPath(modeId);
        if (File.Exists(manifestPath)) File.Delete(manifestPath);

        return true;
    }

    /// <summary>
    /// Retrieves installed modes async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<InstalledModeInfo>> GetInstalledModesAsync(CancellationToken cancellationToken)
    {
        var modes = await _registry.GetModesAsync(cancellationToken).ConfigureAwait(false);
        return modes.Select(m => new InstalledModeInfo
        {
            ModeId = m.Id,
            Key = m.Key,
            Name = m.Name,
            Version = m.Version,
            Source = m.Source,
            InstalledAt = m.CreatedAt,
            IsEnabled = m.IsEnabled
        }).ToArray();
    }

    /// <summary>
    /// Performs save manifest async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SaveManifestAsync(Guid modeId, ModePackageManifest manifest, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(_paths.DataDirectory, "mode-packages");
        Directory.CreateDirectory(dir);
        var path = GetManifestPath(modeId);
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves manifest path for the current operation.
    /// </summary>
    private string GetManifestPath(Guid modeId) =>
        Path.Combine(_paths.DataDirectory, "mode-packages", modeId.ToString("N") + ".json");

    /// <summary>
    /// Performs the parse major step owned by this component.
    /// </summary>
    private static int ParseMajor(string version) => version.Split('.') is { Length: >= 1 } parts && int.TryParse(parts[0], out var v) ? v : 1;
    /// <summary>
    /// Performs the parse minor step owned by this component.
    /// </summary>
    private static int ParseMinor(string version) => version.Split('.') is { Length: >= 2 } parts && int.TryParse(parts[1], out var v) ? v : 0;
    /// <summary>
    /// Performs the parse patch step owned by this component.
    /// </summary>
    private static int ParsePatch(string version) => version.Split('.') is { Length: >= 3 } parts && int.TryParse(parts[2], out var v) ? v : 0;
}
