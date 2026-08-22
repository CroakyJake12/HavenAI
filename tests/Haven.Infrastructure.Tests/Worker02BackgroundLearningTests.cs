using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class Worker02BackgroundLearningTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task Scheduler_persists_controls_and_task_state()
    {
        var database = await CreateDatabaseAsync();
        var first = new BackgroundLearningScheduler(database);
        await first.InitializeAsync(CancellationToken.None);
        await first.SetModeAsync(BackgroundLearningMode.Proactive, CancellationToken.None);
        await first.SetCategoryEnabledAsync(KnowledgeCategory.LearnMe, false, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.EnqueueAsync("Learn preference", KnowledgeCategory.LearnMe, BackgroundLearningPriority.Low, CancellationToken.None));
        await first.SetCategoryEnabledAsync(KnowledgeCategory.LearnMe, true, CancellationToken.None);
        var task = await first.EnqueueAsync("Learn preference", KnowledgeCategory.LearnMe, BackgroundLearningPriority.Low, CancellationToken.None);
        Assert.True(await first.PauseAsync(task.Id, CancellationToken.None));

        var reopened = new BackgroundLearningScheduler(database);
        await reopened.InitializeAsync(CancellationToken.None);
        var snapshot = await reopened.GetSnapshotAsync(CancellationToken.None);
        Assert.Equal(BackgroundLearningMode.Proactive, snapshot.Mode);
        Assert.Contains(snapshot.Tasks, item => item.Id == task.Id && item.Status == BackgroundLearningTaskStatus.Paused);
        await reopened.SetGlobalEnabledAsync(false, CancellationToken.None);

        var secondReopen = new BackgroundLearningScheduler(database);
        await secondReopen.InitializeAsync(CancellationToken.None);
        Assert.False(secondReopen.IsGloballyEnabled);
    }

    [Fact]
    public async Task Correction_and_rejection_preserve_user_authority()
    {
        var (_, library, _, maintenance) = await CreateServicesAsync();
        var now = DateTimeOffset.UtcNow;
        var original = NewKnowledge("Editor", "The user prefers Editor A.", now, KnowledgeFreshnessClass.Changing);
        await library.UpsertAsync(original, original.Summary, CancellationToken.None);
        var corrected = await library.CorrectAsync(original.Id, "The user prefers Editor B.", "User correction", CancellationToken.None);
        Assert.Equal(KnowledgeRecordStatus.Superseded, (await library.GetAsync(original.Id, CancellationToken.None))!.Status);
        Assert.Equal(KnowledgeOrigin.Explicit, corrected.Origin);
        Assert.Equal(original.Id, corrected.SupersedesId);

        var rejected = NewKnowledge("Drink", "The user prefers coffee.", now, KnowledgeFreshnessClass.Changing);
        await library.UpsertAsync(rejected, rejected.Summary, CancellationToken.None);
        Assert.True(await library.RejectAsync(rejected.Id, "Incorrect inference", CancellationToken.None));
        var reinferred = rejected with { Id = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => library.UpsertAsync(reinferred, reinferred.Summary, CancellationToken.None));

        await maintenance.CleanupAsync(CancellationToken.None);
        Assert.Null(await library.GetAsync(rejected.Id, CancellationToken.None));
        var reinferredAfterCleanup = rejected with { Id = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => library.UpsertAsync(reinferredAfterCleanup, reinferredAfterCleanup.Summary, CancellationToken.None));
    }

    [Fact]
    public async Task Secrets_are_rejected_and_api_metadata_persists()
    {
        var (_, library, apiBank, _) = await CreateServicesAsync();
        var now = DateTimeOffset.UtcNow;
        var secret = NewKnowledge("Token", "access_token=super-secret-token-value", now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => library.UpsertAsync(secret, secret.Summary, CancellationToken.None));

        var api = new ApiBankRecord(Guid.NewGuid(), "Example App", "Example API", "v1", "https://example.test/docs", "[{\"id\":\"lookup\"}]", "OAuth 2.0", true, true, null, "[]", null, now, "hash", "[{\"name\":\"query\"}]", "[{\"name\":\"result\"}]", "[\"read:data\"]", "100 requests/minute", "Free tier", "Lookup capability", "Internet required", "Queue while offline", false, "https://example.test/docs");
        await apiBank.UpsertAsync(api, CancellationToken.None);
        Assert.True(await apiBank.SetPinnedAsync(api.Id, true, CancellationToken.None));
        var stored = Assert.Single(await apiBank.SearchAsync("Example API", CancellationToken.None));
        Assert.Equal("100 requests/minute", stored.RateLimits);
        Assert.Equal("[\"read:data\"]", stored.ScopesJson);
        Assert.True(stored.IsPinned);

        await Assert.ThrowsAsync<InvalidOperationException>(() => apiBank.UpsertAsync(api with { Id = Guid.NewGuid(), Authentication = "client_secret=must-not-be-stored" }, CancellationToken.None));
    }

    [Fact]
    public async Task Cleanup_protects_pinned_items_and_exposes_separate_caps()
    {
        var (_, library, _, maintenance) = await CreateServicesAsync();
        var now = DateTimeOffset.UtcNow;
        var expired = NewKnowledge("Expired", "Expired changing fact", now.AddDays(-5), KnowledgeFreshnessClass.Changing) with { ExpiresAt = now.AddDays(-1) };
        var pinned = NewKnowledge("Pinned", "Pinned changing fact", now.AddDays(-5), KnowledgeFreshnessClass.Changing) with { ExpiresAt = now.AddDays(-1), IsPinned = true };
        await library.UpsertAsync(expired, expired.Summary, CancellationToken.None);
        await library.UpsertAsync(pinned, pinned.Summary, CancellationToken.None);
        var result = await maintenance.CleanupAsync(CancellationToken.None);
        Assert.True(result.KnowledgeRemoved >= 1);
        Assert.Null(await library.GetAsync(expired.Id, CancellationToken.None));
        Assert.NotNull(await library.GetAsync(pinned.Id, CancellationToken.None));
        var storage = await maintenance.GetStorageAsync(CancellationToken.None);
        Assert.Equal(512L * 1024 * 1024, storage.KnowledgeLimitBytes);
        Assert.Equal(1024L * 1024 * 1024, storage.ApiBankLimitBytes);
    }

    private async Task<SqliteDatabase> CreateDatabaseAsync()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        return database;
    }

    private async Task<(SqliteDatabase, KnowledgeLibraryService, ApiBankService, KnowledgeMaintenanceService)> CreateServicesAsync()
    {
        var database = await CreateDatabaseAsync();
        var library = new KnowledgeLibraryService(database, new RetrievalIndexService(database, new LocalHashEmbeddingService()));
        var apiBank = new ApiBankService(database);
        return (database, library, apiBank, new KnowledgeMaintenanceService(database, library, apiBank));
    }

    private static KnowledgeRecord NewKnowledge(string title, string summary, DateTimeOffset now, KnowledgeFreshnessClass freshness = KnowledgeFreshnessClass.Durable)
        => new(Guid.NewGuid(), KnowledgeCategory.LearnMe, "preferences", title, summary, KnowledgePrivacyClass.Normal, .8, false, now, now, null, "conversation", [], freshness, now, "user", KnowledgeRecordStatus.Active, KnowledgeOrigin.Inferred);

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-background-learning-tests-" + Guid.NewGuid().ToString("N"));
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
