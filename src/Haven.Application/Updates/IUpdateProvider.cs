/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Updates/IUpdateProvider.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns IUpdateProvider. Read the member comments below as a map of each responsibility.
 * How: Public members form the callable contract; implementations in Haven.Infrastructure supply Store-managed and direct-install behaviour; cancellation flows through every asynchronous member.
 * Why: The orchestrator must stay ignorant of whether the Microsoft Store or Haven's own staging pipeline owns an update, so policy remains testable and platform details stay replaceable.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Defines one update source backend (Microsoft Store-managed or direct download) so callers depend on a capability rather than one implementation.
/// </summary>
public interface IUpdateProvider
{
    /// <summary>
    /// Gets a baseline status snapshot from this provider. Implementations return synchronously available facts only and never perform network or file I/O.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled.</param>
    /// <returns>The provider's baseline report; the owning service may overlay richer lifecycle state on top.</returns>
    Task<UpdateStatusReport> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks this source for a release newer than <paramref name="currentVersion"/>.
    /// </summary>
    /// <param name="currentVersion">Version string of the running executable; compared with the offered manifest version.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled.</param>
    /// <returns>The newer <see cref="UpdateManifest"/>, or <c>null</c> when no newer release is offered (which for the Store provider also means the Store owns availability).</returns>
    Task<UpdateManifest?> CheckForUpdateAsync(string currentVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads the package described by <paramref name="manifest"/> into the provider's staging area, verifies its SHA-256 digest while streaming, and returns the staged package path.
    /// </summary>
    /// <param name="manifest">The validated manifest previously returned by <see cref="CheckForUpdateAsync"/>.</param>
    /// <param name="progress">Optional receiver of 0-100 download percentages.</param>
    /// <param name="cancellationToken">Propagates notification that operations should be cancelled; partial downloads are cleaned up.</param>
    /// <returns>Full path to the verified staged package.</returns>
    /// <exception cref="NotSupportedException">When this source never downloads binaries itself (Store-managed installs).</exception>
    /// <exception cref="InvalidDataException">When hash or size verification fails; no partial file survives.</exception>
    Task<string> DownloadAndStageAsync(UpdateManifest manifest, IProgress<int>? progress, CancellationToken cancellationToken);
}
