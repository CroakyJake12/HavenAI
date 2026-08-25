/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/Updates/HavenDirectUpdateProvider.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns DirectUpdateOptions, UpdateManifestValidator and HavenDirectUpdateProvider. Read the member comments below as a map of each responsibility.
 * How: Checks fetch a small HTTPS manifest and validate it strictly; downloads stream to a staging temp file while an incremental SHA-256 accumulates, then move atomically to the pending directory only after verification succeeds.
 * Why: Direct installs have no Store guarding their updates, so Haven enforces its own transport, metadata and hash integrity gates before anything is ever considered staged.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file. Never weaken validation or report unverified bytes as staged.
 */

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Configuration for <see cref="HavenDirectUpdateProvider"/>.
/// </summary>
/// <remarks>
/// The default URLs point at a PLACEHOLDER domain (<c>updates.haven.example</c>). Product configuration MUST override
/// all three channel URLs before release; no runtime log announces this because this component intentionally logs nothing.
/// </remarks>
public sealed record DirectUpdateOptions
{
    /// <summary>Placeholder channel manifest template; each configured URL may contain a <c>{channel}</c> token replaced with the lowercase channel name.</summary>
    public const string PlaceholderChannelTemplate = "https://updates.haven.example/channel/{channel}/manifest.json";

    /// <summary>Gets or sets the stable-channel manifest URL template. MUST be overridden before release.</summary>
    public string StableUrl { get; init; } = PlaceholderChannelTemplate;

    /// <summary>Gets or sets the preview-channel manifest URL template. MUST be overridden before release.</summary>
    public string PreviewUrl { get; init; } = PlaceholderChannelTemplate;

    /// <summary>Gets or sets the development-channel manifest URL template. MUST be overridden before release.</summary>
    public string DevelopmentUrl { get; init; } = PlaceholderChannelTemplate;

    /// <summary>Gets or sets the app data directory under which <c>updates/staging</c> and <c>updates/pending</c> live. Required.</summary>
    public required string DataDirectory { get; init; }

    /// <summary>
    /// Resolves the manifest URL for a channel and refuses anything that is not an absolute HTTPS URL.
    /// </summary>
    /// <param name="channel">The channel to resolve.</param>
    /// <returns>An absolute HTTPS manifest URL.</returns>
    /// <exception cref="ArgumentException">When the resolved template is missing or not absolute HTTPS.</exception>
    public string ManifestUrlFor(UpdateChannel channel)
    {
        var template = channel switch
        {
            UpdateChannel.Stable => StableUrl,
            UpdateChannel.Preview => PreviewUrl,
            UpdateChannel.Development => DevelopmentUrl,
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown update channel."),
        };
        var resolved = template.Replace("{channel}", channel.ToString().ToLowerInvariant(), StringComparison.Ordinal);
        if (!Uri.TryCreate(resolved, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException($"Update manifest URL for channel '{channel}' must be an absolute HTTPS URL (got '{resolved}').", nameof(channel));
        }
        return resolved;
    }
}

/// <summary>
/// Strictly validates raw update-manifest JSON before anything trusts it. Oversized payloads, non-HTTPS download targets,
/// malformed versions or digests, and impossible publish dates are rejected outright.
/// </summary>
public static partial class UpdateManifestValidator
{
    /// <summary>Maximum accepted manifest payload size in bytes.</summary>
    public const int MaxManifestBytes = 256 * 1024;

    private const string SemverishPattern = """^\d+\.\d+(\.\d+){0,2}(-[0-9A-Za-z.\-]+)?$""";
    private const string Sha256HexPattern = """^[0-9a-fA-F]{64}$""";

    [GeneratedRegex(SemverishPattern)]
    private static partial Regex Semverish();

    [GeneratedRegex(Sha256HexPattern)]
    private static partial Regex Sha256Hex();

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Parses and validates raw manifest JSON against every integrity rule.
    /// </summary>
    /// <param name="json">Raw manifest text as received from the network.</param>
    /// <param name="utcNow">Current UTC time, injected so validation stays testable.</param>
    /// <returns>The validated <see cref="UpdateManifest"/>.</returns>
    /// <exception cref="InvalidDataException">When the payload exceeds the size cap or fails any field rule.</exception>
    public static UpdateManifest ParseAndValidate(string json, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length > MaxManifestBytes)
        {
            throw new InvalidDataException($"Update manifest exceeds the {MaxManifestBytes}-byte limit.");
        }

        UpdateManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions) ?? throw new InvalidDataException("Update manifest was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Update manifest is not valid JSON: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(manifest.Version) || !Semverish().IsMatch(manifest.Version))
        {
            throw new InvalidDataException("Update manifest version is missing or not semver-ish.");
        }
        if (string.IsNullOrWhiteSpace(manifest.Channel))
        {
            throw new InvalidDataException("Update manifest channel is missing.");
        }
        if (string.IsNullOrWhiteSpace(manifest.Sha256) || !Sha256Hex().IsMatch(manifest.Sha256))
        {
            throw new InvalidDataException("Update manifest SHA-256 digest must be exactly 64 hexadecimal characters.");
        }
        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("Update manifest download URL must be an absolute HTTPS URL.");
        }
        if (manifest.SizeBytes < 0)
        {
            throw new InvalidDataException("Update manifest size cannot be negative.");
        }
        if (manifest.PublishedAt > utcNow.AddDays(1))
        {
            throw new InvalidDataException("Update manifest publish date lies implausibly far in the future.");
        }
        if (manifest.PublishedAt < new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero))
        {
            throw new InvalidDataException("Update manifest publish date is missing or implausibly old.");
        }

        return manifest with
        {
            Version = manifest.Version.Trim(),
            Channel = manifest.Channel.Trim(),
            DownloadUrl = manifest.DownloadUrl.Trim(),
            Sha256 = manifest.Sha256.Trim().ToLowerInvariant(),
            ReleaseNotes = manifest.ReleaseNotes ?? string.Empty,
        };
    }
}

/// <summary>
/// Update provider for direct installs: checks a per-channel HTTPS manifest feed and stages verified packages locally.
/// </summary>
public sealed class HavenDirectUpdateProvider(DirectUpdateOptions options, HttpClient httpClient) : IUpdateProvider
{
    /// <summary>Suggested named HttpClient registration for dependency-injected clients.</summary>
    public const string HttpClientName = "haven-updates";

    private readonly byte[] copyBuffer = new byte[81920];

    /// <summary>Gets or sets the channel used by the next check; the orchestrator keeps this aligned with user preferences before each check.</summary>
    public UpdateChannel Channel { get; set; } = UpdateChannel.Stable;

    /// <summary>Creates a provider with a privately owned HttpClient when none is supplied.</summary>
    /// <param name="options">Direct update options.</param>
    public HavenDirectUpdateProvider(DirectUpdateOptions options) : this(options, new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
    {
    }

    /// <inheritdoc />
    public Task<UpdateStatusReport> GetStatusAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(new UpdateStatusReport(
            InstallationSource.DirectInstall,
            Channel,
            CurrentVersion: string.Empty,
            AvailableVersion: null,
            UpdateState.Idle,
            DownloadPercent: null,
            Message: "Ready to check for updates.",
            StoreManaged: false));
    }

    /// <inheritdoc />
    public async Task<UpdateManifest?> CheckForUpdateAsync(string currentVersion, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);
        var url = options.ManifestUrlFor(Channel);
        var json = await DownloadStringCappedAsync(url, cancellationToken).ConfigureAwait(false);
        var manifest = UpdateManifestValidator.ParseAndValidate(json, DateTimeOffset.UtcNow);
        return IsNewerVersion(manifest.Version, currentVersion) ? manifest : null;
    }

    /// <inheritdoc />
    public async Task<string> DownloadAndStageAsync(UpdateManifest manifest, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Update package URL must be an absolute HTTPS URL.", nameof(manifest));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Update package download failed with HTTP {(int)response.StatusCode}.");
        }

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await StageFromStreamAsync(content, manifest, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Streams <paramref name="source"/> into a staging temp file while hashing incrementally, verifies the digest and size,
    /// then atomically moves the verified package to <c>updates/pending/{version}.zip</c>. Public so tests can exercise real
    /// bytes end-to-end without a network endpoint.
    /// </summary>
    /// <param name="source">Payload stream, consumed fully.</param>
    /// <param name="manifest">Manifest whose SHA-256 and size gate acceptance.</param>
    /// <param name="progress">Optional receiver of 0-100 percentages; reported only when the integer value changes.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled; the temp file is removed.</param>
    /// <returns>Full path to the verified staged package.</returns>
    /// <exception cref="InvalidDataException">On hash or size mismatch ("signature/hash verification failed"); no partial file survives.</exception>
    public async Task<string> StageFromStreamAsync(Stream source, UpdateManifest manifest, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(manifest);

        var stagingDirectory = Path.Combine(options.DataDirectory, "updates", "staging");
        var pendingDirectory = Path.Combine(options.DataDirectory, "updates", "pending");
        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(pendingDirectory);

        var tempPath = Path.Combine(stagingDirectory, $"download-{Guid.NewGuid():n}.tmp");
        var lastReportedPercent = -1;
        string actualHash;
        try
        {
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: copyBuffer.Length, useAsync: true))
            using (var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                long totalRead = 0;
                int read;
                while ((read = await source.ReadAsync(copyBuffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    sha256.AppendData(copyBuffer, 0, read);
                    await output.WriteAsync(copyBuffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    totalRead += read;

                    if (manifest.SizeBytes > 0 && totalRead > manifest.SizeBytes)
                    {
                        throw new InvalidDataException("signature/hash verification failed: downloaded payload exceeds the manifest size.");
                    }
                    if (manifest.SizeBytes > 0 && progress is not null)
                    {
                        var percent = (int)(totalRead * 100 / manifest.SizeBytes);
                        if (percent != lastReportedPercent)
                        {
                            lastReportedPercent = percent;
                            progress.Report(percent);
                        }
                    }
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                actualHash = Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();
            }

            var actualLength = new FileInfo(tempPath).Length;
            if (manifest.SizeBytes > 0 && actualLength != manifest.SizeBytes)
            {
                throw new InvalidDataException($"signature/hash verification failed: downloaded {actualLength} bytes but the manifest declared {manifest.SizeBytes}.");
            }

            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("signature/hash verification failed.");
            }

            var destinationPath = Path.Combine(pendingDirectory, $"{manifest.Version}.zip");
            File.Move(tempPath, destinationPath, overwrite: true);
            CleanupOldPendingPackages(pendingDirectory, keepPath: destinationPath);
            return destinationPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Decides whether <paramref name="candidateVersion"/> is newer than <paramref name="currentVersion"/>, comparing as
    /// <see cref="Version"/> when both parse (normalized to three components) and falling back to ordinal-ignore-case string
    /// comparison otherwise (e.g. prerelease suffixes).
    /// </summary>
    /// <param name="candidateVersion">Offered version string.</param>
    /// <param name="currentVersion">Running version string.</param>
    /// <returns><c>true</c> only when the candidate is strictly newer.</returns>
    public static bool IsNewerVersion(string candidateVersion, string currentVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);
        if (TryParseNormalizedVersion(candidateVersion, out var candidate) && TryParseNormalizedVersion(currentVersion, out var current))
        {
            return candidate > current;
        }
        return string.Compare(candidateVersion.Trim(), currentVersion.Trim(), StringComparison.OrdinalIgnoreCase) > 0;
    }

    /// <summary>
    /// Performs try parse normalized version for the current operation.
    /// </summary>
    private static bool TryParseNormalizedVersion(string text, out Version version)
    {
        version = new Version(0, 0, 0);
        var trimmed = text.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }
        var plusIndex = trimmed.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex >= 0)
        {
            trimmed = trimmed[..plusIndex];
        }
        if (!Version.TryParse(trimmed, out var parsed))
        {
            return false;
        }
        version = parsed.Build < 0 ? new Version(parsed.Major, parsed.Minor, 0) : parsed;
        return true;
    }

    /// <summary>
    /// Performs download string capped asynchronous so I/O does not block the caller's thread.
    /// </summary>
    private async Task<string> DownloadStringCappedAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Update manifest request failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffered = new MemoryStream(capacity: 4096);
        int read;
        while ((read = await stream.ReadAsync(copyBuffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffered.Length + read > UpdateManifestValidator.MaxManifestBytes)
            {
                throw new InvalidDataException($"Update manifest exceeds the {UpdateManifestValidator.MaxManifestBytes}-byte limit.");
            }
            buffered.Write(copyBuffer, 0, read);
        }
        return Encoding.UTF8.GetString(buffered.ToArray());
    }

    /// <summary>
    /// Removes older verified packages, keeping only the newest one; deletion of individual stale files is best-effort housekeeping and never fails staging.
    /// </summary>
    private void CleanupOldPendingPackages(string pendingDirectory, string keepPath)
    {
        foreach (var candidate in Directory.EnumerateFiles(pendingDirectory, "*.zip"))
        {
            if (string.Equals(candidate, keepPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                File.Delete(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    /// <summary>
    /// Performs try delete for the current operation.
    /// </summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }
}
