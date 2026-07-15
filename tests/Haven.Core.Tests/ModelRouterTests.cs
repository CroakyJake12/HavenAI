using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ModelRouterTests
{
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

    [Fact]
    public async Task LocalOnlyPolicyRejectsCloudOnlyCapability()
    {
        var cloud = Descriptor("openai", false, "vision", ToolCapability.Text, ToolCapability.Vision);
        var router = new ModelRouter(new StubRegistry([cloud]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => router.RouteAsync(new ModelRoutingRequest(null,
            new HashSet<ToolCapability> { ToolCapability.Vision }, new ModelRoutingPolicy(ModelRoutingMode.Automatic, true, false, [])), CancellationToken.None));
    }

    private static ProviderModelDescriptor Descriptor(string provider, bool local, string name, params ToolCapability[] capabilities) =>
        new(provider, local, new ModelDescriptor(name, 0, provider, string.Empty, string.Empty, capabilities.ToHashSet(), DateTimeOffset.UtcNow));

    private sealed class StubRegistry(IReadOnlyList<ProviderModelDescriptor> models) : IModelProviderRegistry
    {
        public IReadOnlyList<IModelProvider> Providers => [];
        public IModelProvider? Find(string providerId) => null;
        public IModelProvider GetRequired(string providerId) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult(models);
    }
}
