/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/BrowserSecretAndLiveStateTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns BrowserSecretAndLiveStateTests, AllowPolicy, MemoryStore, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents browser secret and live state tests and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserSecretAndLiveStateTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the signed download url remains session only and is redacted from audit step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the live approved action is not mistaken for post restart recovery step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents allow policy and keeps its related state and behavior together.
    /// </summary>
    private sealed class AllowPolicy : IBrowserNavigationPolicy
    {
        /// <summary>
        /// Performs assess async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<BrowserNavigationAssessment> AssessAsync(Uri address, CancellationToken cancellationToken) =>
            Task.FromResult(new BrowserNavigationAssessment(address, true, "test", ["8.8.8.8"]));
    }

    /// <summary>
    /// Represents memory store and keeps its related state and behavior together.
    /// </summary>
    private sealed class MemoryStore : IBrowserAutomationStore
    {
        /// <summary>
        /// Gets or updates actions, the bindable or domain state represented by this property.
        /// </summary>
        public List<BrowserPendingAction> Actions { get; } = [];
        /// <summary>
        /// Gets or updates audit, the bindable or domain state represented by this property.
        /// </summary>
        public List<BrowserAuditEntry> Audit { get; } = [];
        /// <summary>
        /// Gets or updates downloads, the bindable or domain state represented by this property.
        /// </summary>
        public List<BrowserDownloadRecord> Downloads { get; } = [];
        /// <summary>
        /// Retrieves pending async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserPendingAction>>(Actions.Where(item => item.State == BrowserActionState.Pending).ToArray());
        /// <summary>
        /// Retrieves audit async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserAuditEntry>>(Audit.TakeLast(limit).Reverse().ToArray());
        /// <summary>
        /// Retrieves downloads async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>(Downloads.TakeLast(limit).Reverse().ToArray());
        /// <summary>
        /// Performs add pending async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<BrowserPendingAction> AddPendingAsync(BrowserPendingAction action, CancellationToken cancellationToken) { Actions.Add(action); return Task.FromResult(action); }
        /// <summary>
        /// Retrieves action async for the current operation.
        /// </summary>
        public Task<BrowserPendingAction?> GetActionAsync(Guid actionId, CancellationToken cancellationToken) => Task.FromResult(Actions.FirstOrDefault(item => item.Id == actionId));
        /// <summary>
        /// Performs update action async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<BrowserPendingAction> UpdateActionAsync(BrowserPendingAction action, CancellationToken cancellationToken) { Actions[Actions.FindIndex(item => item.Id == action.Id)] = action; return Task.FromResult(action); }
        /// <summary>
        /// Performs add audit async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task AddAuditAsync(BrowserAuditEntry entry, CancellationToken cancellationToken) { Audit.Add(entry); return Task.CompletedTask; }
        /// <summary>
        /// Performs add download async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task AddDownloadAsync(BrowserDownloadRecord download, CancellationToken cancellationToken) { Downloads.Add(download); return Task.CompletedTask; }
    }

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
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
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
