/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ModeSeedService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ModeSeedService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents mode seed service and keeps its related state and behavior together.
/// </summary>
public sealed class ModeSeedService
{
    /// <summary>
    /// Stores registry locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModeRegistry _registry;

    public ModeSeedService(IModeRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Performs seed built in modes asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task SeedBuiltInModesAsync(CancellationToken cancellationToken)
    {
        // Call is an in-chat action, not a mode. Remove the retired seeded entry so
        // existing profiles cannot keep surfacing it in launchers or mode libraries.
        await _registry.DeleteModeByKeyAsync("call", cancellationToken).ConfigureAwait(false);
        // The old Do entry has been replaced by Research inside the shared Chat
        // experience. Keep HavenMode.Do as the persisted compatibility value while
        // removing the retired launcher entry and wording.
        await _registry.DeleteModeByKeyAsync("do", cancellationToken).ConfigureAwait(false);

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