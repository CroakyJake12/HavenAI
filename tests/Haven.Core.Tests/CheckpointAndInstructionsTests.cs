using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class CheckpointServiceTests
{
    private sealed class FakeRepository : ICheckpointRepository
    {
        public CheckpointInfo? Saved;
        public long LatestSequence = 5;
        public List<WorkspaceRestoreEntry> Versions { get; } = [];

        public Task SaveAsync(CheckpointInfo checkpoint, CancellationToken cancellationToken)
        {
            Saved = checkpoint;
            return Task.CompletedTask;
        }
        public Task<CheckpointInfo?> GetLatestAsync(Guid? conversationId, string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(Saved);
        public Task<CheckpointInfo?> GetAsync(Guid checkpointId, CancellationToken cancellationToken)
            => Task.FromResult(Saved is not null && Saved.Id == checkpointId ? Saved : null);
        public Task<long> GetLatestVersionSequenceAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(LatestSequence);
        public Task<IReadOnlyList<WorkspaceRestoreEntry>> GetVersionsSinceAsync(string workspaceRoot, long sequence, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<WorkspaceRestoreEntry>>(Versions.Where(item => item.Sequence > sequence).ToList());
        public Task<WorkspaceRestoreEntry?> GetLatestVersionAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(Versions.OrderByDescending(item => item.Sequence).FirstOrDefault());
    }

    private sealed class FakeRestorer : ICheckpointRestorer
    {
        public CheckpointRestorePlan? LastPlan;
        public IReadOnlyList<string> Restored = [];
        public Task<IReadOnlyList<string>> RestoreAsync(string workspaceRoot, CheckpointRestorePlan plan, CancellationToken cancellationToken)
        {
            LastPlan = plan;
            return Task.FromResult(Restored);
        }
    }

    [Fact]
    public async Task OffModeNeverCreatesCheckpoints()
    {
        var repository = new FakeRepository();
        var service = new CheckpointService(repository, new FakeRestorer());

        var checkpoint = await service.EnsureBeforeMutationAsync(
            Guid.NewGuid(), null, null, "C:\\ws", CheckpointMode.Off, CancellationToken.None);

        Assert.Null(checkpoint);
        Assert.Null(repository.Saved);
    }

    [Fact]
    public async Task OneCheckpointPerExecutionIsReused()
    {
        var repository = new FakeRepository();
        var service = new CheckpointService(repository, new FakeRestorer());
        var executionId = Guid.NewGuid();

        var first = await service.EnsureBeforeMutationAsync(executionId, null, null, "C:\\ws", CheckpointMode.AgenticTasks, CancellationToken.None);
        var second = await service.EnsureBeforeMutationAsync(executionId, null, null, "C:\\ws", CheckpointMode.AgenticTasks, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(5, first!.StartSequence);
    }

    [Fact]
    public async Task RestorePlanAppliesLatestBeforeContentPerPath()
    {
        var repository = new FakeRepository();
        repository.Versions.AddRange(
        [
            new WorkspaceRestoreEntry(6, "src/a.cs", 0, "original a", "edited a 1"),
            new WorkspaceRestoreEntry(7, "src/b.cs", 0, "original b", "edited b"),
            new WorkspaceRestoreEntry(8, "src/a.cs", 0, "edited a 1", "edited a 2")
        ]);
        var restorer = new FakeRestorer { Restored = ["src/a.cs", "src/b.cs"] };
        var service = new CheckpointService(repository, restorer);
        var checkpointId = Guid.NewGuid();
        repository.Saved = new CheckpointInfo(checkpointId, null, null, "C:\\ws", "label", CheckpointMode.Always, 5, DateTimeOffset.UtcNow);

        var restored = await service.RestoreCheckpointAsync(checkpointId, CancellationToken.None);

        Assert.Equal(2, restored.Count);
        Assert.NotNull(restorer.LastPlan);
        // Latest before-content per path wins: a.cs returns to "original a" (sequence 8's before).
        Assert.Equal("edited a 1", restorer.LastPlan!.PathToBeforeContent["src/a.cs"]);
        Assert.Equal("original b", restorer.LastPlan.PathToBeforeContent["src/b.cs"]);
    }

    [Fact]
    public async Task UndoLastActionReversesOnlyTheMostRecentMutation()
    {
        var repository = new FakeRepository();
        repository.Versions.Add(new WorkspaceRestoreEntry(9, "src/only.cs", 0, "before", "after"));
        var restorer = new FakeRestorer { Restored = ["src/only.cs"] };
        var service = new CheckpointService(repository, restorer);

        var undone = await service.UndoLastActionAsync("C:\\ws", CancellationToken.None);

        Assert.True(undone);
        Assert.NotNull(restorer.LastPlan);
        var entry = Assert.Single(restorer.LastPlan!.PathToBeforeContent);
        Assert.Equal("src/only.cs", entry.Key);
        Assert.Equal("before", entry.Value);
    }
}

public sealed class ProjectAgentInstructionsTests
{
    [Fact]
    public void MergeOrdersBroadestToMostSpecific()
    {
        var merged = ProjectAgentInstructions.Merge(
        [
            new ProjectInstructionFile("tasks/agents/AGENT.md", 2, "Nested rule."),
            new ProjectInstructionFile("AGENT.md", 0, "Root rule."),
            new ProjectInstructionFile("agent.md", 1, "Mid rule.")
        ]);

        Assert.StartsWith("From AGENT.md:\nRoot rule.", merged);
        Assert.Contains("From agent.md:\nMid rule.", merged);
        Assert.EndsWith("Nested rule.", merged);
        Assert.True(merged.IndexOf("Root rule.", StringComparison.Ordinal) < merged.IndexOf("Mid rule.", StringComparison.Ordinal));
    }
}
