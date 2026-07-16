namespace Haven.Application;

public enum ReliabilitySeverity
{
    Trace = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

public sealed record ReliabilityEvent(
    DateTimeOffset Timestamp,
    ReliabilitySeverity Severity,
    string Component,
    string EventName,
    string Message,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Data);

public sealed record DatabaseHealthReport(
    bool IsHealthy,
    int SchemaVersion,
    IReadOnlyList<string> IntegrityMessages,
    IReadOnlyList<string> ForeignKeyViolations,
    DateTimeOffset CheckedAt);

public sealed record DatabaseBackupInfo(
    string DatabasePath,
    string ManifestPath,
    int FromVersion,
    int ToVersion,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CreatedAt);

public sealed record StartupRecoveryState(
    bool IsSafeMode,
    int RecentUncleanStarts,
    string RunId,
    string Reason,
    DateTimeOffset StartedAt);

public interface IProductionDiagnostics : IAsyncDisposable
{
    ValueTask WriteAsync(
        ReliabilitySeverity severity,
        string component,
        string eventName,
        string message,
        IReadOnlyDictionary<string, string>? data = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken);
}

public interface IDatabaseMaintenance
{
    Task<DatabaseBackupInfo?> PrepareForMigrationAsync(int targetVersion, CancellationToken cancellationToken);
    Task<DatabaseHealthReport> VerifyIntegrityAsync(CancellationToken cancellationToken);
}

public interface IStartupRecoveryCoordinator
{
    StartupRecoveryState Current { get; }
    Task<StartupRecoveryState> BeginStartupAsync(CancellationToken cancellationToken);
    Task MarkStartupCompletedAsync(CancellationToken cancellationToken);
    Task MarkCleanShutdownAsync(CancellationToken cancellationToken);
}

public interface IDiagnosticsBundleService
{
    Task<string> CreateBundleAsync(string destinationDirectory, CancellationToken cancellationToken);
}

/// <summary>
/// Process-wide fail-closed state used by runtime policy gates. It is set only by
/// startup recovery before user/model work begins and reset after a clean restart.
/// </summary>
public static class RuntimeSafetyState
{
    private static int _safeMode;
    private static string _reason = string.Empty;

    public static bool IsSafeMode => Volatile.Read(ref _safeMode) == 1;
    public static string Reason => Volatile.Read(ref _reason) ?? string.Empty;

    public static void EnableSafeMode(string reason)
    {
        Volatile.Write(ref _reason, string.IsNullOrWhiteSpace(reason) ? "Crash-loop recovery safe mode is active." : reason.Trim());
        Volatile.Write(ref _safeMode, 1);
    }

    public static void DisableSafeMode()
    {
        Volatile.Write(ref _safeMode, 0);
        Volatile.Write(ref _reason, string.Empty);
    }
}
