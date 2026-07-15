using Haven.Core;

namespace Haven.Application;

public sealed class ModeSeedService
{
    private readonly IModeRegistry _registry;

    public ModeSeedService(IModeRegistry registry)
    {
        _registry = registry;
    }

    public async Task SeedBuiltInModesAsync(CancellationToken cancellationToken)
    {
        var existing = await _registry.GetModesAsync(cancellationToken).ConfigureAwait(false);
        var existingKeys = existing.Select(m => m.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var mode in BuiltInModeSeed.Modes)
        {
            if (!existingKeys.Contains(mode.Key))
            {
                await _registry.UpsertModeAsync(mode, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
