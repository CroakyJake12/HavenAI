using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class GenerativeUiContractTests
{
    [Fact]
    public void TrustedHavenUiDocumentWithStableIdsIsAccepted()
    {
        var document = Document(Button("calculate", GenUiRouteKind.Local, CapabilityRiskClass.Low));

        Assert.Empty(GenerativeUiContractValidator.Validate(document));
    }

    [Fact]
    public void ArbitraryRenderingAndConsequentialLocalActionsAreRejected()
    {
        var root = Button("run", GenUiRouteKind.Local, CapabilityRiskClass.Consequential) with
        {
            ComponentType = "RawHtml",
            Properties = new Dictionary<string, JsonElement>
            {
                ["javascript"] = JsonSerializer.SerializeToElement("doSomething()")
            }
        };

        var errors = GenerativeUiContractValidator.Validate(Document(root));

        Assert.Contains(errors, error => error.Contains("trusted HavenUI", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("forbidden", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("cannot own consequential", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateComponentIdentityIsRejected()
    {
        var child = Text("same", "One");
        var root = new GenUiComponent(
            "same", "HavenStack", EmptyProperties(), [], [child]);

        Assert.Contains(
            GenerativeUiContractValidator.Validate(Document(root)),
            error => error.Contains("Duplicate component ID", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LocalEventRoutesWithoutModelAndPatchesItsOriginatingInstanceOnce()
    {
        var document = Document(Button("calculate", GenUiRouteKind.Local, CapabilityRiskClass.Low));
        var store = new GenUiInstanceStore();
        store.Register(document);
        var local = new GenUiLocalActionRegistry();
        var patchId = Guid.NewGuid();
        local.Register("calculator.evaluate", (semanticEvent, _) => Task.FromResult(
            GenerativeUiEventRouter.Result(
                semanticEvent,
                GenUiActionStatus.Completed,
                "Calculated locally.",
                JsonSerializer.SerializeToElement(new { result = 4 }),
                [new GenUiStatePatch(
                    patchId,
                    semanticEvent.Origin.InstanceId,
                    GenUiPatchOperation.Replace,
                    "state",
                    "result",
                    JsonSerializer.SerializeToElement(4),
                    DateTimeOffset.UtcNow)])));
        var audit = new BoundedGenUiEventAuditSink();
        var router = new GenerativeUiEventRouter([local], audit, store);
        var semanticEvent = Event(document.Origin, "calculate", "calculate");
        var binding = document.Root.Actions.Single();

        var result = await router.RouteAsync(semanticEvent, binding, CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Completed, result.Status);
        Assert.Equal(4, store.TryGet(document.Origin.InstanceId)!.State["result"].GetInt32());
        Assert.False(store.ApplyPatch(result.Patches.Single()));
        Assert.Single(audit.Snapshot());
    }

    [Fact]
    public async Task MissingDestinationReturnsStructuredUnavailableResult()
    {
        var document = Document(Button("explain", GenUiRouteKind.Agent, CapabilityRiskClass.Low));
        var store = new GenUiInstanceStore();
        store.Register(document);
        var router = new GenerativeUiEventRouter([], new BoundedGenUiEventAuditSink(), store);
        var semanticEvent = Event(document.Origin, "explain", "explain");

        var result = await router.RouteAsync(semanticEvent, document.Root.Actions.Single(), CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Unavailable, result.Status);
        Assert.Equal(semanticEvent.EventId, result.EventId);
        Assert.Equal(document.Origin, result.Origin);
    }

    private static GenUiDocument Document(GenUiComponent root)
    {
        var origin = new GenUiOrigin(Guid.NewGuid(), "chat", null, Guid.NewGuid());
        return new GenUiDocument(
            Guid.NewGuid(),
            GenerativeUiContractValidator.CurrentContractVersion,
            origin,
            "Test",
            "chat",
            root,
            new Dictionary<string, JsonElement> { ["result"] = JsonSerializer.SerializeToElement(0) },
            DateTimeOffset.UtcNow);
    }

    private static GenUiComponent Button(string id, GenUiRouteKind route, CapabilityRiskClass risk) => new(
        id,
        "HavenButton",
        new Dictionary<string, JsonElement> { ["label"] = JsonSerializer.SerializeToElement("Run") },
        [new GenUiActionBinding(id, route, route == GenUiRouteKind.Local ? "calculator.evaluate" : "agent.explain", risk, RequiresPermission: false)],
        []);

    private static GenUiComponent Text(string id, string value) => new(
        id,
        "HavenText",
        new Dictionary<string, JsonElement> { ["text"] = JsonSerializer.SerializeToElement(value) },
        [],
        []);

    private static GenUiEvent Event(GenUiOrigin origin, string component, string action) => new(
        Guid.NewGuid(),
        GenUiEventType.ActionInvoked,
        DateTimeOffset.UtcNow,
        origin,
        component,
        action,
        null,
        null,
        null,
        JsonSerializer.SerializeToElement(new { }),
        GenUiEventSource.User,
        "User activated a generated control.");

    private static IReadOnlyDictionary<string, JsonElement> EmptyProperties() =>
        new Dictionary<string, JsonElement>();
}
