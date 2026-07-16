using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed class DatabaseMaintenanceService(IAppPaths paths, IProductionDiagnostics diagnostics) : IDatabaseMaintenance
{
    private const int MaximumBackups = 10;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _backupDirectory = Path.Combine(paths.DataDirectory, "Backups");
    private int _highestPreparedTarget;

    public async Task<DatabaseBackupInfo?> PrepareForMigrationAsync(int targetVersion, CancellationToken cancellationToken)
    {
        if (targetVersion <= 0) throw new ArgumentOutOfRangeException(nameof(targetVersion));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (targetVersion <= _highestPreparedTarget) return null;
            if (!File.Exists(paths.DatabasePath) || new FileInfo(paths.DatabasePath).Length == 0)
            {
                _highestPreparedTarget = targetVersion;
                return null;
            }

            await using var source = CreateConnection(paths.DatabasePath, SqliteOpenMode.ReadWrite);
            await source.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ConfigureConnectionAsync(source, cancellationToken).ConfigureAwait(false);
            var preflight = await RunCheckAsync(source, "PRAGMA quick_check;", cancellationToken).ConfigureAwait(false);
            var preflightForeignKeys = await RunForeignKeyCheckAsync(source, cancellationToken).ConfigureAwait(false);
            if (preflight.Count != 1 || !preflight[0].Equals("ok", StringComparison.OrdinalIgnoreCase) || preflightForeignKeys.Count > 0)
            {
                await diagnostics.WriteAsync(
                    ReliabilitySeverity.Critical,
                    "database",
                    "pre-migration-integrity-failed",
                    "The Haven database failed SQLite quick_check or foreign-key validation. Migrations were not started.",
                    new Dictionary<string, string>
                    {
                        ["integrityResults"] = string.Join(" | ", preflight.Take(20)),
                        ["foreignKeyViolations"] = string.Join(" | ", preflightForeignKeys.Take(20))
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                throw new InvalidDataException("The Haven database failed its pre-migration integrity checks. Migrations were stopped to protect the existing data.");
            }

            var currentVersion = await ReadSchemaVersionAsync(source, cancellationToken).ConfigureAwait(false);
            if (currentVersion >= targetVersion)
            {
                _highestPreparedTarget = targetVersion;
                return null;
            }

            Directory.CreateDirectory(_backupDirectory);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var stem = $"haven-v{currentVersion}-to-v{targetVersion}-{timestamp}-{Guid.NewGuid():N}";
            var finalDatabasePath = Path.Combine(_backupDirectory, stem + ".db");
            var finalManifestPath = Path.Combine(_backupDirectory, stem + ".json");
            var temporaryDatabasePath = finalDatabasePath + ".tmp";
            var temporaryManifestPath = finalManifestPath + ".tmp";

            try
            {
                await using (var destination = CreateConnection(temporaryDatabasePath, SqliteOpenMode.ReadWriteCreate))
                {
                    await destination.OpenAsync(cancellationToken).ConfigureAwait(false);
                    source.BackupDatabase(destination);
                    await destination.CloseAsync().ConfigureAwait(false);
                }

                var backupHealth = await VerifyFileAsync(temporaryDatabasePath, cancellationToken).ConfigureAwait(false);
                if (!backupHealth.IsHealthy)
                    throw new InvalidDataException("The pre-migration database backup failed its integrity check.");

                var info = new FileInfo(temporaryDatabasePath);
                var hash = await ComputeSha256Async(temporaryDatabasePath, cancellationToken).ConfigureAwait(false);
                var createdAt = DateTimeOffset.UtcNow;
                var manifest = new DatabaseBackupManifest(
                    1,
                    Path.GetFileName(finalDatabasePath),
                    currentVersion,
                    targetVersion,
                    info.Length,
                    hash,
                    createdAt,
                    backupHealth.IntegrityMessages,
                    backupHealth.ForeignKeyViolations);
                await WriteManifestAsync(temporaryManifestPath, manifest, cancellationToken).ConfigureAwait(false);

                File.Move(temporaryDatabasePath, finalDatabasePath);
                File.Move(temporaryManifestPath, finalManifestPath);
                ApplyRetention();
                _highestPreparedTarget = targetVersion;

                var result = new DatabaseBackupInfo(
                    finalDatabasePath,
                    finalManifestPath,
                    currentVersion,
                    targetVersion,
                    info.Length,
                    hash,
                    createdAt);
                await diagnostics.WriteAsync(
                    ReliabilitySeverity.Information,
                    "database",
                    "pre-migration-backup-created",
                    "A verified pre-migration database backup was created.",
                    new Dictionary<string, string>
                    {
                        ["fromVersion"] = currentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["toVersion"] = targetVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["sizeBytes"] = info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["sha256"] = hash,
                        ["fileName"] = Path.GetFileName(finalDatabasePath)
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return result;
            }
            finally
            {
                TryDelete(temporaryDatabasePath);
                TryDelete(temporaryManifestPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DatabaseHealthReport> VerifyIntegrityAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(paths.DatabasePath) || new FileInfo(paths.DatabasePath).Length == 0)
                return new DatabaseHealthReport(true, 0, ["ok"], [], DateTimeOffset.UtcNow);

            DatabaseHealthReport result;
            try
            {
                result = await VerifyFileAsync(paths.DatabasePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                result = new DatabaseHealthReport(
                    false,
                    0,
                    ["Unable to complete SQLite integrity_check: " + ex.GetType().Name],
                    [],
                    DateTimeOffset.UtcNow);
            }

            await diagnostics.WriteAsync(
                result.IsHealthy ? ReliabilitySeverity.Information : ReliabilitySeverity.Critical,
                "database",
                result.IsHealthy ? "integrity-check-passed" : "integrity-check-failed",
                result.IsHealthy ? "The Haven database passed SQLite integrity and foreign-key checks." : "The Haven database failed SQLite integrity or foreign-key checks.",
                new Dictionary<string, string>
                {
                    ["schemaVersion"] = result.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["integrityMessages"] = string.Join(" | ", result.IntegrityMessages.Take(20)),
                    ["foreignKeyViolations"] = string.Join(" | ", result.ForeignKeyViolations.Take(20))
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<DatabaseHealthReport> VerifyFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(path, SqliteOpenMode.ReadOnly);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var pragmas = connection.CreateCommand())
        {
            pragmas.CommandText = "PRAGMA query_only=ON; PRAGMA busy_timeout=5000;";
            await pragmas.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        var integrity = await RunCheckAsync(connection, "PRAGMA integrity_check;", cancellationToken).ConfigureAwait(false);
        var foreignKeys = await RunForeignKeyCheckAsync(connection, cancellationToken).ConfigureAwait(false);
        var version = await ReadSchemaVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        var healthy = integrity.Count == 1 && integrity[0].Equals("ok", StringComparison.OrdinalIgnoreCase) && foreignKeys.Count == 0;
        return new DatabaseHealthReport(healthy, version, integrity, foreignKeys, DateTimeOffset.UtcNow);
    }

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

    private static async Task ConfigureConnectionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
        if (Convert.ToInt64(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 0) return 0;
        await using var version = connection.CreateCommand();
        version.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        return Convert.ToInt32(await version.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<string>> RunCheckAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        return result;
    }

    private static async Task<IReadOnlyList<string>> RunForeignKeyCheckAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var table = reader.IsDBNull(0) ? "?" : reader.GetString(0);
            var rowId = reader.IsDBNull(1) ? "?" : reader.GetInt64(1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var parent = reader.IsDBNull(2) ? "?" : reader.GetString(2);
            var constraint = reader.IsDBNull(3) ? "?" : reader.GetInt64(3).ToString(System.Globalization.CultureInfo.InvariantCulture);
            result.Add($"table={table}; rowid={rowId}; parent={parent}; constraint={constraint}");
        }
        return result;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task WriteManifestAsync(string path, DatabaseBackupManifest manifest, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private void ApplyRetention()
    {
        if (!Directory.Exists(_backupDirectory)) return;
        var databases = Directory.EnumerateFiles(_backupDirectory, "haven-v*-to-v*.db", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetCreationTimeUtc)
            .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var database in databases.Skip(MaximumBackups))
        {
            TryDelete(database);
            TryDelete(Path.ChangeExtension(database, ".json"));
        }
        foreach (var temp in Directory.EnumerateFiles(_backupDirectory, "*.tmp", SearchOption.TopDirectoryOnly)) TryDelete(temp);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed record DatabaseBackupManifest(
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
