/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/CapabilityPreflightTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns CapabilityPreflightTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents capability preflight tests and keeps its related state and behavior together.
/// </summary>
public sealed class CapabilityPreflightTests
{
    /// <summary>
    /// Performs the vision attachment stops text only model and suggests vision model step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the tool capable model can use browser plugin step owned by this component.
    /// </summary>
    [Fact]
    public void ToolCapableModelCanUseBrowserPlugin()
    {
        var model = Model("tools", ToolCapability.Text, ToolCapability.Tools);
        var result = new CapabilityPreflightService().Evaluate(model, [new ActivePlugin("BrowserUse", "browser-use", false)], false, [model]);
        Assert.True(result.IsCompatible);
    }

    /// <summary>
    /// Performs the model step owned by this component.
    /// </summary>
    private static ModelDescriptor Model(string name, params ToolCapability[] capabilities) =>
        new(name, 1, "test", "test", "test", capabilities.ToHashSet(), DateTimeOffset.UtcNow);
}
