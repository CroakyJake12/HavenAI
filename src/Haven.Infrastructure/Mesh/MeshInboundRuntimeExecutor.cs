using System.Text;
using Haven.Application;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

/// <summary>Executes explicitly permitted Mesh model/agent requests through the target device's existing Haven runtime.</summary>
public sealed class MeshInboundRuntimeExecutor(IServiceProvider services) : IMeshInboundRuntimeExecutor
{
    public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var registry = services.GetRequiredService<IModelProviderRegistry>();
        var result = new List<ProviderModelDescriptor>();
        foreach (var provider in registry.Providers.Where(provider => !string.Equals(provider.Id, MeshRemoteModelProvider.MeshProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { result.AddRange(await provider.GetModelsAsync(cancellationToken).ConfigureAwait(false)); }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException) { }
        }
        return result;
    }

    public Task<string> CompleteModelAsync(string providerId, OllamaChatRequest request, CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerId);
        return provider.CompleteAsync(request, cancellationToken);
    }

    public Task<OllamaToolResponse> ChatWithToolsAsync(string providerId, OllamaToolRequest request, CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerId);
        return provider.ChatWithToolsAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentDefinition>> GetAgentsAsync(CancellationToken cancellationToken) =>
        (await services.GetRequiredService<ICatalogRepository>().GetAgentsAsync(cancellationToken).ConfigureAwait(false))
        .Where(agent => agent.IsEnabled)
        .OrderBy(agent => agent.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public async Task<string> ExecuteAgentAsync(Guid agentId, string prompt, CancellationToken cancellationToken)
    {
        if (agentId == Guid.Empty) throw new ArgumentException("Agent ID is required.", nameof(agentId));
        if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("Agent prompt is required.", nameof(prompt));
        var catalog = services.GetRequiredService<ICatalogRepository>();
        var agent = (await catalog.GetAgentsAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.Id == agentId && item.IsEnabled)
            ?? throw new KeyNotFoundException("The requested remote agent is missing or disabled on this device.");
        var registry = services.GetRequiredService<IModelProviderRegistry>();
        var model = await ResolveAgentModelAsync(registry, agent, cancellationToken).ConfigureAwait(false);
        if (model.Name.StartsWith(MeshRemoteModelProvider.MeshProviderId + ":", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A Mesh agent cannot recursively execute another Mesh model.");

        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, $"Mesh · {agent.Name}", null, null, false, true, now, now);
        var session = services.GetRequiredService<ChatSessionService>();
        var output = new StringBuilder();
        await foreach (var item in session.SendAsync(
                           conversation, prompt.Trim(), model, EffortLevel.Medium, Array.Empty<ActiveCapability>(),
                           agent.Name, agent.Instructions, DuoMode.Solo, null, null, null, null, cancellationToken,
                           prompts: null, registeredContext: null, generationOptions: null,
                           filePermission: PermissionMode.Ask, commandPermission: PermissionMode.Ask, browserPermission: PermissionMode.Ask,
                           explicitCapabilities: Array.Empty<ToolCapability>(), availableCapabilities: Array.Empty<ActiveCapability>()).ConfigureAwait(false))
        {
            if (item.Kind == ChatStreamEventKind.AssistantDelta && item.Delta is { Length: > 0 }) output.Append(item.Delta);
            else if (item.Kind == ChatStreamEventKind.AssistantCompleted && output.Length == 0 && item.Message?.Content is { Length: > 0 } complete) output.Append(complete);
            else if (item.Kind == ChatStreamEventKind.PreflightFailed)
            {
                var missing = string.Join(", ", item.PreflightResult?.Missing.Select(requirement => requirement.Capability.ToString()) ?? []);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(missing) ? "The remote agent model failed Haven capability preflight." : $"The remote agent model cannot satisfy this request without additional capabilities: {missing}.");
            }
        }
        return output.ToString();
    }

    private IModelProvider ResolveProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) throw new ArgumentException("Target provider ID is required.", nameof(providerId));
        if (string.Equals(providerId, MeshRemoteModelProvider.MeshProviderId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Mesh model recursion is not allowed.");
        return services.GetRequiredService<IModelProviderRegistry>().GetRequired(providerId);
    }

    private static async Task<ModelDescriptor> ResolveAgentModelAsync(IModelProviderRegistry registry, AgentDefinition agent, CancellationToken cancellationToken)
    {
        foreach (var candidate in new[] { agent.PreferredModel, agent.FallbackModel }.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (await TryResolveModelAsync(registry, candidate!, cancellationToken).ConfigureAwait(false) is { } resolved) return resolved;
        }
        if (registry.Find("ollama") is { } ollama)
        {
            var first = (await ollama.GetModelsAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault();
            if (first is not null) return ToCompatibilityDescriptor(first, ollama.Id);
        }
        throw new InvalidOperationException($"Agent '{agent.Name}' has no available non-Mesh model on this device.");
    }

    private static async Task<ModelDescriptor?> TryResolveModelAsync(IModelProviderRegistry registry, string candidate, CancellationToken cancellationToken)
    {
        var trimmed = candidate.Trim();
        if (trimmed.StartsWith(MeshRemoteModelProvider.MeshProviderId + ":", StringComparison.OrdinalIgnoreCase)) return null;
        var separator = trimmed.IndexOf(':');
        IModelProvider provider;
        string modelName;
        if (separator > 0 && registry.Find(trimmed[..separator]) is { } qualified)
        {
            provider = qualified;
            modelName = trimmed[(separator + 1)..];
        }
        else
        {
            var localOllama = registry.Find("ollama");
            if (localOllama is null) return null;
            provider = localOllama;
            modelName = trimmed;
        }
        if (string.Equals(provider.Id, MeshRemoteModelProvider.MeshProviderId, StringComparison.OrdinalIgnoreCase)) return null;
        var descriptor = (await provider.GetModelsAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase) || item.Key.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        return descriptor is null ? null : ToCompatibilityDescriptor(descriptor, provider.Id);
    }

    private static ModelDescriptor ToCompatibilityDescriptor(ProviderModelDescriptor descriptor, string providerId)
    {
        var requestName = providerId.Equals("ollama", StringComparison.OrdinalIgnoreCase) ? descriptor.Name : $"{providerId}:{descriptor.Name}";
        return descriptor.Model with { Name = requestName, Family = string.IsNullOrWhiteSpace(descriptor.DisplayName) ? descriptor.Model.Family : descriptor.DisplayName! };
    }
}
