/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/ResilientProviderRoutingModelClient.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns ResilientProviderRoutingModelClient. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Net;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents resilient provider routing model client and keeps its related state and behavior together.
/// Honours the user's ordered fallback preference ahead of automatic ranking and records real
/// model switches in the Action Graph.
/// </summary>
public sealed class ResilientProviderRoutingModelClient(
    ProviderRoutingModelClient primary,
    IModelProviderRegistry providers,
    IProviderConfigurationStore configurations,
    IPrivacyPreferenceStore privacy,
    IModelFallbackOrderStore? fallbackOrder = null,
    IExecutionEventSink? executionEvents = null) : IProviderModelClient
{
    /// <summary>
    /// Reports whether available async applies to the current state.
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => primary.IsAvailableAsync(cancellationToken);
    /// <summary>
    /// Retrieves models async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => primary.GetModelsAsync(cancellationToken);
    /// <summary>
    /// Performs pull model asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) => primary.PullModelAsync(model, progress, cancellationToken);
    /// <summary>
    /// Performs delete model asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteModelAsync(string model, CancellationToken cancellationToken) => primary.DeleteModelAsync(model, cancellationToken);

    /// <summary>
    /// Performs stream chat asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async IAsyncEnumerable<string> StreamChatAsync(
        OllamaChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var emitted = false;
        Exception? firstFailure = null;
        foreach (var (model, index) in (await GetCandidatesAsync(request.Model, RequiredCapabilities(request), cancellationToken).ConfigureAwait(false)).Select((value, index) => (value, index)))
        {
            if (index > 0) PublishFallback(request.Model, model, firstFailure);
            var routedRequest = request with { Model = model };
            var failedBeforeOutput = false;
            await using var enumerator = primary.StreamChatAsync(routedRequest, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool movedNext;
                try
                {
                    movedNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (!emitted && IsRecoverable(ex, cancellationToken))
                {
                    firstFailure ??= ex;
                    failedBeforeOutput = true;
                    break;
                }

                if (!movedNext) break;
                emitted = true;
                yield return enumerator.Current;
            }

            if (!failedBeforeOutput) yield break;
        }
        throw new InvalidOperationException("Every compatible model failed before producing output.", firstFailure);
    }

    /// <summary>
    /// Performs complete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
    {
        Exception? firstFailure = null;
        var candidates = await GetCandidatesAsync(request.Model, RequiredCapabilities(request), cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < candidates.Count; index++)
        {
            if (index > 0) PublishFallback(request.Model, candidates[index], firstFailure);
            try { return await primary.CompleteAsync(request with { Model = candidates[index] }, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (IsRecoverable(ex, cancellationToken)) { firstFailure ??= ex; }
        }
        throw new InvalidOperationException("Every compatible model failed before completing the request.", firstFailure);
    }

    /// <summary>
    /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
    {
        var hasPriorToolState = request.Messages.Any(message => !string.IsNullOrWhiteSpace(message.ToolName) || message.ToolCalls is { Count: > 0 });
        if (hasPriorToolState) return await primary.ChatWithToolsAsync(request, cancellationToken).ConfigureAwait(false);

        Exception? firstFailure = null;
        var candidates = await GetCandidatesAsync(request.Model, RequiredCapabilities(request), cancellationToken).ConfigureAwait(false);
        for (var index = 0; index < candidates.Count; index++)
        {
            if (index > 0) PublishFallback(request.Model, candidates[index], firstFailure);
            try { return await primary.ChatWithToolsAsync(request with { Model = candidates[index] }, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (IsRecoverable(ex, cancellationToken)) { firstFailure ??= ex; }
        }
        throw new InvalidOperationException("Every compatible model failed before the first tool call.", firstFailure);
    }

    /// <summary>
    /// Retrieves candidates async for the current operation.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetCandidatesAsync(
        string requestedModel,
        IReadOnlySet<ToolCapability> required,
        CancellationToken cancellationToken)
    {
        var descriptors = await GetEligibleModelsAsync(cancellationToken).ConfigureAwait(false);
        var requested = descriptors.FirstOrDefault(item => item.Matches(requestedModel));
        if (requested is null)
        {
            var providerId = ProviderId(requestedModel);
            var modelName = ModelName(requestedModel);
            requested = descriptors.FirstOrDefault(item => item.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase)
                                                            && item.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));
        }

        var firstKey = requested?.IsLocal == false ? requested.Key : requested?.Name ?? requestedModel;
        var selectedProviderId = requested?.ProviderId ?? ProviderId(requestedModel);
        var selectedConfiguration = await configurations.GetAsync(selectedProviderId, cancellationToken).ConfigureAwait(false);
        var allowCloud = !privacy.Current.LocalOnlyMode
                         && (requested?.IsLocal == false || selectedConfiguration?.AllowCloudFallback == true);
        var compatible = descriptors.Where(item => required.All(item.Supports) && (allowCloud || item.IsLocal)).ToArray();
        var result = new List<string> { firstKey };

        // The user's ordered fallback preference always outranks per-provider chains and automatic ranking.
        if (fallbackOrder is not null)
        {
            foreach (var key in await fallbackOrder.GetOrderAsync(cancellationToken).ConfigureAwait(false))
            {
                var descriptor = compatible.FirstOrDefault(item => item.Matches(key));
                if (descriptor is not null) Add(descriptor);
            }
        }

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

    /// <summary>
    /// Records a real model switch in the Action Graph without interrupting execution.
    /// </summary>
    private void PublishFallback(string requestedModel, string actualModel, Exception? cause)
    {
        if (executionEvents is null) return;
        var requestedLabel = string.IsNullOrWhiteSpace(requestedModel) ? actualModel : requestedModel;
        executionEvents.TryPublish(new ExecutionEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExecutionOrigin.Haven,
            ExecutionActionType.ModelFallback, ExecutionActionStatus.Completed,
            $"{requestedLabel} unavailable — switched to {actualModel}.", null,
            cause?.GetType().Name, "model-routing", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            SafeMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["requested"] = requestedLabel,
                ["used"] = actualModel
            }));
    }

    private async Task<IReadOnlyList<ProviderModelDescriptor>> GetEligibleModelsAsync(CancellationToken cancellationToken)
    {
        var models = new List<ProviderModelDescriptor>();
        foreach (var provider in providers.Providers.Where(item => !privacy.Current.LocalOnlyMode || item.IsLocal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                models.AddRange(await provider.GetModelsAsync(cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException
                                             || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // An unavailable eligible provider does not prevent fallback to another eligible provider.
            }
        }

        return models
            .GroupBy(model => model.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    /// <summary>
    /// Performs the required capabilities step owned by this component.
    /// </summary>
    private static IReadOnlySet<ToolCapability> RequiredCapabilities(OllamaChatRequest request)
    {
        var required = new HashSet<ToolCapability> { ToolCapability.Text };
        if (request.Messages.Any(message => message.Images is { Count: > 0 })) required.Add(ToolCapability.Vision);
        return required;
    }

    /// <summary>
    /// Performs the required capabilities step owned by this component.
    /// </summary>
    private static IReadOnlySet<ToolCapability> RequiredCapabilities(OllamaToolRequest request)
    {
        var required = new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools };
        if (request.Messages.Any(message => message.Images is { Count: > 0 })) required.Add(ToolCapability.Vision);
        return required;
    }

    /// <summary>
    /// Reports whether recoverable applies to the current state.
    /// </summary>
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

    /// <summary>
    /// Performs the provider id step owned by this component.
    /// </summary>
    private static string ProviderId(string model)
    {
        var separator = model.IndexOf(':');
        if (separator <= 0) return "ollama";
        var prefix = model[..separator];
        return prefix is "openai" or "anthropic" or "gemini" or "openrouter" or "openai-compatible" or "ollama" ? prefix : "ollama";
    }

    /// <summary>
    /// Performs the model name step owned by this component.
    /// </summary>
    private static string ModelName(string model)
    {
        var provider = ProviderId(model);
        return provider == "ollama" || !model.StartsWith(provider + ":", StringComparison.OrdinalIgnoreCase)
            ? model
            : model[(provider.Length + 1)..];
    }
}
