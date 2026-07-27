/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/OllamaModelProvider.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns OllamaModelProvider. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Diagnostics;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents ollama model provider and keeps its related state and behavior together.
/// </summary>
public sealed class OllamaModelProvider(IOllamaClient client, IProviderConfigurationStore configurations) : IModelProvider
{
    /// <summary>
    /// Gets or updates id, the bindable or domain state represented by this property.
    /// </summary>
    public string Id => "ollama";
    /// <summary>
    /// Gets or updates display name, the bindable or domain state represented by this property.
    /// </summary>
    public string DisplayName => "Ollama";
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public ModelProviderKind Kind => ModelProviderKind.Ollama;
    /// <summary>
    /// Reports whether local applies to the current state.
    /// </summary>
    public bool IsLocal => true;
    /// <summary>
    /// Reports whether manage models applies to the current state.
    /// </summary>
    public bool CanManageModels => true;

    /// <summary>
    /// Performs check health asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var configuration = await configurations.GetAsync(Id, cancellationToken).ConfigureAwait(false);
            if (configuration?.IsEnabled == false)
                return new(Id, false, "Ollama is disabled in Haven settings.", Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
            var available = await client.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
            var endpoint = configuration?.Endpoint ?? Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://127.0.0.1:11434/";
            return new(Id, available, available ? $"Connected to Ollama at {endpoint}." : $"Ollama did not respond at {endpoint}.",
                Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return new(Id, false, ex.Message, Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Retrieves models async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var configuration = await configurations.GetAsync(Id, cancellationToken).ConfigureAwait(false);
        if (configuration?.IsEnabled == false) return [];
        var isLocal = configuration?.IsLocal ?? true;
        return (await client.GetModelsAsync(cancellationToken).ConfigureAwait(false))
            .Select(model => new ProviderModelDescriptor(Id, isLocal, model)).ToArray();
    }

    /// <summary>
    /// Performs stream chat asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken cancellationToken) => client.StreamChatAsync(request, cancellationToken);
    /// <summary>
    /// Performs complete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => client.CompleteAsync(request, cancellationToken);
    /// <summary>
    /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) => client.ChatWithToolsAsync(request, cancellationToken);
    /// <summary>
    /// Performs pull model asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) => client.PullModelAsync(model, progress, cancellationToken);
    /// <summary>
    /// Performs delete model asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteModelAsync(string model, CancellationToken cancellationToken) => client.DeleteModelAsync(model, cancellationToken);
}
