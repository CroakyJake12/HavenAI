using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed class BrowserDataServiceTests : IDisposable
{
    private readonly BrowserTestPaths _paths = new();

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

    public void Dispose() => _paths.Dispose();

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
        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
