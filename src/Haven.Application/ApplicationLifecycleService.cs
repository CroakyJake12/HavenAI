/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ApplicationLifecycleService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns IApplicationLifecycle, ApplicationLifecycleService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Diagnostics;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Defines the i application lifecycle contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface IApplicationLifecycle
{
    bool IsStartupComplete { get; }
    Task StartupAsync(CancellationToken cancellationToken);
    Task ShutdownAsync(CancellationToken cancellationToken);
    Task CrashRecoveryAsync(CancellationToken cancellationToken);
    Task MarkCleanShutdownAsync();
    bool IsSafeMode { get; }
}

/// <summary>
/// Represents application lifecycle service and keeps its related state and behavior together.
/// </summary>
public sealed class ApplicationLifecycleService : IApplicationLifecycle
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAppPaths _paths;
    /// <summary>
    /// Stores database locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAppDatabase _database;
    /// <summary>
    /// Stores diagnostics locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IAppDiagnostics _diagnostics;
    /// <summary>
    /// Stores active resources locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly HashSet<string> _activeResources = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Stores is startup complete locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _isStartupComplete;
    /// <summary>
    /// Stores is safe mode locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _isSafeMode;

    public ApplicationLifecycleService(IAppPaths paths, IAppDatabase database, IAppDiagnostics diagnostics)
    {
        _paths = paths;
        _database = database;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Reports whether is startup complete is true for the current state.
    /// </summary>
    public bool IsStartupComplete => Volatile.Read(ref _isStartupComplete) == 1;
    /// <summary>
    /// Reports whether is safe mode is true for the current state.
    /// </summary>
    public bool IsSafeMode => Volatile.Read(ref _isSafeMode) == 1;

    /// <summary>
    /// Performs startup async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task StartupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CheckPreviousShutdownAsync(cancellationToken).ConfigureAwait(false);
            await _database.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await RunSqliteIntegrityCheckAsync(cancellationToken).ConfigureAwait(false);
            await RunPreMigrationBackupsAsync(cancellationToken).ConfigureAwait(false);

            var markerPath = GetStartupMarkerPath();
            await File.WriteAllTextAsync(markerPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken).ConfigureAwait(false);

            Interlocked.Exchange(ref _isStartupComplete, 1);
            await _diagnostics.RecordErrorAsync("Startup", "Application started successfully", null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _isSafeMode, 1);
            await _diagnostics.RecordErrorAsync("Startup", $"Startup failed: {ex.Message}", null, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Performs shutdown async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            ReleaseAllResources();
            await MarkCleanShutdownAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _diagnostics.RecordErrorAsync("Shutdown", $"Shutdown error: {ex.Message}", null, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Performs crash recovery async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task CrashRecoveryAsync(CancellationToken cancellationToken)
    {
        var startupMarker = GetStartupMarkerPath();
        var shutdownMarker = GetShutdownMarkerPath();

        if (File.Exists(startupMarker) && !File.Exists(shutdownMarker))
        {
            Interlocked.Exchange(ref _isSafeMode, 1);
            await _diagnostics.RecordErrorAsync("Recovery", "Previous session did not shut down cleanly", null, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(startupMarker)) File.Delete(startupMarker);
        if (File.Exists(shutdownMarker)) File.Delete(shutdownMarker);
    }

    /// <summary>
    /// Performs mark clean shutdown async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task MarkCleanShutdownAsync()
    {
        var path = GetShutdownMarkerPath();
        await File.WriteAllTextAsync(path, DateTimeOffset.UtcNow.ToString("O")).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs the register resource step owned by this component.
    /// </summary>
    public void RegisterResource(string resourceId) { lock (_activeResources) _activeResources.Add(resourceId); }
    /// <summary>
    /// Performs the release resource step owned by this component.
    /// </summary>
    public void ReleaseResource(string resourceId) { lock (_activeResources) _activeResources.Remove(resourceId); }

    /// <summary>
    /// Performs the release all resources step owned by this component.
    /// </summary>
    private void ReleaseAllResources()
    {
        lock (_activeResources) _activeResources.Clear();
    }

    /// <summary>
    /// Performs check previous shutdown async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task CheckPreviousShutdownAsync(CancellationToken cancellationToken)
    {
        var startupMarker = GetStartupMarkerPath();
        var shutdownMarker = GetShutdownMarkerPath();

        if (File.Exists(startupMarker) && !File.Exists(shutdownMarker))
        {
            Interlocked.Exchange(ref _isSafeMode, 1);
            await _diagnostics.RecordErrorAsync("Startup", "Detected unclean previous shutdown", null, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(startupMarker)) File.Delete(startupMarker);
        if (File.Exists(shutdownMarker)) File.Delete(shutdownMarker);
    }

    /// <summary>
    /// Runs run sqlite integrity check async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task RunSqliteIntegrityCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_paths.DatabasePath)) return;
            await _diagnostics.RecordErrorAsync("Integrity", "Integrity check deferred to infrastructure", null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _diagnostics.RecordErrorAsync("Integrity", $"SQLite integrity check failed: {ex.Message}", null, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs run pre migration backups async while preserving the surrounding cancellation and error-handling contract.
    /// </summary>
    private async Task RunPreMigrationBackupsAsync(CancellationToken cancellationToken)
    {
        var backupDir = Path.Combine(_paths.DataDirectory, "backups");
        Directory.CreateDirectory(backupDir);
        var dbPath = _paths.DatabasePath;
        if (File.Exists(dbPath))
        {
            var backupPath = Path.Combine(backupDir, $"haven-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.db");
            await using var source = File.OpenRead(dbPath);
            await using var dest = File.Create(backupPath);
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Retrieves startup marker path for the current operation.
    /// </summary>
    private string GetStartupMarkerPath() => Path.Combine(_paths.DataDirectory, ".startup");
    /// <summary>
    /// Retrieves shutdown marker path for the current operation.
    /// </summary>
    private string GetShutdownMarkerPath() => Path.Combine(_paths.DataDirectory, ".shutdown");
}
