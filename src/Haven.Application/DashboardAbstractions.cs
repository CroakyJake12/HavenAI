/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/DashboardAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns IDashboardRepository, IDashboardLayoutRepository, IDashboardTileProvider, IDashboardTileProviderRegistry, DashboardTileProviderRegistry, DashboardTileManifestPolicy. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Defines the i dashboard repository contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IDashboardRepository
{
    Task<DashboardSnapshot> GetSnapshotAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the i dashboard layout repository contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IDashboardLayoutRepository
{
    Task<IReadOnlyList<DashboardTileLayout>> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<DashboardTileLayout> layout, CancellationToken cancellationToken);
}

/// <summary>
/// A safe dashboard extension point. Providers supply data only; navigation is
/// resolved through a whitelisted action key owned by the shell.
/// </summary>
public interface IDashboardTileProvider
{
    DashboardTileDefinition Definition { get; }
    Task<DashboardTileData> GetDataAsync(DashboardSnapshot snapshot, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the i dashboard tile provider registry contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IDashboardTileProviderRegistry
{
    IReadOnlyList<IDashboardTileProvider> Providers { get; }
}

/// <summary>
/// Represents dashboard tile provider registry and keeps its related state and behavior together.
/// </summary>
public sealed class DashboardTileProviderRegistry(IEnumerable<IDashboardTileProvider> providers) : IDashboardTileProviderRegistry
{
    /// <summary>
    /// Gets or updates providers, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<IDashboardTileProvider> Providers { get; } = providers
        .GroupBy(provider => provider.Definition.Key, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.Last())
        .OrderBy(provider => provider.Definition.DefaultOrder)
        .ToArray();
}

/// <summary>
/// The validation boundary for declarative dashboard extensions. A manifest
/// can select only Haven-owned data and navigation keys; it cannot identify
/// executable callbacks or load arbitrary .NET code.
/// </summary>
public static class DashboardTileManifestPolicy
{
    /// <summary>
    /// Stores approved providers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> ApprovedProviders = new(StringComparer.OrdinalIgnoreCase)
        { "action", "calls", "plan", "projects", "teaching", "groups", "automations", "conversations" };
    /// <summary>
    /// Stores approved actions locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> ApprovedActions = new(StringComparer.OrdinalIgnoreCase)
        { "new-chat", "chat", "teach", "call", "plan", "browse", "studio", "automations" };
    /// <summary>
    /// Stores reserved keys locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly HashSet<string> ReservedKeys = new(StringComparer.OrdinalIgnoreCase)
        { "new-chat", "call", "plan", "browse", "studio", "teaching", "groups", "automations" };

    /// <summary>
    /// Reports whether is approved is true for the current state.
    /// </summary>
    public static bool IsApproved(DashboardPluginTileManifest manifest, bool rejectReservedKey = true) =>
        !string.IsNullOrWhiteSpace(manifest.Key) && manifest.Key.Trim().Length <= 100 &&
        (!rejectReservedKey || !ReservedKeys.Contains(manifest.Key.Trim())) &&
        !string.IsNullOrWhiteSpace(manifest.Title) &&
        !string.IsNullOrWhiteSpace(manifest.ProviderKey) && ApprovedProviders.Contains(manifest.ProviderKey.Trim()) &&
        !string.IsNullOrWhiteSpace(manifest.ActionKey) && ApprovedActions.Contains(manifest.ActionKey.Trim()) &&
        Enum.TryParse<DashboardTileSize>(manifest.Size, true, out _);

    /// <summary>
    /// Validates for import before it crosses the next trust or persistence boundary.
    /// </summary>
    public static void ValidateForImport(IReadOnlyList<DashboardPluginTileManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        if (manifests.Count > 12)
            throw new InvalidOperationException("A declarative plugin may add at most 12 dashboard tiles.");
        if (manifests.Select(tile => tile.Key?.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifests.Count)
            throw new InvalidOperationException("Dashboard tile keys must be unique within a plugin.");
        if (manifests.Any(tile => !IsApproved(tile)))
            throw new InvalidOperationException("A dashboard tile has an invalid or reserved key, size, data provider, or navigation action.");
    }
}
