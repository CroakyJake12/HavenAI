using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Tests;

public sealed class ChatPerformanceTraceTests
{
    [Fact]
    public void Trace_CapturesEachMilestoneOnceAndCalculatesDuration()
    {
        var operationId = Guid.NewGuid();
        var startedAt = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var trace = new ChatPerformanceTrace(operationId, startedAt);

        Assert.True(trace.TryMark(
            ChatPerformanceMilestone.SendClicked,
            timestamp: startedAt));
        Assert.True(trace.TryMark(
            ChatPerformanceMilestone.UserBubbleRendered,
            timestamp: startedAt.AddMilliseconds(40)));
        Assert.False(trace.TryMark(
            ChatPerformanceMilestone.UserBubbleRendered,
            timestamp: startedAt.AddMilliseconds(60)));

        Assert.Equal(
            (TimeSpan?)TimeSpan.FromMilliseconds(40),
            trace.DurationBetween(
                ChatPerformanceMilestone.SendClicked,
                ChatPerformanceMilestone.UserBubbleRendered));
        Assert.Equal(2, trace.Snapshot.Count);
    }

    [Fact]
    public void Trace_RejectsNegativeScalarDimensions()
    {
        var trace = new ChatPerformanceTrace(Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            trace.TryMark(
                ChatPerformanceMilestone.ContextAssemblyCompleted,
                new ChatPerformanceDimensions(ContextTokenEstimate: -1)));
    }

    [Fact]
    public async Task ExecutionTracker_MapsCoarseStagesToPerformanceMilestones()
    {
        await using var tracker = new ChatExecutionTracker();

        tracker.Update(
            ChatExecutionStage.LoadingModel,
            performanceDimensions: new ChatPerformanceDimensions(IsWarmModel: false));
        tracker.Update(ChatExecutionStage.LoadingContext);
        tracker.Update(
            ChatExecutionStage.SelectingCapabilities,
            performanceDimensions: new ChatPerformanceDimensions(
                ToolSchemaBytes: 0,
                Streaming: true,
                ToolCount: 0));
        tracker.Update(ChatExecutionStage.Generating);
        tracker.Complete();

        Assert.NotNull(tracker.Performance.Get(ChatPerformanceMilestone.SendClicked));

        var modelMark = tracker.Performance.Get(
            ChatPerformanceMilestone.ModelSelectionStarted);
        Assert.NotNull(modelMark);
        Assert.Equal(false, modelMark.Dimensions.IsWarmModel);

        var toolMark = tracker.Performance.Get(
            ChatPerformanceMilestone.ToolSelectionStarted);
        Assert.NotNull(toolMark);
        Assert.Equal(0, toolMark.Dimensions.ToolCount.GetValueOrDefault());

        Assert.NotNull(tracker.Performance.Get(
            ChatPerformanceMilestone.ProviderRequestStarted));
        Assert.NotNull(tracker.Performance.Get(
            ChatPerformanceMilestone.CompletionReceived));
    }
}
