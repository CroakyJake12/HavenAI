using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Feature implementation for the deterministic Calculator preview template.</summary>
public sealed class CalculatorTemplateRuntime
{
    private const string EvaluateAction = "calculator.evaluate";
    private const string ClearAction = "calculator.clear";
    private readonly GenUiInstanceStore _instances;

    public CalculatorTemplateRuntime(GenUiLocalActionRegistry localActions, GenUiInstanceStore instances)
    {
        _instances = instances;
        localActions.RegisterOrReplace(EvaluateAction, EvaluateAsync);
        localActions.RegisterOrReplace(ClearAction, ClearAsync);
    }

    public GenUiDocument Create(Guid threadId, string appKey = "chat", string? initialExpression = null)
    {
        var instanceId = Guid.NewGuid();
        var origin = new GenUiOrigin(
            threadId,
            appKey,
            TemplateRegistryCatalog.BuiltIns.Single(item => item.Key == "calculator").Id,
            instanceId);
        return new GenUiDocument(
            Guid.NewGuid(),
            GenerativeUiContractValidator.CurrentContractVersion,
            origin,
            "Calculator",
            appKey,
            new GenUiComponent(
                "calculator.workspace",
                "HavenWorkspace",
                Props(("spacing", 12)),
                [],
                [
                    new GenUiComponent("calculator.help", "HavenText",
                        Props(("text", "Enter an expression. Supports +, −, ×, ÷, powers, parentheses, pi, e, sqrt, abs, sin, cos, tan, min and max.")), [], []),
                    new GenUiComponent("calculator.expression", "HavenTextInput",
                        Props(("placeholder", "For example: sqrt(81) + 2^3"), ("automationName", "Calculator expression"),
                            ("value", initialExpression?.Trim() ?? string.Empty)),
                        [Action(EvaluateAction)], []),
                    new GenUiComponent("calculator.actions", "HavenToolbar", Props(("spacing", 10)), [],
                    [
                        new GenUiComponent("calculator.calculate", "HavenButton", Props(("label", "Calculate"), ("kind", "primary")), [Action(EvaluateAction)], []),
                        new GenUiComponent("calculator.clear", "HavenButton", Props(("label", "Clear")), [Action(ClearAction)], [])
                    ]),
                    new GenUiComponent("calculator.result", "HavenStatus", Props(("text", "Ready"), ("automationName", "Calculator result")), [], []),
                    new GenUiComponent("calculator.history", "HavenList", Props(("items", Array.Empty<string>()), ("automationName", "Calculation history")), [], [])
                ]),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["result"] = JsonSerializer.SerializeToElement(string.Empty),
                ["history"] = JsonSerializer.SerializeToElement(Array.Empty<string>())
            },
            DateTimeOffset.UtcNow);
    }

    private Task<GenUiActionResult> EvaluateAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var expression = ReadExpression(semanticEvent.StructuredPayload);
        if (string.IsNullOrWhiteSpace(expression))
            return Task.FromResult(Failed(semanticEvent, "Enter an expression."));

        try
        {
            var formatted = DeterministicCalculator.Format(DeterministicCalculator.Evaluate(expression));
            var line = $"{expression} = {formatted}";
            var history = ReadHistory(semanticEvent.Origin.InstanceId);
            history.Insert(0, line);
            if (history.Count > 50) history.RemoveRange(50, history.Count - 50);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(GenerativeUiEventRouter.Result(
                semanticEvent,
                GenUiActionStatus.Completed,
                "Calculated locally without a model.",
                JsonSerializer.SerializeToElement(new { expression, result = formatted }),
                [
                    Patch(semanticEvent, "state", "result", formatted, now),
                    Patch(semanticEvent, "state", "history", history, now),
                    Patch(semanticEvent, "calculator.result", "text", formatted, now),
                    Patch(semanticEvent, "calculator.history", "items", history, now)
                ]));
        }
        catch (InvalidOperationException exception)
        {
            return Task.FromResult(Failed(semanticEvent, exception.Message));
        }
    }

    private Task<GenUiActionResult> ClearAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent,
            GenUiActionStatus.Completed,
            "Calculator cleared.",
            JsonSerializer.SerializeToElement(new { }),
            [
                Patch(semanticEvent, "state", "result", string.Empty, now),
                Patch(semanticEvent, "state", "history", Array.Empty<string>(), now),
                Patch(semanticEvent, "calculator.expression", "value", string.Empty, now),
                Patch(semanticEvent, "calculator.result", "text", "Ready", now),
                Patch(semanticEvent, "calculator.history", "items", Array.Empty<string>(), now)
            ]));
    }

    private GenUiActionResult Failed(GenUiEvent semanticEvent, string message)
    {
        var text = "Could not calculate: " + message;
        return GenerativeUiEventRouter.Result(
            semanticEvent,
            GenUiActionStatus.Failed,
            text,
            JsonSerializer.SerializeToElement(new { error = message }),
            [Patch(semanticEvent, "calculator.result", "text", text, DateTimeOffset.UtcNow)]);
    }

    private List<string> ReadHistory(Guid instanceId)
    {
        var document = _instances.TryGet(instanceId);
        if (document?.State.TryGetValue("history", out var value) != true || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Select(item => item.GetString()).OfType<string>().ToList();
    }

    private static string ReadExpression(JsonElement payload)
    {
        if (!payload.TryGetProperty("values", out var values)
            || !values.TryGetProperty("calculator.expression", out var expression)) return string.Empty;
        return expression.ValueKind == JsonValueKind.String ? expression.GetString()?.Trim() ?? string.Empty : expression.ToString();
    }

    private static GenUiStatePatch Patch<T>(
        GenUiEvent semanticEvent,
        string target,
        string path,
        T value,
        DateTimeOffset timestamp) => new(
        Guid.NewGuid(), semanticEvent.Origin.InstanceId, GenUiPatchOperation.Replace,
        target, path, JsonSerializer.SerializeToElement(value), timestamp);

    private static GenUiActionBinding Action(string id) => new(
        id, GenUiRouteKind.Local, id, CapabilityRiskClass.Low, RequiresPermission: false);

    private static IReadOnlyDictionary<string, JsonElement> Props(params (string Key, object? Value)[] values) =>
        values.ToDictionary(item => item.Key, item => JsonSerializer.SerializeToElement(item.Value), StringComparer.Ordinal);
}
