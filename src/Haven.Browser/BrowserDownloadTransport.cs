using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

public sealed class BrowserDownloadTransport(IBrowserNavigationPolicy policy, IAppPaths paths)
{
    private const long MaximumDownloadBytes = 250L * 1024 * 1024;
    private readonly string _downloadDirectory = ResolveDownloadDirectory(paths);

    public async Task<BrowserDownloadRecord> DownloadAsync(BrowserPendingAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Kind != BrowserActionKind.Download) throw new ArgumentException("The action is not a download.", nameof(action));
        var current = new Uri(action.Target, UriKind.Absolute);

        for (var redirect = 0; redirect <= 8; redirect++)
        {
            var assessment = await policy.AssessAsync(current, cancellationToken).ConfigureAwait(false);
            if (!assessment.IsAllowed) throw new UnauthorizedAccessException("Download destination blocked: " + assessment.Reason);
            var addresses = assessment.ResolvedAddresses.Select(IPAddress.Parse).ToArray();
            if (addresses.Length == 0) throw new UnauthorizedAccessException("The approved download destination has no pinned addresses.");

            using var handler = CreatePinnedHandler(current, addresses);
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location is { } location)
            {
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
                throw new InvalidOperationException("The download exceeds Haven's 250 MB limit.");
            return await SaveAsync(action, current, response, cancellationToken).ConfigureAwait(false);
        }
        throw new HttpRequestException("The download exceeded Haven's eight-redirect limit.");
    }

    private async Task<BrowserDownloadRecord> SaveAsync(
        BrowserPendingAction action,
        Uri finalAddress,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_downloadDirectory);
        var fileName = SafeFileName(action.SuggestedFileName)
                       ?? SafeFileName(FileNameFromHeaders(response.Content.Headers.ContentDisposition))
                       ?? SafeFileName(Path.GetFileName(finalAddress.LocalPath))
                       ?? "download.bin";
        var destination = UniquePath(_downloadDirectory, fileName);
        var temporary = destination + ".haven-download-" + Guid.NewGuid().ToString("N") + ".tmp";
        long size = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                size += read;
                if (size > MaximumDownloadBytes)
                    throw new InvalidOperationException("The download exceeded Haven's 250 MB limit while streaming.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, false);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
        }

        return new BrowserDownloadRecord(
            Guid.NewGuid(),
            action.Id,
            finalAddress.ToString(),
            Path.GetFileName(destination),
            destination,
            size,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            response.Content.Headers.ContentType?.MediaType,
            DateTimeOffset.UtcNow);
    }

    private static SocketsHttpHandler CreatePinnedHandler(Uri address, IReadOnlyList<IPAddress> addresses)
    {
        var expectedHost = address.DnsSafeHost;
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
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

    private static string ResolveDownloadDirectory(IAppPaths paths)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile)
            ? Path.Combine(paths.DataDirectory, "Downloads")
            : Path.Combine(profile, "Downloads", "Haven");
    }

    private static string? FileNameFromHeaders(ContentDispositionHeaderValue? disposition)
    {
        var value = disposition?.FileNameStar ?? disposition?.FileName;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');
    }

    private static string? SafeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var name = Path.GetFileName(value.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        name = name.Trim().TrimEnd('.');
        if (name is "" or "." or "..") return null;
        return name.Length <= 180 ? name : name[..180];
    }

    private static string UniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; index < 10_000; index++)
        {
            path = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(path)) return path;
        }
        throw new IOException("Could not allocate a unique download file name.");
    }
}
