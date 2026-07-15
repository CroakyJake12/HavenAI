using System.Net;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class ResilientProviderRoutingModelClient(
    ProviderRoutingModelClient primary,
    IModelProviderRegistry providers,
    IProviderConfigurationStore configurations) : IOllamaClient
{
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => primary.IsAvailableAsync(cancellationToken);
    public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => primary.GetModelsAsync(cancellationToken);
    public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) => primary.PullModelAsync(model, progress, cancellationToken);
    public Task DeleteModelAsync(string model, CancellationToken cancellationToken) => primary.DeleteModelAsync(model, cancellationToken);

    public async IAsyncEnumerable<string> StreamChatAsync(
        OllamaChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var emitted = false;
        Exception? firstFailure = null;
        foreach (var model in await GetCandidatesAsync(request.Model, RequiredCapabilities(request), cancellationToken).ConfigureAwait(false))
        {
            var routedRequest = request with { Model = model };
            try
            {
                await foreach (var chunk in primary.StreamChatAsync(routedRequest, cancellationToken).ConfigureAwait(false))
                {
                    emitted = true;
                    yield return chunk;
                }
                yield break;
            }
            catch (Exception ex) when (!emitted && IsRecoverable(ex, cancellationToken))
            {
                firstFailure ??= ex;
            }
        }
        throw new InvalidOperationException("Every compatible model failed before producing output.", firstFailure);
    }

    public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        Exception? firstFailure = null;
        foreach (var model in await GetCandidatesAsync(request.Model, RequiredCapabilities(request), cancellationToken).ConfigureAwait(false))
        {
            try { return await primary.CompleteAsync(request with { Model = model }, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (IsRecoverable(ex, cancellationToken)) { firstFailure ??= ex; }
        }
        throw new InvalidOperationException("Every compatible model failed before completing the request.", firstFailure);
    }

    public async Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
    {
        var hasPriorToolState = request.Messages.Any(message => !string.IsNullOrWhiteSpace(message.ToolName) || message.ToolCalls is { Count: > 0 });
        if (hasPriorToolState) return await primary.ChatWithToolsAsync(request, cancellationToken).ConfigureAwait(false);

        Exception? firstFailure = null;
        foreach (var model in await GetCandidatesAsync(request.Model, RequiredCapabilities(request), cancellationToken).ConfigureAwait(false))
        {
            try { return await primary.ChatWithToolsAsync(request with { Model = model }, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (IsRecoverable(ex, cancellationToken)) { firstFailure ??= ex; }
        }
        throw new InvalidOperationException("Every compatible model failed before the first tool call.", firstFailure);
    }

    private async Task<IReadOnlyList<string>> GetCandidatesAsync(
        string requestedModel,
        IReadOnlySet<ToolCapability> required,
        CancellationToken cancellationToken)
    {
        var descriptors = await providers.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var requested = descriptors.FirstOrDefault(item => item.Matches(requestedModel));
        if (requested is null)
        {
            var providerId = ProviderId(requestedModel);
            var modelName = ModelName(requestedModel);
            requested = descriptors.FirstOrDefault(item => item.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase)
                                                            && item.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));
        }

        var firstKey = requested?.Key ?? requestedModel;
        var selectedProviderId = requested?.ProviderId ?? ProviderId(requestedModel);
        var selectedConfiguration = await configurations.GetAsync(selectedProviderId, cancellationToken).ConfigureAwait(false);
        var allowCloud = requested?.IsLocal == false || selectedConfiguration?.AllowCloudFallback == true;
        var compatible = descriptors.Where(item => required.All(item.Supports) && (allowCloud || item.IsLocal)).ToArray();
        var result = new List<string> { requested?.IsLocal == false ? requested.Key : requested?.Name ?? requestedModel };

        if (selectedConfiguration?.Metadata.TryGetValue("fallback-chain", out var chain) == true)
        {
            foreach (var key in chain.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var descriptor = compatible.FirstOrDefault(item => item.Matches(key));
                if (descriptor is not null) Add(descriptor);
            }
        }

        foreach (var descriptor in compatible
                     .OrderByDescending(item => item.IsLocal)
                     .ThenByDescending(item => item.Capabilities.Count)
                     .ThenByDescending(item => item.ContextWindow ?? 0)
                     .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase))
            Add(descriptor);
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        void Add(ProviderModelDescriptor descriptor)
        {
            var key = descriptor.IsLocal ? descriptor.Name : descriptor.Key;
            if (!key.Equals(firstKey, StringComparison.OrdinalIgnoreCase)) result.Add(key);
        }
    }

    private static IReadOnlySet<ToolCapability> RequiredCapabilities(OllamaChatRequest request)
    {
        var required = new HashSet<ToolCapability> { ToolCapability.Text };
        if (request.Messages.Any(message => message.Images is { Count: > 0 })) required.Add(ToolCapability.Vision);
        return required;
    }

    private static IReadOnlySet<ToolCapability> RequiredCapabilities(OllamaToolRequest request)
    {
        var required = new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools };
        if (request.Messages.Any(message => message.Images is { Count: > 0 })) required.Add(ToolCapability.Vision);
        return required;
    }

    private static bool IsRecoverable(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => false,
        TaskCanceledException => true,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests } => true,
        HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError } => true,
        InvalidOperationException => true,
        _ => false
    };

    private static string ProviderId(string model)
    {
        var separator = model.IndexOf(':');
        if (separator <= 0) return "ollama";
        var prefix = model[..separator];
        return prefix is "openai" or "anthropic" or "gemini" or "openrouter" or "openai-compatible" or "ollama" ? prefix : "ollama";
    }

    private static string ModelName(string model)
    {
        var provider = ProviderId(model);
        return provider == "ollama" || !model.StartsWith(provider + ":", StringComparison.OrdinalIgnoreCase)
            ? model
            : model[(provider.Length + 1)..];
    }
}
