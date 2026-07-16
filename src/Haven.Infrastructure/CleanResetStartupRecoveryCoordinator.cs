using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Keeps the durable crash evidence while Haven is running, then removes the crash
/// history only after the application has completed its coordinated clean shutdown.
/// A process crash never reaches this reset path.
/// </summary>
public sealed class CleanResetStartupRecoveryCoordinator(
    StartupRecoveryCoordinator inner,
    IAppPaths paths,
    IProductionDiagnostics diagnostics) : IStartupRecoveryCoordinator
{
    public StartupRecoveryState Current => inner.Current;

    public Task<StartupRecoveryState> BeginStartupAsync(CancellationToken cancellationToken) =>
        inner.BeginStartupAsync(cancellationToken);

    public Task MarkStartupCompletedAsync(CancellationToken cancellationToken) =>
        inner.MarkStartupCompletedAsync(cancellationToken);

    public async Task MarkCleanShutdownAsync(CancellationToken cancellationToken)
    {
        await inner.MarkCleanShutdownAsync(cancellationToken).ConfigureAwait(false);
        var statePath = Path.Combine(paths.DataDirectory, "startup-recovery.json");
        var backupPath = statePath + ".bak";
        try
        {
            if (File.Exists(statePath)) File.Delete(statePath);
            if (File.Exists(backupPath)) File.Delete(backupPath);
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "startup",
                "crash-history-reset",
                "Crash-loop history was cleared after a confirmed clean shutdown.",
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Warning,
                "startup",
                "crash-history-reset-failed",
                "Haven completed shutdown but could not clear the crash-loop state file.",
                new Dictionary<string, string> { ["exceptionType"] = ex.GetType().Name },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
