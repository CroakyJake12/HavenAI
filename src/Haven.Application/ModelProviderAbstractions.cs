/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ModelProviderAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns IProviderModelClient, IModelProvider, IModelProviderRegistry, IModelRouter, IProviderConfigurationStore, IProviderSecretStore. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// The compatibility model client used by user-facing surfaces that must see both
/// first-class local Ollama models and provider-qualified remote models.
/// </summary>
public interface IProviderModelClient : IOllamaClient
{
}

/// <summary>
/// Defines the i model provider contract so callers depend on a capability rather than one implementation.
/// </summary>
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

/// <summary>
/// Defines the i model provider registry contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IModelProviderRegistry
{
    IReadOnlyList<IModelProvider> Providers { get; }
    IModelProvider? Find(string providerId);
    IModelProvider GetRequired(string providerId);
    Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Defines the i model router contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IModelRouter
{
    Task<ModelRoutingDecision> RouteAsync(ModelRoutingRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the i provider configuration store contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IProviderConfigurationStore
{
    Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken);
    Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken cancellationToken);
    Task UpsertAsync(ProviderConfiguration configuration, CancellationToken cancellationToken);
    Task DeleteAsync(string providerId, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the i provider secret store contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IProviderSecretStore
{
    Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken);
    Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken);
    Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken);
}
