/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/BrowserAutomationStoreTransactionTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns BrowserAutomationStoreTransactionTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Browser;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents browser automation store transaction tests and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserAutomationStoreTransactionTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the failed add is not visible in memory after storage recovers step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the failed update preserves previous persisted state in memory step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the pending action step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestPaths : Haven.Application.IAppPaths, IDisposable
    {
        /// <summary>
        /// Stores blocked locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
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
        /// Performs the block data directory step owned by this component.
        /// </summary>
        public void BlockDataDirectory()
        {
            if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true);
            File.WriteAllText(DataDirectory, "blocked");
            _blocked = true;
        }

        /// <summary>
        /// Performs the restore data directory step owned by this component.
        /// </summary>
        public void RestoreDataDirectory()
        {
            if (!_blocked) return;
            if (File.Exists(DataDirectory)) File.Delete(DataDirectory);
            Directory.CreateDirectory(DataDirectory);
            _blocked = false;
        }

        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose()
        {
            RestoreDataDirectory();
            try { Directory.Delete(DataDirectory, true); }
            catch (IOException) { }
        }
    }
}
