/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ModelProviderRegistry.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ModelProviderRegistry, ModelRouter. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents model provider registry and keeps its related state and behavior together.
/// </summary>
public sealed class ModelProviderRegistry(IEnumerable<IModelProvider> providers) : IModelProviderRegistry
{
    /// <summary>
    /// Stores providers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IReadOnlyList<IModelProvider> _providers = providers
        .GroupBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.Last())
        .OrderByDescending(provider => provider.IsLocal)
        .ThenBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// Gets or updates providers, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<IModelProvider> Providers => _providers;
    /// <summary>
    /// Performs the find step owned by this component.
    /// </summary>
    public IModelProvider? Find(string providerId) => _providers.FirstOrDefault(provider => provider.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    /// <summary>
    /// Retrieves required for the current operation.
    /// </summary>
    public IModelProvider GetRequired(string providerId) => Find(providerId) ?? throw new InvalidOperationException($"Model provider '{providerId}' is not registered.");

    /// <summary>
    /// Retrieves models async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var models = new List<ProviderModelDescriptor>();
        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { models.AddRange(await provider.GetModelsAsync(cancellationToken).ConfigureAwait(false)); }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException) { }
        }
        return models.GroupBy(model => model.Key, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
            .OrderByDescending(model => model.IsLocal)
            .ThenBy(model => model.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(model => model.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

/// <summary>
/// Represents model router and keeps its related state and behavior together.
/// </summary>
public sealed class ModelRouter(IModelProviderRegistry providers) : IModelRouter
{
    /// <summary>
    /// Performs route asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<ModelRoutingDecision> RouteAsync(ModelRoutingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var compatible = (await providers.GetModelsAsync(cancellationToken).ConfigureAwait(false))
            .Where(model => request.RequiredCapabilities.All(model.Supports));
        if (!request.Policy.AllowCloud) compatible = compatible.Where(model => model.IsLocal);
        var candidates = compatible.ToArray();

        if (request.SelectedModel is { } selected && candidates.Any(model => model.Key.Equals(selected.Key, StringComparison.OrdinalIgnoreCase)))
            return new(selected, "The selected model supports the required capabilities.", false);

        if (request.Policy.Mode == ModelRoutingMode.ManualFallback)
            foreach (var key in request.Policy.PreferredModelKeys)
                if (candidates.FirstOrDefault(model => model.Matches(key)) is { } fallback)
                    return new(fallback, $"Selected the next compatible model in the configured fallback chain: {fallback.Label}.", true);

        var automatic = candidates
            .OrderByDescending(model => request.Policy.PreferLocal && model.IsLocal)
            .ThenByDescending(model => model.Capabilities.Count)
            .ThenByDescending(model => model.ContextWindow ?? 0)
            .ThenBy(model => model.Label, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return automatic is null
            ? throw new InvalidOperationException("No enabled model supports the capabilities required for this request.")
            : new(automatic, automatic.IsLocal ? "Selected a compatible local model." : "Selected a compatible cloud model.", request.SelectedModel is not null);
    }
}
