using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class CapabilityPreflightTests
{
    [Fact]
    public void VisionAttachmentStopsTextOnlyModelAndSuggestsVisionModel()
    {
        var text = Model("text", ToolCapability.Text);
        var vision = Model("vision", ToolCapability.Text, ToolCapability.Vision);
        var result = new CapabilityPreflightService().Evaluate(text, [], true, [text, vision]);
        Assert.False(result.IsCompatible);
        Assert.Contains(result.Missing, x => x.Capability == ToolCapability.Vision);
        Assert.Equal("vision", result.SuggestedModel?.Name);
    }

    [Fact]
    public void ToolCapableModelCanUseBrowserPlugin()
    {
        var model = Model("tools", ToolCapability.Text, ToolCapability.Tools);
        var result = new CapabilityPreflightService().Evaluate(model, [new ActivePlugin("BrowserUse", "browser-use", false)], false, [model]);
        Assert.True(result.IsCompatible);
    }

    private static ModelDescriptor Model(string name, params ToolCapability[] capabilities) =>
        new(name, 1, "test", "test", "test", capabilities.ToHashSet(), DateTimeOffset.UtcNow);
}
