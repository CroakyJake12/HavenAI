using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public abstract class MailHttpProviderBase(
    IHttpClientFactory httpClientFactory,
    IConnectedAccountAccessTokenProvider tokenProvider)
{
    protected HttpClient Client => httpClientFactory.CreateClient("HavenMail");
    protected IConnectedAccountAccessTokenProvider Tokens => tokenProvider;

    protected abstract CalendarProviderKind ProviderKind { get; }
    protected abstract IReadOnlyCollection<string> Scopes { get; }

    protected Task<ConnectedAccountAccessToken> GetTokenAsync(Guid accountId, CancellationToken cancellationToken)
        => tokenProvider.GetAsync(accountId, ProviderKind, Scopes, cancellationToken);

    protected async Task<JsonDocument> SendJsonAsync(
        Guid accountId,
        HttpMethod method,
        Uri uri,
        HttpContent? content,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var token = await GetTokenAsync(accountId, cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        if (headers is not null)
            foreach (var pair in headers) request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);

        try
        {
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) ThrowProviderError(response.StatusCode, body);
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
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
            throw new MailProviderException(MailFailureKind.ProviderError, "The mail provider returned an invalid response.", ex);
        }
    }

    protected async Task SendAsync(
        Guid accountId,
        HttpMethod method,
        Uri uri,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var _ = await SendJsonAsync(accountId, method, uri, content, cancellationToken).ConfigureAwait(false);
    }

    protected static MailOperationResult Failure(Exception exception) => exception switch
    {
        MailProviderException provider => new(false, provider.Message, provider.FailureKind, SuggestedRemediation: RemediationFor(provider.FailureKind)),
        OperationCanceledException => throw exception,
        _ => new(false, "The mail provider could not complete this operation.", MailFailureKind.ProviderError)
    };

    private static void ThrowProviderError(HttpStatusCode statusCode, string body)
    {
        _ = body; // Never expose provider response bodies: they can contain request/auth context.
        if (statusCode is HttpStatusCode.Unauthorized)
            throw new MailProviderException(MailFailureKind.ReconnectRequired, "The mail session has expired. Reconnect the account in Settings.");
        if (statusCode is HttpStatusCode.Forbidden)
            throw new MailProviderException(MailFailureKind.PermissionDenied, "The provider denied this Mail action. Reconnect the account and review its permissions.");
        if (statusCode is HttpStatusCode.NotFound)
            throw new MailProviderException(MailFailureKind.InvalidRequest, "The requested mail item was not found.");
        if (statusCode is HttpStatusCode.BadRequest)
            throw new MailProviderException(MailFailureKind.InvalidRequest, "The mail provider rejected this request.");
        if ((int)statusCode == 429)
            throw new MailProviderException(MailFailureKind.ProviderError, "The mail provider is temporarily rate limiting requests. Try again shortly.");
        throw new MailProviderException(MailFailureKind.ProviderError, $"Mail provider request failed with status {(int)statusCode}.");
    }

    private static RemediationType? RemediationFor(MailFailureKind kind) => kind is MailFailureKind.NotConnected or MailFailureKind.ReconnectRequired or MailFailureKind.PermissionDenied
        ? RemediationType.OAuthReconnect
        : null;
}
