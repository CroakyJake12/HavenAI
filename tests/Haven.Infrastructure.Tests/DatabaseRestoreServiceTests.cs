using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class DatabaseRestoreServiceTests : IDisposable
{
    private readonly TestPaths _paths = new();

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
        var backup = Assert.IsType<DatabaseBackupInfo>(await maintenance.PrepareForMigrationAsync(10, CancellationToken.None));

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
        var backup = Assert.IsType<DatabaseBackupInfo>(await maintenance.PrepareForMigrationAsync(10, CancellationToken.None));
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

    [Fact]
    public async Task CancelPendingRestoreLeavesCurrentDatabaseUntouched()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var maintenance = new DatabaseMaintenanceService(_paths, diagnostics);
        var backup = Assert.IsType<DatabaseBackupInfo>(await maintenance.PrepareForMigrationAsync(10, CancellationToken.None));
        var restore = new DatabaseRestoreService(_paths, diagnostics);
        await restore.RequestRestoreAsync(Path.GetFileName(backup.DatabasePath), CancellationToken.None);

        await restore.CancelPendingAsync(CancellationToken.None);

        Assert.Null(await restore.GetPendingAsync(CancellationToken.None));
        Assert.Null(await restore.ApplyPendingRestoreAsync(CancellationToken.None));
        Assert.True((await maintenance.VerifyIntegrityAsync(CancellationToken.None)).IsHealthy);
    }

    public void Dispose() => _paths.Dispose();

    private static Conversation Conversation(string title)
    {
        var now = DateTimeOffset.UtcNow;
        return new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, title, null, null, false, false, now, now);
    }

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
