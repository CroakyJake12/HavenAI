/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/LocalConversationShareService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns LocalConversationShareService, ShareRuntime. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents local conversation share service and keeps its related state and behavior together.
/// </summary>
public sealed class LocalConversationShareService(
    IConversationProductionRepository conversations) : ILocalConversationShareService
{
    /// <summary>
    /// Stores active locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, ShareRuntime> _active = new();

    /// <summary>
    /// Performs start async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<LocalShareHandle> StartAsync(Guid conversationId, TimeSpan duration, CancellationToken cancellationToken)
    {
        if (conversationId == Guid.Empty) throw new ArgumentException("Conversation identifier is required.", nameof(conversationId));
        if (duration < TimeSpan.FromMinutes(1) || duration > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(duration), "LAN shares may last from one minute to 24 hours.");

        await StopAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var address = FindPrivateAddress()
                      ?? throw new InvalidOperationException("No private local-network address is available. Connect to a private Wi-Fi or Ethernet network before sharing.");
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToHexString(tokenBytes).ToLowerInvariant();
        CryptographicOperations.ZeroMemory(tokenBytes);
        var tokenHash = HashToken(token);
        var listener = new TcpListener(address, 0);
        listener.Start(32);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var now = DateTimeOffset.UtcNow;
        var session = new SharedSession(
            Guid.NewGuid(), conversationId, tokenHash, address.ToString(), port,
            SharedSessionState.Active, now, now.Add(duration), null);
        await conversations.UpsertShareAsync(session, cancellationToken).ConfigureAwait(false);

        var cancellation = new CancellationTokenSource();
        var runtime = new ShareRuntime(session, token, listener, cancellation);
        if (!_active.TryAdd(conversationId, runtime))
        {
            cancellation.Dispose();
            listener.Stop();
            throw new InvalidOperationException("A share is already active for this conversation.");
        }

        runtime.ServerTask = RunServerAsync(runtime);
        runtime.ExpiryTask = ExpireAsync(runtime);
        var host = address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();
        var uri = new Uri($"http://{host}:{port}/share/{token}", UriKind.Absolute);
        return new LocalShareHandle(
            session.Id,
            uri,
            session.ExpiresAt,
            "Read-only LAN share. Anyone on this private network with the full link can view the exported conversation until it expires or you stop it.");
    }

    /// <summary>
    /// Performs stop async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task StopAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        if (!_active.TryRemove(conversationId, out var runtime))
        {
            if (await conversations.GetActiveShareAsync(conversationId, cancellationToken).ConfigureAwait(false) is { } stored)
                await conversations.StopShareAsync(stored.Id, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            return;
        }

        runtime.Cancellation.Cancel();
        runtime.Listener.Stop();
        try
        {
            if (runtime.ServerTask is not null) await runtime.ServerTask.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or SocketException) { }
        await conversations.StopShareAsync(runtime.Session.Id, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        runtime.Dispose();
    }

    /// <summary>
    /// Retrieves active async for the current operation.
    /// </summary>
    public Task<SharedSession?> GetActiveAsync(Guid conversationId, CancellationToken cancellationToken) =>
        conversations.GetActiveShareAsync(conversationId, cancellationToken);

    /// <summary>
    /// Performs dispose async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var conversationId in _active.Keys.ToArray())
        {
            try { await StopAsync(conversationId, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException) { }
        }
    }

    /// <summary>
    /// Runs run server async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task RunServerAsync(ShareRuntime runtime)
    {
        try
        {
            while (!runtime.Cancellation.IsCancellationRequested && DateTimeOffset.UtcNow < runtime.Session.ExpiresAt)
            {
                TcpClient client;
                try
                {
                    client = await runtime.Listener.AcceptTcpClientAsync(runtime.Cancellation.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (runtime.Cancellation.IsCancellationRequested && ex is OperationCanceledException or SocketException)
                {
                    break;
                }
                _ = HandleClientAsync(runtime, client);
            }
        }
        finally
        {
            runtime.Listener.Stop();
        }
    }

    /// <summary>
    /// Performs handle client async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task HandleClientAsync(ShareRuntime runtime, TcpClient client)
    {
        using (client)
        {
            client.ReceiveTimeout = 5_000;
            client.SendTimeout = 10_000;
            if (client.Client.RemoteEndPoint is not IPEndPoint remote || !IsPrivateOrLoopback(remote.Address))
            {
                await WriteResponseAsync(client, 403, "Forbidden", "This share is available only from the local private network.", "text/plain; charset=utf-8", runtime.Cancellation.Token).ConfigureAwait(false);
                return;
            }

            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4_096, leaveOpen: true);
                var requestLine = await ReadBoundedLineAsync(reader, 4_096, runtime.Cancellation.Token).ConfigureAwait(false);
                if (requestLine is null)
                    return;
                var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                var headerBytes = requestLine.Length;
                for (var count = 0; count < 100; count++)
                {
                    var line = await ReadBoundedLineAsync(reader, 8_192, runtime.Cancellation.Token).ConfigureAwait(false);
                    if (line is null || line.Length == 0) break;
                    headerBytes += line.Length;
                    if (headerBytes > 16_384) throw new InvalidDataException("Request headers are too large.");
                }

                if (parts.Length < 2 || !parts[0].Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(stream, 405, "Method Not Allowed", "Read-only GET requests are supported.", "text/plain; charset=utf-8", runtime.Cancellation.Token).ConfigureAwait(false);
                    return;
                }

                var expectedPath = "/share/" + runtime.Token;
                if (!FixedTimeEquals(parts[1].Split('?', 2)[0], expectedPath) || DateTimeOffset.UtcNow >= runtime.Session.ExpiresAt)
                {
                    await WriteResponseAsync(stream, 404, "Not Found", "The share link is invalid or has expired.", "text/plain; charset=utf-8", runtime.Cancellation.Token).ConfigureAwait(false);
                    return;
                }

                var document = await conversations.BuildExportAsync(runtime.Session.ConversationId, runtime.Cancellation.Token).ConfigureAwait(false);
                var html = BuildHtml(document, runtime.Session.ExpiresAt);
                await WriteResponseAsync(stream, 200, "OK", html, "text/html; charset=utf-8", runtime.Cancellation.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
            {
                // A client can disconnect at any point. The share remains available until its explicit expiry.
            }
        }
    }

    /// <summary>
    /// Performs expire async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ExpireAsync(ShareRuntime runtime)
    {
        var delay = runtime.Session.ExpiresAt - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero)
        {
            try { await Task.Delay(delay, runtime.Cancellation.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
        if (_active.TryRemove(runtime.Session.ConversationId, out _))
        {
            runtime.Cancellation.Cancel();
            runtime.Listener.Stop();
            try { await conversations.StopShareAsync(runtime.Session.Id, DateTimeOffset.UtcNow, CancellationToken.None).ConfigureAwait(false); }
            finally { runtime.Dispose(); }
        }
    }

    /// <summary>
    /// Builds html from the currently available inputs.
    /// </summary>
    private static string BuildHtml(ConversationExportDocument document, DateTimeOffset expiresAt)
    {
        var encode = HtmlEncoder.Default;
        var builder = new StringBuilder(16_384);
        builder.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>").Append(encode.Encode(document.Conversation.Title)).Append(" · Haven</title>")
            .Append("<style>body{margin:0;background:#11151b;color:#f4f6f8;font:16px/1.55 system-ui,sans-serif}main{max-width:900px;margin:auto;padding:32px 20px 80px}.notice{color:#aab4c0;font-size:13px}.message{margin:24px 0;padding:18px;border:1px solid #2e3742;border-radius:16px;background:#171d25}.role{font-weight:700;color:#7dcfff}.content{white-space:pre-wrap;overflow-wrap:anywhere}.meta{color:#8995a3;font-size:12px}a{color:#8ed0ff}</style>")
            .Append("</head><body><main><h1>").Append(encode.Encode(document.Conversation.Title)).Append("</h1><p class=\"notice\">Read-only Haven LAN share · expires ")
            .Append(encode.Encode(expiresAt.LocalDateTime.ToString("g"))).Append("</p>");
        foreach (var message in document.Messages.OrderBy(item => item.CreatedAt))
        {
            var name = message.Role == MessageRole.User ? "You" : message.AgentName ?? "Haven";
            builder.Append("<article class=\"message\"><div class=\"role\">").Append(encode.Encode(name)).Append("</div>")
                .Append("<div class=\"meta\">").Append(encode.Encode(message.CreatedAt.LocalDateTime.ToString("g")));
            if (!string.IsNullOrWhiteSpace(message.ModelName)) builder.Append(" · ").Append(encode.Encode(message.ModelName));
            builder.Append("</div><p class=\"content\">").Append(encode.Encode(message.Content)).Append("</p></article>");
        }
        return builder.Append("</main></body></html>").ToString();
    }

    /// <summary>
    /// Performs the find private address step owned by this component.
    /// </summary>
    private static IPAddress? FindPrivateAddress() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(item => item.OperationalStatus == OperationalStatus.Up && item.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
        .SelectMany(item => item.GetIPProperties().UnicastAddresses)
        .Select(item => item.Address)
        .Where(address => address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(address))
        .OrderBy(address => address.ToString(), StringComparer.Ordinal)
        .FirstOrDefault();

    /// <summary>
    /// Reports whether is private or loopback is true for the current state.
    /// </summary>
    private static bool IsPrivateOrLoopback(IPAddress address) => IPAddress.IsLoopback(address) || IsPrivate(address) || address.IsIPv6LinkLocal || IsUniqueLocalIpv6(address);

    /// <summary>
    /// Reports whether is private is true for the current state.
    /// </summary>
    private static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               bytes[0] == 192 && bytes[1] == 168 ||
               bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
               bytes[0] == 169 && bytes[1] == 254;
    }

    /// <summary>
    /// Reports whether is unique local ipv6 is true for the current state.
    /// </summary>
    private static bool IsUniqueLocalIpv6(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;
        var bytes = address.GetAddressBytes();
        return (bytes[0] & 0xFE) == 0xFC;
    }

    /// <summary>
    /// Reports whether hash token is true for the current state.
    /// </summary>
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>
    /// Performs the fixed time equals step owned by this component.
    /// </summary>
    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        try { return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes); }
        finally { CryptographicOperations.ZeroMemory(leftBytes); CryptographicOperations.ZeroMemory(rightBytes); }
    }

    /// <summary>
    /// Performs read bounded line async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<string?> ReadBoundedLineAsync(StreamReader reader, int maximumLength, CancellationToken cancellationToken)
    {
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is { Length: > 0 } && line.Length > maximumLength) throw new InvalidDataException("Request line is too long.");
        return line;
    }

    /// <summary>
    /// Performs write response async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static Task WriteResponseAsync(TcpClient client, int status, string reason, string content, string contentType, CancellationToken cancellationToken) =>
        WriteResponseAsync(client.GetStream(), status, reason, content, contentType, cancellationToken);

    /// <summary>
    /// Performs write response async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task WriteResponseAsync(Stream stream, int status, string reason, string content, string contentType, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(content);
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nPragma: no-cache\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; frame-ancestors 'none'\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Represents share runtime and keeps its related state and behavior together.
    /// </summary>
    private sealed class ShareRuntime(SharedSession session, string token, TcpListener listener, CancellationTokenSource cancellation) : IDisposable
    {
        /// <summary>
        /// Gets or updates session, the bindable or domain state represented by this property.
        /// </summary>
        public SharedSession Session { get; } = session;
        /// <summary>
        /// Gets or updates token, the bindable or domain state represented by this property.
        /// </summary>
        public string Token { get; } = token;
        /// <summary>
        /// Gets or updates listener, the bindable or domain state represented by this property.
        /// </summary>
        public TcpListener Listener { get; } = listener;
        /// <summary>
        /// Reports whether cancellation is true for the current state.
        /// </summary>
        public CancellationTokenSource Cancellation { get; } = cancellation;
        /// <summary>
        /// Gets or updates server task, the bindable or domain state represented by this property.
        /// </summary>
        public Task? ServerTask { get; set; }
        /// <summary>
        /// Gets or updates expiry task, the bindable or domain state represented by this property.
        /// </summary>
        public Task? ExpiryTask { get; set; }
        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose()
        {
            Listener.Stop();
            Cancellation.Dispose();
        }
    }
}
