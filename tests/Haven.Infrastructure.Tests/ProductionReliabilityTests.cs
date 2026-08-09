/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/ProductionReliabilityTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ProductionReliabilityTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.IO.Compression;
using Haven.Application;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents production reliability tests and keeps its related state and behavior together.
/// </summary>
public sealed class ProductionReliabilityTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the pre migration backup uses sqlite backup and produces verified manifest step owned by this component.
    /// </summary>
    [Fact]
    public async Task PreMigrationBackupUsesSqliteBackupAndProducesVerifiedManifest()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var maintenance = new DatabaseMaintenanceService(_paths, diagnostics);

        var backup = await maintenance.PrepareForMigrationAsync(13, CancellationToken.None);

        Assert.NotNull(backup);
        Assert.Equal(12, backup!.FromVersion);
        Assert.Equal(13, backup.ToVersion);
        Assert.True(File.Exists(backup.DatabasePath));
        Assert.True(File.Exists(backup.ManifestPath));
        Assert.True(backup.SizeBytes > 0);
        Assert.Equal(64, backup.Sha256.Length);
        Assert.DoesNotContain(".tmp", backup.DatabasePath, StringComparison.OrdinalIgnoreCase);

        await using var connection = CreateConnection(backup.DatabasePath, SqliteOpenMode.ReadOnly);
        await connection.OpenAsync(CancellationToken.None);
        await using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA integrity_check;";
        Assert.Equal("ok", Convert.ToString(await check.ExecuteScalarAsync(CancellationToken.None), System.Globalization.CultureInfo.InvariantCulture));

        Assert.Null(await maintenance.PrepareForMigrationAsync(13, CancellationToken.None));
    }

    /// <summary>
    /// Performs the integrity check reports foreign key corruption step owned by this component.
    /// </summary>
    [Fact]
    public async Task IntegrityCheckReportsForeignKeyCorruption()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        await using (var connection = await database.OpenAsync(CancellationToken.None))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys=OFF;
                INSERT INTO messages(id,conversation_id,role,content,created_at)
                VALUES('orphan-message','missing-conversation',0,'orphan','2026-07-16T00:00:00Z');
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        var maintenance = new DatabaseMaintenanceService(_paths, diagnostics);

        var health = await maintenance.VerifyIntegrityAsync(CancellationToken.None);

        Assert.False(health.IsHealthy);
        Assert.Contains(health.IntegrityMessages, message => message.Equals("ok", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(health.ForeignKeyViolations, message => message.Contains("messages", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Performs the rolling diagnostics redacts secrets urls and user profile step owned by this component.
    /// </summary>
    [Fact]
    public async Task RollingDiagnosticsRedactsSecretsUrlsAndUserProfile()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await diagnostics.WriteAsync(
            ReliabilitySeverity.Error,
            "provider",
            "request-failed",
            $"password=hunter2 failed at https://example.test/download?token=signed-secret#fragment in {profile}",
            new Dictionary<string, string>
            {
                ["apiKey"] = "top-secret-key",
                ["safeValue"] = "https://example.test/path?access_token=another-secret"
            },
            "test-correlation",
            CancellationToken.None);

        var events = await diagnostics.ReadRecentAsync(10, CancellationToken.None);
        var item = Assert.Single(events);
        Assert.DoesNotContain("hunter2", item.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("signed-secret", item.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("fragment", item.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(profile, item.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("<redacted>", item.Data["apiKey"]);
        Assert.DoesNotContain("another-secret", item.Data["safeValue"], StringComparison.Ordinal);

        var raw = string.Join(Environment.NewLine, Directory.EnumerateFiles(_paths.LogsDirectory).Select(File.ReadAllText));
        Assert.DoesNotContain("hunter2", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret-key", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("signed-secret", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("another-secret", raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// Performs the repeated unclean starts enter safe mode and clean shutdown resets history step owned by this component.
    /// </summary>
    [Fact]
    public async Task RepeatedUncleanStartsEnterSafeModeAndCleanShutdownResetsHistory()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        StartupRecoveryState? state = null;
        StartupRecoveryCoordinator? coordinator = null;
        try
        {
            for (var index = 0; index < 4; index++)
            {
                coordinator = new StartupRecoveryCoordinator(_paths, diagnostics);
                state = await coordinator.BeginStartupAsync(CancellationToken.None);
            }

            Assert.NotNull(state);
            Assert.True(state!.IsSafeMode);
            Assert.True(RuntimeSafetyState.IsSafeMode);
            Assert.True(state.RecentUncleanStarts >= 3);

            var reset = new CleanResetStartupRecoveryCoordinator(coordinator!, _paths);
            await reset.MarkCleanShutdownAsync(CancellationToken.None);
            Assert.False(RuntimeSafetyState.IsSafeMode);
            Assert.False(File.Exists(Path.Combine(_paths.DataDirectory, "startup-recovery.json")));

            var next = new StartupRecoveryCoordinator(_paths, diagnostics);
            var normal = await next.BeginStartupAsync(CancellationToken.None);
            Assert.False(normal.IsSafeMode);
            Assert.Equal(0, normal.RecentUncleanStarts);
        }
        finally
        {
            RuntimeSafetyState.DisableSafeMode();
        }
    }

    /// <summary>
    /// Performs the diagnostics bundle contains only redacted operational evidence step owned by this component.
    /// </summary>
    [Fact]
    public async Task DiagnosticsBundleContainsOnlyRedactedOperationalEvidence()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var maintenance = new DatabaseMaintenanceService(_paths, diagnostics);
        var startup = new StartupRecoveryCoordinator(_paths, diagnostics);
        await startup.BeginStartupAsync(CancellationToken.None);
        await diagnostics.WriteAsync(
            ReliabilitySeverity.Warning,
            "test",
            "redaction-check",
            "authorization=very-secret and https://example.test/path?key=secret-query",
            cancellationToken: CancellationToken.None);
        var bundles = Path.Combine(_paths.DataDirectory, "Support");
        var service = new DiagnosticsBundleService(_paths, maintenance, startup, diagnostics);

        var path = await service.CreateBundleAsync(bundles, CancellationToken.None);

        Assert.True(File.Exists(path));
        using var archive = ZipFile.OpenRead(path);
        Assert.Contains(archive.Entries, entry => entry.FullName == "environment.json");
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("conversation", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("attachment", StringComparison.OrdinalIgnoreCase));
        foreach (var entry in archive.Entries)
        {
            await using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync(CancellationToken.None);
            Assert.DoesNotContain("very-secret", content, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-query", content, StringComparison.Ordinal);
        }
        RuntimeSafetyState.DisableSafeMode();
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        RuntimeSafetyState.DisableSafeMode();
        _paths.Dispose();
    }

    /// <summary>
    /// Creates connection with the invariants required by its callers.
    /// </summary>
    private static SqliteConnection CreateConnection(string path, SqliteOpenMode mode) => new(new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = mode,
        Pooling = false,
        ForeignKeys = true
    }.ToString());

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-production-reliability-tests-" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }

        /// <summary>
        /// Gets or updates data directory, the bindable or domain state represented by this property.
        /// </summary>
        public string DataDirectory { get; }
        /// <summary>
        /// Gets or updates database path, the bindable or domain state represented by this property.
        /// </summary>
        public string DatabasePath { get; }
        /// <summary>
        /// Gets or updates browser profile directory, the bindable or domain state represented by this property.
        /// </summary>
        public string BrowserProfileDirectory { get; }
        /// <summary>
        /// Gets or updates attachments directory, the bindable or domain state represented by this property.
        /// </summary>
        public string AttachmentsDirectory { get; }
        /// <summary>
        /// Gets or updates logs directory, the bindable or domain state represented by this property.
        /// </summary>
        public string LogsDirectory { get; }
        /// <summary>
        /// Gets or updates legacy state path, the bindable or domain state represented by this property.
        /// </summary>
        public string LegacyStatePath { get; }

        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
