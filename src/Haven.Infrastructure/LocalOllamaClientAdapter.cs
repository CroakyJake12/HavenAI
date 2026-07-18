/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/LocalOllamaClientAdapter.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns LocalOllamaClientAdapter. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents local ollama client adapter and keeps its related state and behavior together.
/// </summary>
internal sealed class LocalOllamaClientAdapter(OllamaClient inner) : ILocalOllamaClient
{
    /// <summary>
    /// Reports whether available async applies to the current state.
    /// </summary>
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        inner.IsAvailableAsync(cancellationToken);

    /// <summary>
    /// Retrieves models async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
        inner.GetModelsAsync(cancellationToken);

    /// <summary>
    /// Performs stream chat asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public IAsyncEnumerable<string> StreamChatAsync(
        OllamaChatRequest request,
        CancellationToken cancellationToken) =>
        inner.StreamChatAsync(request, cancellationToken);

    /// <summary>
    /// Performs complete asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<string> CompleteAsync(
        OllamaChatRequest request,
        CancellationToken cancellationToken) =>
        inner.CompleteAsync(request, cancellationToken);

    /// <summary>
    /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<OllamaToolResponse> ChatWithToolsAsync(
        OllamaToolRequest request,
        CancellationToken cancellationToken) =>
        inner.ChatWithToolsAsync(request, cancellationToken);

    /// <summary>
    /// Performs pull model asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task PullModelAsync(
        string model,
        IProgress<double>? progress,
        CancellationToken cancellationToken) =>
        inner.PullModelAsync(model, progress, cancellationToken);

    /// <summary>
    /// Performs delete model asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task DeleteModelAsync(string model, CancellationToken cancellationToken) =>
        inner.DeleteModelAsync(model, cancellationToken);
}
