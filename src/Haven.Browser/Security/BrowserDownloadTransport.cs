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
    public const long MaximumDownloadBytes = 250L * 1024 * 1024;

    /// <summary>
    /// Describes a policy-approved native WebView transfer. The WebView writes only to PartialPath; Haven hashes and atomically promotes it to FinalPath after completion.
    /// </summary>
    public sealed record NativeDownloadPlan(
        Guid ActionId,
        string RecordAddress,
        string FileName,
        string FinalPath,
        string PartialPath,
        DateTimeOffset PreparedAt);

    /// <summary>
    /// Allocates confined paths for a native WebView download after applying the same Browser navigation and filename policies used by managed downloads.
    /// </summary>
    public async Task<NativeDownloadPlan> PrepareNativeDownloadAsync(
        Guid actionId,
        Uri sourceAddress,
        Uri? initiatorAddress,
        string? suggestedFileName,
        string? contentDisposition,
        CancellationToken cancellationToken)
    {
        if (actionId == Guid.Empty) throw new ArgumentException("A native download requires a non-empty action id.", nameof(actionId));
        ArgumentNullException.ThrowIfNull(sourceAddress);
        if (!sourceAddress.IsAbsoluteUri) throw new ArgumentException("The native download address must be absolute.", nameof(sourceAddress));

        var policyAddress = IsHttpAddress(sourceAddress)
            ? sourceAddress
            : initiatorAddress is not null && IsHttpAddress(initiatorAddress)
                ? initiatorAddress
                : throw new UnauthorizedAccessException("Browser-local downloads require an active HTTP or HTTPS page origin.");
        var assessment = await _policy.AssessAsync(policyAddress, cancellationToken).ConfigureAwait(false);
        if (!assessment.IsAllowed) throw new UnauthorizedAccessException("Download blocked: " + assessment.Reason);

        Directory.CreateDirectory(_downloadDirectory);
        BrowserDownloadFilePolicy.CleanupStalePartialFiles(_downloadDirectory, DateTimeOffset.UtcNow);
        ContentDispositionHeaderValue? parsedDisposition = null;
        if (!string.IsNullOrWhiteSpace(contentDisposition))
            ContentDispositionHeaderValue.TryParse(contentDisposition, out parsedDisposition);
        var fileName = BrowserDownloadFilePolicy.SanitizeFileName(suggestedFileName)
                       ?? BrowserDownloadFilePolicy.SanitizeFileName(FileNameFromHeaders(parsedDisposition))
                       ?? BrowserDownloadFilePolicy.SanitizeFileName(Path.GetFileName(sourceAddress.LocalPath))
                       ?? "download.bin";
        var finalPath = BrowserDownloadFilePolicy.AllocateUniquePath(_downloadDirectory, fileName);
        var partialPath = BrowserDownloadFilePolicy.CreatePartialPath(finalPath);
        return new NativeDownloadPlan(
            actionId,
            RecordAddress(sourceAddress, initiatorAddress),
            Path.GetFileName(finalPath),
            finalPath,
            partialPath,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Validates, hashes, and atomically promotes a file written by the native WebView into Haven's Downloads folder.
    /// </summary>
    public async Task<BrowserDownloadRecord> FinalizeNativeDownloadAsync(
        NativeDownloadPlan plan,
        string? contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var plannedFinal = EnsureOwnedNativePath(plan.FinalPath);
        var partialPath = EnsureOwnedNativePath(plan.PartialPath);
        if (!partialPath.StartsWith(plannedFinal + ".haven-download-", StringComparison.OrdinalIgnoreCase)
            || !partialPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The native download partial path is not a Haven-managed partial.");
        if (!File.Exists(partialPath)) throw new FileNotFoundException("The native download did not produce its expected partial file.", partialPath);

        var completed = false;
        try
        {
            var initialLength = new FileInfo(partialPath).Length;
            if (initialLength > MaximumDownloadBytes)
                throw new InvalidOperationException("The download exceeds Haven's 250 MB limit.");

            long size = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = new FileStream(
                             partialPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    size += read;
                    if (size > MaximumDownloadBytes)
                        throw new InvalidOperationException("The download exceeded Haven's 250 MB limit while finalizing.");
                    hash.AppendData(buffer, 0, read);
                }
            }

            var finalPath = File.Exists(plannedFinal) || Directory.Exists(plannedFinal)
                ? BrowserDownloadFilePolicy.AllocateUniquePath(_downloadDirectory, plan.FileName)
                : plannedFinal;
            File.Move(partialPath, finalPath, false);
            completed = true;
            return new BrowserDownloadRecord(
                Guid.NewGuid(),
                plan.ActionId,
                plan.RecordAddress,
                Path.GetFileName(finalPath),
                finalPath,
                size,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
                string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim(),
                DateTimeOffset.UtcNow);
        }
        finally
        {
            if (!completed) TryDeleteNativePartial(partialPath);
        }
    }

    /// <summary>
    /// Removes a Haven-owned native download partial after rejection, cancellation, interruption, or disposal.
    /// </summary>
    public void AbortNativeDownload(NativeDownloadPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var plannedFinal = EnsureOwnedNativePath(plan.FinalPath);
        var partialPath = EnsureOwnedNativePath(plan.PartialPath);
        if (!partialPath.StartsWith(plannedFinal + ".haven-download-", StringComparison.OrdinalIgnoreCase)
            || !partialPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The native download partial path is not a Haven-managed partial.");
        TryDeleteNativePartial(partialPath);
    }

    private string EnsureOwnedNativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var root = Path.GetFullPath(_downloadDirectory);
        var full = Path.GetFullPath(path);
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The native download path escaped Haven's download directory.");
        return full;
    }

    private static string RecordAddress(Uri sourceAddress, Uri? initiatorAddress)
    {
        var record = IsHttpAddress(sourceAddress) ? sourceAddress : initiatorAddress!;
        return record.GetLeftPart(UriPartial.Path);
    }

    private static bool IsHttpAddress(Uri address) =>
        address.IsAbsoluteUri && address.Scheme is "http" or "https" && string.IsNullOrEmpty(address.UserInfo);

    private static void TryDeleteNativePartial(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
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
    /// Performs download asynchronously so I/O does not block the caller's thread.
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
    /// Performs save asynchronously so I/O does not block the caller's thread.
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
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(
                             temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
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
            }
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
