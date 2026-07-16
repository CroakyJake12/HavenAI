using System.IO.Compression;
using Haven.Application;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure.Tests;

public sealed class ProductionReliabilityTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task PreMigrationBackupUsesSqliteBackupAndProducesVerifiedManifest()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var maintenance = new DatabaseMaintenanceService(_paths, diagnostics);

        var backup = await maintenance.PrepareForMigrationAsync(10, CancellationToken.None);

        Assert.NotNull(backup);
        Assert.Equal(8, backup!.FromVersion);
        Assert.Equal(10, backup.ToVersion);
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

        Assert.Null(await maintenance.PrepareForMigrationAsync(10, CancellationToken.None));
    }

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

    public void Dispose()
    {
        RuntimeSafetyState.DisableSafeMode();
        _paths.Dispose();
    }

    private static SqliteConnection CreateConnection(string path, SqliteOpenMode mode) => new(new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = mode,
        Pooling = false,
        ForeignKeys = true
    }.ToString());

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

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
