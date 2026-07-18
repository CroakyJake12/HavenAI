/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/BrowserSitePermissionStoreTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns BrowserSitePermissionStoreTests, PermissionTestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Browser;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents browser site permission store tests and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserSitePermissionStoreTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly PermissionTestPaths _paths = new();

    /// <summary>
    /// Performs the persists exact origin decisions and ask removes the override step owned by this component.
    /// </summary>
    [Fact]
    public async Task PersistsExactOriginDecisionsAndAskRemovesTheOverride()
    {
        var origin = new Uri("https://Example.COM:443/some/path?query=1");
        using (var store = new BrowserSitePermissionStore(_paths))
        {
            await store.SetDecisionAsync(origin, BrowserSitePermissionKind.Camera, BrowserSitePermissionDecision.Allow, CancellationToken.None);
            Assert.Equal(BrowserSitePermissionDecision.Allow, store.GetDecision(new Uri("https://example.com/other"), BrowserSitePermissionKind.Camera));
            Assert.Equal(BrowserSitePermissionDecision.Ask, store.GetDecision(new Uri("https://sub.example.com"), BrowserSitePermissionKind.Camera));
        }

        using (var reloaded = new BrowserSitePermissionStore(_paths))
        {
            var permission = Assert.Single(reloaded.Permissions);
            Assert.Equal("https://example.com", permission.Origin);
            Assert.Equal(BrowserSitePermissionDecision.Allow, permission.Decision);
            await reloaded.SetDecisionAsync(origin, BrowserSitePermissionKind.Camera, BrowserSitePermissionDecision.Ask, CancellationToken.None);
            Assert.Empty(reloaded.Permissions);
            Assert.Equal(BrowserSitePermissionDecision.Ask, reloaded.GetDecision(origin, BrowserSitePermissionKind.Camera));
            Assert.Equal(2, reloaded.Audit.Count);
        }
    }

    /// <summary>
    /// Performs the rejects non web and credential bearing origins step owned by this component.
    /// </summary>
    [Fact]
    public void RejectsNonWebAndCredentialBearingOrigins()
    {
        Assert.Throws<ArgumentException>(() => BrowserSitePermissionStore.CanonicalOrigin(new Uri("file:///C:/secret.txt")));
        Assert.Throws<ArgumentException>(() => BrowserSitePermissionStore.CanonicalOrigin(new Uri("https://user:password@example.com")));
        Assert.Equal("http://example.com:8080", BrowserSitePermissionStore.CanonicalOrigin(new Uri("http://EXAMPLE.com:8080/path")));
    }

    /// <summary>
    /// Performs the concurrent mutations are serialized without lost decisions step owned by this component.
    /// </summary>
    [Fact]
    public async Task ConcurrentMutationsAreSerializedWithoutLostDecisions()
    {
        using (var store = new BrowserSitePermissionStore(_paths))
        {
            var writes = Enumerable.Range(0, 40).Select(index => store.SetDecisionAsync(
                new Uri($"https://site-{index}.example"),
                BrowserSitePermissionKind.Notifications,
                index % 2 == 0 ? BrowserSitePermissionDecision.Allow : BrowserSitePermissionDecision.Deny,
                CancellationToken.None));
            await Task.WhenAll(writes);
            Assert.Equal(40, store.Permissions.Count);
        }

        using var reloaded = new BrowserSitePermissionStore(_paths);
        Assert.Equal(40, reloaded.Permissions.Count);
        Assert.Equal(40, reloaded.Permissions.Select(item => item.Origin).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Performs the revoke origin resets every permission and records audit step owned by this component.
    /// </summary>
    [Fact]
    public async Task RevokeOriginResetsEveryPermissionAndRecordsAudit()
    {
        var origin = new Uri("https://example.com/page");
        using var store = new BrowserSitePermissionStore(_paths);
        await store.SetDecisionAsync(origin, BrowserSitePermissionKind.Camera, BrowserSitePermissionDecision.Allow, CancellationToken.None);
        await store.SetDecisionAsync(origin, BrowserSitePermissionKind.Microphone, BrowserSitePermissionDecision.Deny, CancellationToken.None);

        await store.RevokeOriginAsync(origin, CancellationToken.None);

        Assert.Empty(store.Permissions);
        Assert.Equal(BrowserSitePermissionDecision.Ask, store.GetDecision(origin, BrowserSitePermissionKind.Camera));
        Assert.Contains(store.Audit, item => item.Kind == BrowserSitePermissionKind.Camera && item.Decision == BrowserSitePermissionDecision.Ask);
        Assert.Contains(store.Audit, item => item.Kind == BrowserSitePermissionKind.Microphone && item.Decision == BrowserSitePermissionDecision.Ask);
    }

    /// <summary>
    /// Performs the failed write rolls back in memory and cleans temporary files step owned by this component.
    /// </summary>
    [Fact]
    public async Task FailedWriteRollsBackInMemoryAndCleansTemporaryFiles()
    {
        using var store = new BrowserSitePermissionStore(_paths);
        Directory.CreateDirectory(Path.Combine(_paths.DataDirectory, "browser-site-permissions.json"));

        await Assert.ThrowsAnyAsync<IOException>(() => store.SetDecisionAsync(
            new Uri("https://example.com"), BrowserSitePermissionKind.Geolocation,
            BrowserSitePermissionDecision.Allow, CancellationToken.None));

        Assert.Empty(store.Permissions);
        Assert.Empty(store.Audit);
        Assert.Empty(Directory.EnumerateFiles(_paths.DataDirectory, "browser-site-permissions.json.tmp-*"));
    }

    /// <summary>
    /// Performs the corrupt primary is quarantined and last valid backup is recovered step owned by this component.
    /// </summary>
    [Fact]
    public async Task CorruptPrimaryIsQuarantinedAndLastValidBackupIsRecovered()
    {
        var origin = new Uri("https://example.com");
        using (var store = new BrowserSitePermissionStore(_paths))
        {
            await store.SetDecisionAsync(origin, BrowserSitePermissionKind.Notifications, BrowserSitePermissionDecision.Allow, CancellationToken.None);
            await store.SetDecisionAsync(origin, BrowserSitePermissionKind.Notifications, BrowserSitePermissionDecision.Deny, CancellationToken.None);
        }

        var primary = Path.Combine(_paths.DataDirectory, "browser-site-permissions.json");
        Assert.True(File.Exists(primary + ".bak"));
        await File.WriteAllTextAsync(primary, "{ invalid-json");

        using var recovered = new BrowserSitePermissionStore(_paths);
        Assert.Equal(BrowserSitePermissionDecision.Allow, recovered.GetDecision(origin, BrowserSitePermissionKind.Notifications));
        Assert.Single(Directory.EnumerateFiles(_paths.DataDirectory, "browser-site-permissions.json.corrupt-*"));
    }

    /// <summary>
    /// Performs the unsupported future schema is quarantined and fails closed to ask step owned by this component.
    /// </summary>
    [Fact]
    public void UnsupportedFutureSchemaIsQuarantinedAndFailsClosedToAsk()
    {
        var primary = Path.Combine(_paths.DataDirectory, "browser-site-permissions.json");
        File.WriteAllText(primary, """
        {
          "schemaVersion": 999,
          "permissions": [
            { "origin": "https://example.com", "kind": 0, "decision": 1, "updatedAt": "2026-07-17T00:00:00+00:00" }
          ],
          "audit": []
        }
        """);

        using var store = new BrowserSitePermissionStore(_paths);
        Assert.Empty(store.Permissions);
        Assert.Equal(BrowserSitePermissionDecision.Ask,
            store.GetDecision(new Uri("https://example.com"), BrowserSitePermissionKind.Camera));
        Assert.Single(Directory.EnumerateFiles(_paths.DataDirectory, "browser-site-permissions.json.corrupt-*"));
    }

    /// <summary>
    /// Performs the provider returns one store for the same data directory step owned by this component.
    /// </summary>
    [Fact]
    public void ProviderReturnsOneStoreForTheSameDataDirectory()
    {
        Assert.Same(BrowserSitePermissionStoreProvider.Get(_paths), BrowserSitePermissionStoreProvider.Get(_paths));
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents permission test paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class PermissionTestPaths : IAppPaths, IDisposable
    {
        public PermissionTestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-browser-permission-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
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
            try { Directory.Delete(DataDirectory, true); }
            catch (IOException) { }
        }
    }
}
