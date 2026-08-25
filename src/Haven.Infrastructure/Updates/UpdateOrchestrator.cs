/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/Updates/UpdateOrchestrator.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns VersionedUpdatePreferenceStore, PendingStartupUpdate and UpdateOrchestrator. Read the member comments below as a map of each responsibility.
 * How: The orchestrator serializes check/download/apply cycles through one operation gate, keeps every report consistent behind a short state lock, raises StatusChanged outside locks, and persists preferences JSON through the versioned settings store under key "updates.preferences.v1".
 * Why: One honest facade must route Store-managed versus direct installs correctly, survive restarts, and never fake success: staging only ever marks a verified package for an EXTERNAL installer/bootstrapper; Haven does not replace its own running binaries.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file. Keep the honest-staging contract: do not add self-swap logic without an observed, approved mechanism.
 */

using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Persists <see cref="UpdatePreferences"/> through <see cref="IVersionedSettingsStore"/> so update preferences ride Haven's existing versioned settings export/import.
/// </summary>
public sealed class VersionedUpdatePreferenceStore(IVersionedSettingsStore settings) : IUpdatePreferenceStore
{
    /// <summary>Settings key holding the preferences JSON; part of the persistence contract.</summary>
    public const string PreferenceKey = "updates.preferences.v1";

    /// <inheritdoc />
    public async Task<UpdatePreferences> LoadAsync(CancellationToken cancellationToken)
    {
        return await settings.GetAsync<UpdatePreferences>(PreferenceKey, cancellationToken).ConfigureAwait(false)
            ?? new UpdatePreferences(true, UpdateChannel.Stable);
    }

    /// <inheritdoc />
    public Task SaveAsync(UpdatePreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return settings.SetAsync(PreferenceKey, preferences, cancellationToken);
    }
}

/// <summary>
/// A verified package discovered staged at startup, surfaced so the shell can prompt the user before the external installer applies it.
/// </summary>
/// <param name="Version">Version recorded in the pending marker.</param>
/// <param name="PackagePath">Full path to the verified package in the pending directory.</param>
/// <param name="ExpectedSha256">SHA-256 digest recorded at staging time.</param>
public sealed record PendingStartupUpdate(string Version, string PackagePath, string ExpectedSha256);

/// <summary>
/// Orchestrates source-aware updates for Haven. Chooses the Store provider for <see cref="InstallationSource.MicrosoftStore"/>,
/// treats <see cref="InstallationSource.Unknown"/> as direct while reporting the uncertainty, and runs direct checks/downloads/staging itself.
/// Timer-less by design: hosts call <c>CheckInBackgroundAsync</c> from their own tick; this component owns no threads.
/// Rollback safety: every byte this component writes lives under <c>{dataDirectory}/updates/staging</c> and <c>{dataDirectory}/updates/pending</c>;
/// the installation directory is never modified, so removing those directories fully rolls staging back.
/// </summary>
public sealed class UpdateOrchestrator(
    Func<InstallationInfo> installationDetector,
    IReadOnlyDictionary<InstallationSource, IUpdateProvider> providers,
    IUpdatePreferenceStore preferenceStore,
    Func<string> currentVersionProvider) : IUpdateService
{
    /// <summary>Name of the pending marker consumed by the external installer/bootstrapper on next start.</summary>
    public const string PendingMarkerFileName = "apply-on-next-start.json";

    private const string UpdatesRootFolderName = "updates";
    private const string PendingFolderName = "pending";

    private static readonly JsonSerializerOptions MarkerJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _preferencesGate = new(1, 1);

    private UpdatePreferences _preferences = new(true, UpdateChannel.Stable);
    private bool _preferencesLoaded;
    private UpdateState _state = UpdateState.Idle;
    private string? _message;
    private UpdateManifest? _available;
    private int? _downloadPercent;
    private string? _stagedPath;
    private string? _stagedSha256;
    private int _backgroundBusy;

    /// <inheritdoc />
    public event Action<UpdateStatusReport>? StatusChanged;

    /// <summary>Raised by <see cref="ApplyOnStartupCheck"/> when a previously staged update is detected at startup so the shell can prompt the user.</summary>
    public static event Action<UpdateStatusReport>? PendingUpdateDetectedOnStartup;

    /// <inheritdoc />
    public async Task<UpdatePreferences> GetPreferencesAsync(CancellationToken cancellationToken)
    {
        if (_preferencesLoaded)
        {
            return _preferences;
        }
        await _preferencesGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_preferencesLoaded)
            {
                _preferences = await preferenceStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                _preferencesLoaded = true;
            }
            return _preferences;
        }
        finally
        {
            _preferencesGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SetPreferencesAsync(UpdatePreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        await _preferencesGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await preferenceStore.SaveAsync(preferences, cancellationToken).ConfigureAwait(false);
            _preferences = preferences;
            _preferencesLoaded = true;
        }
        finally
        {
            _preferencesGate.Release();
        }
        RaiseStatus();
    }

    /// <inheritdoc />
    public async Task<UpdateStatusReport> GetStatusAsync(CancellationToken cancellationToken)
    {
        var preferences = await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
        var info = BuildSnapshot(out var availableVersion, out var percent, out var message, out var state);
        return ComposeReport(info.Source, preferences.PreferredChannel, availableVersion, state, percent, message);
    }

    /// <inheritdoc />
    public async Task CheckInBackgroundAsync(CancellationToken cancellationToken)
    {
        UpdatePreferences preferences;
        try
        {
            preferences = await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            PublishState(UpdateState.Failed, $"Background update check failed: {ex.Message}");
            return;
        }

        if (!preferences.BackgroundChecksEnabled)
        {
            PublishState(UpdateState.Idle, "Background update checks are disabled in preferences.");
            return;
        }

        if (Interlocked.CompareExchange(ref _backgroundBusy, 1, 0) != 0)
        {
            return;
        }
        try
        {
            await CheckCoreAsync(preferences, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishState(UpdateState.Idle, "Background update check cancelled.");
        }
        catch (Exception ex)
        {
            PublishState(UpdateState.Failed, $"Background update check failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _backgroundBusy, 0);
        }
    }

    /// <inheritdoc />
    public async Task DownloadUpdateAsync(IProgress<int>? progress, CancellationToken cancellationToken)
    {
        UpdateManifest manifest;
        lock (_stateLock)
        {
            manifest = _available ?? throw new InvalidOperationException("No update is available to download yet. Check for updates first.");
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IUpdateProvider provider;
            string sourceNote;
            lock (_stateLock)
            {
                if (!ReferenceEquals(_available, manifest))
                {
                    throw new InvalidOperationException("The available update changed while starting the download. Run the check again.");
                }
            }
            provider = ResolveProvider(out sourceNote);
            if (provider is StoreUpdateProvider)
            {
                throw StoreUpdateProvider.CreateNotSupportedForDownload();
            }

            PublishState(UpdateState.Downloading, $"Downloading version {manifest.Version}...");
            var relay = new ProgressRelay(percent =>
            {
                lock (_stateLock)
                {
                    _downloadPercent = percent;
                }
                progress?.Report(percent);
                RaiseStatus();
            });

            string stagedPath;
            try
            {
                stagedPath = await provider.DownloadAndStageAsync(manifest, relay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                PublishState(_available is null ? UpdateState.Idle : UpdateState.Available, "Download cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                PublishState(UpdateState.Failed, $"Download failed: {ex.Message}");
                throw;
            }

            lock (_stateLock)
            {
                _stagedPath = stagedPath;
                _stagedSha256 = manifest.Sha256;
                _downloadPercent = null;
            }
            PublishState(
                UpdateState.StagedPendingRestart,
                $"{sourceNote}Version {manifest.Version} is verified and staged. The external installer/bootstrapper applies it on the next start; Haven has not replaced its own binaries.".Trim());
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ApplyStagedUpdateAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var source = installationDetector();
            if (source.Source == InstallationSource.MicrosoftStore)
            {
                throw StoreUpdateProvider.CreateNotSupportedForDownload();
            }

            string stagedPath;
            string stagedSha256;
            string stagedVersion;
            lock (_stateLock)
            {
                if (_stagedPath is null || _stagedSha256 is null || _available is null)
                {
                    throw new InvalidOperationException("No staged update is ready to apply. Download and stage an update first.");
                }
                stagedPath = _stagedPath;
                stagedSha256 = _stagedSha256;
                stagedVersion = _available.Version;
            }

            if (!File.Exists(stagedPath))
            {
                PublishState(UpdateState.Failed, "The staged update package is missing from the pending directory.");
                throw new InvalidOperationException("The staged update package is missing from the pending directory.");
            }

            var actualSha256 = await ComputeSha256HexAsync(stagedPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualSha256, stagedSha256, StringComparison.Ordinal))
            {
                DeletePendingMarker(GetDataRootFromPackagePath(stagedPath));
                PublishState(UpdateState.Failed, "signature/hash verification failed for the staged package.");
                throw new InvalidDataException("signature/hash verification failed");
            }

            var dataDirectory = GetDataRootFromPackagePath(stagedPath);
            await WritePendingMarkerAsync(dataDirectory, new StartupMarker(stagedVersion, stagedPath, stagedSha256, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);

            PublishState(
                UpdateState.StagedPendingRestart,
                $"Update {stagedVersion} is marked for application on next start by the external installer/bootstrapper. Haven does not swap its own running binaries.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// Detects a pending staged update at startup and reports it through <see cref="PendingUpdateDetectedOnStartup"/>.
    /// Honest-staging contract: this only DETECTS and reports; the actual binary swap belongs to the external installer/bootstrapper flow.
    /// </summary>
    /// <param name="dataDirectory">App data directory containing <c>updates/pending</c>.</param>
    /// <param name="currentVersion">Version of the running executable.</param>
    /// <param name="source">Installation source used for the report.</param>
    /// <returns>The pending update when a valid marker plus package exist; otherwise <c>null</c>.</returns>
    public static PendingStartupUpdate? ApplyOnStartupCheck(string dataDirectory, string currentVersion, InstallationSource source = InstallationSource.DirectInstall)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);
        var markerPath = Path.Combine(GetPendingDirectory(dataDirectory), PendingMarkerFileName);
        if (!File.Exists(markerPath))
        {
            return null;
        }

        StartupMarker? marker;
        try
        {
            marker = JsonSerializer.Deserialize<StartupMarker>(File.ReadAllText(markerPath), MarkerJsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            QuarantineCorruptMarker(markerPath);
            return null;
        }

        if (marker is null || string.IsNullOrWhiteSpace(marker.Version) || string.IsNullOrWhiteSpace(marker.PackagePath) || string.IsNullOrWhiteSpace(marker.Sha256))
        {
            QuarantineCorruptMarker(markerPath);
            return null;
        }

        if (!File.Exists(marker.PackagePath))
        {
            TryDelete(markerPath);
            return null;
        }

        var report = new UpdateStatusReport(
            source,
            UpdateChannel.Stable,
            currentVersion,
            marker.Version,
            UpdateState.StagedPendingRestart,
            DownloadPercent: null,
            $"An update ({marker.Version}) was staged before the last session and is waiting for the external installer/bootstrapper to apply it on next start.",
            StoreManaged: false);
        PendingUpdateDetectedOnStartup?.Invoke(report);
        return new PendingStartupUpdate(marker.Version, marker.PackagePath, marker.Sha256);
    }

    /// <summary>
    /// Gets the full path of the pending directory for an app data directory; shared with the external installer/bootstrapper contract.
    /// </summary>
    public static string GetPendingDirectory(string dataDirectory) =>
        Path.Combine(dataDirectory, UpdatesRootFolderName, PendingFolderName);

    /// <summary>
    /// Performs check core asynchronous so policy stays in one place for both manual and background checks.
    /// </summary>
    private async Task CheckCoreAsync(UpdatePreferences preferences, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var provider = ResolveProvider(out var sourceNote);
            if (provider is HavenDirectUpdateProvider direct)
            {
                direct.Channel = preferences.PreferredChannel;
            }

            lock (_stateLock)
            {
                _message = $"{sourceNote}Checking for updates...";
            }
            RaiseStatus();

            var manifest = await provider.CheckForUpdateAsync(currentVersionProvider(), cancellationToken).ConfigureAwait(false);
            if (manifest is null)
            {
                var detectedSource = installationDetector().Source;
                lock (_stateLock)
                {
                    _available = null;
                    _state = UpdateState.UpToDate;
                    _message = detectedSource == InstallationSource.MicrosoftStore
                        ? $"{StoreUpdateProvider.ManagedByStoreMessage} Check the Store library for new releases."
                        : $"{sourceNote}You are up to date.";
                }
            }
            else
            {
                lock (_stateLock)
                {
                    _available = manifest;
                    _state = UpdateState.Available;
                    _message = $"{sourceNote}Version {manifest.Version} is available for download.";
                }
            }
            RaiseStatus();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// Resolves the provider for the detected installation source; Unknown is treated as direct install while reporting the uncertainty honestly.
    /// </summary>
    private IUpdateProvider ResolveProvider(out string sourceNote)
    {
        var info = installationDetector();
        if (info.Source == InstallationSource.Unknown && providers.TryGetValue(InstallationSource.DirectInstall, out var unconfirmed))
        {
            sourceNote = "Installation source unconfirmed; treating as direct install. ";
            return unconfirmed;
        }
        if (providers.TryGetValue(info.Source, out var provider))
        {
            sourceNote = info.Source == InstallationSource.Unknown ? "Installation source unconfirmed; treating as direct install. " : string.Empty;
            return provider;
        }
        if (providers.TryGetValue(InstallationSource.DirectInstall, out var fallback))
        {
            sourceNote = $"No provider registered for source '{info.Source}'; falling back to direct install. ";
            return fallback;
        }
        throw new InvalidOperationException($"No update provider is registered for installation source '{info.Source}'.");
    }

    /// <summary>
    /// Builds a thread-safe snapshot of the mutable lifecycle state.
    /// </summary>
    private InstallationInfo BuildSnapshot(out string? availableVersion, out int? percent, out string? message, out UpdateState state)
    {
        lock (_stateLock)
        {
            availableVersion = _available?.Version;
            percent = _downloadPercent;
            message = _message;
            state = _state;
        }
        return installationDetector();
    }

    /// <summary>
    /// Publishes a pure state transition with no availability change.
    /// </summary>
    private void PublishState(UpdateState state, string message)
    {
        lock (_stateLock)
        {
            _state = state;
            _message = message;
            if (state != UpdateState.Downloading && state != UpdateState.StagedPendingRestart)
            {
                _downloadPercent = null;
            }
        }
        RaiseStatus();
    }

    /// <summary>
    /// Composes the immutable report handed to callers and listeners.
    /// </summary>
    private UpdateStatusReport ComposeReport(InstallationSource source, UpdateChannel channel, string? availableVersion, UpdateState state, int? percent, string? message) => new(
        source,
        channel,
        currentVersionProvider(),
        availableVersion,
        state,
        percent,
        message,
        source == InstallationSource.MicrosoftStore);

    /// <summary>
    /// Raises StatusChanged outside every lock so handlers can safely call back into this instance.
    /// </summary>
    private void RaiseStatus()
    {
        var handler = StatusChanged;
        if (handler is null)
        {
            return;
        }
        InstallationInfo info;
        UpdatePreferences preferences;
        string? availableVersion;
        int? percent;
        string? message;
        UpdateState state;
        lock (_stateLock)
        {
            preferences = _preferencesLoaded ? _preferences : new UpdatePreferences(true, UpdateChannel.Stable);
            availableVersion = _available?.Version;
            percent = _downloadPercent;
            message = _message;
            state = _state;
        }
        info = installationDetector();
        handler.Invoke(ComposeReport(info.Source, preferences.PreferredChannel, availableVersion, state, percent, message));
    }

    /// <summary>
    /// Writes the pending marker atomically (temp file plus move) so the installer never reads a torn file.
    /// </summary>
    private static async Task WritePendingMarkerAsync(string dataDirectory, StartupMarker marker, CancellationToken cancellationToken)
    {
        var pendingDirectory = GetPendingDirectory(dataDirectory);
        Directory.CreateDirectory(pendingDirectory);
        var markerPath = Path.Combine(pendingDirectory, PendingMarkerFileName);
        var tempPath = markerPath + $".tmp-{Guid.NewGuid():n}";
        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(marker, MarkerJsonOptions), cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, markerPath, overwrite: true);
    }

    /// <summary>
    /// Deletes the pending marker; absence of the marker means nothing is queued for the installer.
    /// </summary>
    private static void DeletePendingMarker(string dataDirectory)
    {
        TryDelete(Path.Combine(GetPendingDirectory(dataDirectory), PendingMarkerFileName));
    }

    /// <summary>
    /// Derives the app data root back from a staged package path (<c>{root}/updates/pending/file.zip</c>) so markers land beside their packages.
    /// </summary>
    private static string GetDataRootFromPackagePath(string packagePath)
    {
        var pendingDirectory = Path.GetDirectoryName(packagePath);
        if (pendingDirectory is null)
        {
            throw new InvalidOperationException($"Staged package path '{packagePath}' has no pending directory.");
        }
        var updatesDirectory = Path.GetDirectoryName(pendingDirectory);
        if (updatesDirectory is null)
        {
            throw new InvalidOperationException($"Pending directory '{pendingDirectory}' has no parent updates directory.");
        }
        return Path.GetDirectoryName(updatesDirectory)
            ?? throw new InvalidOperationException($"Updates directory '{updatesDirectory}' has no data root.");
    }

    /// <summary>
    /// Renames an unreadable marker aside instead of deleting it, so corruption stays visible on disk.
    /// </summary>
    private static void QuarantineCorruptMarker(string markerPath)
    {
        try
        {
            File.Move(markerPath, $"{markerPath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }

    /// <summary>
    /// Performs try delete without letting housekeeping failures mask the primary result.
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

    /// <summary>
    /// Streams a file through SHA-256 and returns the lowercase hex digest.
    /// </summary>
    private static async Task<string> ComputeSha256HexAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Relays download percentages into the orchestrator's reported state and onward to the caller's progress sink.
    /// </summary>
    private sealed class ProgressRelay(Action<int> onPercent) : IProgress<int>
    {
        /// <inheritdoc />
        public void Report(int value) => onPercent(value);
    }

    /// <summary>
    /// Serialized form of the pending marker file; property names are part of the installer contract.
    /// </summary>
    private sealed record StartupMarker(string Version, string PackagePath, string Sha256, DateTimeOffset StagedAtUtc);
}
