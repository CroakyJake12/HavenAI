using System.Diagnostics;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class OllamaModelProvider(IOllamaClient client, IProviderConfigurationStore configurations) : IModelProvider
{
    public string Id => "ollama";
    public string DisplayName => "Ollama";
    public ModelProviderKind Kind => ModelProviderKind.Ollama;
    public bool IsLocal => true;
    public bool CanManageModels => true;

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
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return new(Id, false, ex.Message, Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
    }

    public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var configuration = await configurations.GetAsync(Id, cancellationToken).ConfigureAwait(false);
        if (configuration?.IsEnabled == false) return [];
        var isLocal = configuration?.IsLocal ?? true;
        return (await client.GetModelsAsync(cancellationToken).ConfigureAwait(false))
            .Select(model => new ProviderModelDescriptor(Id, isLocal, model)).ToArray();
    }

    public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken cancellationToken) => client.StreamChatAsync(request, cancellationToken);
    public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => client.CompleteAsync(request, cancellationToken);
    public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) => client.ChatWithToolsAsync(request, cancellationToken);
    public Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) => client.PullModelAsync(model, progress, cancellationToken);
    public Task DeleteModelAsync(string model, CancellationToken cancellationToken) => client.DeleteModelAsync(model, cancellationToken);
}
