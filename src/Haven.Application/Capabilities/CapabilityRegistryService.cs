using Haven.Core;

namespace Haven.Application;

/// <summary>Discovers one coherent capability catalogue for the current platform, including transient connection-backed capabilities.</summary>
public sealed class CapabilityRegistryService(ICapabilityRepository repository, IEnumerable<IDynamicCapabilityProvider>? dynamicProviders = null)
{
    public async Task<IReadOnlyList<CapabilityDefinition>> DiscoverAsync(
        CapabilityPlatform platform,
        CancellationToken cancellationToken)
    {
        if (platform is CapabilityPlatform.None or CapabilityPlatform.All)
            throw new ArgumentOutOfRangeException(nameof(platform), platform, "Select one current host platform.");

        var items = new List<CapabilityDefinition>(await repository.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false));
        if (dynamicProviders is not null)
            foreach (var provider in dynamicProviders)
                items.AddRange(await provider.GetCapabilitiesAsync(platform, cancellationToken).ConfigureAwait(false));

        return items
            .Where(item => item.IsEnabled && item.Platforms.HasFlag(platform))
            .GroupBy(item => item.Id)
            .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
            .OrderBy(item => item.OwnerAppKey.Equals(CapabilityRegistryCatalog.GeneralOwner, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.OwnerAppKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
