/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/ProviderRoutingModelClient.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ProviderRoutingModelClient, ResolvedProvider. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Compatibility bridge for the existing chat pipeline. It exposes the established
/// IOllamaClient request DTOs while routing provider-qualified model keys to the
/// registered provider. Local Ollama model names remain unchanged and first-class.
/// </summary>
public sealed class ProviderRoutingModelClient(
    IOllamaClient localOllama,
    IModelProviderRegistry providers) : IProviderModelClient
{
    /// <summary>
    /// Reports whether is available async is true for the current state.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        if (await localOllama.IsAvailableAsync(cancellationToken).ConfigureAwait(false)) return true;
        if (RuntimeSafetyState.IsSafeMode) return false;
        return (await providers.GetModelsAsync(cancellationToken).ConfigureAwait(false)).Count > 0;
    }

    /// <summary>
    /// Retrieves models async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
        (await providers.GetModelsAsync(cancellationToken).ConfigureAwait(false))
        .Where(item => !RuntimeSafetyState.IsSafeMode || item.IsLocal)
        .Select(ToCompatibilityDescriptor)
        .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .OrderByDescending(item => !item.Name.Contains(':', StringComparison.Ordinal))
        .ThenBy(item => item.Family, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// Performs stream chat async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async IAsyncEnumerable<string> StreamChatAsync(
        OllamaChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resolved = Resolve(request.Model);
        await foreach (var chunk in resolved.Provider.StreamChatAsync(request with { Model = resolved.ModelName }, cancellationToken).ConfigureAwait(false))
            yield return chunk;
    }

    /// <summary>
    /// Performs complete async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        var resolved = Resolve(request.Model);
        return resolved.Provider.CompleteAsync(request with { Model = resolved.ModelName }, cancellationToken);
    }

    /// <summary>
    /// Performs chat with tools async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
    {
        var resolved = Resolve(request.Model);
        return resolved.Provider.ChatWithToolsAsync(request with { Model = resolved.ModelName }, cancellationToken);
    }

    /// <summary>
    /// Performs pull model async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) =>
        localOllama.PullModelAsync(UnqualifyLocal(model), progress, cancellationToken);

    /// <summary>
    /// Performs delete model async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteModelAsync(string model, CancellationToken cancellationToken) =>
        localOllama.DeleteModelAsync(UnqualifyLocal(model), cancellationToken);

    /// <summary>
    /// Performs the to compatibility descriptor step owned by this component.
    /// </summary>
    public ModelDescriptor ToCompatibilityDescriptor(ProviderModelDescriptor descriptor)
    {
        var requestName = descriptor.ProviderId.Equals("ollama", StringComparison.OrdinalIgnoreCase)
            ? descriptor.Name
            : descriptor.Key;
        var family = descriptor.IsLocal
            ? descriptor.Model.Family
            : string.IsNullOrWhiteSpace(descriptor.DisplayName)
                ? descriptor.ProviderId
                : descriptor.DisplayName;
        return descriptor.Model with
        {
            Name = requestName,
            Family = family,
            Capabilities = descriptor.Capabilities
        };
    }

    /// <summary>
    /// Performs the resolve step owned by this component.
    /// </summary>
    private ResolvedProvider Resolve(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("A model is required.", nameof(model));
        var trimmed = model.Trim();
        var separator = trimmed.IndexOf(':');
        if (separator > 0)
        {
            var providerId = trimmed[..separator];
            if (providers.Find(providerId) is { } provider)
            {
                if (RuntimeSafetyState.IsSafeMode && !provider.IsLocal)
                    throw new InvalidOperationException("Cloud model providers are disabled while Haven is in crash-loop recovery safe mode. Select a local Ollama model or restart after resolving the startup problem.");
                var actualModel = trimmed[(separator + 1)..];
                if (string.IsNullOrWhiteSpace(actualModel)) throw new ArgumentException("The provider-qualified model key is incomplete.", nameof(model));
                return new ResolvedProvider(provider, actualModel);
            }
        }
        return new ResolvedProvider(providers.GetRequired("ollama"), trimmed);
    }

    /// <summary>
    /// Performs the unqualify local step owned by this component.
    /// </summary>
    private static string UnqualifyLocal(string model) => model.StartsWith("ollama:", StringComparison.OrdinalIgnoreCase)
        ? model["ollama:".Length..]
        : model;

    /// <summary>
    /// Represents resolved provider and keeps its related state and behavior together.
    /// </summary>
    private sealed record ResolvedProvider(IModelProvider Provider, string ModelName);
}
