using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Catalog;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class AgentsHavenSceneTests
{
    [Fact]
    public async Task Scene_projects_persisted_agents_through_DynamicUI()
    {
        var now = DateTimeOffset.UtcNow;
        var builtIn = Agent(Guid.NewGuid(), "General", "General helper", "default", true, now);
        var custom = Agent(Guid.NewGuid(), "Revision coach", "Helps with revision", "qwen", false, now);
        var repository = new FakeCatalogRepository([builtIn, custom]);
        var viewModel = new CatalogPageViewModel(CatalogPageKind.Agents, repository, new FakeOllamaClient(), true);
        await viewModel.RefreshCommand.ExecuteAsync();

        using var scene = new AgentsHavenScene(viewModel);

        Assert.Equal(2, scene.AgentCards.Items.Count);
        var builtInItem = scene.AgentCards.GetItem(builtIn.Id.ToString("N"));
        var customItem = scene.AgentCards.GetItem(custom.Id.ToString("N"));
        Assert.Equal("General", builtInItem.GetComponent<Text>("Name").Content);
        Assert.Equal("BUILT-IN", builtInItem.GetComponent<Text>("Badge").Content);
        Assert.Equal(HavenVisibility.Collapsed, builtInItem.GetComponent<Button>("Delete").GetValue(HavenProperties.Visibility));
        Assert.Equal("qwen", customItem.GetComponent<Text>("Model").Content.Replace("Model · ", string.Empty, StringComparison.Ordinal));
        Assert.Equal(HavenVisibility.Visible, customItem.GetComponent<Button>("Delete").GetValue(HavenProperties.Visibility));
    }

    [Fact]
    public async Task Scene_create_duplicate_and_confirm_delete_use_real_catalog_commands()
    {
        var now = DateTimeOffset.UtcNow;
        var existing = Agent(Guid.NewGuid(), "Existing", "Existing agent", "default", false, now);
        var repository = new FakeCatalogRepository([existing]);
        var viewModel = new CatalogPageViewModel(CatalogPageKind.Agents, repository, new FakeOllamaClient(), true);
        await viewModel.RefreshCommand.ExecuteAsync();
        using var scene = new AgentsHavenScene(viewModel);

        viewModel.IsCreating = true;
        scene.NameInput.Text = "Planner";
        scene.DescriptionInput.Text = "Plans work";
        scene.InstructionsInput.Text = "Create practical plans.";
        scene.ModelInput.Text = "mistral";
        await scene.CreateAgentAsync();

        var created = Assert.Single(repository.Agents, agent => agent.Name == "Planner");
        Assert.Equal("mistral", created.PreferredModel);
        Assert.Contains(scene.AgentCards.Items, item => item.InstanceID == created.Id.ToString("N"));

        var card = Assert.Single(viewModel.Items, item => item.Id == existing.Id);
        await scene.DuplicateAgentAsync(card);
        Assert.Contains(repository.Agents, agent => agent.Name == "Existing Copy");

        var deleteCard = Assert.Single(viewModel.Items, item => item.Id == existing.Id);
        Assert.False(await scene.DeleteAgentAsync(deleteCard));
        Assert.Contains(repository.Agents, agent => agent.Id == existing.Id);
        Assert.True(await scene.DeleteAgentAsync(deleteCard));
        Assert.DoesNotContain(repository.Agents, agent => agent.Id == existing.Id);
    }

    private static AgentDefinition Agent(Guid id, string name, string description, string model, bool builtIn, DateTimeOffset updatedAt) =>
        new(id, name, description, "Instructions", "agent", model, null, string.Empty, "{}", builtIn, true, updatedAt);

    private sealed class FakeCatalogRepository(IEnumerable<AgentDefinition> agents) : ICatalogRepository
    {
        public List<AgentDefinition> Agents { get; } = agents.ToList();
        private readonly List<PromptDefinition> _prompts = [];

        public Task<IReadOnlyList<AgentDefinition>> GetAgentsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentDefinition>>(Agents.Where(agent => agent.IsEnabled).OrderByDescending(agent => agent.IsBuiltIn).ThenBy(agent => agent.Name).ToArray());

        public Task<IReadOnlyList<PromptDefinition>> GetPromptsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PromptDefinition>>(_prompts.Where(prompt => prompt.IsEnabled).ToArray());

        public Task UpsertAgentAsync(AgentDefinition agent, CancellationToken cancellationToken)
        {
            var index = Agents.FindIndex(existing => existing.Id == agent.Id);
            if (index >= 0) Agents[index] = agent;
            else Agents.Add(agent);
            return Task.CompletedTask;
        }

        public Task UpsertPromptAsync(PromptDefinition prompt, CancellationToken cancellationToken)
        {
            var index = _prompts.FindIndex(existing => existing.Id == prompt.Id);
            if (index >= 0) _prompts[index] = prompt;
            else _prompts.Add(prompt);
            return Task.CompletedTask;
        }

        public Task SetAgentEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
        {
            var index = Agents.FindIndex(agent => agent.Id == id);
            if (index >= 0) Agents[index] = Agents[index] with { IsEnabled = enabled, UpdatedAt = DateTimeOffset.UtcNow };
            return Task.CompletedTask;
        }

        public Task SetPromptEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteCustomAgentAsync(Guid id, CancellationToken cancellationToken)
        {
            Agents.RemoveAll(agent => agent.Id == id && !agent.IsBuiltIn);
            return Task.CompletedTask;
        }

        public Task DeleteCustomPromptAsync(Guid id, CancellationToken cancellationToken)
        {
            _prompts.RemoveAll(prompt => prompt.Id == id && !prompt.IsBuiltIn);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOllamaClient : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);

        public async IAsyncEnumerable<string> StreamChatAsync(
            OllamaChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }

        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult("Drafted agent instructions.");

        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Empty, []));
    }
}
