using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Finds nearby Haven Mesh listeners. Discovery data is intentionally untrusted; pairing remains
/// the only route that can create a trusted peer.
/// </summary>
public sealed class LanMeshDiscoveryService : IMeshDiscoveryService
{
    private static readonly IPAddress Group = IPAddress.Parse("239.255.72.86");
    private const int Port = 45886;
    private static readonly TimeSpan AnnouncementInterval = TimeSpan.FromSeconds(5);
    private UdpClient? _socket;
    private CancellationTokenSource? _lifetime;
    private Task? _receiveLoop;
    private Task? _announceLoop;
    private MeshDiscoveryAnnouncement? _local;

    public event Action<MeshDiscoveryCandidate>? CandidateObserved;
    public bool IsRunning => _socket is not null && _lifetime is { IsCancellationRequested: false };

    public Task StartAsync(MeshLocalIdentity localIdentity, string localEndpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(localIdentity);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsRunning) return Task.CompletedTask;
        var listenerPort = ParsePrivateEndpointPort(localEndpoint);
        var socket = new UdpClient(AddressFamily.InterNetwork);
        socket.Client.ExclusiveAddressUse = false;
        socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
        socket.JoinMulticastGroup(Group);
        socket.MulticastLoopback = false;
        _socket = socket;
        _local = new(localIdentity.DeviceId, localIdentity.DisplayName, localIdentity.DeviceClass, localIdentity.Platform, localIdentity.PublicKeyFingerprint, listenerPort);
        _lifetime = new CancellationTokenSource();
        _receiveLoop = ReceiveLoopAsync(_lifetime.Token);
        _announceLoop = AnnounceLoopAsync(_lifetime.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lifetime = _lifetime;
        _lifetime = null;
        try { lifetime?.Cancel(); } catch { }
        try { _socket?.DropMulticastGroup(Group); } catch { }
        try { _socket?.Dispose(); } catch { }
        _socket = null;
        foreach (var task in new[] { _receiveLoop, _announceLoop }.Where(task => task is not null))
        {
            try { await task!.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }
        _receiveLoop = null;
        _announceLoop = null;
        _local = null;
        lifetime?.Dispose();
    }

    private async Task AnnounceLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var socket = _socket;
            var local = _local;
            if (socket is null || local is null) return;
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(local);
                if (bytes.Length <= 4096) await socket.SendAsync(bytes, new IPEndPoint(Group, Port), cancellationToken).ConfigureAwait(false);
                await Task.Delay(AnnouncementInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException)
            {
                try { await Task.Delay(AnnouncementInterval, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try { result = await _socket!.ReceiveAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; }
            if (!IsPrivate(result.RemoteEndPoint.Address) || result.Buffer.Length is <= 0 or > 4096) continue;
            MeshDiscoveryAnnouncement? announcement;
            try { announcement = JsonSerializer.Deserialize<MeshDiscoveryAnnouncement>(result.Buffer); }
            catch (JsonException) { continue; }
            var local = _local;
            if (announcement is null || local is null || announcement.DeviceId == Guid.Empty || announcement.DeviceId == local.DeviceId) continue;
            if (announcement.ListenerPort is < 1 or > 65535 || string.IsNullOrWhiteSpace(announcement.DisplayName) || announcement.DisplayName.Length > 120) continue;
            if (string.IsNullOrWhiteSpace(announcement.PublicKeyFingerprint) || announcement.PublicKeyFingerprint.Length != 64 || announcement.PublicKeyFingerprint.Any(ch => !Uri.IsHexDigit(ch))) continue;
            var endpoint = $"{result.RemoteEndPoint.Address}:{announcement.ListenerPort}";
            CandidateObserved?.Invoke(new MeshDiscoveryCandidate(
                announcement.DeviceId, announcement.DisplayName.Trim(), announcement.DeviceClass, announcement.Platform,
                announcement.PublicKeyFingerprint.ToLowerInvariant(), endpoint, DateTimeOffset.UtcNow));
        }
    }

    private static int ParsePrivateEndpointPort(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("Mesh discovery needs the active listener endpoint.", nameof(endpoint));
        var split = endpoint.Trim().Split(':', StringSplitOptions.TrimEntries);
        if (split.Length != 2 || !IPAddress.TryParse(split[0], out var address) || !IsPrivate(address) || !int.TryParse(split[1], out var port) || port is < 1 or > 65535)
            throw new ArgumentException("Mesh discovery only advertises a private IPv4 listener endpoint.", nameof(endpoint));
        return port;
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254));
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None).ConfigureAwait(false);

    private sealed record MeshDiscoveryAnnouncement(
        Guid DeviceId,
        string DisplayName,
        MeshDeviceClass DeviceClass,
        CapabilityPlatform Platform,
        string PublicKeyFingerprint,
        int ListenerPort);
}
