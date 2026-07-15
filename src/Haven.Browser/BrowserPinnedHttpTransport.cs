using System.Net;
using System.Net.Sockets;
using Haven.Application;

namespace Haven.Browser;

internal static class BrowserPinnedHttpTransport
{
    public static async Task<BrowserPinnedHttpResponse> SendAsync(
        IBrowserNavigationPolicy policy,
        Uri initialAddress,
        int maximumRedirects,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(initialAddress);
        var current = initialAddress;
        for (var redirect = 0; redirect <= maximumRedirects; redirect++)
        {
            var assessment = await policy.AssessAsync(current, cancellationToken).ConfigureAwait(false);
            if (!assessment.IsAllowed)
                throw new UnauthorizedAccessException("Browser destination blocked: " + assessment.Reason);
            var addresses = assessment.ResolvedAddresses.Select(IPAddress.Parse).ToArray();
            if (addresses.Length == 0)
                throw new UnauthorizedAccessException("The approved browser destination has no pinned addresses.");

            var handler = CreatePinnedHandler(current, addresses);
            var client = new HttpClient(handler) { Timeout = timeout };
            HttpResponseMessage? response = null;
            try
            {
                response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location is { } location)
                {
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    response.Dispose();
                    client.Dispose();
                    continue;
                }
                return new BrowserPinnedHttpResponse(current, client, response);
            }
            catch
            {
                response?.Dispose();
                client.Dispose();
                throw;
            }
        }
        throw new HttpRequestException($"The browser request exceeded Haven's {maximumRedirects}-redirect limit.");
    }

    private static SocketsHttpHandler CreatePinnedHandler(Uri address, IReadOnlyList<IPAddress> addresses)
    {
        var expectedHost = address.DnsSafeHost;
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (!context.DnsEndPoint.Host.Equals(expectedHost, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("The HTTP connection attempted to change the approved host.");
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(addresses.ToArray(), context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
    }
}

internal sealed class BrowserPinnedHttpResponse(
    Uri finalAddress,
    HttpClient client,
    HttpResponseMessage response) : IAsyncDisposable
{
    public Uri FinalAddress { get; } = finalAddress;
    public HttpResponseMessage Response { get; } = response;

    public ValueTask DisposeAsync()
    {
        Response.Dispose();
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}
