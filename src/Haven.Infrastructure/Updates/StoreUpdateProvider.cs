/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/Updates/StoreUpdateProvider.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns StoreUpdateProvider. Read the member comments below as a map of each responsibility.
 * How: The provider performs no network or file I/O; it reports honest baseline state, always yields null manifests, and refuses download/apply operations with guidance the caller converts into honest UI state.
 * Why: For Microsoft Store installs the Store owns availability and installation; Haven must never shadow-download binaries that would bypass Store signing and update integrity.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Update provider for Microsoft Store-managed installations. Never downloads or stages binaries itself.
/// </summary>
/// <remarks>
/// State semantics: <see cref="UpdateState.Idle"/> until the first check completes; because availability lives inside the Store,
/// a completed check transitions to <see cref="UpdateState.UpToDate"/> meaning "nothing for Haven to act on — the Store owns updates",
/// not a proof that no Store release exists. <see cref="StoreUpdateProvider.CheckForUpdateAsync"/> always returns <c>null</c>.
/// </remarks>
public sealed class StoreUpdateProvider(
    Func<InstallationInfo> installationDetector,
    Func<string> currentVersionProvider) : IUpdateProvider
{
    /// <summary>Honest status message surfaced on every report from this provider.</summary>
    public const string ManagedByStoreMessage = "Updates are managed by the Microsoft Store.";

    private UpdateState _state = UpdateState.Idle;

    /// <inheritdoc />
    public Task<UpdateStatusReport> GetStatusAsync(CancellationToken cancellationToken)
    {
        var info = installationDetector();
        var message = _state == UpdateState.Idle ? ManagedByStoreMessage : $"{ManagedByStoreMessage} Check the Store library for new releases.";
        return Task.FromResult(new UpdateStatusReport(
            info.Source,
            UpdateChannel.Stable,
            currentVersionProvider(),
            AvailableVersion: null,
            _state,
            DownloadPercent: null,
            message,
            StoreManaged: true));
    }

    /// <inheritdoc />
    public Task<UpdateManifest?> CheckForUpdateAsync(string currentVersion, CancellationToken cancellationToken)
    {
        _ = currentVersion;
        _ = cancellationToken;
        _state = UpdateState.UpToDate;
        return Task.FromResult<UpdateManifest?>(null);
    }

    /// <inheritdoc />
    public Task<string> DownloadAndStageAsync(UpdateManifest manifest, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        _ = manifest;
        _ = progress;
        _ = cancellationToken;
        throw CreateNotSupportedForDownload();
    }

    /// <summary>
    /// Refuses to apply staged updates because the Store owns installation; callers convert this into an honest UI state directing users to the Store library.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled.</param>
    /// <returns>Never returns normally.</returns>
    /// <exception cref="NotSupportedException">Always; the message explains the Store-managed flow.</exception>
    public Task ApplyStagedUpdateAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        throw new NotSupportedException("Updates are managed by the Microsoft Store. Open the Microsoft Store library and let it install the update; Haven does not replace its own binaries for Store installs.");
    }

    /// <summary>
    /// Builds the refusal exception used by every operation that would download or replace binaries.
    /// </summary>
    internal static NotSupportedException CreateNotSupportedForDownload() => new(
        "Updates are managed by the Microsoft Store. Haven never downloads update binaries for Store installs; open the Microsoft Store library instead.");
}
