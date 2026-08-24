using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class CalculatorTemplateRuntimeTests
{
    [Fact]
    public async Task Structured_event_calculates_and_incrementally_patches_result_and_history()
    {
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        var runtime = new CalculatorTemplateRuntime(local, store);
        var audit = new BoundedGenUiEventAuditSink();
        var router = new GenerativeUiEventRouter([local], audit, store);
        var document = runtime.Create(Guid.NewGuid());
        store.Register(document);
        var binding = Find(document.Root, "calculator.calculate").Actions.Single();
        var semanticEvent = CreateEvent(document.Origin, binding, "sqrt(81) + 2^3");

        var result = await router.RouteAsync(semanticEvent, binding, CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Completed, result.Status);
        Assert.Equal("17", store.TryGet(document.Origin.InstanceId)!.State["result"].GetString());
        Assert.Equal("sqrt(81) + 2^3 = 17", store.TryGet(document.Origin.InstanceId)!.State["history"][0].GetString());
        Assert.Equal("17", Find(store.TryGet(document.Origin.InstanceId)!.Root, "calculator.result").Properties["text"].GetString());
        Assert.Contains("without a model", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Single(audit.Snapshot());
    }

    [Fact]
    public async Task Invalid_expression_returns_structured_failure_without_executing_code()
    {
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        var runtime = new CalculatorTemplateRuntime(local, store);
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        var document = runtime.Create(Guid.NewGuid());
        store.Register(document);
        var binding = Find(document.Root, "calculator.calculate").Actions.Single();

        var result = await router.RouteAsync(
            CreateEvent(document.Origin, binding, "System.IO.File.Delete(1)"),
            binding,
            CancellationToken.None);

        Assert.Equal(GenUiActionStatus.Failed, result.Status);
        Assert.StartsWith("Could not calculate:", result.Summary, StringComparison.Ordinal);
        Assert.StartsWith("Could not calculate:", Find(store.TryGet(document.Origin.InstanceId)!.Root, "calculator.result").Properties["text"].GetString(), StringComparison.Ordinal);
    }

    private static GenUiEvent CreateEvent(GenUiOrigin origin, GenUiActionBinding binding, string expression) => new(
        Guid.NewGuid(),
        GenUiEventType.ActionInvoked,
        DateTimeOffset.UtcNow,
        origin,
        "calculator.calculate",
        binding.ActionId,
        null,
        null,
        null,
        JsonSerializer.SerializeToElement(new
        {
            values = new Dictionary<string, string> { ["calculator.expression"] = expression }
        }),
        GenUiEventSource.User,
        "User requested a deterministic calculation.");

    private static GenUiComponent Find(GenUiComponent component, string id)
    {
        if (component.ComponentId == id) return component;
        foreach (var child in component.Children)
        {
            var found = FindOrNull(child, id);
            if (found is not null) return found;
        }
        throw new InvalidOperationException($"Component '{id}' was not found.");
    }

    private static GenUiComponent? FindOrNull(GenUiComponent component, string id)
    {
        if (component.ComponentId == id) return component;
        foreach (var child in component.Children)
        {
            var found = FindOrNull(child, id);
            if (found is not null) return found;
        }
        return null;
    }
}
