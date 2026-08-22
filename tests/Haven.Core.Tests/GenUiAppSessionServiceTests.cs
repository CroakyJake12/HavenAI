using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class GenUiAppSessionServiceTests
{
    [Fact]
    public async Task SaveCloseAndOpenRehydratesPersistedLiveState()
    {
        var repository = new MemoryRepository();
        var firstStore = new GenUiInstanceStore();
        var firstSession = new GenUiAppSessionService(repository, firstStore);
        var app = CreateApp();

        await firstSession.SaveAsync(app, CancellationToken.None);
        var instanceId = app.Document.Origin.InstanceId;
        firstStore.ApplyPatch(new GenUiStatePatch(
            Guid.NewGuid(), instanceId, GenUiPatchOperation.Replace, "state", "count",
            JsonSerializer.SerializeToElement(7), DateTimeOffset.UtcNow));

        await firstSession.CloseAsync(instanceId, persist: true, CancellationToken.None);

        Assert.Null(firstStore.TryGet(instanceId));
        Assert.Equal(7, (await repository.GetAsync(instanceId, CancellationToken.None))!.Document.State["count"].GetInt32());

        var secondStore = new GenUiInstanceStore();
        var secondSession = new GenUiAppSessionService(repository, secondStore);
        var reopened = await secondSession.OpenAsync(instanceId, CancellationToken.None);

        Assert.Equal(7, reopened.Document.State["count"].GetInt32());
        Assert.Equal(7, secondStore.TryGet(instanceId)!.State["count"].GetInt32());
    }

    private static GenUiAppDefinition CreateApp()
    {
        var origin = new GenUiOrigin(Guid.NewGuid(), "genui", null, Guid.NewGuid());
        var root = new GenUiComponent("root", "HavenText",
            new Dictionary<string, JsonElement> { ["text"] = JsonSerializer.SerializeToElement("Counter") }, [], []);
        var document = new GenUiDocument(Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin,
            "Counter", "genui", root,
            new Dictionary<string, JsonElement> { ["count"] = JsonSerializer.SerializeToElement(0) },
            DateTimeOffset.UtcNow);
        return new GenUiAppDefinition("counter", GenUiSemanticValidator.CurrentSchemaVersion, document,
            [new("count", GenUiValueType.Integer, GenUiPersistenceScope.Instance, true, 0)], [], [],
            [new("home", "root", GenUiNavigationKind.Root, null, null, true)], "haven-genui-runtime/1");
    }

    private sealed class MemoryRepository : IGenUiAppRepository
    {
        private readonly Dictionary<Guid, GenUiAppDefinition> _items = new();
        public Task UpsertAsync(GenUiAppDefinition definition, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _items[definition.Document.Origin.InstanceId] = definition;
            return Task.CompletedTask;
        }
        public Task<GenUiAppDefinition?> GetAsync(Guid instanceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_items.GetValueOrDefault(instanceId));
        }
        public Task<IReadOnlyList<GenUiAppDefinition>> GetRecentAsync(int limit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<GenUiAppDefinition>>(_items.Values.Take(limit).ToArray());
        }
        public Task<IReadOnlyList<GenUiAppDefinition>> GetPinnedAsync(int limit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<GenUiAppDefinition>>(Array.Empty<GenUiAppDefinition>());
        }
        public Task SetPinnedAsync(Guid instanceId, bool pinned, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid instanceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _items.Remove(instanceId);
            return Task.CompletedTask;
        }
    }
}
