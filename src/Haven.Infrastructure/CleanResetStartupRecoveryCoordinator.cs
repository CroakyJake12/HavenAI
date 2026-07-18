/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/CleanResetStartupRecoveryCoordinator.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns CleanResetStartupRecoveryCoordinator. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Keeps durable crash evidence while Haven is running. The desktop calls the clean
/// reset only after every registered service has disposed successfully, so a crash or
/// disposal failure cannot be mistaken for a clean application exit.
/// </summary>
public sealed class CleanResetStartupRecoveryCoordinator(
    StartupRecoveryCoordinator inner,
    IAppPaths paths) : IStartupRecoveryCoordinator
{
    /// <summary>
    /// Gets or updates current, the bindable or domain state represented by this property.
    /// </summary>
    public StartupRecoveryState Current => inner.Current;

    /// <summary>
    /// Performs begin startup async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<StartupRecoveryState> BeginStartupAsync(CancellationToken cancellationToken) =>
        inner.BeginStartupAsync(cancellationToken);

    /// <summary>
    /// Performs mark startup completed async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task MarkStartupCompletedAsync(CancellationToken cancellationToken) =>
        inner.MarkStartupCompletedAsync(cancellationToken);

    /// <summary>
    /// Performs mark clean shutdown async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task MarkCleanShutdownAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var statePath = Path.Combine(paths.DataDirectory, "startup-recovery.json");
        var backupPath = statePath + ".bak";
        try
        {
            if (File.Exists(statePath)) File.Delete(statePath);
            if (File.Exists(backupPath)) File.Delete(backupPath);
            RuntimeSafetyState.DisableSafeMode();
            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Haven disposed its services but could not clear the crash-loop state. The next launch may enter recovery safe mode.", ex);
        }
    }
}
