using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed class DashboardTemplateRuntime
{
    private const string SelectAction = "dashboard.select";
    private const string FilterAction = "dashboard.filter";

    public DashboardTemplateRuntime(GenUiLocalActionRegistry localActions)
    {
        localActions.RegisterOrReplace(SelectAction, SelectAsync);
        localActions.RegisterOrReplace(FilterAction, FilterAsync);
    }

    public GenUiDocument Create(Guid threadId, string appKey, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        var template = TemplateRegistryCatalog.BuiltIns.Single(item => item.Key == "dashboard");
        var instanceId = Guid.NewGuid();
        var origin = new GenUiOrigin(threadId, appKey, template.Id, instanceId);
        var panels = inputs.TryGetValue("panels", out var panelsEl) && panelsEl.ValueKind == JsonValueKind.Array
            ? panelsEl.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();

        var children = new List<GenUiComponent>();
        for (var i = 0; i < panels.Length; i++)
        {
            var panel = panels[i];
            var title = panel.TryGetProperty("title", out var t) ? t.GetString() ?? $"Panel {i + 1}" : $"Panel {i + 1}";
            var value = panel.TryGetProperty("value", out var v) ? v.GetString() ?? "—" : "—";
            var trend = panel.TryGetProperty("trend", out var tr) ? tr.GetString() ?? "" : "";

            children.Add(new GenUiComponent($"dashboard.card.{i}", "HavenCard", Props(("spacing", 8)), [],
            [
                new GenUiComponent($"dashboard.title.{i}", "HavenText",
                    Props(("text", title), ("emphasis", true), ("automationName", $"Dashboard {title} title")), [], []),
                new GenUiComponent($"dashboard.value.{i}", "HavenText",
                    Props(("text", value), ("automationName", $"{title} value")), [], []),
                new GenUiComponent($"dashboard.trend.{i}", "HavenStatus",
                    Props(("text", trend), ("automationName", $"{title} trend")), [], [])
            ]));
        }

        children.Add(new GenUiComponent("dashboard.filter-input", "HavenTextInput",
            Props(("placeholder", "Filter panels…"), ("automationName", "Dashboard filter")), [Action(FilterAction)], []));
        children.Add(new GenUiComponent("dashboard.status", "HavenStatus",
            Props(("text", $"{panels.Length} panels"), ("automationName", "Dashboard status")), [], []));

        return new GenUiDocument(
            Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin, "Dashboard", appKey,
            new GenUiComponent("dashboard.workspace", "HavenGrid", Props(("columns", 2), ("spacing", 10)), [], children),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["selection"] = JsonSerializer.SerializeToElement(string.Empty),
                ["filter"] = JsonSerializer.SerializeToElement(string.Empty)
            }, DateTimeOffset.UtcNow);
    }

    private static Task<GenUiActionResult> SelectAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Panel selected.",
            JsonSerializer.SerializeToElement(new { selected = true }),
            [new GenUiStatePatch(Guid.NewGuid(), semanticEvent.Origin.InstanceId, GenUiPatchOperation.Replace,
                "state", "selection", semanticEvent.Value ?? JsonSerializer.SerializeToElement(string.Empty), now)]));
    }

    private static Task<GenUiActionResult> FilterAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Filter applied.",
            JsonSerializer.SerializeToElement(new { filtered = true }),
            [new GenUiStatePatch(Guid.NewGuid(), semanticEvent.Origin.InstanceId, GenUiPatchOperation.Replace,
                "state", "filter", semanticEvent.Value ?? JsonSerializer.SerializeToElement(string.Empty), now)]));
    }

    private static GenUiActionBinding Action(string id) => new(id, GenUiRouteKind.Local, id, CapabilityRiskClass.Low, false);
    private static IReadOnlyDictionary<string, JsonElement> Props(params (string Key, object? Value)[] values) =>
        values.ToDictionary(item => item.Key, item => JsonSerializer.SerializeToElement(item.Value), StringComparer.Ordinal);
}

public sealed class AssessmentTemplateRuntime
{
    private const string AnswerAction = "assessment.answer";
    private const string NextAction = "assessment.next";

    public AssessmentTemplateRuntime(GenUiLocalActionRegistry localActions)
    {
        localActions.RegisterOrReplace(AnswerAction, AnswerAsync);
        localActions.RegisterOrReplace(NextAction, NextAsync);
    }

    public GenUiDocument Create(Guid threadId, string appKey, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        var template = TemplateRegistryCatalog.BuiltIns.Single(item => item.Key == "assessment");
        var instanceId = Guid.NewGuid();
        var origin = new GenUiOrigin(threadId, appKey, template.Id, instanceId);
        var title = inputs.TryGetValue("title", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() ?? "Assessment" : "Assessment";
        var questions = inputs.TryGetValue("questions", out var q) && q.ValueKind == JsonValueKind.Array
            ? q.EnumerateArray().ToArray() : Array.Empty<JsonElement>();

        var firstQuestion = questions.Length > 0 && questions[0].TryGetProperty("text", out var text)
            ? text.GetString() ?? "No question" : "No questions";

        var children = new List<GenUiComponent>
        {
            new("assessment.progress", "HavenProgress",
                Props(("value", 0), ("automationName", "Assessment progress")), [], []),
            new("assessment.question", "HavenCard", Props(("spacing", 10)), [],
            [
                new("assessment.question-text", "HavenText",
                    Props(("text", firstQuestion), ("emphasis", true), ("automationName", "Current question")), [], []),
                new("assessment.answer-input", "HavenTextInput",
                    Props(("placeholder", "Type your answer…"), ("automationName", "Answer input")), [Action(AnswerAction)], [])
            ]),
            new("assessment.actions", "HavenToolbar", Props(("spacing", 10)), [],
            [
                new("assessment.submit", "HavenButton",
                    Props(("label", "Submit Answer"), ("kind", "primary")), [Action(AnswerAction)], []),
                new("assessment.next", "HavenButton",
                    Props(("label", "Next")), [Action(NextAction)], [])
            ]),
            new("assessment.status", "HavenStatus",
                Props(("text", $"Question 1 of {questions.Length}"), ("automationName", "Assessment status")), [], [])
        };

        return new GenUiDocument(
            Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin, title, appKey,
            new GenUiComponent("assessment.workspace", "HavenStack", Props(("spacing", 12)), [], children),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["currentIndex"] = JsonSerializer.SerializeToElement(0),
                ["answers"] = JsonSerializer.SerializeToElement(new Dictionary<string, string>()),
                ["score"] = JsonSerializer.SerializeToElement(0),
                ["totalQuestions"] = JsonSerializer.SerializeToElement(questions.Length)
            }, DateTimeOffset.UtcNow);
    }

    private Task<GenUiActionResult> AnswerAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Answer submitted for marking.",
            JsonSerializer.SerializeToElement(new { submitted = true }),
            [Patch(semanticEvent, "assessment.status", "text", "Answer submitted — awaiting feedback", now)]));
    }

    private Task<GenUiActionResult> NextAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Next question.",
            JsonSerializer.SerializeToElement(new { next = true }),
            [
                Patch(semanticEvent, "assessment.answer-input", "value", string.Empty, now),
                Patch(semanticEvent, "assessment.status", "text", "Next question loaded", now)
            ]));
    }

    private static GenUiActionBinding Action(string id) => new(id, GenUiRouteKind.Local, id, CapabilityRiskClass.Low, false);
    private static GenUiStatePatch Patch<T>(GenUiEvent evt, string target, string path, T value, DateTimeOffset now) =>
        new(Guid.NewGuid(), evt.Origin.InstanceId, GenUiPatchOperation.Replace, target, path, JsonSerializer.SerializeToElement(value), now);
    private static IReadOnlyDictionary<string, JsonElement> Props(params (string Key, object? Value)[] values) =>
        values.ToDictionary(item => item.Key, item => JsonSerializer.SerializeToElement(item.Value), StringComparer.Ordinal);
}

public sealed class WorkflowTemplateRuntime
{
    private const string AdvanceAction = "workflow.advance";
    private const string ApproveAction = "workflow.approve";
    private const string RetryAction = "workflow.retry";

    public WorkflowTemplateRuntime(GenUiLocalActionRegistry localActions)
    {
        localActions.RegisterOrReplace(AdvanceAction, AdvanceAsync);
        localActions.RegisterOrReplace(ApproveAction, ApproveAsync);
        localActions.RegisterOrReplace(RetryAction, RetryAsync);
    }

    public GenUiDocument Create(Guid threadId, string appKey, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        var template = TemplateRegistryCatalog.BuiltIns.Single(item => item.Key == "workflow");
        var instanceId = Guid.NewGuid();
        var origin = new GenUiOrigin(threadId, appKey, template.Id, instanceId);
        var steps = inputs.TryGetValue("steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array
            ? stepsEl.EnumerateArray().ToArray() : Array.Empty<JsonElement>();

        var children = new List<GenUiComponent>();
        for (var i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            var stepTitle = step.TryGetProperty("title", out var t) ? t.GetString() ?? $"Step {i + 1}" : $"Step {i + 1}";
            var status = step.TryGetProperty("status", out var s) ? s.GetString() ?? "pending" : "pending";

            children.Add(new GenUiComponent($"workflow.step.{i}", "HavenCard", Props(("spacing", 6)), [],
            [
                new GenUiComponent($"workflow.step-title.{i}", "HavenText",
                    Props(("text", $"{i + 1}. {stepTitle}"), ("emphasis", true)), [], []),
                new GenUiComponent($"workflow.step-status.{i}", "HavenStatus",
                    Props(("text", status)), [], [])
            ]));
        }

        children.Add(new GenUiComponent("workflow.actions", "HavenToolbar", Props(("spacing", 10)), [],
        [
            new GenUiComponent("workflow.advance", "HavenButton",
                Props(("label", "Advance"), ("kind", "primary")), [Action(AdvanceAction)], []),
            new GenUiComponent("workflow.approve", "HavenButton",
                Props(("label", "Approve")), [Action(ApproveAction)], []),
            new GenUiComponent("workflow.retry", "HavenButton",
                Props(("label", "Retry")), [Action(RetryAction)], [])
        ]));
        children.Add(new GenUiComponent("workflow.status", "HavenStatus",
            Props(("text", $"{steps.Length} steps"), ("automationName", "Workflow status")), [], []));

        return new GenUiDocument(
            Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin, "Workflow", appKey,
            new GenUiComponent("workflow.workspace", "HavenStack", Props(("spacing", 10)), [], children),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["currentStep"] = JsonSerializer.SerializeToElement(0),
                ["state"] = JsonSerializer.SerializeToElement("running"),
                ["results"] = JsonSerializer.SerializeToElement(new { })
            }, DateTimeOffset.UtcNow);
    }

    private Task<GenUiActionResult> AdvanceAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Workflow advanced.",
            JsonSerializer.SerializeToElement(new { advanced = true }),
            [Patch(semanticEvent, "workflow.status", "text", "Advanced to next step", now)]));
    }

    private Task<GenUiActionResult> ApproveAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Step approved.",
            JsonSerializer.SerializeToElement(new { approved = true }),
            [Patch(semanticEvent, "workflow.status", "text", "Step approved", now)]));
    }

    private Task<GenUiActionResult> RetryAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Step retrying.",
            JsonSerializer.SerializeToElement(new { retrying = true }),
            [Patch(semanticEvent, "workflow.status", "text", "Retrying step…", now)]));
    }

    private static GenUiActionBinding Action(string id) => new(id, GenUiRouteKind.Local, id, CapabilityRiskClass.Low, false);
    private static GenUiStatePatch Patch<T>(GenUiEvent evt, string target, string path, T value, DateTimeOffset now) =>
        new(Guid.NewGuid(), evt.Origin.InstanceId, GenUiPatchOperation.Replace, target, path, JsonSerializer.SerializeToElement(value), now);
    private static IReadOnlyDictionary<string, JsonElement> Props(params (string Key, object? Value)[] values) =>
        values.ToDictionary(item => item.Key, item => JsonSerializer.SerializeToElement(item.Value), StringComparer.Ordinal);
}
