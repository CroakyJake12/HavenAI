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
    public StartupRecoveryState Current => inner.Current;

    public Task<StartupRecoveryState> BeginStartupAsync(CancellationToken cancellationToken) =>
        inner.BeginStartupAsync(cancellationToken);

    public Task MarkStartupCompletedAsync(CancellationToken cancellationToken) =>
        inner.MarkStartupCompletedAsync(cancellationToken);

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
