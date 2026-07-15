using System.Net;
using System.Net.Sockets;
using System.Text;
using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed class BrowserTransportAndRecoveryTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task BackgroundLoaderValidatesAndPinsEveryRedirectHop()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var policy = new RecordingPolicy();
        var loader = new BrowserBackgroundPageLoader(policy);
        var server = ServeRedirectAndPageAsync(listener, CancellationToken.None);

        var snapshot = await loader.LoadAsync(new Uri($"http://127.0.0.1:{port}/start"), CancellationToken.None);
        await server;

        Assert.Equal($"http://127.0.0.1:{port}/page", snapshot.Address?.ToString());
        Assert.Equal("Pinned test", snapshot.Title);
        Assert.Contains("Security heading", snapshot.Headings);
        Assert.Contains("Safe background content", snapshot.Text);
        Assert.False(snapshot.IsInteractive);
        Assert.Equal(2, policy.Assessed.Count);
        Assert.EndsWith("/start", policy.Assessed[0].AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith("/page", policy.Assessed[1].AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApprovedActionInterruptedByRestartBecomesFailedAndAudited()
    {
        var now = DateTimeOffset.UtcNow;
        var pending = new BrowserPendingAction(
            Guid.NewGuid(),
            BrowserActionKind.Download,
            "https://example.test",
            "Download example",
            "https://example.test/file.bin",
            "file.bin",
            BrowserActionState.Pending,
            now,
            now.AddMinutes(10),
            now,
            null);
        using (var store = new BrowserAutomationStore(_paths))
        {
            await store.AddPendingAsync(pending, CancellationToken.None);
            await store.UpdateActionAsync(pending with
            {
                State = BrowserActionState.Approved,
                UpdatedAt = now.AddSeconds(1)
            }, CancellationToken.None);
        }

        using var recovered = new BrowserAutomationStore(_paths);
        var action = await recovered.GetActionAsync(pending.Id, CancellationToken.None);
        var audit = await recovered.GetAuditAsync(20, CancellationToken.None);

        Assert.Equal(BrowserActionState.Failed, action?.State);
        Assert.Contains("not resumed", action?.Failure ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(audit, item => item.Operation == "recovery-interrupted" && item.Kind == BrowserActionKind.Download && !item.Succeeded);
    }

    private static async Task ServeRedirectAndPageAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        for (var index = 0; index < 2; index++)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            var requestLine = await ReadRequestLineAsync(stream, cancellationToken);
            await DrainHeadersAsync(stream, cancellationToken);
            if (requestLine.Contains("/start", StringComparison.Ordinal))
            {
                var redirect = "HTTP/1.1 302 Found\r\nLocation: /page\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(redirect), cancellationToken);
            }
            else
            {
                const string html = "<!doctype html><html><head><title>Pinned test</title></head><body><h1>Security heading</h1><p>Safe background content</p></body></html>";
                var body = Encoding.UTF8.GetBytes(html);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, cancellationToken);
                await stream.WriteAsync(body, cancellationToken);
            }
            await stream.FlushAsync(cancellationToken);
        }
    }

    private static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0 || buffer[0] == (byte)'\n') break;
            if (buffer[0] != (byte)'\r') builder.Append((char)buffer[0]);
            if (builder.Length > 8_192) throw new InvalidDataException("Test request line was too long.");
        }
        return builder.ToString();
    }

    private static async Task DrainHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var matched = 0;
        var delimiter = "\r\n\r\n"u8.ToArray();
        var buffer = new byte[1];
        while (matched < delimiter.Length)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            matched = buffer[0] == delimiter[matched] ? matched + 1 : buffer[0] == delimiter[0] ? 1 : 0;
        }
    }

    public void Dispose() => _paths.Dispose();

    private sealed class RecordingPolicy : IBrowserNavigationPolicy
    {
        public List<Uri> Assessed { get; } = [];
        public Task<BrowserNavigationAssessment> AssessAsync(Uri address, CancellationToken cancellationToken)
        {
            Assessed.Add(address);
            return Task.FromResult(new BrowserNavigationAssessment(address, true, "test-pinned", ["127.0.0.1"]));
        }
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-browser-transport-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser-profile");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "legacy.json");
        }
        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
