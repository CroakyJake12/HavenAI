/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Browser/BrowserPinnedHttpTransport.cs, in the Browser layer, which isolates browser state, safety policy, transport, and automation.
 * What: This file owns BrowserPinnedHttpTransport, BrowserPinnedHttpResponse. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Browser capabilities are isolated behind explicit policy boundaries because navigation and automation process untrusted external content.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Net;
using System.Net.Sockets;
using Haven.Application;

namespace Haven.Browser;

/// <summary>
/// Represents browser pinned http transport and keeps its related state and behavior together.
/// </summary>
internal static class BrowserPinnedHttpTransport
{
    /// <summary>
    /// Performs send asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Creates pinned handler with the invariants required by its callers.
    /// </summary>
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

/// <summary>
/// Represents browser pinned http response and keeps its related state and behavior together.
/// </summary>
internal sealed class BrowserPinnedHttpResponse(
    Uri finalAddress,
    HttpClient client,
    HttpResponseMessage response) : IAsyncDisposable
{
    /// <summary>
    /// Gets or updates final address, the bindable or domain state represented by this property.
    /// </summary>
    public Uri FinalAddress { get; } = finalAddress;
    /// <summary>
    /// Gets or updates response, the bindable or domain state represented by this property.
    /// </summary>
    public HttpResponseMessage Response { get; } = response;

    /// <summary>
    /// Performs dispose asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Response.Dispose();
        client.Dispose();
        return ValueTask.CompletedTask;
    }
}
