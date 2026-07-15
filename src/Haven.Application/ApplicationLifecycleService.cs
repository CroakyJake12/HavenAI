using System.Diagnostics;
using Haven.Core;

namespace Haven.Application;

public interface IApplicationLifecycle
{
    bool IsStartupComplete { get; }
    Task StartupAsync(CancellationToken cancellationToken);
    Task ShutdownAsync(CancellationToken cancellationToken);
    Task CrashRecoveryAsync(CancellationToken cancellationToken);
    Task MarkCleanShutdownAsync();
    bool IsSafeMode { get; }
}

public sealed class ApplicationLifecycleService : IApplicationLifecycle
{
    private readonly IAppPaths _paths;
    private readonly IAppDatabase _database;
    private readonly IAppDiagnostics _diagnostics;
    private readonly HashSet<string> _activeResources = new(StringComparer.OrdinalIgnoreCase);
    private int _isStartupComplete;
    private int _isSafeMode;

    public ApplicationLifecycleService(IAppPaths paths, IAppDatabase database, IAppDiagnostics diagnostics)
    {
        _paths = paths;
        _database = database;
        _diagnostics = diagnostics;
    }

    public bool IsStartupComplete => Volatile.Read(ref _isStartupComplete) == 1;
    public bool IsSafeMode => Volatile.Read(ref _isSafeMode) == 1;

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

    public async Task MarkCleanShutdownAsync()
    {
        var path = GetShutdownMarkerPath();
        await File.WriteAllTextAsync(path, DateTimeOffset.UtcNow.ToString("O")).ConfigureAwait(false);
    }

    public void RegisterResource(string resourceId) { lock (_activeResources) _activeResources.Add(resourceId); }
    public void ReleaseResource(string resourceId) { lock (_activeResources) _activeResources.Remove(resourceId); }

    private void ReleaseAllResources()
    {
        lock (_activeResources) _activeResources.Clear();
    }

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

    private string GetStartupMarkerPath() => Path.Combine(_paths.DataDirectory, ".startup");
    private string GetShutdownMarkerPath() => Path.Combine(_paths.DataDirectory, ".shutdown");
}
