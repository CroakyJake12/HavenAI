using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

internal sealed class LocalOllamaClientAdapter(OllamaClient inner) : ILocalOllamaClient
{
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        inner.IsAvailableAsync(cancellationToken);

    public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
        inner.GetModelsAsync(cancellationToken);

    public IAsyncEnumerable<string> StreamChatAsync(
        OllamaChatRequest request,
        CancellationToken cancellationToken) =>
        inner.StreamChatAsync(request, cancellationToken);

    public Task<string> CompleteAsync(
        OllamaChatRequest request,
        CancellationToken cancellationToken) =>
        inner.CompleteAsync(request, cancellationToken);

    public Task<OllamaToolResponse> ChatWithToolsAsync(
        OllamaToolRequest request,
        CancellationToken cancellationToken) =>
        inner.ChatWithToolsAsync(request, cancellationToken);

    public Task PullModelAsync(
        string model,
        IProgress<double>? progress,
        CancellationToken cancellationToken) =>
        inner.PullModelAsync(model, progress, cancellationToken);

    public Task DeleteModelAsync(string model, CancellationToken cancellationToken) =>
        inner.DeleteModelAsync(model, cancellationToken);
}
