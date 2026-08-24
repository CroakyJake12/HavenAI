using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class PrivacyPreferenceStoreTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task DefaultsDisableLearningAndSharingAndPersistLocalOnlyChoice()
    {
        var store = new PrivacyPreferenceStore(_paths);
        Assert.False(store.Current.BackgroundLearningEnabled);
        Assert.False(store.Current.ModelImprovementSharingEnabled);

        await store.UpdateAsync(
            store.Current with { LocalOnlyMode = true },
            CancellationToken.None);

        var restarted = new PrivacyPreferenceStore(_paths);
        Assert.True(restarted.Current.LocalOnlyMode);
        Assert.False(restarted.Current.BackgroundLearningEnabled);
        Assert.False(restarted.Current.ModelImprovementSharingEnabled);
        Assert.True(restarted.Current.UpdatedAt > DateTimeOffset.UnixEpoch);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-privacy-tests-" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
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
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
