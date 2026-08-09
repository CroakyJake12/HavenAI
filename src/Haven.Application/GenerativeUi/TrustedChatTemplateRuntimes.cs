using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed class StructuredFormTemplateRuntime
{
    private const string SubmitAction = "structured-form.submit";
    private const string ResetAction = "structured-form.reset";
    private readonly GenUiInstanceStore _instances;

    public StructuredFormTemplateRuntime(GenUiLocalActionRegistry localActions, GenUiInstanceStore instances)
    {
        _instances = instances;
        localActions.RegisterOrReplace(SubmitAction, SubmitAsync);
        localActions.RegisterOrReplace(ResetAction, ResetAsync);
    }

    public GenUiDocument Create(Guid threadId, string appKey, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        var template = TemplateRegistryCatalog.BuiltIns.Single(item => item.Key == "structured-form");
        var instanceId = Guid.NewGuid();
        var origin = new GenUiOrigin(threadId, appKey, template.Id, instanceId);
        var title = ReadString(inputs, "title") ?? "Structured Form";
        var initialValues = inputs.TryGetValue("initialValues", out var initial) && initial.ValueKind == JsonValueKind.Object
            ? initial
            : JsonSerializer.SerializeToElement(new { });
        var fields = inputs["schema"].EnumerateArray().Select(field => BuildField(field, initialValues)).ToArray();
        var root = new GenUiComponent(
            "structured-form.workspace", "HavenForm", Props(("spacing", 12)), [],
            [
                ..fields,
                new GenUiComponent("structured-form.actions", "HavenToolbar", Props(("spacing", 10)), [],
                [
                    new GenUiComponent("structured-form.submit", "HavenButton", Props(("label", "Submit"), ("kind", "primary")), [Action(SubmitAction)], []),
                    new GenUiComponent("structured-form.reset", "HavenButton", Props(("label", "Reset")), [Action(ResetAction)], [])
                ]),
                new GenUiComponent("structured-form.status", "HavenStatus", Props(("text", "Ready"), ("automationName", "Form status")), [], [])
            ]);
        return new GenUiDocument(
            Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin, title, appKey, root,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["submittedValues"] = JsonSerializer.SerializeToElement(new { })
            }, DateTimeOffset.UtcNow);
    }

    private Task<GenUiActionResult> SubmitAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = semanticEvent.StructuredPayload.TryGetProperty("values", out var captured)
            ? captured.Clone()
            : JsonSerializer.SerializeToElement(new { });
        var now = DateTimeOffset.UtcNow;
        var patches = new List<GenUiStatePatch>
        {
            Patch(semanticEvent, "state", "submittedValues", values, now),
            Patch(semanticEvent, "structured-form.status", "text", JsonSerializer.SerializeToElement("Submitted"), now)
        };
        if (values.ValueKind == JsonValueKind.Object)
        {
            foreach (var value in values.EnumerateObject())
            {
                if (!value.Name.StartsWith("structured-form.input.", StringComparison.Ordinal)) continue;
                patches.Add(Patch(semanticEvent, value.Name, "value", value.Value.Clone(), now));
            }
        }
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Form values captured locally.", values, patches));
    }

    private Task<GenUiActionResult> ResetAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = _instances.TryGet(semanticEvent.Origin.InstanceId);
        var now = DateTimeOffset.UtcNow;
        var patches = document is null
            ? new List<GenUiStatePatch>()
            : Enumerate(document.Root)
                .Where(component => component.ComponentType is "HavenTextInput" or "HavenSelect" or "HavenToggle")
                .Select(component => Patch(
                    semanticEvent,
                    component.ComponentId,
                    "value",
                    component.ComponentType == "HavenToggle" ? JsonSerializer.SerializeToElement(false) : JsonSerializer.SerializeToElement(string.Empty),
                    now))
                .ToList();
        patches.Add(Patch(semanticEvent, "structured-form.status", "text", JsonSerializer.SerializeToElement("Reset"), now));
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Form reset locally.", JsonSerializer.SerializeToElement(new { }), patches));
    }

    private static GenUiComponent BuildField(JsonElement field, JsonElement initialValues)
    {
        var id = field.GetProperty("id").GetString()!;
        var label = field.GetProperty("label").GetString()!;
        var type = field.GetProperty("type").GetString()!;
        var value = initialValues.TryGetProperty(id, out var initial) ? initial.Clone() : DefaultValue(type);
        var inputProperties = new List<(string Key, object? Value)>
        {
            ("automationName", label),
            ("value", JsonValue(value))
        };
        if (field.TryGetProperty("placeholder", out var placeholder) && placeholder.ValueKind == JsonValueKind.String)
            inputProperties.Add(("placeholder", placeholder.GetString()));
        if (field.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
            inputProperties.Add(("options", options.EnumerateArray().Select(item => item.GetString()).OfType<string>().ToArray()));
        var componentType = type switch
        {
            "select" => "HavenSelect",
            "toggle" => "HavenToggle",
            _ => "HavenTextInput"
        };
        return new GenUiComponent($"structured-form.field.{id}", "HavenStack", Props(("spacing", 6)), [],
        [
            new GenUiComponent($"structured-form.label.{id}", "HavenText", Props(("text", label), ("emphasis", true)), [], []),
            new GenUiComponent($"structured-form.input.{id}", componentType, Props(inputProperties.ToArray()), [], [])
        ]);
    }

    private static JsonElement DefaultValue(string type) => type == "toggle"
        ? JsonSerializer.SerializeToElement(false)
        : JsonSerializer.SerializeToElement(string.Empty);

    private static object? JsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetDouble(out var number) => number,
        _ => value.ToString()
    };

    private static IEnumerable<GenUiComponent> Enumerate(GenUiComponent component)
    {
        yield return component;
        foreach (var child in component.Children)
        foreach (var nested in Enumerate(child))
            yield return nested;
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonElement> inputs, string key) =>
        inputs.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static GenUiActionBinding Action(string id) => new(id, GenUiRouteKind.Local, id, CapabilityRiskClass.Low, false);
    private static GenUiStatePatch Patch(GenUiEvent evt, string target, string path, JsonElement value, DateTimeOffset now) =>
        new(Guid.NewGuid(), evt.Origin.InstanceId, GenUiPatchOperation.Replace, target, path, value, now);
    private static IReadOnlyDictionary<string, JsonElement> Props(params (string Key, object? Value)[] values) =>
        values.ToDictionary(item => item.Key, item => JsonSerializer.SerializeToElement(item.Value), StringComparer.Ordinal);
}

public sealed class ChoicePromptTemplateRuntime
{
    private const string SelectAction = "choice-prompt.select";

    public ChoicePromptTemplateRuntime(GenUiLocalActionRegistry localActions) =>
        localActions.RegisterOrReplace(SelectAction, SelectAsync);

    public GenUiDocument Create(Guid threadId, string appKey, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        var template = TemplateRegistryCatalog.BuiltIns.Single(item => item.Key == "choice-prompt");
        var instanceId = Guid.NewGuid();
        var origin = new GenUiOrigin(threadId, appKey, template.Id, instanceId);
        var question = inputs["question"].GetString()!;
        var options = inputs["options"].EnumerateArray().Select(item => item.GetString()).OfType<string>().ToArray();
        var buttons = options.Select((option, index) => new GenUiComponent(
            $"choice-prompt.option.{index}", "HavenButton",
            Props(("label", option), ("value", option), ("kind", index == 0 ? "primary" : "secondary")),
            [Action(SelectAction)], [])).ToArray();
        return new GenUiDocument(
            Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin, "Choose an option", appKey,
            new GenUiComponent("choice-prompt.workspace", "HavenStack", Props(("spacing", 12)), [],
            [
                new GenUiComponent("choice-prompt.question", "HavenText", Props(("text", question), ("emphasis", true)), [], []),
                new GenUiComponent("choice-prompt.actions", "HavenToolbar", Props(("spacing", 10)), [], buttons),
                new GenUiComponent("choice-prompt.status", "HavenStatus", Props(("text", "Choose one option.")), [], [])
            ]),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["selection"] = JsonSerializer.SerializeToElement(string.Empty)
            }, DateTimeOffset.UtcNow);
    }

    private static Task<GenUiActionResult> SelectAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = semanticEvent.Value is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? string.Empty : string.Empty;
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, $"Selected: {selection}", JsonSerializer.SerializeToElement(new { selection }),
            [
                Patch(semanticEvent, "state", "selection", selection, now),
                Patch(semanticEvent, "choice-prompt.status", "text", $"Selected: {selection}", now)
            ]));
    }

    private static GenUiActionBinding Action(string id) => new(id, GenUiRouteKind.Local, id, CapabilityRiskClass.Low, false);
    private static GenUiStatePatch Patch<T>(GenUiEvent evt, string target, string path, T value, DateTimeOffset now) =>
        new(Guid.NewGuid(), evt.Origin.InstanceId, GenUiPatchOperation.Replace, target, path, JsonSerializer.SerializeToElement(value), now);
    private static IReadOnlyDictionary<string, JsonElement> Props(params (string Key, object? Value)[] values) =>
        values.ToDictionary(item => item.Key, item => JsonSerializer.SerializeToElement(item.Value), StringComparer.Ordinal);
}
