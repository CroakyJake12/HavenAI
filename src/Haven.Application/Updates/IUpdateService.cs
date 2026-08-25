/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Updates/IUpdateService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns IUpdateService and IUpdatePreferenceStore. Read the member comments below as a map of each responsibility.
 * How: Public members form the callable contract; the orchestrator implementation combines an installation detector with per-source providers; preference persistence goes through <see cref="IUpdatePreferenceStore"/>.
 * Why: The shell and settings UI need one honest update facade regardless of installation source, and preferences must survive restarts through the versioned settings store.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file. Never let a background path throw or fake success.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Defines persistence for <see cref="UpdatePreferences"/> so the orchestrator depends on a capability rather than one storage detail.
/// </summary>
public interface IUpdatePreferenceStore
{
    /// <summary>Loads stored preferences, returning sensible defaults when nothing (or nothing parseable) is stored.</summary>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled.</param>
    Task<UpdatePreferences> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Persists the given preferences durably.</summary>
    /// <param name="preferences">The preference snapshot to persist; never <c>null</c>.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled.</param>
    Task SaveAsync(UpdatePreferences preferences, CancellationToken cancellationToken);
}

/// <summary>
/// Orchestrates source-aware updates: detects whether this install is Store-managed or direct, routes checks/downloads/staging to the matching provider, persists preferences, and reports every state change honestly.
/// </summary>
public interface IUpdateService
{
    /// <summary>Raised on every status transition so the shell can reflect progress without polling.</summary>
    event Action<UpdateStatusReport>? StatusChanged;

    /// <summary>
    /// Gets a report of current update state, including detected installation source, effective channel, lifecycle state and an honest message (including detection caveats).
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled.</param>
    Task<UpdateStatusReport> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs one background check cycle. Honest no-throw: every failure (including network errors) is caught, converted to a <see cref="UpdateState.Failed"/> report raised via <see cref="StatusChanged"/>, and swallowed only in that explicit sense. Designed to be called from a host tick; owns no threads or timers.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled; cancellation is reported as a cancelled check, not a failure.</param>
    Task CheckInBackgroundAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Downloads the previously discovered update into staging with streamed SHA-256 verification.
    /// </summary>
    /// <param name="progress">Optional receiver of 0-100 download percentages.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled; partial downloads are cleaned up.</param>
    /// <exception cref="InvalidOperationException">When no check has found an update to download.</exception>
    /// <exception cref="NotSupportedException">When the install is Store-managed; the caller converts this into an honest UI state pointing at the Store.</exception>
    Task DownloadUpdateAsync(IProgress<int>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Completes staging for direct installs: re-verifies the staged package hash and writes the pending marker consumed by the external installer/bootstrapper on next start. Store-managed installs are refused.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled.</param>
    /// <exception cref="InvalidOperationException">When no package is staged.</exception>
    /// <exception cref="NotSupportedException">When the install is Store-managed.</exception>
    /// <exception cref="InvalidDataException">When the staged package no longer matches its recorded SHA-256 digest.</exception>
    Task ApplyStagedUpdateAsync(CancellationToken cancellationToken);

    /// <summary>Gets the currently cached preferences, loading them from the store on first use. Async counterpart of a getter because the backing store is asynchronous.</summary>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled.</param>
    Task<UpdatePreferences> GetPreferencesAsync(CancellationToken cancellationToken);

    /// <summary>Sets new preferences: caches them immediately and persists them through <see cref="IUpdatePreferenceStore"/>. Async counterpart of a setter because the backing store is asynchronous.</summary>
    /// <param name="preferences">New preference snapshot; never <c>null</c>.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled.</param>
    Task SetPreferencesAsync(UpdatePreferences preferences, CancellationToken cancellationToken);
}
