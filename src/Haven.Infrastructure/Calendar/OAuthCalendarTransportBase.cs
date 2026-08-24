/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/OAuthCalendarTransportBase.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns OAuthCalendarTransportBase, CalendarHttpException. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents o auth calendar transport base and keeps its related state and behavior together.
/// </summary>
public abstract class OAuthCalendarTransportBase(
    CalendarProviderConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IPlannerRepository repository,
    ICalendarSyncStore store,
    ICalendarTokenStore tokenStore,
    IOAuthBrowserLauncher browserLauncher) : ICalendarProviderTransport
{
    /// <summary>
    /// Gets or updates client, the bindable or domain state represented by this property.
    /// </summary>
    protected HttpClient Client => httpClientFactory.CreateClient("HavenCalendarSync");
    /// <summary>
    /// Gets or updates repository, the bindable or domain state represented by this property.
    /// </summary>
    protected IPlannerRepository Repository => repository;
    /// <summary>
    /// Gets or updates store, the bindable or domain state represented by this property.
    /// </summary>
    protected ICalendarSyncStore Store => store;
    /// <summary>
    /// Gets or updates configuration, the bindable or domain state represented by this property.
    /// </summary>
    protected CalendarProviderConfiguration Configuration => configuration;
    /// <summary>
    /// Gets or updates kind, the bindable or domain state represented by this property.
    /// </summary>
    public CalendarProviderKind Kind => configuration.Provider;

    /// <summary>
    /// Gets or updates authorization endpoint, the bindable or domain state represented by this property.
    /// </summary>
    protected abstract Uri AuthorizationEndpoint { get; }
    /// <summary>
    /// Gets or updates token endpoint, the bindable or domain state represented by this property.
    /// </summary>
    protected abstract Uri TokenEndpoint { get; }
    protected abstract Task<(string Identifier, string DisplayName)> GetIdentityAsync(string accessToken, CancellationToken cancellationToken);
    /// <summary>
    /// Performs sync core asynchronously so I/O does not block the caller's thread.
    /// </summary>
    protected abstract Task<CalendarSyncResult> SyncCoreAsync(CalendarAccount account, CalendarTokenEnvelope token, CalendarSyncRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Performs connect asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<CalendarAuthorizationResult> ConnectAsync(CalendarProviderConfiguration suppliedConfiguration, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(suppliedConfiguration, configuration) && suppliedConfiguration.Provider != Kind)
            throw new ArgumentException("The OAuth configuration does not match this provider.", nameof(suppliedConfiguration));
        if (!configuration.IsConfigured) return new(false, CalendarSyncStatus.NotConfigured, $"{Kind} Calendar is not configured.");

        try
        {
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var state = Base64Url(RandomNumberGenerator.GetBytes(32));
            var authorization = BuildAuthorizationUri(state, challenge);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var query = await ReceiveAuthorizationRedirectAsync(configuration.RedirectUri, authorization, browserLauncher, timeout.Token).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(query.GetValueOrDefault("state") ?? string.Empty)))
                throw new InvalidOperationException("Calendar sign-in returned an invalid state value.");
            if (!string.IsNullOrWhiteSpace(query.GetValueOrDefault("error")))
                throw new InvalidOperationException("Calendar sign-in was declined.");
            var code = query.GetValueOrDefault("code") ?? throw new InvalidOperationException("Calendar sign-in did not return an authorization code.");
            var token = await ExchangeCodeAsync(code, verifier, cancellationToken).ConfigureAwait(false);
            var identity = await GetIdentityAsync(token.AccessToken, cancellationToken).ConfigureAwait(false);
            var accounts = await repository.GetCalendarAccountsAsync(cancellationToken).ConfigureAwait(false);
            var existing = accounts.FirstOrDefault(account => account.Provider == Kind && account.AccountIdentifier.Equals(identity.Identifier, StringComparison.OrdinalIgnoreCase));
            var now = DateTimeOffset.UtcNow;
            var account = existing is null
                ? new CalendarAccount(Guid.NewGuid(), Kind, identity.DisplayName, identity.Identifier, CalendarSyncStatus.Ready, "Connected", null, now, now)
                : existing with { DisplayName = identity.DisplayName, Status = CalendarSyncStatus.Ready, StatusMessage = "Connected", UpdatedAt = now };
            await repository.UpsertCalendarAccountAsync(account, cancellationToken).ConfigureAwait(false);
            await tokenStore.SaveAsync(account.Id, token, cancellationToken).ConfigureAwait(false);
            return new(true, CalendarSyncStatus.Ready, $"Connected {identity.DisplayName} to {Kind} Calendar.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, CalendarSyncStatus.Error, "Calendar sign-in timed out.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or SocketException or IOException)
        {
            return new(false, CalendarSyncStatus.Error, $"{Kind} Calendar sign-in failed.");
        }
    }

    /// <summary>
    /// Performs sync asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<CalendarSyncResult> SyncAsync(CalendarSyncRequest request, CancellationToken cancellationToken)
    {
        var account = (await repository.GetCalendarAccountsAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == request.AccountId && item.Provider == Kind);
        if (account is null) return new(false, CalendarSyncStatus.Disconnected, 0, 0, 0, 0, $"No connected {Kind} Calendar account was found.");
        var unresolved = (await repository.GetUnresolvedConflictsAsync(cancellationToken).ConfigureAwait(false))
            .Count(conflict => conflict.AccountId == account.Id);
        if (unresolved > 0)
        {
            var message = $"Resolve {unresolved} {Kind} Calendar conflict{(unresolved == 1 ? string.Empty : "s")} before synchronising again.";
            await repository.UpsertCalendarAccountAsync(account with
            {
                Status = CalendarSyncStatus.Ready,
                StatusMessage = message,
                UpdatedAt = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);
            return new(false, CalendarSyncStatus.Ready, 0, 0, 0, unresolved, message);
        }
        var token = await tokenStore.GetAsync(account.Id, cancellationToken).ConfigureAwait(false);
        if (token is null) return new(false, CalendarSyncStatus.Disconnected, 0, 0, 0, 0, $"{Kind} Calendar needs to be connected again.");

        var now = DateTimeOffset.UtcNow;
        await repository.UpsertCalendarAccountAsync(account with { Status = CalendarSyncStatus.Syncing, StatusMessage = "Synchronising…", UpdatedAt = now }, cancellationToken).ConfigureAwait(false);
        try
        {
            token = await RefreshIfNeededAsync(account.Id, token, cancellationToken).ConfigureAwait(false);
            var result = await SyncCoreAsync(account, token, request, cancellationToken).ConfigureAwait(false);
            var completedAt = DateTimeOffset.UtcNow;
            await repository.UpsertCalendarAccountAsync(account with
            {
                Status = result.Status,
                StatusMessage = result.Message,
                LastSyncedAt = result.Succeeded ? completedAt : account.LastSyncedAt,
                UpdatedAt = completedAt
            }, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            var result = new CalendarSyncResult(false, CalendarSyncStatus.Offline, 0, 0, 0, 0, $"{Kind} Calendar is offline.");
            await repository.UpsertCalendarAccountAsync(account with { Status = result.Status, StatusMessage = result.Message, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            var result = new CalendarSyncResult(false, CalendarSyncStatus.Error, 0, 0, 0, 0, $"{Kind} Calendar sync failed.");
            await repository.UpsertCalendarAccountAsync(account with { Status = result.Status, StatusMessage = result.Message, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
            return result;
        }
    }

    /// <summary>
    /// Performs disconnect asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task DisconnectAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await tokenStore.DeleteAsync(accountId, cancellationToken).ConfigureAwait(false);
        var account = (await repository.GetCalendarAccountsAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(item => item.Id == accountId && item.Provider == Kind);
        if (account is not null)
            await repository.UpsertCalendarAccountAsync(account with { Status = CalendarSyncStatus.Disconnected, StatusMessage = "Disconnected", UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves json async for the current operation.
    /// </summary>
    protected async Task<JsonDocument> GetJsonAsync(Uri uri, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await SendJsonAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs send json asynchronously so I/O does not block the caller's thread.
    /// </summary>
    protected async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new CalendarHttpException(response.StatusCode, string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase ?? "Calendar request failed." : body);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    }

    /// <summary>
    /// Performs drain outbox asynchronously so I/O does not block the caller's thread.
    /// </summary>
    protected async Task DrainOutboxAsync(CalendarAccount account, CalendarTokenEnvelope token, Func<CalendarOutboxItem, PlannerEvent, string, CancellationToken, Task> apply,
        CancellationToken cancellationToken)
    {
        var items = await store.GetDueOutboxAsync(account.Id, DateTimeOffset.UtcNow, 100, cancellationToken).ConfigureAwait(false);
        foreach (var item in items)
        {
            try
            {
                if (item.EventId is null) { await store.CompleteOutboxAsync(item.Id, cancellationToken).ConfigureAwait(false); continue; }
                var plannerEvent = JsonSerializer.Deserialize<PlannerEvent>(item.PayloadJson)
                                   ?? throw new JsonException("Calendar outbox event is empty.");
                await apply(item, plannerEvent, token.AccessToken, cancellationToken).ConfigureAwait(false);
                await store.CompleteOutboxAsync(item.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
            {
                var delayMinutes = Math.Min(60, Math.Pow(2, Math.Min(item.AttemptCount, 5)));
                await store.FailOutboxAsync(item.Id, ex.Message, DateTimeOffset.UtcNow.AddMinutes(delayMinutes), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Builds authorization uri from the currently available inputs.
    /// </summary>
    private Uri BuildAuthorizationUri(string state, string challenge)
    {
        var values = new Dictionary<string, string?>
        {
            ["client_id"] = configuration.ClientId,
            ["redirect_uri"] = configuration.RedirectUri.AbsoluteUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', configuration.Scopes),
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["access_type"] = Kind == CalendarProviderKind.Google ? "offline" : null,
            ["prompt"] = Kind == CalendarProviderKind.Google ? "consent" : "select_account"
        };
        return WithQuery(AuthorizationEndpoint, values);
    }

    /// <summary>
    /// Performs exchange code asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<CalendarTokenEnvelope> ExchangeCodeAsync(string code, string verifier, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            ["client_id"] = configuration.ClientId!, ["grant_type"] = "authorization_code", ["code"] = code,
            ["redirect_uri"] = configuration.RedirectUri.AbsoluteUri, ["code_verifier"] = verifier
        };
        return await RequestTokenAsync(values, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs refresh if needed asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<CalendarTokenEnvelope> RefreshIfNeededAsync(Guid accountId, CalendarTokenEnvelope token, CancellationToken cancellationToken)
    {
        if (token.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return token;
        if (string.IsNullOrWhiteSpace(token.RefreshToken)) throw new InvalidOperationException("The calendar session expired and must be connected again.");
        var values = new Dictionary<string, string>
        {
            ["client_id"] = configuration.ClientId!, ["grant_type"] = "refresh_token", ["refresh_token"] = token.RefreshToken
        };
        var refreshed = await RequestTokenAsync(values, token.RefreshToken, cancellationToken).ConfigureAwait(false);
        await tokenStore.SaveAsync(accountId, refreshed, cancellationToken).ConfigureAwait(false);
        return refreshed;
    }

    /// <summary>
    /// Performs request token asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<CalendarTokenEnvelope> RequestTokenAsync(Dictionary<string, string> values, string? existingRefreshToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = new FormUrlEncodedContent(values) };
        using var document = await SendJsonAsync(request, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var accessToken = root.GetProperty("access_token").GetString() ?? throw new JsonException("Token response omitted access_token.");
        var refreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : existingRefreshToken;
        var expiresIn = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) ? seconds : 3600;
        var scope = root.TryGetProperty("scope", out var scopeElement) ? scopeElement.GetString() ?? string.Join(' ', configuration.Scopes) : string.Join(' ', configuration.Scopes);
        return new(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn)), scope);
    }

    private static async Task<IReadOnlyDictionary<string, string?>> ReceiveAuthorizationRedirectAsync(
        Uri redirectUri,
        Uri authorizationUri,
        IOAuthBrowserLauncher browserLauncher,
        CancellationToken cancellationToken)
    {
        if (!redirectUri.IsLoopback)
            throw new InvalidOperationException("OAuth redirect URI must use a loopback host.");

        var address = redirectUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            ? IPAddress.Loopback
            : IPAddress.TryParse(redirectUri.Host, out var parsed) && IPAddress.IsLoopback(parsed)
                ? parsed
                : throw new InvalidOperationException("OAuth redirect URI must use localhost or a loopback address.");

        using var listener = new TcpListener(address, redirectUri.Port);
        listener.Server.ExclusiveAddressUse = true;
        listener.Start(1);
        await browserLauncher.LaunchAsync(authorizationUri, cancellationToken).ConfigureAwait(false);

        using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                          ?? throw new InvalidOperationException("OAuth redirect returned an empty HTTP request.");
        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("GET", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("OAuth redirect returned an unsupported HTTP request.");

        var requestUri = new Uri(new Uri($"http://{redirectUri.Host}:{redirectUri.Port}"), parts[1]);
        if (!requestUri.AbsolutePath.Equals(redirectUri.AbsolutePath, StringComparison.Ordinal))
            throw new InvalidOperationException("OAuth redirect returned to an unexpected path.");

        string? header;
        do
        {
            header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        } while (!string.IsNullOrEmpty(header));

        var query = ParseQuery(requestUri.Query);
        await WriteLoopbackResponseAsync(stream, query.GetValueOrDefault("error") is null, cancellationToken).ConfigureAwait(false);
        return query;
    }

    private static Dictionary<string, string?> ParseQuery(string query)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = WebUtility.UrlDecode(pair[0]);
            if (string.IsNullOrWhiteSpace(key)) continue;
            result[key] = pair.Length == 2 ? WebUtility.UrlDecode(pair[1]) : string.Empty;
        }
        return result;
    }

    private static async Task WriteLoopbackResponseAsync(Stream stream, bool success, CancellationToken cancellationToken)
    {
        var html = success
            ? "<!doctype html><title>Haven</title><meta name='viewport' content='width=device-width'><style>body{font:16px system-ui;background:#0b111a;color:#eef7ff;padding:3rem}</style><h1>Connected to Haven</h1><p>You can return to the app.</p>"
            : "<!doctype html><title>Haven</title><meta name='viewport' content='width=device-width'><style>body{font:16px system-ui;background:#0b111a;color:#eef7ff;padding:3rem}</style><h1>Sign-in was not completed</h1><p>You can return to Haven and try again.</p>";
        var body = Encoding.UTF8.GetBytes(html);
        var crlf = ((char)13).ToString() + (char)10;
        var headers = Encoding.ASCII.GetBytes(string.Join(crlf,
        [
            "HTTP/1.1 200 OK",
            "Content-Type: text/html; charset=utf-8",
            $"Content-Length: {body.Length}",
            "Connection: close",
            "Cache-Control: no-store",
            string.Empty,
            string.Empty
        ]));
        await stream.WriteAsync(headers, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the with query step owned by this component.
    /// </summary>
    protected static Uri WithQuery(Uri uri, IReadOnlyDictionary<string, string?> values)
    {
        var query = string.Join('&', values.Where(pair => pair.Value is not null).Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
        return new UriBuilder(uri) { Query = query }.Uri;
    }

    /// <summary>
    /// Performs the base64 url step owned by this component.
    /// </summary>
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Represents calendar http exception and keeps its related state and behavior together.
/// </summary>
public sealed class CalendarHttpException(HttpStatusCode statusCode, string message) : HttpRequestException(message, null, statusCode)
{
    /// <summary>
    /// Gets or updates calendar status code, the bindable or domain state represented by this property.
    /// </summary>
    public HttpStatusCode CalendarStatusCode => statusCode;
}
