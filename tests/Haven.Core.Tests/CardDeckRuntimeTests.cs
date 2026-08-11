using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class CardDeckRuntimeTests
{
    [Fact]
    public async Task ItemAliasDeckFlipsTheStoredBackText()
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
        var component = Find(document.Root, "card-deck.card.0");
        var binding = component.Actions.Single();
        var evt = CreateEvent(document, component, binding, "Flip card");

        var result = await router.RouteAsync(evt, binding, CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Completed, result.Status);
        var updated = instances.TryGet(document.Origin.InstanceId)!;
        var text = Find(updated.Root, "card-deck.text.0");
        Assert.Equal("5", text.Properties["text"].GetString());
        Assert.True(updated.State["slot0Revealed"].GetBoolean());
    }

    [Fact]
    public async Task TwoCardDeckUsesWideViewportAndNavigationUpdatesBothSlots()
    {
        var actions = new GenUiLocalActionRegistry();
        var instances = new GenUiInstanceStore();
        var runtime = new CardDeckTemplateRuntime(actions, instances);
        var document = runtime.Create(
            Guid.NewGuid(),
            "study",
            new Dictionary<string, JsonElement>
            {
                ["flashcards"] = JsonSerializer.SerializeToElement(new[]
                {
                    new { question = "Quadratic Formula", answer = "x = (-b ± √(b² - 4ac)) / 2a" },
                    new { question = "Pythagoras", answer = "a² + b² = c²" },
                    new { question = "Circle area", answer = "πr²" }
                })
            });
        instances.Register(document);

        var viewport = Find(document.Root, "card-deck.viewport");
        Assert.Equal("HavenGrid", viewport.ComponentType);
        Assert.Equal(2, viewport.Properties["columns"].GetInt32());
        Assert.True(viewport.Properties["responsive"].GetBoolean());
        Assert.Equal(2, viewport.Children.Count);
        Assert.Equal("Quadratic Formula", Find(document.Root, "card-deck.text.0").Properties["text"].GetString());
        Assert.Equal("Pythagoras", Find(document.Root, "card-deck.text.1").Properties["text"].GetString());

        var router = new GenerativeUiEventRouter(
            [actions], new BoundedGenUiEventAuditSink(), instances);
        var next = Find(document.Root, "card-deck.next");
        var binding = next.Actions.Single();
        var result = await router.RouteAsync(
            CreateEvent(document, next, binding, "Next card"), binding, CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Completed, result.Status);
        var updated = instances.TryGet(document.Origin.InstanceId)!;
        Assert.Equal(1, updated.State["currentIndex"].GetInt32());
        Assert.Equal("Pythagoras", Find(updated.Root, "card-deck.text.0").Properties["text"].GetString());
        Assert.Equal("Circle area", Find(updated.Root, "card-deck.text.1").Properties["text"].GetString());
        Assert.False(updated.State["slot0Revealed"].GetBoolean());
    }

    private static GenUiEvent CreateEvent(
        GenUiDocument document,
        GenUiComponent component,
        GenUiActionBinding binding,
        string summary) => new(
        Guid.NewGuid(), GenUiEventType.ActionInvoked, DateTimeOffset.UtcNow,
        document.Origin, component.ComponentId, binding.ActionId, null, null, null,
        JsonSerializer.SerializeToElement(new { values = new { } }),
        GenUiEventSource.User, summary);

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
