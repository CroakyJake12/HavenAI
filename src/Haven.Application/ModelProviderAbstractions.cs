using Haven.Core;

namespace Haven.Application;

/// <summary>
/// The compatibility model client used by user-facing surfaces that must see both
/// first-class local Ollama models and provider-qualified remote models.
/// </summary>
public interface IProviderModelClient : IOllamaClient
{
}

public interface IModelProvider
{
    string Id { get; }
    string DisplayName { get; }
    ModelProviderKind Kind { get; }
    bool IsLocal { get; }
    bool CanManageModels { get; }
    Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken cancellationToken);
    Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken);
    Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken);
    Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) => Task.FromException(new NotSupportedException($"{DisplayName} does not support installing models."));
    Task DeleteModelAsync(string model, CancellationToken cancellationToken) => Task.FromException(new NotSupportedException($"{DisplayName} does not support removing models."));
}

public interface IModelProviderRegistry
{
    IReadOnlyList<IModelProvider> Providers { get; }
    IModelProvider? Find(string providerId);
    IModelProvider GetRequired(string providerId);
    Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken);
}

public interface IModelRouter
{
    Task<ModelRoutingDecision> RouteAsync(ModelRoutingRequest request, CancellationToken cancellationToken);
}

public interface IProviderConfigurationStore
{
    Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken);
    Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken cancellationToken);
    Task UpsertAsync(ProviderConfiguration configuration, CancellationToken cancellationToken);
    Task DeleteAsync(string providerId, CancellationToken cancellationToken);
}

public interface IProviderSecretStore
{
    Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken);
    Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken);
    Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken);
}
