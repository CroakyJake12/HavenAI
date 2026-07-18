/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ProductionReliabilityAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ReliabilitySeverity, ReliabilityEvent, DatabaseHealthReport, DatabaseBackupInfo, ManagedDatabaseBackup, PendingDatabaseRestore, DatabaseRestoreResult, StartupRecoveryState, RecoverySafetyAssessment, IProductionDiagnostics, IDatabaseMaintenance, IDatabaseRestoreService, IStartupRecoveryCoordinator, IRecoverySafetyProbe, IDiagnosticsBundleService, RuntimeSafetyState. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Application;

/// <summary>
/// Lists the supported reliability severity values used to make state explicit and type-safe.
/// </summary>
public enum ReliabilitySeverity
{
    Trace = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

/// <summary>
/// Represents reliability event and keeps its related state and behavior together.
/// </summary>
public sealed record ReliabilityEvent(
    DateTimeOffset Timestamp,
    ReliabilitySeverity Severity,
    string Component,
    string EventName,
    string Message,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Data);

/// <summary>
/// Represents database health report and keeps its related state and behavior together.
/// </summary>
public sealed record DatabaseHealthReport(
    bool IsHealthy,
    int SchemaVersion,
    IReadOnlyList<string> IntegrityMessages,
    IReadOnlyList<string> ForeignKeyViolations,
    DateTimeOffset CheckedAt);

/// <summary>
/// Represents database backup info and keeps its related state and behavior together.
/// </summary>
public sealed record DatabaseBackupInfo(
    string DatabasePath,
    string ManifestPath,
    int FromVersion,
    int ToVersion,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents managed database backup and keeps its related state and behavior together.
/// </summary>
public sealed record ManagedDatabaseBackup(
    string FileName,
    string DatabasePath,
    string ManifestPath,
    int FromVersion,
    int ToVersion,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CreatedAt,
    bool IsVerified,
    string VerificationMessage);

/// <summary>
/// Represents pending database restore and keeps its related state and behavior together.
/// </summary>
public sealed record PendingDatabaseRestore(
    string BackupFileName,
    string Sha256,
    DateTimeOffset RequestedAt,
    bool IsPending,
    string Message);

/// <summary>
/// Represents database restore result and keeps its related state and behavior together.
/// </summary>
public sealed record DatabaseRestoreResult(
    string BackupFileName,
    string EmergencyBackupPath,
    int RestoredSchemaVersion,
    DateTimeOffset RestoredAt,
    string Message);

/// <summary>
/// Represents startup recovery state and keeps its related state and behavior together.
/// </summary>
public sealed record StartupRecoveryState(
    bool IsSafeMode,
    int RecentUncleanStarts,
    string RunId,
    string Reason,
    DateTimeOffset StartedAt);

/// <summary>
/// Represents recovery safety assessment and keeps its related state and behavior together.
/// </summary>
public sealed record RecoverySafetyAssessment(
    bool IsSafeMode,
    bool StateWasReadable,
    int RecentUncleanStarts,
    string Reason,
    DateTimeOffset AssessedAt);

/// <summary>
/// Defines the i production diagnostics contract so callers depend on a capability rather than one implementation.
/// </summary>
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

/// <summary>
/// Defines the i database maintenance contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IDatabaseMaintenance
{
    Task<DatabaseBackupInfo?> PrepareForMigrationAsync(int targetVersion, CancellationToken cancellationToken);
    Task<DatabaseHealthReport> VerifyIntegrityAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Defines the i database restore service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IDatabaseRestoreService
{
    Task<IReadOnlyList<ManagedDatabaseBackup>> GetBackupsAsync(CancellationToken cancellationToken);
    Task<PendingDatabaseRestore?> GetPendingAsync(CancellationToken cancellationToken);
    Task<PendingDatabaseRestore> RequestRestoreAsync(string backupFileName, CancellationToken cancellationToken);
    Task CancelPendingAsync(CancellationToken cancellationToken);
    Task<DatabaseRestoreResult?> ApplyPendingRestoreAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Defines the i startup recovery coordinator contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IStartupRecoveryCoordinator
{
    StartupRecoveryState Current { get; }
    Task<StartupRecoveryState> BeginStartupAsync(CancellationToken cancellationToken);
    Task MarkStartupCompletedAsync(CancellationToken cancellationToken);
    Task MarkCleanShutdownAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Defines the i recovery safety probe contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IRecoverySafetyProbe
{
    Task<RecoverySafetyAssessment> AssessAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Defines the i diagnostics bundle service contract so callers depend on a capability rather than one implementation.
/// </summary>
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
    /// <summary>
    /// Stores safe mode locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static int _safeMode;
    /// <summary>
    /// Stores reason locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static string _reason = string.Empty;

    /// <summary>
    /// Reports whether is safe mode is true for the current state.
    /// </summary>
    public static bool IsSafeMode => Volatile.Read(ref _safeMode) == 1;
    /// <summary>
    /// Gets or updates reason, the bindable or domain state represented by this property.
    /// </summary>
    public static string Reason => Volatile.Read(ref _reason) ?? string.Empty;

    /// <summary>
    /// Performs the enable safe mode step owned by this component.
    /// </summary>
    public static void EnableSafeMode(string reason)
    {
        Volatile.Write(ref _reason, string.IsNullOrWhiteSpace(reason) ? "Crash-loop recovery safe mode is active." : reason.Trim());
        Volatile.Write(ref _safeMode, 1);
    }

    /// <summary>
    /// Performs the disable safe mode step owned by this component.
    /// </summary>
    public static void DisableSafeMode()
    {
        Volatile.Write(ref _safeMode, 0);
        Volatile.Write(ref _reason, string.Empty);
    }
}
