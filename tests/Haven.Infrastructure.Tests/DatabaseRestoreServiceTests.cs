/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/DatabaseRestoreServiceTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns DatabaseRestoreServiceTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents database restore service tests and keeps its related state and behavior together.
/// </summary>
public sealed class DatabaseRestoreServiceTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the verified backup restores previous data and keeps emergency current copy step owned by this component.
    /// </summary>
    [Fact]
    public async Task VerifiedBackupRestoresPreviousDataAndKeepsEmergencyCurrentCopy()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var conversations = new ConversationRepository(database);
        var original = Conversation("present-in-backup");
        await conversations.UpsertConversationAsync(original, CancellationToken.None);
        var maintenance = new DatabaseMaintenanceService(_paths, diagnostics);
        var backup = Assert.IsType<DatabaseBackupInfo>(await maintenance.PrepareForMigrationAsync(17, CancellationToken.None));

        var newer = Conversation("created-after-backup");
        await conversations.UpsertConversationAsync(newer, CancellationToken.None);
        Assert.NotNull(await conversations.GetAsync(newer.Id, CancellationToken.None));

        var restore = new DatabaseRestoreService(_paths, diagnostics);
        var request = await restore.RequestRestoreAsync(Path.GetFileName(backup.DatabasePath), CancellationToken.None);
        Assert.True(request.IsPending);
        Assert.NotNull(await restore.GetPendingAsync(CancellationToken.None));

        var result = Assert.IsType<DatabaseRestoreResult>(await restore.ApplyPendingRestoreAsync(CancellationToken.None));

        Assert.Equal(Path.GetFileName(backup.DatabasePath), result.BackupFileName);
        Assert.True(File.Exists(result.EmergencyBackupPath));
        Assert.Null(await restore.GetPendingAsync(CancellationToken.None));
        var restoredRepository = new ConversationRepository(database);
        Assert.NotNull(await restoredRepository.GetAsync(original.Id, CancellationToken.None));
        Assert.Null(await restoredRepository.GetAsync(newer.Id, CancellationToken.None));
        Assert.True((await maintenance.VerifyIntegrityAsync(CancellationToken.None)).IsHealthy);
    }

    /// <summary>
    /// Performs the tampered backup is quarantined without changing current database step owned by this component.
    /// </summary>
    [Fact]
    public async Task TamperedBackupIsQuarantinedWithoutChangingCurrentDatabase()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var conversations = new ConversationRepository(database);
        var original = Conversation("original");
        await conversations.UpsertConversationAsync(original, CancellationToken.None);
        var maintenance = new DatabaseMaintenanceService(_paths, diagnostics);
        var backup = Assert.IsType<DatabaseBackupInfo>(await maintenance.PrepareForMigrationAsync(17, CancellationToken.None));
        var current = Conversation("must-survive-refused-restore");
        await conversations.UpsertConversationAsync(current, CancellationToken.None);

        var restore = new DatabaseRestoreService(_paths, diagnostics);
        await restore.RequestRestoreAsync(Path.GetFileName(backup.DatabasePath), CancellationToken.None);
        await File.AppendAllTextAsync(backup.DatabasePath, "tampered", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(() => restore.ApplyPendingRestoreAsync(CancellationToken.None));

        var currentRepository = new ConversationRepository(database);
        Assert.NotNull(await currentRepository.GetAsync(original.Id, CancellationToken.None));
        Assert.NotNull(await currentRepository.GetAsync(current.Id, CancellationToken.None));
        Assert.Null(await restore.GetPendingAsync(CancellationToken.None));
        Assert.Contains(
            Directory.EnumerateFiles(_paths.DataDirectory, "pending-database-restore.json.verification-failed-*", SearchOption.TopDirectoryOnly),
            path => File.Exists(path));
    }

    /// <summary>
    /// Performs the restore request rejects paths outside managed backups step owned by this component.
    /// </summary>
    [Theory]
    [InlineData("../haven.db")]
    [InlineData("subfolder/backup.db")]
    [InlineData("not-a-database.txt")]
    public async Task RestoreRequestRejectsPathsOutsideManagedBackups(string value)
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var restore = new DatabaseRestoreService(_paths, diagnostics);

        await Assert.ThrowsAsync<ArgumentException>(() => restore.RequestRestoreAsync(value, CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(_paths.DataDirectory, "pending-database-restore.json")));
    }

    /// <summary>
    /// Reports whether cancel pending restore leaves current database untouched is true for the current state.
    /// </summary>
    [Fact]
    public async Task CancelPendingRestoreLeavesCurrentDatabaseUntouched()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var maintenance = new DatabaseMaintenanceService(_paths, diagnostics);
        var backup = Assert.IsType<DatabaseBackupInfo>(await maintenance.PrepareForMigrationAsync(17, CancellationToken.None));
        var restore = new DatabaseRestoreService(_paths, diagnostics);
        await restore.RequestRestoreAsync(Path.GetFileName(backup.DatabasePath), CancellationToken.None);

        await restore.CancelPendingAsync(CancellationToken.None);

        Assert.Null(await restore.GetPendingAsync(CancellationToken.None));
        Assert.Null(await restore.ApplyPendingRestoreAsync(CancellationToken.None));
        Assert.True((await maintenance.VerifyIntegrityAsync(CancellationToken.None)).IsHealthy);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Performs the conversation step owned by this component.
    /// </summary>
    private static Conversation Conversation(string title)
    {
        var now = DateTimeOffset.UtcNow;
        return new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, title, null, null, false, false, now, now);
    }

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-database-restore-tests-" + Guid.NewGuid().ToString("N"));
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
