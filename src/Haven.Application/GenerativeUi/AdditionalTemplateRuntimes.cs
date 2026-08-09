using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public sealed class ChecklistTemplateRuntime
{
    private const string ToggleAction = "checklist.toggle";
    private const string AddAction = "checklist.add";

    public ChecklistTemplateRuntime(GenUiLocalActionRegistry localActions, GenUiInstanceStore instances)
    {
        localActions.RegisterOrReplace(ToggleAction, ToggleAsync);
        localActions.RegisterOrReplace(AddAction, AddAsync);
    }

    public GenUiDocument Create(Guid threadId, string appKey, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        var template = TemplateRegistryCatalog.BuiltIns.Single(item => item.Key == "checklist");
        var instanceId = Guid.NewGuid();
        var origin = new GenUiOrigin(threadId, appKey, template.Id, instanceId);
        var items = inputs.TryGetValue("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array
            ? itemsElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
            : Array.Empty<string>();

        var children = items.Select((item, index) => new GenUiComponent(
            $"checklist.item.{index}", "HavenStack", Props(("spacing", 4)), [],
            [
                new GenUiComponent($"checklist.toggle.{index}", "HavenToggle",
                    Props(("label", item), ("value", false), ("onLabel", "Done"), ("offLabel", "Pending")),
                    [Action(ToggleAction)], [])
            ])).Cast<GenUiComponent>().ToList();

        children.Add(new GenUiComponent("checklist.add-row", "HavenToolbar", Props(("spacing", 8)), [],
        [
            new GenUiComponent("checklist.new-item", "HavenTextInput",
                Props(("placeholder", "Add a new item…"), ("automationName", "New checklist item")), [], []),
            new GenUiComponent("checklist.add", "HavenButton", Props(("label", "Add")), [Action(AddAction)], [])
        ]));
        children.Add(new GenUiComponent("checklist.status", "HavenStatus",
            Props(("text", $"{items.Length} items"), ("automationName", "Checklist status")), [], []));

        return new GenUiDocument(
            Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin, "Checklist", appKey,
            new GenUiComponent("checklist.workspace", "HavenStack", Props(("spacing", 8)), [], children),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["completion"] = JsonSerializer.SerializeToElement(0)
            }, DateTimeOffset.UtcNow);
    }

    private Task<GenUiActionResult> ToggleAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Item toggled.",
            JsonSerializer.SerializeToElement(new { toggled = true }),
            [Patch(semanticEvent, "checklist.status", "text", "Item updated", now)]));
    }

    private Task<GenUiActionResult> AddAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Item added.",
            JsonSerializer.SerializeToElement(new { added = true }),
            [Patch(semanticEvent, "checklist.status", "text", "Item added", now)]));
    }

    private static GenUiActionBinding Action(string id) => new(id, GenUiRouteKind.Local, id, CapabilityRiskClass.Low, false);
    private static GenUiStatePatch Patch(GenUiEvent evt, string target, string path, string value, DateTimeOffset now) =>
        new(Guid.NewGuid(), evt.Origin.InstanceId, GenUiPatchOperation.Replace, target, path, JsonSerializer.SerializeToElement(value), now);
    private static IReadOnlyDictionary<string, JsonElement> Props(params (string Key, object? Value)[] values) =>
        values.ToDictionary(item => item.Key, item => JsonSerializer.SerializeToElement(item.Value), StringComparer.Ordinal);
}

public sealed class DataGridTemplateRuntime
{
    private const string SelectAction = "data-grid.select";
    private const string FilterAction = "data-grid.filter";

    public DataGridTemplateRuntime(GenUiLocalActionRegistry localActions)
    {
        localActions.RegisterOrReplace(SelectAction, SelectAsync);
        localActions.RegisterOrReplace(FilterAction, FilterAsync);
    }

    public GenUiDocument Create(Guid threadId, string appKey, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        var template = TemplateRegistryCatalog.BuiltIns.Single(item => item.Key == "data-grid");
        var instanceId = Guid.NewGuid();
        var origin = new GenUiOrigin(threadId, appKey, template.Id, instanceId);
        var columns = inputs.TryGetValue("columns", out var cols) && cols.ValueKind == JsonValueKind.Array
            ? cols.EnumerateArray().Select(c => c.GetString() ?? "").ToArray()
            : Array.Empty<string>();
        var rows = inputs.TryGetValue("rows", out var rowsEl) && rowsEl.ValueKind == JsonValueKind.Array
            ? rowsEl.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();

        var headerRow = string.Join(" | ", columns);
        var bodyLines = rows.Select(row =>
            row.ValueKind == JsonValueKind.Array
                ? string.Join(" | ", row.EnumerateArray().Select(cell => cell.ToString()))
                : row.ToString());
        var tableText = headerRow + "\n" + string.Join("\n", bodyLines);

        var children = new List<GenUiComponent>
        {
            new("data-grid.table", "HavenText", Props(("text", tableText), ("automationName", "Data table")), [], []),
            new("data-grid.filter-input", "HavenTextInput",
                Props(("placeholder", "Filter rows…"), ("automationName", "Filter")), [Action(FilterAction)], []),
            new("data-grid.status", "HavenStatus",
                Props(("text", $"{rows.Length} rows × {columns.Length} columns"), ("automationName", "Grid status")), [], [])
        };

        return new GenUiDocument(
            Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin, "Data Grid", appKey,
            new GenUiComponent("data-grid.workspace", "HavenStack", Props(("spacing", 10)), [], children),
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
            semanticEvent, GenUiActionStatus.Completed, "Row selected.",
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

public sealed class CardDeckTemplateRuntime
{
    private const string RevealAction = "card-deck.reveal";
    private const string RateAction = "card-deck.rate";
    private const string NextAction = "card-deck.next";
    private readonly GenUiInstanceStore _instances;

    public CardDeckTemplateRuntime(GenUiLocalActionRegistry localActions, GenUiInstanceStore instances)
    {
        _instances = instances;
        localActions.RegisterOrReplace(RevealAction, RevealAsync);
        localActions.RegisterOrReplace(RateAction, RateAsync);
        localActions.RegisterOrReplace(NextAction, NextAsync);
    }

    public GenUiDocument Create(Guid threadId, string appKey, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        var template = TemplateRegistryCatalog.BuiltIns.Single(item => item.Key == "card-deck");
        var instanceId = Guid.NewGuid();
        var origin = new GenUiOrigin(threadId, appKey, template.Id, instanceId);
        // Accept both "cards" and "items" as input names
        var cards = inputs.TryGetValue("cards", out var cardsEl) && cardsEl.ValueKind == JsonValueKind.Array
            ? cardsEl.EnumerateArray().ToArray()
            : inputs.TryGetValue("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array
                ? itemsEl.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();

        var front = cards.Length > 0 ? ReadCardText(cards[0], "front") : "No cards";
        var total = cards.Length;

        return new GenUiDocument(
            Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin, "Flashcards", appKey,
            new GenUiComponent("card-deck.workspace", "HavenStack", Props(("spacing", 12)), [],
            [
                new GenUiComponent("card-deck.progress", "HavenProgress",
                    Props(("value", 0), ("automationName", "Card progress")), [], []),
                new GenUiComponent("card-deck.card", "HavenCard", Props(("spacing", 10)), [],
                [
                    new GenUiComponent("card-deck.front", "HavenText",
                        Props(("text", front), ("emphasis", true), ("automationName", "Card front")), [], []),
                    new GenUiComponent("card-deck.back", "HavenText",
                        Props(("text", "Press Reveal to see the answer"), ("automationName", "Card back")), [], [])
                ]),
                new GenUiComponent("card-deck.actions", "HavenToolbar", Props(("spacing", 10)), [],
                [
                    new GenUiComponent("card-deck.reveal", "HavenButton", Props(("label", "Reveal")), [Action(RevealAction)], []),
                    new GenUiComponent("card-deck.rate-easy", "HavenButton", Props(("label", "Easy")), [Action(RateAction)], []),
                    new GenUiComponent("card-deck.rate-hard", "HavenButton", Props(("label", "Hard")), [Action(RateAction)], []),
                    new GenUiComponent("card-deck.next", "HavenButton", Props(("label", "Next")), [Action(NextAction)], [])
                ]),
                new GenUiComponent("card-deck.status", "HavenStatus",
                    Props(("text", $"Card 1 of {total}"), ("automationName", "Deck status")), [], [])
            ]),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["currentIndex"] = JsonSerializer.SerializeToElement(0),
                ["revealed"] = JsonSerializer.SerializeToElement(false),
                ["totalCards"] = JsonSerializer.SerializeToElement(total),
                ["cardsData"] = cards.Length > 0 ? JsonSerializer.SerializeToElement(cards.Select(c => new
                {
                    front = ReadCardText(c, "front"),
                    back = ReadCardText(c, "back")
                }).ToArray()) : JsonSerializer.SerializeToElement(Array.Empty<object>())
            }, DateTimeOffset.UtcNow);
    }

    private Task<GenUiActionResult> RevealAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var document = _instances.TryGet(semanticEvent.Origin.InstanceId);
        var backText = "Answer revealed";
        if (document?.State.TryGetValue("cardsData", out var cardsData) == true && cardsData.ValueKind == JsonValueKind.Array
            && document.State.TryGetValue("currentIndex", out var idx) && idx.TryGetInt32(out var currentIndex))
        {
            var cards = cardsData.EnumerateArray().ToArray();
            if (currentIndex >= 0 && currentIndex < cards.Length && cards[currentIndex].TryGetProperty("back", out var back))
                backText = back.GetString() ?? backText;
        }
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Card revealed.",
            JsonSerializer.SerializeToElement(new { revealed = true }),
            [
                Patch(semanticEvent, "state", "revealed", true, now),
                Patch(semanticEvent, "card-deck.back", "text", backText, now),
                Patch(semanticEvent, "card-deck.status", "text", "Revealed! Rate your confidence.", now)
            ]));
    }

    private Task<GenUiActionResult> RateAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Rating recorded.",
            JsonSerializer.SerializeToElement(new { rated = true }),
            [Patch(semanticEvent, "card-deck.status", "text", "Rated. Press Next.", now)]));
    }

    private Task<GenUiActionResult> NextAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var document = _instances.TryGet(semanticEvent.Origin.InstanceId);
        var nextIndex = 0;
        var nextFront = "Next card";
        var cardNum = 1;
        var total = 0;

        if (document?.State.TryGetValue("currentIndex", out var idx) == true && idx.TryGetInt32(out var currentIndex)
            && document.State.TryGetValue("totalCards", out var tot) && tot.TryGetInt32(out var totalCards))
        {
            nextIndex = (currentIndex + 1) % totalCards;
            cardNum = nextIndex + 1;
            total = totalCards;
        }

        if (document?.State.TryGetValue("cardsData", out var cardsData) == true && cardsData.ValueKind == JsonValueKind.Array)
        {
            var cards = cardsData.EnumerateArray().ToArray();
            if (nextIndex >= 0 && nextIndex < cards.Length && cards[nextIndex].TryGetProperty("front", out var front))
                nextFront = front.GetString() ?? nextFront;
        }

        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Next card.",
            JsonSerializer.SerializeToElement(new { next = true }),
            [
                Patch(semanticEvent, "state", "currentIndex", nextIndex, now),
                Patch(semanticEvent, "state", "revealed", false, now),
                Patch(semanticEvent, "card-deck.front", "text", nextFront, now),
                Patch(semanticEvent, "card-deck.back", "text", "Press Reveal to see the answer", now),
                Patch(semanticEvent, "card-deck.status", "text", $"Card {cardNum} of {total}", now)
            ]));
    }

    /// <summary>Reads card text from either a string or an object with a "text" property.</summary>
    private static string ReadCardText(JsonElement card, string field)
    {
        if (!card.TryGetProperty(field, out var el)) return "";
        if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? "";
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("text", out var text))
            return text.GetString() ?? "";
        return el.ToString();
    }

    private static GenUiActionBinding Action(string id) => new(id, GenUiRouteKind.Local, id, CapabilityRiskClass.Low, false);
    private static GenUiStatePatch Patch<T>(GenUiEvent evt, string target, string path, T value, DateTimeOffset now) =>
        new(Guid.NewGuid(), evt.Origin.InstanceId, GenUiPatchOperation.Replace, target, path, JsonSerializer.SerializeToElement(value), now);
    private static IReadOnlyDictionary<string, JsonElement> Props(params (string Key, object? Value)[] values) =>
        values.ToDictionary(item => item.Key, item => JsonSerializer.SerializeToElement(item.Value), StringComparer.Ordinal);
}

public sealed class GraphTemplateRuntime
{
    private const string UpdateAction = "graph.update";

    public GraphTemplateRuntime(GenUiLocalActionRegistry localActions)
    {
        localActions.RegisterOrReplace(UpdateAction, UpdateAsync);
    }

    public GenUiDocument Create(Guid threadId, string appKey, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        var template = TemplateRegistryCatalog.BuiltIns.Single(item => item.Key == "graph");
        var instanceId = Guid.NewGuid();
        var origin = new GenUiOrigin(threadId, appKey, template.Id, instanceId);
        var expressions = inputs.TryGetValue("expressions", out var expr) && expr.ValueKind == JsonValueKind.Array
            ? expr.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
            : new[] { "sin(x)" };

        return new GenUiDocument(
            Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin, "Graph", appKey,
            new GenUiComponent("graph.workspace", "HavenStack", Props(("spacing", 12)), [],
            [
                new GenUiComponent("graph.expression-input", "HavenTextInput",
                    Props(("value", string.Join(", ", expressions)), ("placeholder", "Enter expressions…"),
                        ("automationName", "Graph expression")), [Action(UpdateAction)], []),
                new GenUiComponent("graph.canvas", "HavenGraph",
                    Props(("emptyText", $"Plotting: {string.Join(", ", expressions)}"),
                        ("automationName", "Graph canvas")), [], []),
                new GenUiComponent("graph.status", "HavenStatus",
                    Props(("text", $"{expressions.Length} expression(s)"), ("automationName", "Graph status")), [], [])
            ]),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["expressions"] = JsonSerializer.SerializeToElement(expressions),
                ["viewport"] = JsonSerializer.SerializeToElement(new { xMin = -10, xMax = 10, yMin = -2, yMax = 2 })
            }, DateTimeOffset.UtcNow);
    }

    private static Task<GenUiActionResult> UpdateAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Graph updated.",
            JsonSerializer.SerializeToElement(new { updated = true }),
            [new GenUiStatePatch(Guid.NewGuid(), semanticEvent.Origin.InstanceId, GenUiPatchOperation.Replace,
                "graph.status", "text", JsonSerializer.SerializeToElement("Expressions updated"), now)]));
    }

    private static GenUiActionBinding Action(string id) => new(id, GenUiRouteKind.Local, id, CapabilityRiskClass.Low, false);
    private static IReadOnlyDictionary<string, JsonElement> Props(params (string Key, object? Value)[] values) =>
        values.ToDictionary(item => item.Key, item => JsonSerializer.SerializeToElement(item.Value), StringComparer.Ordinal);
}

public sealed class TaskListTemplateRuntime
{
    private const string RunAction = "task-list.run";
    private const string CancelAction = "task-list.cancel";

    public TaskListTemplateRuntime(GenUiLocalActionRegistry localActions)
    {
        localActions.RegisterOrReplace(RunAction, RunAsync);
        localActions.RegisterOrReplace(CancelAction, CancelAsync);
    }

    public GenUiDocument Create(Guid threadId, string appKey, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        var template = TemplateRegistryCatalog.BuiltIns.Single(item => item.Key == "task-list");
        var instanceId = Guid.NewGuid();
        var origin = new GenUiOrigin(threadId, appKey, template.Id, instanceId);
        var tasks = inputs.TryGetValue("tasks", out var tasksEl) && tasksEl.ValueKind == JsonValueKind.Array
            ? tasksEl.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();

        var children = new List<GenUiComponent>();
        for (var i = 0; i < tasks.Length; i++)
        {
            var task = tasks[i];
            var title = task.TryGetProperty("title", out var t) ? t.GetString() ?? $"Task {i + 1}" : $"Task {i + 1}";
            var status = task.TryGetProperty("status", out var s) ? s.GetString() ?? "pending" : "pending";
            children.Add(new GenUiComponent($"task-list.item.{i}", "HavenCard", Props(("spacing", 6)), [],
            [
                new GenUiComponent($"task-list.title.{i}", "HavenText", Props(("text", title), ("emphasis", true)), [], []),
                new GenUiComponent($"task-list.status.{i}", "HavenStatus", Props(("text", status)), [], []),
                new GenUiComponent($"task-list.actions.{i}", "HavenToolbar", Props(("spacing", 8)), [],
                [
                    new GenUiComponent($"task-list.run.{i}", "HavenButton", Props(("label", "Run")), [Action(RunAction)], []),
                    new GenUiComponent($"task-list.cancel.{i}", "HavenButton", Props(("label", "Cancel")), [Action(CancelAction)], [])
                ])
            ]));
        }
        children.Add(new GenUiComponent("task-list.status", "HavenStatus",
            Props(("text", $"{tasks.Length} tasks"), ("automationName", "Task list status")), [], []));

        return new GenUiDocument(
            Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin, "Task List", appKey,
            new GenUiComponent("task-list.workspace", "HavenStack", Props(("spacing", 10)), [], children),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["results"] = JsonSerializer.SerializeToElement(new { })
            }, DateTimeOffset.UtcNow);
    }

    private static Task<GenUiActionResult> RunAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Task started.",
            JsonSerializer.SerializeToElement(new { started = true }),
            [new GenUiStatePatch(Guid.NewGuid(), semanticEvent.Origin.InstanceId, GenUiPatchOperation.Replace,
                "task-list.status", "text", JsonSerializer.SerializeToElement("Task started"), now)]));
    }

    private static Task<GenUiActionResult> CancelAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Cancelled, "Task cancelled.",
            JsonSerializer.SerializeToElement(new { cancelled = true }),
            [new GenUiStatePatch(Guid.NewGuid(), semanticEvent.Origin.InstanceId, GenUiPatchOperation.Replace,
                "task-list.status", "text", JsonSerializer.SerializeToElement("Task cancelled"), now)]));
    }

    private static GenUiActionBinding Action(string id) => new(id, GenUiRouteKind.Local, id, CapabilityRiskClass.Low, false);
    private static IReadOnlyDictionary<string, JsonElement> Props(params (string Key, object? Value)[] values) =>
        values.ToDictionary(item => item.Key, item => JsonSerializer.SerializeToElement(item.Value), StringComparer.Ordinal);
}
