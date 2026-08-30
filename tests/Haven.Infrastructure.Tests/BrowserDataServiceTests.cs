/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/BrowserDataServiceTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns BrowserDataServiceTests, BrowserTestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents browser data service tests and keeps its related state and behavior together.
/// </summary>
public sealed class BrowserDataServiceTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly BrowserTestPaths _paths = new();

    /// <summary>
    /// Performs the persists standard tabs bookmarks and history but not private state step owned by this component.
    /// </summary>
    [Fact]
    public async Task PersistsStandardTabsBookmarksAndHistoryButNotPrivateState()
    {
        using (var data = new BrowserDataService(_paths))
        {
            await data.AddBookmarkAsync("Haven", "https://example.com/docs", "Research", CancellationToken.None);
            await data.RecordVisitAsync("Public", "https://example.com/public", false, CancellationToken.None);
            await data.RecordVisitAsync("Private", "https://example.com/private", true, CancellationToken.None);
            await data.SaveTabsAsync([
                new BrowserTabState(Guid.NewGuid(), "Public", "https://example.com/public", BrowserTabPrivacy.Standard, "Work", DateTimeOffset.UtcNow),
                new BrowserTabState(Guid.NewGuid(), "Private", "https://example.com/private", BrowserTabPrivacy.Private, string.Empty, DateTimeOffset.UtcNow)
            ], CancellationToken.None);
        }

        using var reloaded = new BrowserDataService(_paths);
        Assert.Single(reloaded.Bookmarks);
        Assert.Single(reloaded.History);
        Assert.Equal("Public", reloaded.History[0].Title);
        Assert.Single(reloaded.Tabs);
        Assert.Equal(BrowserTabPrivacy.Standard, reloaded.Tabs[0].Privacy);
    }

    /// <summary>
    /// Performs the bookmark changes and vertical tab preference survive reload step owned by this component.
    /// </summary>
    [Fact]
    public async Task BookmarkChangesAndVerticalTabPreferenceSurviveReload()
    {
        Guid bookmarkToRemove;
        using (var data = new BrowserDataService(_paths))
        {
            await data.AddBookmarkAsync("First title", "https://example.com/docs", "Research", CancellationToken.None);
            bookmarkToRemove = Assert.Single(data.Bookmarks).Id;

            await data.AddBookmarkAsync("Updated title", "https://example.com/docs", "Reference", CancellationToken.None);
            var updated = Assert.Single(data.Bookmarks);
            Assert.Equal(bookmarkToRemove, updated.Id);
            Assert.Equal("Updated title", updated.Title);
            Assert.Equal("Reference", updated.Group);

            await data.AddBookmarkAsync("Keep", "https://example.org", "Reading", CancellationToken.None);
            await data.RemoveBookmarkAsync(bookmarkToRemove, CancellationToken.None);
            await data.SaveSettingsAsync(data.Settings with { VerticalTabs = true }, CancellationToken.None);
        }

        using var reloaded = new BrowserDataService(_paths);
        var bookmark = Assert.Single(reloaded.Bookmarks);
        Assert.Equal("Keep", bookmark.Title);
        Assert.Equal("Reading", bookmark.Group);
        Assert.True(reloaded.Settings.VerticalTabs);
    }

    /// <summary>
    /// Performs the failed persistence rolls back the in memory mutation and cleans temporary files step owned by this component.
    /// </summary>
    [Fact]
    public async Task FailedPersistenceRollsBackTheInMemoryMutationAndCleansTemporaryFiles()
    {
        using var data = new BrowserDataService(_paths);
        Directory.CreateDirectory(Path.Combine(_paths.DataDirectory, "browser-data.json"));

        await Assert.ThrowsAnyAsync<IOException>(() =>
            data.AddBookmarkAsync("Must roll back", "https://example.com/rollback", "Tests", CancellationToken.None));

        Assert.Empty(data.Bookmarks);
        Assert.Empty(Directory.EnumerateFiles(_paths.DataDirectory, "browser-data.json.tmp-*"));
    }

    /// <summary>
    /// Performs the concurrent bookmark mutations are serialized without lost updates step owned by this component.
    /// </summary>
    [Fact]
    public async Task ConcurrentBookmarkMutationsAreSerializedWithoutLostUpdates()
    {
        using (var data = new BrowserDataService(_paths))
        {
            var writes = Enumerable.Range(0, 24)
                .Select(index => data.AddBookmarkAsync($"Bookmark {index}", $"https://example.com/{index}", "Concurrent", CancellationToken.None));
            await Task.WhenAll(writes);
            Assert.Equal(24, data.Bookmarks.Count);
        }

        using var reloaded = new BrowserDataService(_paths);
        Assert.Equal(24, reloaded.Bookmarks.Count);
        Assert.Equal(24, reloaded.Bookmarks.Select(item => item.Address).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Performs the corrupt primary is quarantined and last valid backup is recovered step owned by this component.
    /// </summary>
    [Fact]
    public async Task CorruptPrimaryIsQuarantinedAndLastValidBackupIsRecovered()
    {
        using (var data = new BrowserDataService(_paths))
        {
            await data.AddBookmarkAsync("Recovery point", "https://example.com/recovery", "Recovery", CancellationToken.None);
            await data.SaveSettingsAsync(data.Settings with { VerticalTabs = true }, CancellationToken.None);
        }

        var primary = Path.Combine(_paths.DataDirectory, "browser-data.json");
        var backup = primary + ".bak";
        Assert.True(File.Exists(backup));
        await File.WriteAllTextAsync(primary, "{ definitely-not-json");

        using var recovered = new BrowserDataService(_paths);
        var bookmark = Assert.Single(recovered.Bookmarks);
        Assert.Equal("Recovery point", bookmark.Title);
        Assert.False(recovered.Settings.VerticalTabs);
        Assert.Single(Directory.EnumerateFiles(_paths.DataDirectory, "browser-data.json.corrupt-*"));
    }

    /// <summary>
    /// Performs the startup migration purges legacy private tabs and rejects unsupported future schema step owned by this component.
    /// </summary>
    [Fact]
    public void StartupMigrationPurgesLegacyPrivateTabsAndRejectsUnsupportedFutureSchema()
    {
        var primary = Path.Combine(_paths.DataDirectory, "browser-data.json");
        var standardId = Guid.NewGuid();
        var privateId = Guid.NewGuid();
        File.WriteAllText(primary, $$"""
        {
          "bookmarks": [],
          "history": [],
          "tabs": [
            { "id": "{{standardId}}", "title": "Public", "address": "https://example.com/public", "privacy": 0, "group": "", "updatedAt": "2026-07-17T01:00:00+00:00" },
            { "id": "{{privateId}}", "title": "Private", "address": "https://example.com/private", "privacy": 1, "group": "", "updatedAt": "2026-07-17T01:00:00+00:00" }
          ],
          "logins": [],
          "extensions": [],
          "settings": { "homePage": "https://www.google.com", "searchTemplate": "https://www.google.com/search?q={query}", "saveHistory": true, "offerToSaveLogins": true, "restoreTabs": true, "enableExtensions": true, "verticalTabs": false }
        }
        """);

        using (var migrated = new BrowserDataService(_paths))
        {
            var tab = Assert.Single(migrated.Tabs);
            Assert.Equal(standardId, tab.Id);
        }

        File.WriteAllText(primary, """
        { "bookmarks": [], "history": [], "tabs": [], "logins": [], "extensions": [],
          "settings": { "homePage": "https://www.google.com", "searchTemplate": "https://www.google.com/search?q={query}", "saveHistory": true, "offerToSaveLogins": true, "restoreTabs": true, "enableExtensions": true, "verticalTabs": false },
          "schemaVersion": 999 }
        """);

        using var rejected = new BrowserDataService(_paths);
        Assert.Empty(rejected.Tabs);
        Assert.Single(Directory.EnumerateFiles(_paths.DataDirectory, "browser-data.json.corrupt-*"));
    }

    [Fact]
    public async Task ResearchCheckpointPersistsPublicStateAndPrivateSessionCannotOverwriteIt()
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var publicSource = new BrowserResearchSource(
            Guid.NewGuid(), new Uri("https://example.com/public"), "Public source", "Public evidence", ["Heading"],
            capturedAt, false, false, false);
        var privateSource = new BrowserResearchSource(
            Guid.NewGuid(), new Uri("https://example.com/private"), "Private source", "Private evidence", ["Secret"],
            capturedAt, true, false, false);

        using (var data = new BrowserDataService(_paths))
        {
            await data.SaveResearchAsync(new BrowserResearchSessionState(
                "public question", "public answer [S1]", [publicSource], capturedAt), CancellationToken.None);
            await data.SaveResearchAsync(new BrowserResearchSessionState(
                "private question", "private-derived answer", [publicSource, privateSource], capturedAt), CancellationToken.None);
        }

        using var reloaded = new BrowserDataService(_paths);
        Assert.Equal("public question", reloaded.Research.Query);
        Assert.Equal("public answer [S1]", reloaded.Research.Output);
        var restored = Assert.Single(reloaded.Research.Sources);
        Assert.Equal(publicSource.Address, restored.Address);
        Assert.False(restored.IsPrivate);
    }

    [Fact]
    public async Task ClearingResearchRemovesDurableCheckpoint()
    {
        var source = new BrowserResearchSource(
            Guid.NewGuid(), new Uri("https://example.com/source"), "Source", "Evidence", [],
            DateTimeOffset.UtcNow, false, false, false);

        using (var data = new BrowserDataService(_paths))
        {
            await data.SaveResearchAsync(new BrowserResearchSessionState(
                "question", "answer", [source], DateTimeOffset.UtcNow), CancellationToken.None);
            await data.ClearResearchAsync(CancellationToken.None);
        }

        using var reloaded = new BrowserDataService(_paths);
        Assert.Empty(reloaded.Research.Sources);
        Assert.Empty(reloaded.Research.Query);
        Assert.Empty(reloaded.Research.Output);
    }

    [Fact]
    public async Task ToggleBookmarkPersistsAddThenRemove()
    {
        using (var data = new BrowserDataService(_paths))
        {
            Assert.True(await data.ToggleBookmarkAsync("Toggle me", "https://example.com/toggle", "Keyboard", CancellationToken.None));
            Assert.Single(data.Bookmarks);
            Assert.False(await data.ToggleBookmarkAsync("Ignored", "https://example.com/toggle", "Ignored", CancellationToken.None));
            Assert.Empty(data.Bookmarks);
        }
        using var reloaded = new BrowserDataService(_paths);
        Assert.Empty(reloaded.Bookmarks);
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents browser test paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class BrowserTestPaths : IAppPaths, IDisposable
    {
        public BrowserTestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-browser-tests-" + Guid.NewGuid().ToString("N"));
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
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
