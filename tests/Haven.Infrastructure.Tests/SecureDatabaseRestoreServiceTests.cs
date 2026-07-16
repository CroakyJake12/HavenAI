using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class SecureDatabaseRestoreServiceTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task RenamedManifestCannotAuthorizeAnotherManagedDatabaseFile()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var maintenance = new DatabaseMaintenanceService(_paths, diagnostics);
        var backup = Assert.IsType<DatabaseBackupInfo>(
            await maintenance.PrepareForMigrationAsync(10, CancellationToken.None));
        var renamedManifest = Path.Combine(
            Path.GetDirectoryName(backup.ManifestPath)!,
            "renamed-manifest.json");
        File.Move(backup.ManifestPath, renamedManifest);
        var inner = new DatabaseRestoreService(_paths, diagnostics);
        var secure = new SecureDatabaseRestoreService(inner, _paths, diagnostics);

        var inventory = await secure.GetBackupsAsync(CancellationToken.None);
        var item = Assert.Single(inventory);

        Assert.False(item.IsVerified);
        Assert.Contains("filenames do not match", item.VerificationMessage, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            secure.RequestRestoreAsync(item.FileName, CancellationToken.None));
        Assert.Null(await secure.GetPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ProductionBoundaryAcceptsUntamperedManagedBackup()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var maintenance = new DatabaseMaintenanceService(_paths, diagnostics);
        var backup = Assert.IsType<DatabaseBackupInfo>(
            await maintenance.PrepareForMigrationAsync(10, CancellationToken.None));
        var inner = new DatabaseRestoreService(_paths, diagnostics);
        var secure = new SecureDatabaseRestoreService(inner, _paths, diagnostics);

        var inventory = await secure.GetBackupsAsync(CancellationToken.None);
        var item = Assert.Single(inventory);
        var pending = await secure.RequestRestoreAsync(Path.GetFileName(backup.DatabasePath), CancellationToken.None);

        Assert.True(item.IsVerified);
        Assert.True(pending.IsPending);
        await secure.CancelPendingAsync(CancellationToken.None);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-secure-restore-tests-" + Guid.NewGuid().ToString("N"));
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
