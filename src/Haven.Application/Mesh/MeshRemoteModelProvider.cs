using Haven.Core;

namespace Haven.Application;

public sealed class MeshRemoteModelProvider(MeshCoordinator mesh) : IModelProvider
{
    public const string MeshProviderId = "mesh";
    public string Id => MeshProviderId;
    public string DisplayName => "Haven Mesh";
    public ModelProviderKind Kind => ModelProviderKind.OpenAICompatible;
    public bool IsLocal => true;
    public bool CanManageModels => false;

    public async Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var dashboard = await mesh.GetDashboardAsync(cancellationToken).ConfigureAwait(false);
        var connected = dashboard.TrustedPeers.Count(peer => peer.Presence.Connection == MeshConnectionState.Connected);
        return new(Id, connected > 0, connected > 0 ? $"{connected} trusted Mesh device(s) connected." : "No trusted Mesh devices are connected.", System.Diagnostics.Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
    }

    public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => mesh.GetRemoteModelsAsync(cancellationToken);

    public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return await CompleteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
        mesh.CompleteRemoteModelAsync(request.Model, request, cancellationToken);

    public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
        mesh.ChatWithRemoteModelToolsAsync(request.Model, request, cancellationToken);

    public static string EncodeRoute(Guid deviceId, string providerId, string modelName)
    {
        if (deviceId == Guid.Empty) throw new ArgumentException("Mesh device ID is required.", nameof(deviceId));
        if (string.IsNullOrWhiteSpace(providerId) || string.Equals(providerId, MeshProviderId, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("A non-Mesh target provider is required.", nameof(providerId));
        if (string.IsNullOrWhiteSpace(modelName)) throw new ArgumentException("Remote model name is required.", nameof(modelName));
        return $"{deviceId:N}|{Uri.EscapeDataString(providerId.Trim())}|{Uri.EscapeDataString(modelName.Trim())}";
    }

    public static MeshModelRoute DecodeRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route)) throw new ArgumentException("Mesh model route is required.", nameof(route));
        var parts = route.Split('|', 3);
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out var deviceId)) throw new ArgumentException("The Mesh model route is invalid.", nameof(route));
        var providerId = Uri.UnescapeDataString(parts[1]);
        var modelName = Uri.UnescapeDataString(parts[2]);
        if (string.IsNullOrWhiteSpace(providerId) || string.Equals(providerId, MeshProviderId, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(modelName)) throw new ArgumentException("The Mesh model route is incomplete or recursive.", nameof(route));
        return new MeshModelRoute(deviceId, providerId, modelName);
    }
}

public sealed record MeshModelRoute(Guid DeviceId, string ProviderId, string ModelName);
