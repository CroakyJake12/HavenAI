/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/DatabaseRestoreService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns DatabaseRestoreService, PendingRestoreFile, BackupManifest. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>
/// Represents database restore service and keeps its related state and behavior together.
/// </summary>
public sealed class DatabaseRestoreService(
    IAppPaths paths,
    IProductionDiagnostics diagnostics) : IDatabaseRestoreService
{
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>
    /// Stores backup directory locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _backupDirectory = Path.Combine(paths.DataDirectory, "Backups");
    /// <summary>
    /// Stores pending path locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _pendingPath = Path.Combine(paths.DataDirectory, "pending-database-restore.json");

    /// <summary>
    /// Retrieves backups async for the current operation.
    /// </summary>
    public async Task<IReadOnlyList<ManagedDatabaseBackup>> GetBackupsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await GetBackupsCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Retrieves pending async for the current operation.
    /// </summary>
    public async Task<PendingDatabaseRestore?> GetPendingAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ReadPendingCoreAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Performs request restore asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<PendingDatabaseRestore> RequestRestoreAsync(string backupFileName, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalizedName = NormalizeFileName(backupFileName);
            var backups = await GetBackupsCoreAsync(cancellationToken).ConfigureAwait(false);
            var backup = backups.FirstOrDefault(item => item.FileName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
                         ?? throw new FileNotFoundException("The selected managed Haven backup no longer exists.", normalizedName);
            if (!backup.IsVerified)
                throw new InvalidDataException("The selected backup failed verification and cannot be scheduled for restore: " + backup.VerificationMessage);

            var pending = new PendingRestoreFile(1, backup.FileName, backup.Sha256, DateTimeOffset.UtcNow);
            await WriteAtomicJsonAsync(_pendingPath, pending, cancellationToken).ConfigureAwait(false);
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Warning,
                "database",
                "restore-requested",
                "A verified database restore was scheduled for the next Haven launch.",
                new Dictionary<string, string>
                {
                    ["backupFileName"] = backup.FileName,
                    ["sha256"] = backup.Sha256
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return new PendingDatabaseRestore(backup.FileName, backup.Sha256, pending.RequestedAt, true, "Verified restore scheduled for the next launch.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reports whether cancel pending async is true for the current state.
    /// </summary>
    public async Task CancelPendingAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDelete(_pendingPath);
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "database",
                "restore-cancelled",
                "The pending database restore was cancelled.",
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs apply pending restore asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<DatabaseRestoreResult?> ApplyPendingRestoreAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pending = await ReadPendingFileCoreAsync(cancellationToken).ConfigureAwait(false);
            if (pending is null) return null;
            var backup = (await GetBackupsCoreAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => item.FileName.Equals(pending.BackupFileName, StringComparison.OrdinalIgnoreCase));
            if (backup is null || !backup.IsVerified || !backup.Sha256.Equals(pending.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                QuarantinePending("verification-failed");
                throw new InvalidDataException("The pending database backup no longer matches its verified restore request. The request was quarantined and was not applied.");
            }

            Directory.CreateDirectory(paths.DataDirectory);
            Directory.CreateDirectory(_backupDirectory);
            SqliteConnection.ClearAllPools();
            var emergencyBackup = string.Empty;
            var staged = paths.DatabasePath + ".restore-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                if (File.Exists(paths.DatabasePath) && new FileInfo(paths.DatabasePath).Length > 0)
                    emergencyBackup = await CreateEmergencyBackupAsync(backup.FromVersion, cancellationToken).ConfigureAwait(false);

                await CopyDurablyAsync(backup.DatabasePath, staged, cancellationToken).ConfigureAwait(false);
                var stagedVerification = await VerifyDatabaseAsync(staged, cancellationToken).ConfigureAwait(false);
                if (!stagedVerification.IsHealthy)
                    throw new InvalidDataException("The staged restore database failed integrity verification.");

                await CheckpointCurrentDatabaseAsync(cancellationToken).ConfigureAwait(false);
                TryDelete(paths.DatabasePath + "-wal");
                TryDelete(paths.DatabasePath + "-shm");
                if (File.Exists(paths.DatabasePath))
                    await ReplaceFileWithRetryAsync(staged, paths.DatabasePath, cancellationToken).ConfigureAwait(false);
                else
                    File.Move(staged, paths.DatabasePath);

                var restored = await VerifyDatabaseAsync(paths.DatabasePath, cancellationToken).ConfigureAwait(false);
                if (!restored.IsHealthy)
                    throw new InvalidDataException("The restored Haven database failed its final integrity verification.");

                TryDelete(_pendingPath);
                await diagnostics.WriteAsync(
                    ReliabilitySeverity.Warning,
                    "database",
                    "restore-applied",
                    "A verified database backup was restored before Haven opened its data store.",
                    new Dictionary<string, string>
                    {
                        ["backupFileName"] = backup.FileName,
                        ["restoredSchemaVersion"] = restored.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["emergencyBackup"] = string.IsNullOrWhiteSpace(emergencyBackup) ? "none" : Path.GetFileName(emergencyBackup)
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return new DatabaseRestoreResult(
                    backup.FileName,
                    emergencyBackup,
                    restored.SchemaVersion,
                    DateTimeOffset.UtcNow,
                    "Verified backup restored. Normal migrations can now continue.");
            }
            catch (Exception restoreFailure) when (restoreFailure is IOException or InvalidDataException or SqliteException or UnauthorizedAccessException)
            {
                TryDelete(staged);
                if (!string.IsNullOrWhiteSpace(emergencyBackup) && File.Exists(emergencyBackup))
                {
                    try
                    {
                        var rollback = paths.DatabasePath + ".rollback-" + Guid.NewGuid().ToString("N") + ".tmp";
                        await CopyDurablyAsync(emergencyBackup, rollback, cancellationToken).ConfigureAwait(false);
                        TryDelete(paths.DatabasePath + "-wal");
                        TryDelete(paths.DatabasePath + "-shm");
                        if (File.Exists(paths.DatabasePath)) await ReplaceFileWithRetryAsync(rollback, paths.DatabasePath, cancellationToken).ConfigureAwait(false);
                        else File.Move(rollback, paths.DatabasePath);
                    }
                    catch (Exception rollbackFailure) when (rollbackFailure is IOException or UnauthorizedAccessException or SqliteException)
                    {
                        await diagnostics.WriteAsync(
                            ReliabilitySeverity.Critical,
                            "database",
                            "restore-rollback-failed",
                            "Database restore failed and the emergency rollback also failed. Haven startup must remain stopped.",
                            new Dictionary<string, string>
                            {
                                ["restoreException"] = restoreFailure.GetType().Name,
                                ["rollbackException"] = rollbackFailure.GetType().Name
                            },
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                        QuarantinePending("rollback-failed");
                        throw new AggregateException("The database restore and emergency rollback both failed.", restoreFailure, rollbackFailure);
                    }
                }

                QuarantinePending("restore-failed");
                await diagnostics.WriteAsync(
                    ReliabilitySeverity.Critical,
                    "database",
                    "restore-failed",
                    "The pending database restore failed and was not retried automatically.",
                    new Dictionary<string, string> { ["exceptionType"] = restoreFailure.GetType().Name },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                throw new InvalidDataException("The pending database restore failed. The request was quarantined to prevent an automatic retry loop.", restoreFailure);
            }
            finally
            {
                TryDelete(staged);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Retrieves backups core async for the current operation.
    /// </summary>
    private async Task<IReadOnlyList<ManagedDatabaseBackup>> GetBackupsCoreAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_backupDirectory)) return [];
        var result = new List<ManagedDatabaseBackup>();
        foreach (var manifestPath in Directory.EnumerateFiles(_backupDirectory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetCreationTimeUtc)
                     .Take(25))
        {
            cancellationToken.ThrowIfCancellationRequested();
            BackupManifest? manifest;
            try { manifest = await ReadJsonAsync<BackupManifest>(manifestPath, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                result.Add(new ManagedDatabaseBackup(Path.GetFileNameWithoutExtension(manifestPath) + ".db", string.Empty, manifestPath, 0, 0, 0, string.Empty, File.GetCreationTimeUtc(manifestPath), false, "Manifest unreadable: " + ex.Message));
                continue;
            }
            if (manifest is null)
            {
                result.Add(new ManagedDatabaseBackup(Path.GetFileNameWithoutExtension(manifestPath) + ".db", string.Empty, manifestPath, 0, 0, 0, string.Empty, File.GetCreationTimeUtc(manifestPath), false, "Manifest was empty."));
                continue;
            }

            string databasePath;
            try { databasePath = ResolveManagedBackup(manifest.FileName); }
            catch (ArgumentException ex)
            {
                result.Add(new ManagedDatabaseBackup(manifest.FileName, string.Empty, manifestPath, manifest.FromSchemaVersion, manifest.ToSchemaVersion, manifest.SizeBytes, manifest.Sha256, manifest.CreatedAt, false, ex.Message));
                continue;
            }
            if (!File.Exists(databasePath))
            {
                result.Add(new ManagedDatabaseBackup(manifest.FileName, databasePath, manifestPath, manifest.FromSchemaVersion, manifest.ToSchemaVersion, manifest.SizeBytes, manifest.Sha256, manifest.CreatedAt, false, "Database file is missing."));
                continue;
            }

            var info = new FileInfo(databasePath);
            var hash = await ComputeSha256Async(databasePath, cancellationToken).ConfigureAwait(false);
            var verification = await VerifyDatabaseAsync(databasePath, cancellationToken).ConfigureAwait(false);
            var verified = info.Length == manifest.SizeBytes
                           && hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase)
                           && verification.IsHealthy;
            var message = verified
                ? "SHA-256, file length, SQLite integrity and foreign keys verified."
                : $"Verification failed: size={info.Length == manifest.SizeBytes}, sha256={hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase)}, sqlite={verification.IsHealthy}.";
            result.Add(new ManagedDatabaseBackup(
                manifest.FileName,
                databasePath,
                manifestPath,
                manifest.FromSchemaVersion,
                manifest.ToSchemaVersion,
                info.Length,
                hash,
                manifest.CreatedAt,
                verified,
                message));
        }
        return result.OrderByDescending(item => item.CreatedAt).ToArray();
    }

    /// <summary>
    /// Performs read pending core asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<PendingDatabaseRestore?> ReadPendingCoreAsync(CancellationToken cancellationToken)
    {
        var pending = await ReadPendingFileCoreAsync(cancellationToken).ConfigureAwait(false);
        return pending is null
            ? null
            : new PendingDatabaseRestore(pending.BackupFileName, pending.Sha256, pending.RequestedAt, true, "Verified restore pending for the next Haven launch.");
    }

    /// <summary>
    /// Performs read pending file core asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<PendingRestoreFile?> ReadPendingFileCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_pendingPath)) return null;
        try
        {
            var pending = await ReadJsonAsync<PendingRestoreFile>(_pendingPath, cancellationToken).ConfigureAwait(false);
            if (pending is null || pending.Version != 1 || string.IsNullOrWhiteSpace(pending.BackupFileName) || pending.Sha256.Length != 64)
                throw new InvalidDataException("The pending restore request is invalid.");
            _ = NormalizeFileName(pending.BackupFileName);
            return pending;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            QuarantinePending("invalid");
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Warning,
                "database",
                "restore-request-quarantined",
                "An invalid pending database restore request was quarantined.",
                new Dictionary<string, string> { ["exceptionType"] = ex.GetType().Name },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// Creates emergency backup async with the invariants required by its callers.
    /// </summary>
    private async Task<string> CreateEmergencyBackupAsync(int restoreTargetVersion, CancellationToken cancellationToken)
    {
        await CheckpointCurrentDatabaseAsync(cancellationToken).ConfigureAwait(false);
        await using var source = CreateConnection(paths.DatabasePath, SqliteOpenMode.ReadWrite);
        await source.OpenAsync(cancellationToken).ConfigureAwait(false);
        var currentVersion = await ReadSchemaVersionAsync(source, cancellationToken).ConfigureAwait(false);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var stem = $"haven-v{currentVersion}-to-v{restoreTargetVersion}-pre-restore-{timestamp}-{Guid.NewGuid():N}";
        var final = Path.Combine(_backupDirectory, stem + ".db");
        var manifestPath = Path.Combine(_backupDirectory, stem + ".json");
        var temporary = final + ".tmp";
        try
        {
            await using (var destination = CreateConnection(temporary, SqliteOpenMode.ReadWriteCreate))
            {
                await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
                source.BackupDatabase(destination);
            }
            var verification = await VerifyDatabaseAsync(temporary, cancellationToken).ConfigureAwait(false);
            if (!verification.IsHealthy) throw new InvalidDataException("The emergency pre-restore backup failed integrity verification.");
            var info = new FileInfo(temporary);
            var hash = await ComputeSha256Async(temporary, cancellationToken).ConfigureAwait(false);
            var manifest = new BackupManifest(
                1,
                Path.GetFileName(final),
                currentVersion,
                restoreTargetVersion,
                info.Length,
                hash,
                DateTimeOffset.UtcNow,
                verification.IntegrityMessages,
                verification.ForeignKeyViolations);
            await WriteAtomicJsonAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, final);
            return final;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    /// <summary>
    /// Performs checkpoint current database asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task CheckpointCurrentDatabaseAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.DatabasePath) || new FileInfo(paths.DatabasePath).Length == 0) return;
        SqliteConnection.ClearAllPools();
        await using var connection = CreateConnection(paths.DatabasePath, SqliteOpenMode.ReadWrite);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA wal_checkpoint(TRUNCATE);";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs verify database asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task<DatabaseHealthReport> VerifyDatabaseAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(path, SqliteOpenMode.ReadOnly);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var mode = connection.CreateCommand())
        {
            mode.CommandText = "PRAGMA query_only=ON; PRAGMA busy_timeout=5000;";
            await mode.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        var integrity = await ReadStringsAsync(connection, "PRAGMA integrity_check;", cancellationToken).ConfigureAwait(false);
        var foreignKeys = await ReadForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
        var version = await ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        return new DatabaseHealthReport(
            integrity.Count == 1 && integrity[0].Equals("ok", StringComparison.OrdinalIgnoreCase) && foreignKeys.Count == 0,
            version,
            integrity,
            foreignKeys,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Creates connection with the invariants required by its callers.
    /// </summary>
    private static SqliteConnection CreateConnection(string path, SqliteOpenMode mode)
    {
        SqliteProviderBootstrap.EnsureInitialized();
        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            ForeignKeys = true
        }.ToString());
    }

    /// <summary>
    /// Performs read schema version asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<int> ReadSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
        if (Convert.ToInt64(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 0) return 0;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version),0) FROM schema_migrations;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Performs read strings asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadStringsAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        return result;
    }

    /// <summary>
    /// Performs read foreign keys asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadForeignKeysAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add($"table={reader.GetString(0)}; rowid={(reader.IsDBNull(1) ? "?" : reader.GetInt64(1))}; parent={reader.GetString(2)}; constraint={reader.GetInt64(3)}");
        return result;
    }

    /// <summary>
    /// Performs the resolve managed backup step owned by this component.
    /// </summary>
    private string ResolveManagedBackup(string fileName)
    {
        var normalized = NormalizeFileName(fileName);
        var root = Path.GetFullPath(_backupDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(_backupDirectory, normalized));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The backup path is outside Haven's managed backup directory.", nameof(fileName));
        return path;
    }

    /// <summary>
    /// Performs the normalize file name step owned by this component.
    /// </summary>
    private static string NormalizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A backup file name is required.", nameof(fileName));
        var normalized = fileName.Trim();
        if (!normalized.Equals(Path.GetFileName(normalized), StringComparison.Ordinal)
            || !normalized.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
            || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Only a managed Haven database backup file name is allowed.", nameof(fileName));
        return normalized;
    }

    /// <summary>
    /// Performs quarantine pending asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task QuarantinePendingAsync(string reason, CancellationToken cancellationToken)
    {
        if (!File.Exists(_pendingPath)) return;
        var quarantine = _pendingPath + "." + reason + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        try { File.Move(_pendingPath, quarantine, overwrite: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await diagnostics.WriteAsync(ReliabilitySeverity.Warning, "database", "restore-quarantine-failed", "A failed restore request could not be moved to quarantine.", cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Performs the quarantine pending step owned by this component.
    /// </summary>
    private void QuarantinePending(string reason)
    {
        QuarantinePendingAsync(reason, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Performs copy durably asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task CopyDurablyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    private static async Task ReplaceFileWithRetryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        const int maximumAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Replace(source, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
                return;
            }
            catch (IOException) when (attempt < maximumAttempts)
            {
                // Windows file-indexing and sync providers can briefly retain a handle after
                // the SQLite pools are cleared. Keep the replacement atomic and retry within
                // a small, bounded window instead of falling back to a delete-and-move cycle.
                SqliteConnection.ClearAllPools();
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Performs compute sha256 asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteAtomicJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        var backup = path + ".bak";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temp, path, backup, true);
            else File.Move(temp, path);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// Attempts to delete and reports the result without using failure for normal control flow.
    /// </summary>
    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Represents pending restore file and keeps its related state and behavior together.
    /// </summary>
    private sealed record PendingRestoreFile(int Version, string BackupFileName, string Sha256, DateTimeOffset RequestedAt);
    /// <summary>
    /// Represents backup manifest and keeps its related state and behavior together.
    /// </summary>
    private sealed record BackupManifest(
        int Version,
        string FileName,
        int FromSchemaVersion,
        int ToSchemaVersion,
        long SizeBytes,
        string Sha256,
        DateTimeOffset CreatedAt,
        IReadOnlyList<string> IntegrityMessages,
        IReadOnlyList<string> ForeignKeyViolations);
}
