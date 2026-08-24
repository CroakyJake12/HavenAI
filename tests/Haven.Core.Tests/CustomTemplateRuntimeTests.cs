using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class CustomTemplateRuntimeTests
{
    [Fact]
    public async Task DeclarativeActionPatchesComponentStatusAndState()
    {
        var localActions = new GenUiLocalActionRegistry();
        var instances = new GenUiInstanceStore();
        var runtime = new CustomTemplateRuntime(localActions, instances);
        var components = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                id = "slot-label",
                type = "HavenText",
                props = new { text = "Empty" }
            },
            new
            {
                id = "place-button",
                type = "HavenButton",
                props = new { label = "Place" },
                actions = new object[]
                {
                    new
                    {
                        id = "slot.place",
                        message = "Placed oak plank.",
                        patches = new object[]
                        {
                            new { target = "slot-label", path = "text", value = "Oak Plank" },
                            new { target = "status", path = "text", value = "Oak Plank placed" },
                            new { target = "state", path = "slot1", value = "oak_plank" }
                        }
                    }
                }
            },
            new
            {
                id = "status",
                type = "HavenStatus",
                props = new { text = "Ready" }
            }
        });
        var document = runtime.Create(Guid.NewGuid(), "chat", new Dictionary<string, JsonElement>
        {
            ["title"] = JsonSerializer.SerializeToElement("Crafting Table"),
            ["components"] = components
        });
        instances.Register(document);

        var button = Find(document.Root, "place-button");
        var binding = button.Actions.Single();
        var router = new GenerativeUiEventRouter([localActions], new BoundedGenUiEventAuditSink(), instances);
        var evt = new GenUiEvent(
            Guid.NewGuid(), GenUiEventType.ActionInvoked, DateTimeOffset.UtcNow,
            document.Origin, button.ComponentId, binding.ActionId, null, null, null,
            JsonSerializer.SerializeToElement(new { values = new { } }),
            GenUiEventSource.User, "Place oak plank");

        var result = await router.RouteAsync(evt, binding, CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Completed, result.Status);
        var updated = instances.TryGet(document.Origin.InstanceId)!;
        Assert.Equal("Oak Plank", Find(updated.Root, "slot-label").Properties["text"].GetString());
        Assert.Equal("Oak Plank placed", Find(updated.Root, "status").Properties["text"].GetString());
        Assert.Equal("oak_plank", updated.State["slot1"].GetString());
        GenerativeUiContractValidator.ValidateAndThrow(updated);
    }

    [Fact]
    public async Task DeclarativeActionIgnoresUnknownTargetsAndInvalidPaths()
    {
        var localActions = new GenUiLocalActionRegistry();
        var instances = new GenUiInstanceStore();
        var runtime = new CustomTemplateRuntime(localActions, instances);
        var components = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                id = "button",
                type = "HavenButton",
                props = new { label = "Run" },
                actions = new object[]
                {
                    new
                    {
                        id = "safe.action",
                        patches = new object[]
                        {
                            new { target = "does-not-exist", path = "text", value = "unsafe" },
                            new { target = "state", path = "bad/path", value = "unsafe" },
                            new { target = "state", path = "safeKey", value = "safe" }
                        }
                    }
                }
            }
        });
        var document = runtime.Create(Guid.NewGuid(), "chat", new Dictionary<string, JsonElement>
        {
            ["components"] = components
        });
        instances.Register(document);

        var button = Find(document.Root, "button");
        var binding = button.Actions.Single();
        var router = new GenerativeUiEventRouter([localActions], new BoundedGenUiEventAuditSink(), instances);
        var evt = new GenUiEvent(
            Guid.NewGuid(), GenUiEventType.ActionInvoked, DateTimeOffset.UtcNow,
            document.Origin, button.ComponentId, binding.ActionId, null, null, null,
            JsonSerializer.SerializeToElement(new { values = new { } }),
            GenUiEventSource.User, "Run action");

        var result = await router.RouteAsync(evt, binding, CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Completed, result.Status);
        var updated = instances.TryGet(document.Origin.InstanceId)!;
        Assert.Equal("safe", updated.State["safeKey"].GetString());
        Assert.False(updated.State.ContainsKey("bad/path"));
        GenerativeUiContractValidator.ValidateAndThrow(updated);
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
