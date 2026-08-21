using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>Routes Mesh private identity material through Haven's approved OS-backed secret store.</summary>
public sealed class MeshIdentitySecretStore(IProviderSecretStore secrets) : IMeshIdentitySecretStore
{
    private const string ProviderId = "haven.mesh";
    private static string SecretName(Guid deviceId) => $"identity-{deviceId:N}";

    public Task<string?> GetPrivateKeyAsync(Guid deviceId, CancellationToken cancellationToken) =>
        secrets.GetAsync(ProviderId, SecretName(deviceId), cancellationToken);

    public Task SetPrivateKeyAsync(Guid deviceId, string privateKey, CancellationToken cancellationToken) =>
        secrets.SetAsync(ProviderId, SecretName(deviceId), privateKey, cancellationToken);

    public Task DeletePrivateKeyAsync(Guid deviceId, CancellationToken cancellationToken) =>
        secrets.DeleteAsync(ProviderId, SecretName(deviceId), cancellationToken);
}
