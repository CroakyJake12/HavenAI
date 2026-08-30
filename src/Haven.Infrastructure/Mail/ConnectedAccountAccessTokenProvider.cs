using System.Net.Http.Json;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class ConnectedAccountAccessTokenProvider(
    IHttpClientFactory httpClientFactory,
    ICalendarTokenStore tokenStore,
    ICalendarOAuthClientIdProvider clientIds) : IConnectedAccountAccessTokenProvider
{
    public async Task<ConnectedAccountAccessToken> GetAsync(
        Guid accountId,
        CalendarProviderKind provider,
        IReadOnlyCollection<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        var token = await tokenStore.GetAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (token is null)
            throw new MailProviderException(MailFailureKind.NotConnected, "This account is not connected. Connect it in Settings first.");

        var scopes = ParseScopes(token.Scope);
        var missing = requiredScopes.Where(scope => !scopes.Contains(scope)).ToArray();
        if (missing.Length > 0)
            throw new MailProviderException(MailFailureKind.PermissionDenied, "Reconnect this account in Settings to grant Haven Mail access.");

        if (token.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(2))
        {
            token = await RefreshAsync(provider, token, cancellationToken).ConfigureAwait(false);
            await tokenStore.SaveAsync(accountId, token, cancellationToken).ConfigureAwait(false);
            scopes = ParseScopes(token.Scope);
        }

        return new ConnectedAccountAccessToken(token.AccessToken, scopes, token.ExpiresAt);
    }

    private async Task<CalendarTokenEnvelope> RefreshAsync(
        CalendarProviderKind provider,
        CalendarTokenEnvelope token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            throw new MailProviderException(MailFailureKind.ReconnectRequired, "This account session expired. Reconnect it in Settings.");

        var clientId = clientIds.GetClientId(provider);
        if (string.IsNullOrWhiteSpace(clientId))
            throw new MailProviderException(MailFailureKind.NotConnected, $"{provider} is not configured on this Haven installation.");

        var endpoint = provider == CalendarProviderKind.Google
            ? new Uri("https://oauth2.googleapis.com/token")
            : new Uri("https://login.microsoftonline.com/common/oauth2/v2.0/token");

        var client = httpClientFactory.CreateClient("HavenMail");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = token.RefreshToken
            })
        };

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new MailProviderException(MailFailureKind.ReconnectRequired, "The account session could not be refreshed. Reconnect it in Settings.");

            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = document.RootElement;
            var accessToken = root.TryGetProperty("access_token", out var access) ? access.GetString() : null;
            if (string.IsNullOrWhiteSpace(accessToken))
                throw new MailProviderException(MailFailureKind.ProviderError, "The provider did not return a refreshed access token.");
            var refreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : token.RefreshToken;
            var expiresIn = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) ? seconds : 3600;
            var scope = root.TryGetProperty("scope", out var scopeElement) ? scopeElement.GetString() : token.Scope;
            return new CalendarTokenEnvelope(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn)), scope ?? token.Scope);
        }
        catch (MailProviderException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new MailProviderException(MailFailureKind.Offline, "Haven could not reach the mail provider.", ex);
        }
        catch (JsonException ex)
        {
            throw new MailProviderException(MailFailureKind.ProviderError, "The provider returned an invalid token response.", ex);
        }
    }

    private static IReadOnlySet<string> ParseScopes(string value) =>
        new HashSet<string>(value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
}
