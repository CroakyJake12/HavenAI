/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ModePackageAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns IModePackageValidator, IModePackageInstaller, InstalledModeInfo. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Defines the i mode package validator contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IModePackageValidator
{
    ModePackageValidationResult Validate(DeclarativeModeDefinition definition);
}

/// <summary>
/// Defines the i mode package installer contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IModePackageInstaller
{
    Task<ModePackageInstallResult> InstallAsync(ModePackageManifest manifest, CancellationToken cancellationToken);
    Task<ModePackageInstallResult> UpdateAsync(Guid modeId, ModePackageManifest manifest, CancellationToken cancellationToken);
    Task<bool> RollbackAsync(Guid modeId, CancellationToken cancellationToken);
    Task<bool> UninstallAsync(Guid modeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InstalledModeInfo>> GetInstalledModesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Represents installed mode info and keeps its related state and behavior together.
/// </summary>
public sealed class InstalledModeInfo
{
    /// <summary>
    /// Gets or updates mode id, the bindable or domain state represented by this property.
    /// </summary>
    public Guid ModeId { get; init; }
    /// <summary>
    /// Gets or updates key, the bindable or domain state represented by this property.
    /// </summary>
    public string Key { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates name, the bindable or domain state represented by this property.
    /// </summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates version, the bindable or domain state represented by this property.
    /// </summary>
    public string Version { get; init; } = string.Empty;
    /// <summary>
    /// Gets or updates source, the bindable or domain state represented by this property.
    /// </summary>
    public ModeSource Source { get; init; }
    /// <summary>
    /// Gets or updates installed at, the bindable or domain state represented by this property.
    /// </summary>
    public DateTimeOffset InstalledAt { get; init; }
    /// <summary>
    /// Reports whether is enabled is true for the current state.
    /// </summary>
    public bool IsEnabled { get; init; }
}
