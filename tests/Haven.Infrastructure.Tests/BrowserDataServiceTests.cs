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

            // Saving the same URL updates the existing bookmark instead of creating
            // a duplicate, which keeps the bookmark bar and manager in sync.
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
