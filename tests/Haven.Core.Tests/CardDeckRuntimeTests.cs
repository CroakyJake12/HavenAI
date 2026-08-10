using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class CardDeckRuntimeTests
{
    [Fact]
    public async Task ItemAliasDeckRevealsTheStoredBackText()
    {
        var actions = new GenUiLocalActionRegistry();
        var instances = new GenUiInstanceStore();
        var runtime = new CardDeckTemplateRuntime(actions, instances);
        var document = runtime.Create(
            Guid.NewGuid(),
            "chat",
            new Dictionary<string, JsonElement>
            {
                ["items"] = JsonSerializer.SerializeToElement(new[]
                {
                    new { id = 1, front = new { text = "2 + 3" }, back = new { text = "5" } }
                })
            });
        instances.Register(document);

        var router = new GenerativeUiEventRouter(
            [actions], new BoundedGenUiEventAuditSink(), instances);
        var component = Find(document.Root, "card-deck.reveal");
        var binding = component.Actions.Single();
        var evt = new GenUiEvent(
            Guid.NewGuid(), GenUiEventType.ActionInvoked, DateTimeOffset.UtcNow,
            document.Origin, component.ComponentId, binding.ActionId, null, null, null,
            JsonSerializer.SerializeToElement(new { values = new { } }),
            GenUiEventSource.User, "Reveal card");

        var result = await router.RouteAsync(evt, binding, CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Completed, result.Status);
        var updated = instances.TryGet(document.Origin.InstanceId)!;
        var back = Find(updated.Root, "card-deck.back");
        Assert.Equal("5", back.Properties["text"].GetString());
    }

    private static GenUiComponent Find(GenUiComponent component, string id)
    {
        if (component.ComponentId == id) return component;
        foreach (var child in component.Children)
        {
            try { return Find(child, id); }
            catch (KeyNotFoundException) { }
        }
        throw new KeyNotFoundException(id);
    }
}
