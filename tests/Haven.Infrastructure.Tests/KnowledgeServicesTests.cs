using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class KnowledgeServicesTests
{
    [Fact]
    public async Task SchedulerQueuesVisiblePriorityTask()
    {
        var scheduler = new BackgroundLearningScheduler(new TestPrivacy(backgroundLearning: true));

        var task = await scheduler.EnqueueAsync(
            "Index Avalonia documentation",
            KnowledgeCategory.WorldKnowledge,
            BackgroundLearningPriority.Normal,
            CancellationToken.None);

        Assert.Equal(BackgroundLearningTaskStatus.Queued, task.Status);
        Assert.Single(scheduler.Snapshot());
    }

    [Fact]
    public async Task DisabledKnowledgeCategoryCannotBeQueued()
    {
        var scheduler = new BackgroundLearningScheduler(new TestPrivacy(backgroundLearning: true));
        scheduler.SetCategoryEnabled(KnowledgeCategory.LearnMe, false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.EnqueueAsync(
            "Learn preference", KnowledgeCategory.LearnMe, BackgroundLearningPriority.Low, CancellationToken.None));
    }

    [Fact]
    public async Task BackgroundLearningRequiresExplicitOptIn()
    {
        var scheduler = new BackgroundLearningScheduler(new TestPrivacy(backgroundLearning: false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.EnqueueAsync(
            "Background work", KnowledgeCategory.WorldKnowledge, BackgroundLearningPriority.Low, CancellationToken.None));
    }

    [Fact]
    public void NeverLearnIsExplicitlyRepresented()
    {
        var record = new KnowledgeRecord(
            Guid.NewGuid(), KnowledgeCategory.LearnMe, "secrets", "Secrets", "private",
            KnowledgePrivacyClass.NeverLearn, 0, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            null, "conversation", []);

        Assert.Equal(KnowledgePrivacyClass.NeverLearn, record.PrivacyClass);
    }

    private sealed class TestPrivacy(bool backgroundLearning) : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; private set; } =
            PrivacyPreferences.Default with { BackgroundLearningEnabled = backgroundLearning };

        public Task UpdateAsync(PrivacyPreferences preferences, CancellationToken cancellationToken)
        {
            Current = preferences;
            return Task.CompletedTask;
        }
    }
}
