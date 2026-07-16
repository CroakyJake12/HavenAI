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
    IModelProviderRegistry providers) : IOllamaClient
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        if (await localOllama.IsAvailableAsync(cancellationToken).ConfigureAwait(false)) return true;
        if (RuntimeSafetyState.IsSafeMode) return false;
        return (await providers.GetModelsAsync(cancellationToken).ConfigureAwait(false)).Count > 0;
    }

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

    public async IAsyncEnumerable<string> StreamChatAsync(
        OllamaChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resolved = Resolve(request.Model);
        await foreach (var chunk in resolved.Provider.StreamChatAsync(request with { Model = resolved.ModelName }, cancellationToken).ConfigureAwait(false))
            yield return chunk;
    }

    public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        var resolved = Resolve(request.Model);
        return resolved.Provider.CompleteAsync(request with { Model = resolved.ModelName }, cancellationToken);
    }

    public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
    {
        var resolved = Resolve(request.Model);
        return resolved.Provider.ChatWithToolsAsync(request with { Model = resolved.ModelName }, cancellationToken);
    }

    public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) =>
        localOllama.PullModelAsync(UnqualifyLocal(model), progress, cancellationToken);

    public Task DeleteModelAsync(string model, CancellationToken cancellationToken) =>
        localOllama.DeleteModelAsync(UnqualifyLocal(model), cancellationToken);

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

    private static string UnqualifyLocal(string model) => model.StartsWith("ollama:", StringComparison.OrdinalIgnoreCase)
        ? model["ollama:".Length..]
        : model;

    private sealed record ResolvedProvider(IModelProvider Provider, string ModelName);
}
