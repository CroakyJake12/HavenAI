using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Private-LAN Mesh transport. TLS encrypts every session; trusted sessions additionally pin
/// the remote device public-key fingerprint. Pairing is one-time and fingerprint-bound.
/// </summary>
public sealed class SecureLanMeshTransport(IMeshCapabilitySource capabilities) : IMeshTransport
{
    private const int MaximumFrameBytes = 1024 * 1024;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private readonly ConcurrentDictionary<Guid, PeerConnection> _connections = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _trustGate = new();
    private IReadOnlyDictionary<Guid, MeshPeerRecord> _trustedPeers = new Dictionary<Guid, MeshPeerRecord>();
    private MeshPairingChallenge? _pairingChallenge;
    private MeshLocalIdentity? _identity;
    private X509Certificate2? _certificate;
    private TcpListener? _listener;
    private CancellationTokenSource? _lifetime;
    private Task? _acceptLoop;
    private Task? _heartbeatLoop;

    public event Action<MeshTransportPeer>? PeerObserved;
    public event Action<MeshTransportPeer>? PairingCompleted;
    public event Action<Guid, MeshConnectionState>? ConnectionChanged;
    public event Action<MeshTransportMessage>? MessageReceived;

    public bool IsRunning => _listener is not null && _lifetime is { IsCancellationRequested: false };
    public string? LocalEndpoint { get; private set; }
    public string? LastInboundFailure { get; private set; }

    public async Task StartAsync(MeshLocalIdentity localIdentity, string privateKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(localIdentity);
        if (string.IsNullOrWhiteSpace(privateKey)) throw new ArgumentException("Mesh private identity key is required.", nameof(privateKey));
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning) return;
            _identity = localIdentity;
            _certificate = CreateIdentityCertificate(localIdentity, privateKey);
            _listener = new TcpListener(IPAddress.Any, 0);
            _listener.Start(backlog: 32);
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            var address = SelectAdvertisableAddress();
            LocalEndpoint = $"{address}:{port}";
            _lifetime = new CancellationTokenSource();
            _acceptLoop = AcceptLoopAsync(_lifetime.Token);
            _heartbeatLoop = HeartbeatLoopAsync(_lifetime.Token);
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lifetime = _lifetime;
            _lifetime = null;
            try { lifetime?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _listener = null;
            LocalEndpoint = null;
            foreach (var pair in _connections.ToArray())
            {
                if (_connections.TryRemove(pair.Key, out var connection)) await connection.DisposeAsync().ConfigureAwait(false);
            }
            if (_acceptLoop is not null) { try { await _acceptLoop.ConfigureAwait(false); } catch (OperationCanceledException) { } catch (ObjectDisposedException) { } }
            if (_heartbeatLoop is not null) { try { await _heartbeatLoop.ConfigureAwait(false); } catch (OperationCanceledException) { } }
            _acceptLoop = null;
            _heartbeatLoop = null;
            lifetime?.Dispose();
            _certificate?.Dispose();
            _certificate = null;
        }
        finally { _lifecycle.Release(); }
    }

    public Task SetPairingChallengeAsync(MeshPairingChallenge? challenge, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_trustGate) _pairingChallenge = challenge;
        return Task.CompletedTask;
    }

    public Task SetTrustedPeersAsync(IReadOnlyList<MeshPeerRecord> peers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(peers);
        lock (_trustGate)
            _trustedPeers = peers.Where(peer => peer.TrustState == MeshPeerTrustState.Trusted).ToDictionary(peer => peer.DeviceId);
        foreach (var connection in _connections.ToArray())
        {
            MeshPeerRecord? peer;
            lock (_trustGate) _trustedPeers.TryGetValue(connection.Key, out peer);
            if (peer is null || !string.Equals(peer.PublicKeyFingerprint, connection.Value.RemoteFingerprint, StringComparison.OrdinalIgnoreCase))
                _ = DisconnectAsync(connection.Key, CancellationToken.None);
        }
        return Task.CompletedTask;
    }

    public async Task<MeshTransportPeer> PairAsync(string endpoint, Guid challengeId, string verificationCode, string expectedRemoteFingerprint, CancellationToken cancellationToken)
    {
        EnsureRunning();
        if (challengeId == Guid.Empty) throw new ArgumentException("Pairing challenge ID is required.", nameof(challengeId));
        if (string.IsNullOrWhiteSpace(verificationCode)) throw new ArgumentException("Pairing verification code is required.", nameof(verificationCode));
        ValidateFingerprint(expectedRemoteFingerprint);
        var socket = await ConnectSocketAsync(endpoint, cancellationToken).ConfigureAwait(false);
        SslStream? ssl = null;
        try
        {
            ssl = new SslStream(socket.GetStream(), leaveInnerStreamOpen: false, (_, certificate, _, _) =>
                certificate is not null && Fingerprint(certificate).Equals(expectedRemoteFingerprint, StringComparison.OrdinalIgnoreCase));
            await ssl.AuthenticateAsClientAsync(ClientOptions(), cancellationToken).ConfigureAwait(false);
            var localHello = await CreateHelloAsync(cancellationToken).ConfigureAwait(false);
            await WriteFrameAsync(ssl, new WireEnvelope("pair.request", JsonSerializer.Serialize(new PairRequest(challengeId, verificationCode.Trim(), localHello))), cancellationToken).ConfigureAwait(false);
            var response = await ReadFrameAsync(ssl, cancellationToken).ConfigureAwait(false);
            if (response.Kind != "pair.response") throw new InvalidDataException("The remote device did not return a Mesh pairing response.");
            var hello = JsonSerializer.Deserialize<PeerHello>(response.Payload) ?? throw new InvalidDataException("The remote pairing response was empty.");
            var remoteFingerprint = Fingerprint(ssl.RemoteCertificate ?? throw new AuthenticationException("The remote device did not present a certificate."));
            var peer = ValidateHello(hello, endpoint, remoteFingerprint, expectedRemoteFingerprint);
            var connection = new PeerConnection(peer.DeviceId, peer.PublicKeyFingerprint, endpoint, socket, ssl);
            socket = null!; ssl = null;
            await ReplaceConnectionAsync(connection).ConfigureAwait(false);
            _ = ReadLoopAsync(connection, _lifetime!.Token);
            PeerObserved?.Invoke(peer);
            ConnectionChanged?.Invoke(peer.DeviceId, MeshConnectionState.Connected);
            return peer;
        }
        catch
        {
            ssl?.Dispose();
            socket?.Dispose();
            throw;
        }
    }

    public async Task ConnectAsync(MeshPeerRecord peer, CancellationToken cancellationToken)
    {
        EnsureRunning();
        ArgumentNullException.ThrowIfNull(peer);
        if (peer.TrustState != MeshPeerTrustState.Trusted) throw new UnauthorizedAccessException("Mesh transport connects only to trusted peers.");
        if (string.IsNullOrWhiteSpace(peer.LastKnownEndpoint)) throw new InvalidOperationException("The trusted peer has no remembered endpoint.");
        ValidateFingerprint(peer.PublicKeyFingerprint);
        ConnectionChanged?.Invoke(peer.DeviceId, MeshConnectionState.Connecting);
        var socket = await ConnectSocketAsync(peer.LastKnownEndpoint, cancellationToken).ConfigureAwait(false);
        SslStream? ssl = null;
        try
        {
            ssl = new SslStream(socket.GetStream(), false, (_, certificate, _, _) =>
                certificate is not null && Fingerprint(certificate).Equals(peer.PublicKeyFingerprint, StringComparison.OrdinalIgnoreCase));
            await ssl.AuthenticateAsClientAsync(ClientOptions(), cancellationToken).ConfigureAwait(false);
            await WriteFrameAsync(ssl, new WireEnvelope("hello", JsonSerializer.Serialize(await CreateHelloAsync(cancellationToken).ConfigureAwait(false))), cancellationToken).ConfigureAwait(false);
            var response = await ReadFrameAsync(ssl, cancellationToken).ConfigureAwait(false);
            if (response.Kind != "hello.ack") throw new InvalidDataException("The trusted peer did not complete the Mesh identity handshake.");
            var hello = JsonSerializer.Deserialize<PeerHello>(response.Payload) ?? throw new InvalidDataException("The trusted peer returned an empty identity handshake.");
            var remoteFingerprint = Fingerprint(ssl.RemoteCertificate ?? throw new AuthenticationException("The remote device did not present a certificate."));
            var observed = ValidateHello(hello, peer.LastKnownEndpoint, remoteFingerprint, peer.PublicKeyFingerprint);
            if (observed.DeviceId != peer.DeviceId) throw new AuthenticationException("The endpoint presented a different Mesh device ID than the trusted peer.");
            var connection = new PeerConnection(peer.DeviceId, peer.PublicKeyFingerprint, peer.LastKnownEndpoint, socket, ssl);
            socket = null!; ssl = null;
            await ReplaceConnectionAsync(connection).ConfigureAwait(false);
            _ = ReadLoopAsync(connection, _lifetime!.Token);
            PeerObserved?.Invoke(observed);
            ConnectionChanged?.Invoke(peer.DeviceId, MeshConnectionState.Connected);
        }
        catch
        {
            ssl?.Dispose(); socket?.Dispose();
            ConnectionChanged?.Invoke(peer.DeviceId, MeshConnectionState.Failed);
            throw;
        }
    }

    public async Task DisconnectAsync(Guid peerDeviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_connections.TryRemove(peerDeviceId, out var connection)) await connection.DisposeAsync().ConfigureAwait(false);
        ConnectionChanged?.Invoke(peerDeviceId, MeshConnectionState.Disconnected);
    }

    public async Task SendAsync(Guid peerDeviceId, string kind, string payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("Mesh message kind is required.", nameof(kind));
        if (!_connections.TryGetValue(peerDeviceId, out var connection)) throw new IOException("The selected Mesh peer is not connected.");
        await connection.SendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var message = new WireMessage(Guid.NewGuid(), kind.Trim(), payload ?? string.Empty, DateTimeOffset.UtcNow);
            await WriteFrameAsync(connection.Stream, new WireEnvelope("message", JsonSerializer.Serialize(message)), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _ = DisconnectAsync(peerDeviceId, CancellationToken.None);
            throw;
        }
        finally { connection.SendGate.Release(); }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient socket;
            try { socket = await _listener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            _ = HandleInboundAsync(socket, cancellationToken);
        }
    }

    private async Task HandleInboundAsync(TcpClient socket, CancellationToken cancellationToken)
    {
        await using var holder = new AsyncTcpHolder(socket);
        var remote = socket.Client.RemoteEndPoint as IPEndPoint;
        if (remote is null || !IsPrivate(remote.Address)) return;
        using var ssl = new SslStream(socket.GetStream(), false, (_, certificate, _, _) => certificate is not null);
        try
        {
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _certificate ?? throw new InvalidOperationException("Mesh identity certificate is unavailable."),
                ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, cancellationToken).ConfigureAwait(false);
            var remoteCertificate = ssl.RemoteCertificate ?? throw new AuthenticationException("The connecting device did not present a Mesh identity certificate.");
            var presentedFingerprint = Fingerprint(remoteCertificate);
            var first = await ReadFrameAsync(ssl, cancellationToken).ConfigureAwait(false);
            MeshTransportPeer peer;
            if (first.Kind == "pair.request")
            {
                var request = JsonSerializer.Deserialize<PairRequest>(first.Payload) ?? throw new InvalidDataException("Pairing request was empty.");
                MeshPairingChallenge? challenge;
                lock (_trustGate)
                {
                    challenge = _pairingChallenge;
                    if (challenge is not null && challenge.Id == request.ChallengeId && challenge.ExpiresAt > DateTimeOffset.UtcNow && string.Equals(challenge.VerificationCode, request.VerificationCode, StringComparison.Ordinal))
                        _pairingChallenge = null;
                    else challenge = null;
                }
                if (challenge is null) throw new UnauthorizedAccessException("The Mesh pairing challenge was invalid, expired, or already used.");
                peer = ValidateHello(request.Hello, NormalizeAdvertisedEndpoint(request.Hello.Endpoint), presentedFingerprint, presentedFingerprint);
                await WriteFrameAsync(ssl, new WireEnvelope("pair.response", JsonSerializer.Serialize(await CreateHelloAsync(cancellationToken).ConfigureAwait(false))), cancellationToken).ConfigureAwait(false);
            }
            else if (first.Kind == "hello")
            {
                var hello = JsonSerializer.Deserialize<PeerHello>(first.Payload) ?? throw new InvalidDataException("Trusted Mesh hello was empty.");
                MeshPeerRecord? trusted;
                lock (_trustGate) _trustedPeers.TryGetValue(hello.DeviceId, out trusted);
                if (trusted is null || !trusted.PublicKeyFingerprint.Equals(presentedFingerprint, StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("The connecting Mesh identity is not trusted.");
                peer = ValidateHello(hello, NormalizeAdvertisedEndpoint(hello.Endpoint), presentedFingerprint, trusted.PublicKeyFingerprint);
                await WriteFrameAsync(ssl, new WireEnvelope("hello.ack", JsonSerializer.Serialize(await CreateHelloAsync(cancellationToken).ConfigureAwait(false))), cancellationToken).ConfigureAwait(false);
            }
            else return;

            var connection = new PeerConnection(peer.DeviceId, peer.PublicKeyFingerprint, peer.Endpoint, socket, ssl);
            holder.Detach();
            await ReplaceConnectionAsync(connection).ConfigureAwait(false);
            if (first.Kind == "pair.request") PairingCompleted?.Invoke(peer);
            PeerObserved?.Invoke(peer);
            ConnectionChanged?.Invoke(peer.DeviceId, MeshConnectionState.Connected);
            await ReadLoopAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            LastInboundFailure = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private async Task ReadLoopAsync(PeerConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var envelope = await ReadFrameAsync(connection.Stream, cancellationToken).ConfigureAwait(false);
                if (envelope.Kind != "message") continue;
                var message = JsonSerializer.Deserialize<WireMessage>(envelope.Payload);
                if (message is null) continue;
                MessageReceived?.Invoke(new MeshTransportMessage(message.MessageId, connection.DeviceId, message.Kind, message.Payload, DateTimeOffset.UtcNow));
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
        finally
        {
            if (_connections.TryGetValue(connection.DeviceId, out var current) && ReferenceEquals(current, connection))
            {
                _connections.TryRemove(connection.DeviceId, out _);
                await connection.DisposeAsync().ConfigureAwait(false);
                ConnectionChanged?.Invoke(connection.DeviceId, MeshConnectionState.Disconnected);
            }
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await Task.Delay(HeartbeatInterval, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            var identity = _identity;
            if (identity is null) continue;
            IReadOnlyList<MeshCapabilityDescriptor> advertised;
            try { advertised = await capabilities.GetLocalCapabilitiesAsync(cancellationToken).ConfigureAwait(false); }
            catch { continue; }
            var snapshot = new MeshPresenceSnapshot(identity.DeviceId, MeshPresenceState.Available, MeshConnectionState.Connected, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, advertised);
            foreach (var peerId in _connections.Keys)
            {
                try { await SendAsync(peerId, "presence", JsonSerializer.Serialize(snapshot), cancellationToken).ConfigureAwait(false); }
                catch { }
            }
        }
    }

    private async Task<PeerHello> CreateHelloAsync(CancellationToken cancellationToken)
    {
        var identity = _identity ?? throw new InvalidOperationException("Mesh transport has not been started.");
        return new PeerHello(identity.DeviceId, identity.DisplayName, identity.DeviceClass, identity.Platform, identity.PublicKeyFingerprint, LocalEndpoint ?? throw new InvalidOperationException("Mesh listening endpoint is unavailable."), await capabilities.GetLocalCapabilitiesAsync(cancellationToken).ConfigureAwait(false));
    }

    private SslClientAuthenticationOptions ClientOptions() => new()
    {
        TargetHost = "haven-mesh",
        ClientCertificates = new X509CertificateCollection { _certificate ?? throw new InvalidOperationException("Mesh identity certificate is unavailable.") },
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
    };

    private async Task<TcpClient> ConnectSocketAsync(string endpoint, CancellationToken cancellationToken)
    {
        var (address, port) = ParseEndpoint(endpoint);
        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        try
        {
            await client.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    private static (IPAddress Address, int Port) ParseEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("Mesh endpoint is required.", nameof(endpoint));
        var split = endpoint.Trim().Split(':', StringSplitOptions.TrimEntries);
        if (split.Length != 2 || !IPAddress.TryParse(split[0], out var address) || address.AddressFamily != AddressFamily.InterNetwork || !int.TryParse(split[1], out var port) || port is < 1 or > 65535)
            throw new ArgumentException("Mesh endpoints must use a private IPv4 address and port, for example 192.168.1.20:48231.", nameof(endpoint));
        if (!IsPrivate(address)) throw new UnauthorizedAccessException("Mesh does not connect to public Internet addresses.");
        return (address, port);
    }

    private static string NormalizeAdvertisedEndpoint(string endpoint)
    {
        var (address, port) = ParseEndpoint(endpoint);
        return $"{address}:{port}";
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254));
    }

    private static IPAddress SelectAdvertisableAddress()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address) && IsPrivate(address)) ?? IPAddress.Loopback;
        }
        catch { return IPAddress.Loopback; }
    }

    private static X509Certificate2 CreateIdentityCertificate(MeshLocalIdentity identity, string privateKey)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(privateKey); }
        catch (FormatException ex) { throw new InvalidDataException("Mesh identity secret is not a valid encoded private key.", ex); }
        using var key = ECDsa.Create();
        try { key.ImportPkcs8PrivateKey(bytes, out _); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        var fingerprint = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        if (!fingerprint.Equals(identity.PublicKeyFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Mesh private key does not match the persisted device fingerprint.");
        var request = new CertificateRequest($"CN=Haven Mesh {identity.DeviceId:N}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(2));
        var pkcs12 = generated.Export(X509ContentType.Pkcs12);
        try
        {
            return X509CertificateLoader.LoadPkcs12(pkcs12, password: null, X509KeyStorageFlags.DefaultKeySet);
        }
        finally { CryptographicOperations.ZeroMemory(pkcs12); }
    }

    private static string Fingerprint(X509Certificate certificate)
    {
        using var parsed = new X509Certificate2(certificate);
        using var key = parsed.GetECDsaPublicKey() ?? throw new AuthenticationException("Mesh peer certificate does not contain an ECDSA public key.");
        return Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
    }

    private static void ValidateFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length != 64 || fingerprint.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("Mesh device fingerprint must be a SHA-256 hexadecimal value.", nameof(fingerprint));
    }

    private MeshTransportPeer ValidateHello(PeerHello hello, string endpoint, string presentedFingerprint, string expectedFingerprint)
    {
        if (hello.DeviceId == Guid.Empty || hello.DeviceId == _identity?.DeviceId) throw new AuthenticationException("Mesh peer presented an invalid device ID.");
        if (string.IsNullOrWhiteSpace(hello.DisplayName) || hello.DisplayName.Length > 120) throw new InvalidDataException("Mesh peer display name is invalid.");
        if (!hello.PublicKeyFingerprint.Equals(presentedFingerprint, StringComparison.OrdinalIgnoreCase) || !presentedFingerprint.Equals(expectedFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new AuthenticationException("Mesh peer identity fingerprint does not match the TLS certificate or pinned pairing identity.");
        return new MeshTransportPeer(hello.DeviceId, hello.DisplayName.Trim(), hello.DeviceClass, hello.Platform, hello.PublicKeyFingerprint.ToLowerInvariant(), endpoint, hello.Capabilities ?? []);
    }

    private async Task ReplaceConnectionAsync(PeerConnection connection)
    {
        if (_connections.TryGetValue(connection.DeviceId, out var previous))
        {
            _connections[connection.DeviceId] = connection;
            await previous.DisposeAsync().ConfigureAwait(false);
        }
        else _connections[connection.DeviceId] = connection;
    }

    private static async Task WriteFrameAsync(Stream stream, WireEnvelope envelope, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);
        if (payload.Length is <= 0 or > MaximumFrameBytes) throw new InvalidDataException("Mesh frame exceeds the allowed size.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WireEnvelope> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is <= 0 or > MaximumFrameBytes) throw new InvalidDataException("Mesh peer sent an invalid frame size.");
        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<WireEnvelope>(payload) ?? throw new InvalidDataException("Mesh peer sent an empty frame.");
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Mesh peer closed the connection.");
            total += read;
        }
    }

    private void EnsureRunning()
    {
        if (!IsRunning || _certificate is null || _identity is null) throw new InvalidOperationException("Mesh transport is not running.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _lifecycle.Dispose();
    }

    private sealed record WireEnvelope(string Kind, string Payload);
    private sealed record WireMessage(Guid MessageId, string Kind, string Payload, DateTimeOffset SentAt);
    private sealed record PairRequest(Guid ChallengeId, string VerificationCode, PeerHello Hello);
    private sealed record PeerHello(Guid DeviceId, string DisplayName, MeshDeviceClass DeviceClass, CapabilityPlatform Platform, string PublicKeyFingerprint, string Endpoint, IReadOnlyList<MeshCapabilityDescriptor> Capabilities);

    private sealed class PeerConnection(Guid deviceId, string remoteFingerprint, string endpoint, TcpClient socket, SslStream stream) : IAsyncDisposable
    {
        public Guid DeviceId { get; } = deviceId;
        public string RemoteFingerprint { get; } = remoteFingerprint;
        public string Endpoint { get; } = endpoint;
        public TcpClient Socket { get; } = socket;
        public SslStream Stream { get; } = stream;
        public SemaphoreSlim SendGate { get; } = new(1, 1);
        public ValueTask DisposeAsync()
        {
            try { Stream.Dispose(); } catch { }
            try { Socket.Dispose(); } catch { }
            SendGate.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AsyncTcpHolder(TcpClient client) : IAsyncDisposable
    {
        private TcpClient? _client = client;
        public void Detach() => _client = null;
        public ValueTask DisposeAsync() { try { _client?.Dispose(); } catch { } return ValueTask.CompletedTask; }
    }
}
