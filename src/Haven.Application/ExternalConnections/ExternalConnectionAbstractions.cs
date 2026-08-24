using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public interface IExternalConnectionRepository
{
    Task<IReadOnlyList<ExternalConnection>> GetAllAsync(CancellationToken cancellationToken);
    Task<ExternalConnection?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task UpsertAsync(ExternalConnection connection, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IMcpConnectionClient
{
    Task<(McpServerIdentity Identity, IReadOnlyList<McpExternalTool> Tools)> DiscoverAsync(ExternalConnection connection, CancellationToken cancellationToken);
    Task<McpToolInvocationResult> InvokeAsync(ExternalConnection connection, string toolName, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken);
}

/// <summary>Produces transient catalogue capabilities backed by current runtime state.</summary>
public interface IDynamicCapabilityProvider
{
    Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CapabilityPlatform platform, CancellationToken cancellationToken);
}
