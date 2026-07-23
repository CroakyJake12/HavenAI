using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ResponseProgressTests
{
    [Fact]
    public void DisplayText_IncludesClearEta()
    {
        var tracker = new ResponseProgressTracker();
        tracker.Update(ResponseProgressStage.Thinking, "Thinking");
        tracker.SetEta(TimeSpan.FromMinutes(45));

        Assert.Equal("Thinking. ETA for task: 45 minutes", tracker.DisplayText);
    }

    [Fact]
    public void ActivityLog_KeepsMeaningfulBoundedEntries()
    {
        var tracker = new ResponseProgressTracker();

        for (var index = 0; index < 105; index++)
        {
            tracker.Update(ResponseProgressStage.InspectingCode, "Inspecting code", $"Checked component {index}.");
        }

        Assert.Equal(100, tracker.Entries.Count);
        Assert.Equal("Checked component 5.", tracker.Entries[0].Detail);
    }

    [Fact]
    public void TerminalState_HidesIndicator()
    {
        var tracker = new ResponseProgressTracker();

        tracker.Complete();

        Assert.True(tracker.IsTerminal);
        Assert.False(tracker.ShouldShow);
    }
}

public sealed class ChatCapabilitySelectorTests
{
    private static readonly IReadOnlyList<OllamaToolDefinition> Tools =
    [
        Tool("browser_search"),
        Tool("workspace_read_file"),
        Tool("computer_launch_app"),
        Tool("automation_create")
    ];

    [Fact]
    public void GenericConversation_SelectsNoTools()
    {
        var selected = new ChatCapabilitySelector().Select("Hello there", Tools);

        Assert.Empty(selected);
    }

    [Fact]
    public void ExplicitSelection_WinsOverInference()
    {
        var selected = new ChatCapabilitySelector().Select(
            "Hello there",
            Tools,
            ["browser_search"]);

        Assert.Collection(selected, tool => Assert.Equal("browser_search", tool.Name));
    }

    [Fact]
    public void WorkspaceRequest_SelectsOnlyWorkspaceTools()
    {
        var selected = new ChatCapabilitySelector().Select("Inspect this project file", Tools);

        Assert.Collection(selected, tool => Assert.Equal("workspace_read_file", tool.Name));
    }

    private static OllamaToolDefinition Tool(string name) =>
        new(name, name, new Dictionary<string, OllamaToolParameter>());
}

public sealed class CompatibleModelFallbackSelectorTests
{
    [Fact]
    public void CompatibleSelectedModel_IsPreserved()
    {
        var selected = Model("small", 2_000_000_000, "qwen", ToolCapability.Tools);

        var result = new CompatibleModelFallbackSelector().Select(
            selected,
            [selected],
            [ToolCapability.Tools]);

        Assert.NotNull(result);
        Assert.False(result!.IsFallback);
        Assert.Same(selected, result.ActiveModel);
        Assert.Same(selected, result.RestoreModel);
    }

    [Fact]
    public void ClosestCompatibleModel_IsTemporaryFallback()
    {
        var selected = Model("small", 2_000_000_000, "qwen");
        var near = Model("near", 3_000_000_000, "qwen", ToolCapability.Tools);
        var far = Model("far", 12_000_000_000, "llama", ToolCapability.Tools);

        var result = new CompatibleModelFallbackSelector().Select(
            selected,
            [selected, far, near],
            [ToolCapability.Tools]);

        Assert.NotNull(result);
        Assert.True(result!.IsFallback);
        Assert.Same(near, result.ActiveModel);
        Assert.Same(selected, result.RestoreModel);
    }

    private static ModelDescriptor Model(
        string name,
        long size,
        string family,
        params ToolCapability[] capabilities) =>
        new(
            name,
            size,
            family,
            string.Empty,
            string.Empty,
            capabilities.ToHashSet(),
            DateTimeOffset.UtcNow);
}
