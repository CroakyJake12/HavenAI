using System.Text.Json;
using Haven.Application;
using Haven.Core;
using ModelContextProtocol.Authentication;

namespace Haven.Infrastructure;

internal sealed class McpOAuthTokenCache(IProviderSecretStore secrets, Guid connectionId) : ITokenCache
{
    private readonly string _providerId = ExternalConnectionNaming.SecretProviderId(connectionId);
    private const string SecretName = ExternalConnectionNaming.OAuthTokenSecretName;

    public async ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        await secrets.SetAsync(_providerId, SecretName, JsonSerializer.Serialize(tokens), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
    {
        var json = await secrets.GetAsync(_providerId, SecretName, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<TokenContainer>(json); }
        catch (JsonException)
        {
            await secrets.DeleteAsync(_providerId, SecretName, cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    public Task DeleteAsync(CancellationToken cancellationToken) => secrets.DeleteAsync(_providerId, SecretName, cancellationToken);
}
