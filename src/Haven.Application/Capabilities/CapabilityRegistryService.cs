using Haven.Core;

namespace Haven.Application;

/// <summary>Discovers one coherent capability catalogue for the current platform.</summary>
public sealed class CapabilityRegistryService(ICapabilityRepository repository)
{
    public async Task<IReadOnlyList<CapabilityDefinition>> DiscoverAsync(
        CapabilityPlatform platform,
        CancellationToken cancellationToken)
    {
        if (platform is CapabilityPlatform.None or CapabilityPlatform.All)
            throw new ArgumentOutOfRangeException(nameof(platform), platform, "Select one current host platform.");

        return (await repository.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false))
            .Where(item => item.IsEnabled && item.Platforms.HasFlag(platform))
            .OrderBy(item => item.OwnerAppKey.Equals(CapabilityRegistryCatalog.GeneralOwner, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.OwnerAppKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
