using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed class BrowserSecretAndLiveStateTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task SignedDownloadUrlRemainsSessionOnlyAndIsRedactedFromAudit()
    {
        const string secret = "super-secret-signature";
        var browser = new BrowserSessionService(_paths);
        var store = new MemoryStore();
        using var automation = new BrowserAutomationService(browser, new AllowPolicy(), store, _paths);

        var action = await automation.RequestDownloadAsync(
            $"https://example.test/archive.zip?signature={secret}#private",
            "archive.zip",
            CancellationToken.None);

        Assert.StartsWith("ephemeral:", action.Target, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, action.Target, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, action.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(store.Audit, item => item.Detail.Contains(secret, StringComparison.Ordinal));
        Assert.Contains("https://example.test/archive.zip", action.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveApprovedActionIsNotMistakenForPostRestartRecovery()
    {
        var now = DateTimeOffset.UtcNow;
        var action = new BrowserPendingAction(
            Guid.NewGuid(), BrowserActionKind.Download, "https://example.test", "Download example",
            "https://example.test/file.bin", "file.bin", BrowserActionState.Pending,
            now, now.AddMinutes(10), now, null);
        using var store = new BrowserAutomationStore(_paths);
        await store.AddPendingAsync(action, CancellationToken.None);
        await store.UpdateActionAsync(action with { State = BrowserActionState.Approved, UpdatedAt = now.AddSeconds(1) }, CancellationToken.None);

        Assert.Empty(await store.GetPendingAsync(CancellationToken.None));
        Assert.Equal(BrowserActionState.Approved, (await store.GetActionAsync(action.Id, CancellationToken.None))?.State);
        Assert.DoesNotContain(await store.GetAuditAsync(20, CancellationToken.None), item => item.Operation == "recovery-interrupted");
    }

    public void Dispose() => _paths.Dispose();

    private sealed class AllowPolicy : IBrowserNavigationPolicy
    {
        public Task<BrowserNavigationAssessment> AssessAsync(Uri address, CancellationToken cancellationToken) =>
            Task.FromResult(new BrowserNavigationAssessment(address, true, "test", ["8.8.8.8"]));
    }

    private sealed class MemoryStore : IBrowserAutomationStore
    {
        public List<BrowserPendingAction> Actions { get; } = [];
        public List<BrowserAuditEntry> Audit { get; } = [];
        public List<BrowserDownloadRecord> Downloads { get; } = [];
        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserPendingAction>>(Actions.Where(item => item.State == BrowserActionState.Pending).ToArray());
        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserAuditEntry>>(Audit.TakeLast(limit).Reverse().ToArray());
        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>(Downloads.TakeLast(limit).Reverse().ToArray());
        public Task<BrowserPendingAction> AddPendingAsync(BrowserPendingAction action, CancellationToken cancellationToken) { Actions.Add(action); return Task.FromResult(action); }
        public Task<BrowserPendingAction?> GetActionAsync(Guid actionId, CancellationToken cancellationToken) => Task.FromResult(Actions.FirstOrDefault(item => item.Id == actionId));
        public Task<BrowserPendingAction> UpdateActionAsync(BrowserPendingAction action, CancellationToken cancellationToken) { Actions[Actions.FindIndex(item => item.Id == action.Id)] = action; return Task.FromResult(action); }
        public Task AddAuditAsync(BrowserAuditEntry entry, CancellationToken cancellationToken) { Audit.Add(entry); return Task.CompletedTask; }
        public Task AddDownloadAsync(BrowserDownloadRecord download, CancellationToken cancellationToken) { Downloads.Add(download); return Task.CompletedTask; }
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-browser-secret-tests-" + Guid.NewGuid().ToString("N"));
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
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
