/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/ModelRouterTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ModelRouterTests, StubRegistry. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents model router tests and keeps its related state and behavior together.
/// </summary>
public sealed class ModelRouterTests
{
    /// <summary>
    /// Performs the automatic routing prefers compatible local model step owned by this component.
    /// </summary>
    [Fact]
    public async Task AutomaticRoutingPrefersCompatibleLocalModel()
    {
        var local = Descriptor("ollama", true, "qwen", ToolCapability.Text, ToolCapability.Tools);
        var cloud = Descriptor("openai", false, "cloud", ToolCapability.Text, ToolCapability.Tools, ToolCapability.Vision);
        var router = new ModelRouter(new StubRegistry([cloud, local]));

        var result = await router.RouteAsync(new ModelRoutingRequest(null, new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools },
            new ModelRoutingPolicy(ModelRoutingMode.Automatic, true, true, [])), CancellationToken.None);

        Assert.Equal(local.Key, result.Model.Key);
        Assert.Contains("local", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the manual routing uses first compatible fallback step owned by this component.
    /// </summary>
    [Fact]
    public async Task ManualRoutingUsesFirstCompatibleFallback()
    {
        var textOnly = Descriptor("ollama", true, "text", ToolCapability.Text);
        var vision = Descriptor("openai", false, "vision", ToolCapability.Text, ToolCapability.Vision);
        var router = new ModelRouter(new StubRegistry([textOnly, vision]));

        var result = await router.RouteAsync(new ModelRoutingRequest(textOnly, new HashSet<ToolCapability> { ToolCapability.Vision },
            new ModelRoutingPolicy(ModelRoutingMode.ManualFallback, true, true, [textOnly.Key, vision.Key])), CancellationToken.None);

        Assert.Equal(vision.Key, result.Model.Key);
        Assert.True(result.UsedFallback);
    }

    /// <summary>
    /// Performs the local only policy rejects cloud only capability step owned by this component.
    /// </summary>
    [Fact]
    public async Task LocalOnlyPolicyRejectsCloudOnlyCapability()
    {
        var cloud = Descriptor("openai", false, "vision", ToolCapability.Text, ToolCapability.Vision);
        var router = new ModelRouter(new StubRegistry([cloud]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => router.RouteAsync(new ModelRoutingRequest(null,
            new HashSet<ToolCapability> { ToolCapability.Vision }, new ModelRoutingPolicy(ModelRoutingMode.Automatic, true, false, [])), CancellationToken.None));
    }

    /// <summary>
    /// Performs the descriptor step owned by this component.
    /// </summary>
    private static ProviderModelDescriptor Descriptor(string provider, bool local, string name, params ToolCapability[] capabilities) =>
        new(provider, local, new ModelDescriptor(name, 0, provider, string.Empty, string.Empty, capabilities.ToHashSet(), DateTimeOffset.UtcNow));

    /// <summary>
    /// Represents stub registry and keeps its related state and behavior together.
    /// </summary>
    private sealed class StubRegistry(IReadOnlyList<ProviderModelDescriptor> models) : IModelProviderRegistry
    {
        /// <summary>
        /// Gets or updates providers, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<IModelProvider> Providers => [];
        /// <summary>
        /// Performs the find step owned by this component.
        /// </summary>
        public IModelProvider? Find(string providerId) => null;
        /// <summary>
        /// Retrieves required for the current operation.
        /// </summary>
        public IModelProvider GetRequired(string providerId) => throw new NotSupportedException();
        /// <summary>
        /// Retrieves models async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult(models);
    }
}
