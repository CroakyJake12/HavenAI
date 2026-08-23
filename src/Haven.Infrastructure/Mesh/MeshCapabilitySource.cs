using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class MeshCapabilitySource(CapabilityRegistryService capabilities) : IMeshCapabilitySource
{
    public async Task<IReadOnlyList<MeshCapabilityDescriptor>> GetLocalCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var platform = OperatingSystem.IsWindows() ? CapabilityPlatform.Windows : CapabilityPlatform.Android;
        var discovered = await capabilities.DiscoverAsync(platform, cancellationToken).ConfigureAwait(false);
        return discovered.Select(item => new MeshCapabilityDescriptor(
                item.Key,
                item.Name,
                platform,
                item.RiskClass,
                ParseActions(item.SemanticActionsJson)))
            .ToArray();
    }

    private static IReadOnlyList<string> ParseActions(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}
