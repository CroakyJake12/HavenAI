using Haven.Browser;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class BrowserAutomationStoreTransactionTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task FailedAddIsNotVisibleInMemoryAfterStorageRecovers()
    {
        using var store = new BrowserAutomationStore(_paths);
        var action = PendingAction();
        _paths.BlockDataDirectory();

        await Assert.ThrowsAnyAsync<IOException>(() =>
            store.AddPendingAsync(action, CancellationToken.None));

        _paths.RestoreDataDirectory();
        Assert.Null(await store.GetActionAsync(action.Id, CancellationToken.None));
    }

    [Fact]
    public async Task FailedUpdatePreservesPreviousPersistedStateInMemory()
    {
        using var store = new BrowserAutomationStore(_paths);
        var action = PendingAction();
        await store.AddPendingAsync(action, CancellationToken.None);
        _paths.BlockDataDirectory();

        await Assert.ThrowsAnyAsync<IOException>(() =>
            store.UpdateActionAsync(
                action with
                {
                    State = BrowserActionState.Approved,
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                CancellationToken.None));

        _paths.RestoreDataDirectory();
        var current = await store.GetActionAsync(action.Id, CancellationToken.None);
        Assert.Equal(BrowserActionState.Pending, current?.State);
    }

    private static BrowserPendingAction PendingAction()
    {
        var now = DateTimeOffset.UtcNow;
        return new BrowserPendingAction(
            Guid.NewGuid(),
            BrowserActionKind.Download,
            "https://example.test",
            "Download test",
            "https://example.test/file",
            "file.txt",
            BrowserActionState.Pending,
            now,
            now.AddMinutes(10),
            now,
            null);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : Haven.Application.IAppPaths, IDisposable
    {
        private bool _blocked;

        public TestPaths()
        {
            DataDirectory = Path.Combine(
                Path.GetTempPath(),
                "haven-browser-store-transactions-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser-profile");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "legacy.json");
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void BlockDataDirectory()
        {
            if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true);
            File.WriteAllText(DataDirectory, "blocked");
            _blocked = true;
        }

        public void RestoreDataDirectory()
        {
            if (!_blocked) return;
            if (File.Exists(DataDirectory)) File.Delete(DataDirectory);
            Directory.CreateDirectory(DataDirectory);
            _blocked = false;
        }

        public void Dispose()
        {
            RestoreDataDirectory();
            try { Directory.Delete(DataDirectory, true); }
            catch (IOException) { }
        }
    }
}
