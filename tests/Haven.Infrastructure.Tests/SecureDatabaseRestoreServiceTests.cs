/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/SecureDatabaseRestoreServiceTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns SecureDatabaseRestoreServiceTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents secure database restore service tests and keeps its related state and behavior together.
/// </summary>
public sealed class SecureDatabaseRestoreServiceTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the renamed manifest cannot authorize another managed database file step owned by this component.
    /// </summary>
    [Fact]
    public async Task RenamedManifestCannotAuthorizeAnotherManagedDatabaseFile()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var maintenance = new DatabaseMaintenanceService(_paths, diagnostics);
        var backup = Assert.IsType<DatabaseBackupInfo>(
            await maintenance.PrepareForMigrationAsync(15, CancellationToken.None));
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

    /// <summary>
    /// Performs the production boundary accepts untampered managed backup step owned by this component.
    /// </summary>
    [Fact]
    public async Task ProductionBoundaryAcceptsUntamperedManagedBackup()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var maintenance = new DatabaseMaintenanceService(_paths, diagnostics);
        var backup = Assert.IsType<DatabaseBackupInfo>(
            await maintenance.PrepareForMigrationAsync(15, CancellationToken.None));
        var inner = new DatabaseRestoreService(_paths, diagnostics);
        var secure = new SecureDatabaseRestoreService(inner, _paths, diagnostics);

        var inventory = await secure.GetBackupsAsync(CancellationToken.None);
        var item = Assert.Single(inventory);
        var pending = await secure.RequestRestoreAsync(Path.GetFileName(backup.DatabasePath), CancellationToken.None);

        Assert.True(item.IsVerified);
        Assert.True(pending.IsPending);
        await secure.CancelPendingAsync(CancellationToken.None);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
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
