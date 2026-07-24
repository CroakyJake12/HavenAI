using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ChatExecutionPolicyTests
{
    [Fact]
    public void GenericGreeting_UsesStreamingTextWithoutTools()
    {
        var selection = ChatCapabilitySelection.Create("hello");

        Assert.True(selection.IsGenericConversation);
        Assert.Contains(ToolCapability.Text, selection.Required);
        Assert.Contains(ToolCapability.Streaming, selection.Required);
        Assert.DoesNotContain(ToolCapability.Tools, selection.Required);
        Assert.Empty(selection.Explicit);
    }

    [Fact]
    public void ExplicitCapabilities_AreAlwaysRequired()
    {
        var selection = ChatCapabilitySelection.Create(
            "hello",
            [ToolCapability.Browser, ToolCapability.WebSearch]);

        Assert.False(selection.IsGenericConversation);
        Assert.Contains(ToolCapability.Browser, selection.Required);
        Assert.Contains(ToolCapability.WebSearch, selection.Required);
        Assert.Contains(ToolCapability.Browser, selection.Explicit);
    }

    [Fact]
    public void FallbackSelector_PrefersClosestCapableModelInSameFamily()
    {
        var selected = Model("tiny", 2_000, "qwen", ToolCapability.Text, ToolCapability.Streaming);
        var close = Model("close", 3_000, "qwen", ToolCapability.Text, ToolCapability.Streaming, ToolCapability.Tools);
        var far = Model("far", 20_000, "qwen", ToolCapability.Text, ToolCapability.Streaming, ToolCapability.Tools);
        var otherFamily = Model("other", 2_100, "llama", ToolCapability.Text, ToolCapability.Streaming, ToolCapability.Tools);

        var result = ChatModelFallbackSelector.Select(
            selected,
            [otherFamily, far, close],
            new HashSet<ToolCapability>
            {
                ToolCapability.Text,
                ToolCapability.Streaming,
                ToolCapability.Tools
            });

        Assert.Same(close, result);
    }

    [Theory]
    [InlineData("45 minutes", 45)]
    [InlineData("about 2 hours", 120)]
    [InlineData("1 hour 20 minutes", 80)]
    public void EtaParser_AcceptsClearDurations(string value, int expectedMinutes)
    {
        var parsed = ChatEtaFormatter.TryParseClearEstimate(value, out var estimate);

        Assert.True(parsed);
        Assert.Equal(expectedMinutes, (int)estimate.TotalMinutes);
    }

    [Theory]
    [InlineData("It is hard to estimate")]
    [InlineData("unknown")]
    [InlineData("10-20 minutes")]
    [InlineData("it depends")]
    public void EtaParser_RejectsVagueOrRangedAnswers(string value)
    {
        Assert.False(ChatEtaFormatter.TryParseClearEstimate(value, out _));
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
