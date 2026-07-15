using Haven.Core;

namespace Haven.Application;

public interface IDashboardRepository
{
    Task<DashboardSnapshot> GetSnapshotAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

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

public interface IDashboardTileProviderRegistry
{
    IReadOnlyList<IDashboardTileProvider> Providers { get; }
}

public sealed class DashboardTileProviderRegistry(IEnumerable<IDashboardTileProvider> providers) : IDashboardTileProviderRegistry
{
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
    private static readonly HashSet<string> ApprovedProviders = new(StringComparer.OrdinalIgnoreCase)
        { "action", "calls", "plan", "projects", "teaching", "groups", "automations", "conversations" };
    private static readonly HashSet<string> ApprovedActions = new(StringComparer.OrdinalIgnoreCase)
        { "new-chat", "chat", "teach", "call", "plan", "browse", "studio", "automations" };
    private static readonly HashSet<string> ReservedKeys = new(StringComparer.OrdinalIgnoreCase)
        { "new-chat", "call", "plan", "browse", "studio", "teaching", "groups", "automations" };

    public static bool IsApproved(DashboardPluginTileManifest manifest, bool rejectReservedKey = true) =>
        !string.IsNullOrWhiteSpace(manifest.Key) && manifest.Key.Trim().Length <= 100 &&
        (!rejectReservedKey || !ReservedKeys.Contains(manifest.Key.Trim())) &&
        !string.IsNullOrWhiteSpace(manifest.Title) &&
        !string.IsNullOrWhiteSpace(manifest.ProviderKey) && ApprovedProviders.Contains(manifest.ProviderKey.Trim()) &&
        !string.IsNullOrWhiteSpace(manifest.ActionKey) && ApprovedActions.Contains(manifest.ActionKey.Trim()) &&
        Enum.TryParse<DashboardTileSize>(manifest.Size, true, out _);

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
