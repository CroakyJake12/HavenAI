using Haven.Application;

namespace Haven.Core.Tests;

public sealed class SpaceRegistryTests
{
    [Fact]
    public async Task First_load_seeds_built_ins_as_normal_space_records()
    {
        var store = new MemorySettingsStore();
        var registry = new SpaceRegistry(store);

        var spaces = await registry.GetAllAsync();

        Assert.Equal(4, spaces.Count);
        Assert.Contains(spaces, space => space.Id == SpaceRegistry.StudySpaceId && space.Kind == SpaceKind.Study && space.IsBuiltIn);
        Assert.Contains(spaces, space => space.Id == SpaceRegistry.ShoppingSpaceId && space.Kind == SpaceKind.Shopping && space.IsBuiltIn);
        Assert.Contains(spaces, space => space.Id == SpaceRegistry.ResearchSpaceId && space.Kind == SpaceKind.Research && space.IsBuiltIn);
        var agent = spaces.Single(space => space.Id == SpaceRegistry.AgentSpaceId);
        Assert.True(agent.IsBuiltIn);
        Assert.Equal(SpaceKind.Agent, agent.Kind);
        Assert.False(string.IsNullOrWhiteSpace(agent.Instructions));
    }

    [Fact]
    public async Task Lifecycle_create_rename_archive_unarchive_and_delete_is_persisted()
    {
        var store = new MemorySettingsStore();
        var registry = new SpaceRegistry(store);

        var created = await registry.CreateAsync("  Physics project  ", "Exam prep");
        Assert.Equal("Physics project", created.Name);

        var renamed = await registry.RenameAsync(created.Id, "Physics revision");
        Assert.Equal("Physics revision", renamed.Name);
        Assert.True(renamed.UpdatedAt >= created.UpdatedAt);

        await registry.SetArchivedAsync(created.Id, true);
        Assert.DoesNotContain(await registry.GetAllAsync(), space => space.Id == created.Id);
        Assert.Contains(await registry.GetAllAsync(includeArchived: true), space => space.Id == created.Id && space.IsArchived);

        await registry.SetArchivedAsync(created.Id, false);
        await registry.DeleteAsync(created.Id);
        Assert.Null(await registry.GetAsync(created.Id));

        var reloaded = new SpaceRegistry(store);
        Assert.DoesNotContain(await reloaded.GetAllAsync(includeArchived: true), space => space.Id == created.Id);
    }

    [Fact]
    public async Task Fork_copies_configuration_but_gets_independent_identity()
    {
        var store = new MemorySettingsStore();
        var registry = new SpaceRegistry(store);
        var source = (await registry.GetAsync(SpaceRegistry.ResearchSpaceId))!;
        var configured = await registry.UpdateAsync(source with
        {
            ModelName = "local-model",
            Instructions = "Be precise",
            ThinkingMode = SpaceThinkingMode.Deep,
            ExamplePairs = [new SpaceExamplePair("Question", "Answer")]
        });

        var fork = await registry.ForkAsync(configured.Id);

        Assert.NotEqual(configured.Id, fork.Id);
        Assert.False(fork.IsBuiltIn);
        Assert.Equal(configured.Id, fork.ForkedFromSpaceId);
        Assert.Equal(configured.ModelName, fork.ModelName);
        Assert.Equal(configured.Instructions, fork.Instructions);
        Assert.Single(fork.ExamplePairs);
    }

    [Fact]
    public async Task Files_store_normalized_path_and_explicit_permission()
    {
        var store = new MemorySettingsStore();
        var registry = new SpaceRegistry(store);
        var space = await registry.CreateAsync("Files");
        var path = Path.Combine(Path.GetTempPath(), "haven-space", "notes.txt");

        var updated = await registry.AddFileAsync(space.Id, path, SpaceFilePermission.ReadWrite);

        var file = Assert.Single(updated.Files);
        Assert.Equal(Path.GetFullPath(path), file.Path);
        Assert.Equal("notes.txt", file.DisplayName);
        Assert.Equal(SpaceFilePermission.ReadWrite, file.Permission);

        updated = await registry.RemoveFileAsync(space.Id, path);
        Assert.Empty(updated.Files);
    }

    [Fact]
    public async Task Layout_documents_are_snapshotted_and_forked_independently()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        var space = await registry.CreateAsync("Layout Space");
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var nodes = new[]
        {
            new SpaceLayoutNode(firstId, "Input", "Prompt")
            {
                X = 20,
                Y = 30,
                Ports = [new SpaceLayoutPort("out", "Out", SpaceLayoutPortDirection.Output)]
            },
            new SpaceLayoutNode(secondId, "Surface", "Checklist")
            {
                X = 320,
                Y = 30,
                Ports = [new SpaceLayoutPort("in", "In", SpaceLayoutPortDirection.Input)]
            }
        };
        var layout = new SpaceLayoutDocument(nodes,
        [
            new SpaceLayoutEdge(Guid.NewGuid(), firstId, "out", secondId, "in")
        ]);

        var updated = await registry.SetLayoutAsync(space.Id, layout);
        nodes[0] = nodes[0] with { Title = "Mutated caller state" };

        Assert.NotNull(updated.LayoutDocument);
        Assert.Equal("Prompt", updated.LayoutDocument!.Nodes[0].Title);
        Assert.Single(updated.LayoutDocument.Edges);

        var fork = await registry.ForkAsync(updated.Id);
        Assert.NotNull(fork.LayoutDocument);
        Assert.NotSame(updated.LayoutDocument, fork.LayoutDocument);
        Assert.Equal(updated.LayoutDocument.Nodes.Select(node => node.Id), fork.LayoutDocument!.Nodes.Select(node => node.Id));
        Assert.Equal(updated.LayoutDocument.Edges.Select(edge => edge.Id), fork.LayoutDocument.Edges.Select(edge => edge.Id));
    }

    [Fact]
    public async Task Deleting_current_custom_space_returns_to_unscoped_chat()
    {
        var store = new MemorySettingsStore();
        var registry = new SpaceRegistry(store);
        var created = await registry.CreateAsync("Temporary Space");
        await registry.SetCurrentSpaceIdAsync(created.Id);

        await registry.DeleteAsync(created.Id);

        Assert.Null(await registry.GetCurrentSpaceIdAsync());
        Assert.Null(await registry.GetAsync(created.Id));
    }

    [Fact]
    public async Task Built_in_spaces_cannot_be_deleted()
    {
        var registry = new SpaceRegistry(new MemorySettingsStore());
        await registry.GetAllAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => registry.DeleteAsync(SpaceRegistry.StudySpaceId));
        Assert.Contains("Built-in Spaces", error.Message);

        var agentError = await Assert.ThrowsAsync<InvalidOperationException>(() => registry.DeleteAsync(SpaceRegistry.AgentSpaceId));
        Assert.Contains("Built-in Spaces", agentError.Message);
    }

    private sealed class MemorySettingsStore : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : null);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
