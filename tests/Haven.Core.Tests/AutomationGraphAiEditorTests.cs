using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class AutomationGraphAiEditorTests
{
    [Fact]
    public async Task ProposeEditAsync_AcceptsValidatedTypedGraph()
    {
        var node = new AutomationGraphNodeDefinition(Guid.NewGuid(), BuiltInAutomationNodeCategory.Action, null, null,
            new Dictionary<string, string> { ["action"] = "emit", ["value"] = "ready" })
        { Title = "Emit", X = 40, Y = 80 };
        var response = AutomationGraphCodec.Serialize(new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion, [node], []));
        var service = new AutomationGraphAiEditor(new FakeProvider(response));

        var result = await service.ProposeEditAsync(AutomationGraphDefinition.Empty, "Add an emit action", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Graph);
        Assert.Single(result.Graph!.Nodes);
        Assert.Equal(node.Id, result.Graph.Nodes[0].Id);
    }

    [Fact]
    public async Task ProposeEditAsync_RejectsInvalidCycleWithoutReturningGraph()
    {
        var first = ActionNode();
        var second = ActionNode();
        var response = AutomationGraphCodec.Serialize(new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion,
            [first, second],
            [
                new AutomationGraphEdgeDefinition(first.Id, second.Id),
                new AutomationGraphEdgeDefinition(second.Id, first.Id)
            ]));
        var service = new AutomationGraphAiEditor(new FakeProvider(response));

        var result = await service.ProposeEditAsync(AutomationGraphDefinition.Empty, "Make a loop", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("cycle", result.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProposeEditAsync_ProviderDiscoveryFailureReturnsNoMutation()
    {
        var service = new AutomationGraphAiEditor(new FakeProvider("", new InvalidOperationException("No API key configured.")));
        var current = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [ActionNode()], []);

        var result = await service.ProposeEditAsync(current, "Change the graph", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Graph);
        Assert.Contains("No API key", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Single(current.Nodes);
    }

    private static AutomationGraphNodeDefinition ActionNode() =>
        new(Guid.NewGuid(), BuiltInAutomationNodeCategory.Action, null, null,
            new Dictionary<string, string> { ["action"] = "emit", ["value"] = "ready" })
        { Title = "Emit" };

    private sealed class FakeProvider(string response, Exception? discoveryFailure = null) : IProviderModelClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
        {
            if (discoveryFailure is not null) return Task.FromException<IReadOnlyList<ModelDescriptor>>(discoveryFailure);
            IReadOnlySet<ToolCapability> capabilities = new HashSet<ToolCapability> { ToolCapability.Text };
            return Task.FromResult<IReadOnlyList<ModelDescriptor>>(
                [new ModelDescriptor("fake-text", 0, "test", "", "", capabilities, DateTimeOffset.UtcNow)]);
        }
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return await CompleteAsync(request, cancellationToken);
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => Task.FromResult(response);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(response, []));
    }
}
