/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Browser/BrowserDownloadTransport.cs, in the Browser layer, which isolates browser state, safety policy, transport, and automation.
 * What: This file owns BrowserDownloadTransport. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Browser capabilities are isolated behind explicit policy boundaries because navigation and automation process untrusted external content.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Net.Http.Headers;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Core;

namespace Haven.Browser;

/// <summary>
/// Represents browser download transport and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserDownloadTransport
{
    /// <summary>
    /// Stores maximum download bytes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const long MaximumDownloadBytes = 250L * 1024 * 1024;
    /// <summary>
    /// Stores policy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IBrowserNavigationPolicy _policy;
    /// <summary>
    /// Stores download directory locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _downloadDirectory;

    public BrowserDownloadTransport(IBrowserNavigationPolicy policy, IAppPaths paths)
        : this(policy, ResolveDownloadDirectory(paths))
    {
    }

    public BrowserDownloadTransport(IBrowserNavigationPolicy policy, string downloadDirectory)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadDirectory);
        _downloadDirectory = Path.GetFullPath(downloadDirectory);
    }

    /// <summary>
    /// Performs download async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<BrowserDownloadRecord> DownloadAsync(BrowserPendingAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.Kind != BrowserActionKind.Download) throw new ArgumentException("The action is not a download.", nameof(action));

        Directory.CreateDirectory(_downloadDirectory);
        BrowserDownloadFilePolicy.CleanupStalePartialFiles(_downloadDirectory, DateTimeOffset.UtcNow);

        await using var lease = await BrowserPinnedHttpTransport.SendAsync(
            _policy,
            new Uri(action.Target, UriKind.Absolute),
            maximumRedirects: 8,
            timeout: TimeSpan.FromMinutes(10),
            cancellationToken).ConfigureAwait(false);
        var response = lease.Response;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
            throw new InvalidOperationException("The download exceeds Haven's 250 MB limit.");
        return await SaveAsync(action, lease.FinalAddress, response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs save async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<BrowserDownloadRecord> SaveAsync(
        BrowserPendingAction action,
        Uri finalAddress,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var fileName = BrowserDownloadFilePolicy.SanitizeFileName(action.SuggestedFileName)
                       ?? BrowserDownloadFilePolicy.SanitizeFileName(FileNameFromHeaders(response.Content.Headers.ContentDisposition))
                       ?? BrowserDownloadFilePolicy.SanitizeFileName(Path.GetFileName(finalAddress.LocalPath))
                       ?? "download.bin";
        var destination = BrowserDownloadFilePolicy.AllocateUniquePath(_downloadDirectory, fileName);
        var temporary = BrowserDownloadFilePolicy.CreatePartialPath(destination);
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
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return new BrowserDownloadRecord(
            Guid.NewGuid(),
            action.Id,
            finalAddress.GetLeftPart(UriPartial.Path),
            Path.GetFileName(destination),
            destination,
            size,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            response.Content.Headers.ContentType?.MediaType,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Performs the resolve download directory step owned by this component.
    /// </summary>
    private static string ResolveDownloadDirectory(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile)
            ? Path.Combine(paths.DataDirectory, "Downloads")
            : Path.Combine(profile, "Downloads", "Haven");
    }

    /// <summary>
    /// Performs the file name from headers step owned by this component.
    /// </summary>
    private static string? FileNameFromHeaders(ContentDispositionHeaderValue? disposition)
    {
        var value = disposition?.FileNameStar ?? disposition?.FileName;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');
    }
}
