using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class FloatingActivityStateTests
{
    [Fact]
    public void StateStoreTracksAndRemovesActivitySnapshots()
    {
        var store = new FloatingActivityStateStore();
        var snapshot = new FloatingActivitySnapshot(
            Guid.NewGuid(), FloatingActivityState.Presented, 420, 280, 10, 20);

        store.Set(snapshot);

        Assert.Equal(snapshot, store.Get(snapshot.Id));
        Assert.Single(store.Snapshot());
        Assert.True(store.Remove(snapshot.Id));
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void FloatingDefinitionCarriesThreadAndScopedAccent()
    {
        var definition = new FloatingActivityDefinition(
            Guid.NewGuid(), Guid.NewGuid(), "chat", "Assistant", "blue",
            FloatingActivityPresentation.DetachedWindow, true, true, DateTimeOffset.UtcNow);

        Assert.Equal("blue", definition.AccentKey);
        Assert.True(definition.AlwaysOnTop);
    }
}
