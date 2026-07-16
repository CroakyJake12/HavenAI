using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed class BrowserDownloadTransportIntegrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "haven-download-integration-tests-" + Guid.NewGuid().ToString("N"));

    public BrowserDownloadTransportIntegrationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task ApprovedDownloadUsesSanitizedHeaderNameHashAndConfinedDestination()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var body = Encoding.UTF8.GetBytes("verified browser download");
        var server = ServeFileAsync(listener, body, "../../CON\u202Ecod.exe.txt", CancellationToken.None);
        var target = new Uri($"http://127.0.0.1:{port}/payload");
        var action = CreateDownloadAction(target);
        var transport = new BrowserDownloadTransport(new LoopbackTestPolicy(), _directory);

        var record = await transport.DownloadAsync(action, CancellationToken.None);
        await server;

        Assert.Equal("_CONcod.exe.txt", record.FileName);
        Assert.Equal(body.LongLength, record.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(), record.Sha256);
        Assert.Equal(body, await File.ReadAllBytesAsync(record.StoredPath));
        Assert.StartsWith(Path.GetFullPath(_directory) + Path.DirectorySeparatorChar, Path.GetFullPath(record.StoredPath), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Directory.EnumerateFiles(_directory), path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApprovedDownloadRemovesStalePartialBeforeSavingAndAllocatesCollisionName()
    {
        var existing = Path.Combine(_directory, "report.txt");
        await File.WriteAllTextAsync(existing, "existing");
        var stale = BrowserDownloadFilePolicy.CreatePartialPath(existing);
        await File.WriteAllTextAsync(stale, "partial");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow - BrowserDownloadFilePolicy.PartialFileRetention - TimeSpan.FromMinutes(2));

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var body = Encoding.UTF8.GetBytes("replacement");
        var server = ServeFileAsync(listener, body, "report.txt", CancellationToken.None);
        var target = new Uri($"http://127.0.0.1:{port}/report");
        var transport = new BrowserDownloadTransport(new LoopbackTestPolicy(), _directory);

        var record = await transport.DownloadAsync(CreateDownloadAction(target), CancellationToken.None);
        await server;

        Assert.False(File.Exists(stale));
        Assert.Equal("report (2).txt", record.FileName);
        Assert.Equal("existing", await File.ReadAllTextAsync(existing));
        Assert.Equal("replacement", await File.ReadAllTextAsync(record.StoredPath));
    }

    private static BrowserPendingAction CreateDownloadAction(Uri target)
    {
        var now = DateTimeOffset.UtcNow;
        return new BrowserPendingAction(
            Guid.NewGuid(), BrowserActionKind.Download, target.GetLeftPart(UriPartial.Authority),
            "Download test file", target.ToString(), null, BrowserActionState.Approved,
            now, now.AddMinutes(10), now, null);
    }

    private static async Task ServeFileAsync(TcpListener listener, byte[] body, string fileName, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        await DrainRequestAsync(stream, cancellationToken);
        var headers = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/octet-stream\r\n" +
            $"Content-Disposition: attachment; filename=\"{fileName}\"\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task DrainRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var delimiter = "\r\n\r\n"u8.ToArray();
        var matched = 0;
        var buffer = new byte[1];
        while (matched < delimiter.Length)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            matched = buffer[0] == delimiter[matched] ? matched + 1 : buffer[0] == delimiter[0] ? 1 : 0;
        }
    }

    private sealed class LoopbackTestPolicy : IBrowserNavigationPolicy
    {
        public Task<BrowserNavigationAssessment> AssessAsync(Uri address, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new BrowserNavigationAssessment(address, true, "test-pinned", ["127.0.0.1"]));
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
