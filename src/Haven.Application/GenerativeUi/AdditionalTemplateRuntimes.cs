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
    private const string FlipAction = "card-deck.flip";
    private const string RateAction = "card-deck.rate";
    private const string NextAction = "card-deck.next";
    private const string PreviousAction = "card-deck.previous";
    private readonly GenUiInstanceStore _instances;

    public CardDeckTemplateRuntime(GenUiLocalActionRegistry localActions, GenUiInstanceStore instances)
    {
        _instances = instances;
        localActions.RegisterOrReplace(FlipAction, FlipAsync);
        localActions.RegisterOrReplace(RateAction, RateAsync);
        localActions.RegisterOrReplace(NextAction, NextAsync);
        localActions.RegisterOrReplace(PreviousAction, PreviousAsync);
    }

    public GenUiDocument Create(Guid threadId, string appKey, IReadOnlyDictionary<string, JsonElement> inputs)
    {
        var template = TemplateRegistryCatalog.BuiltIns.Single(item => item.Key == "card-deck");
        var instanceId = Guid.NewGuid();
        var origin = new GenUiOrigin(threadId, appKey, template.Id, instanceId);
        var cards = ReadCards(inputs);
        var total = cards.Length;
        var visibleSlots = Math.Min(2, total);
        var cardSlots = Enumerable.Range(0, visibleSlots)
            .Select(slot => BuildCardSlot(slot, slot, cards))
            .ToArray();

        return new GenUiDocument(
            Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin, "Flashcards", appKey,
            new GenUiComponent("card-deck.workspace", "HavenStack",
                Props(("spacing", 14), ("minHeight", 360), ("automationName", "Flashcard deck")), [],
            [
                new GenUiComponent("card-deck.progress", "HavenProgress",
                    Props(("value", total > 0 ? 100d / total : 0d), ("automationName", "Card progress")), [], []),
                new GenUiComponent("card-deck.viewport", "HavenGrid",
                    Props(("columns", Math.Max(1, visibleSlots)), ("spacing", 18), ("responsive", true),
                        ("itemMinWidth", 300), ("automationName", "Flashcard viewport")), [], cardSlots),
                new GenUiComponent("card-deck.controls", "HavenToolbar", Props(("spacing", 10)), [],
                [
                    new GenUiComponent("card-deck.previous", "HavenButton",
                        Props(("label", "‹"), ("kind", "tertiary"), ("automationName", "Previous card")),
                        [Action(PreviousAction)], []),
                    new GenUiComponent("card-deck.rate-review", "HavenButton",
                        Props(("label", "Need review"), ("kind", "text"), ("automationName", "Mark card for review")),
                        [Action(RateAction)], []),
                    new GenUiComponent("card-deck.rate-got-it", "HavenButton",
                        Props(("label", "Got it"), ("kind", "text"), ("automationName", "Mark card as known")),
                        [Action(RateAction)], []),
                    new GenUiComponent("card-deck.next", "HavenButton",
                        Props(("label", "›"), ("kind", "primary"), ("automationName", "Next card")),
                        [Action(NextAction)], [])
                ]),
                new GenUiComponent("card-deck.status", "HavenStatus",
                    Props(("text", total == 0 ? "No cards" : $"Card 1 of {total}"),
                        ("automationName", "Deck status")), [], [])
            ]),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["currentIndex"] = JsonSerializer.SerializeToElement(0),
                ["slot0Revealed"] = JsonSerializer.SerializeToElement(false),
                ["slot1Revealed"] = JsonSerializer.SerializeToElement(false),
                ["totalCards"] = JsonSerializer.SerializeToElement(total),
                ["cardsData"] = JsonSerializer.SerializeToElement(cards.Select(c => new
                {
                    front = ReadCardText(c, "front"),
                    back = ReadCardText(c, "back")
                }).ToArray())
            }, DateTimeOffset.UtcNow);
    }

    private static GenUiComponent BuildCardSlot(int slot, int cardIndex, IReadOnlyList<JsonElement> cards)
    {
        var front = cardIndex >= 0 && cardIndex < cards.Count ? ReadCardText(cards[cardIndex], "front") : "No card";
        return new GenUiComponent($"card-deck.card.{slot}", "HavenCard",
            Props(("variant", "flashcard"), ("minHeight", 280), ("automationName", $"Flashcard {slot + 1}")),
            [Action(FlipAction)],
        [
            new GenUiComponent($"card-deck.text.{slot}", "HavenText",
                Props(("text", front), ("emphasis", true), ("tone", "onAccent"),
                    ("fontSize", 30), ("textAlignment", "center"),
                    ("automationName", $"Flashcard {slot + 1} content")), [], []),
            new GenUiComponent($"card-deck.hint.{slot}", "HavenText",
                Props(("text", "Click to flip"), ("tone", "onAccent"), ("fontSize", 15),
                    ("textAlignment", "center"), ("opacity", 0.9)), [], [])
        ]);
    }

    private Task<GenUiActionResult> FlipAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = _instances.TryGet(semanticEvent.Origin.InstanceId);
        if (document is null)
            return Task.FromResult(GenerativeUiEventRouter.Result(
                semanticEvent, GenUiActionStatus.Failed, "The flashcard deck is no longer available.",
                JsonSerializer.SerializeToElement(new { error = "missing-instance" }), []));

        var slot = ParseSlot(semanticEvent.ComponentId);
        var currentIndex = ReadInt(document, "currentIndex");
        var total = ReadInt(document, "totalCards");
        var cardIndex = total > 0 ? (currentIndex + slot) % total : 0;
        var stateKey = $"slot{slot}Revealed";
        var revealed = document.State.TryGetValue(stateKey, out var revealedElement)
            && revealedElement.ValueKind == JsonValueKind.True;
        var nextRevealed = !revealed;
        var cardText = ReadStoredCardText(document, cardIndex, nextRevealed ? "back" : "front");
        var now = DateTimeOffset.UtcNow;

        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, nextRevealed ? "Card answer revealed." : "Card question shown.",
            JsonSerializer.SerializeToElement(new { cardIndex, revealed = nextRevealed }),
            [
                Patch(semanticEvent, "state", stateKey, nextRevealed, now),
                Patch(semanticEvent, $"card-deck.text.{slot}", "text", cardText, now),
                Patch(semanticEvent, $"card-deck.hint.{slot}", "text",
                    nextRevealed ? "Click to show question" : "Click to flip", now),
                Patch(semanticEvent, "card-deck.status", "text",
                    nextRevealed ? $"Answer shown for card {cardIndex + 1}" : $"Card {cardIndex + 1} question", now)
            ]));
    }

    private Task<GenUiActionResult> RateAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = _instances.TryGet(semanticEvent.Origin.InstanceId);
        var currentIndex = document is null ? 0 : ReadInt(document, "currentIndex");
        var confidence = semanticEvent.ComponentId.EndsWith("got-it", StringComparison.Ordinal)
            ? "got-it"
            : "review";
        var summary = confidence == "got-it" ? "Marked as known." : "Marked for review.";
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, summary,
            JsonSerializer.SerializeToElement(new { cardIndex = currentIndex, confidence }),
            [
                Patch(semanticEvent, "state", $"confidence.{currentIndex}", confidence, now),
                Patch(semanticEvent, "card-deck.status", "text",
                    confidence == "got-it" ? "Got it. Move on when ready." : "Marked for review.", now)
            ]));
    }

    private Task<GenUiActionResult> NextAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken) =>
        MoveAsync(semanticEvent, 1, cancellationToken);

    private Task<GenUiActionResult> PreviousAsync(GenUiEvent semanticEvent, CancellationToken cancellationToken) =>
        MoveAsync(semanticEvent, -1, cancellationToken);

    private Task<GenUiActionResult> MoveAsync(GenUiEvent semanticEvent, int delta, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = _instances.TryGet(semanticEvent.Origin.InstanceId);
        if (document is null)
            return Task.FromResult(GenerativeUiEventRouter.Result(
                semanticEvent, GenUiActionStatus.Failed, "The flashcard deck is no longer available.",
                JsonSerializer.SerializeToElement(new { error = "missing-instance" }), []));

        var currentIndex = ReadInt(document, "currentIndex");
        var total = ReadInt(document, "totalCards");
        if (total <= 0)
            return Task.FromResult(GenerativeUiEventRouter.Result(
                semanticEvent, GenUiActionStatus.Failed, "The flashcard deck contains no cards.",
                JsonSerializer.SerializeToElement(new { error = "empty-deck" }), []));

        var nextIndex = (currentIndex + delta + total) % total;
        var visibleSlots = Math.Min(2, total);
        var now = DateTimeOffset.UtcNow;
        var patches = new List<GenUiStatePatch>
        {
            Patch(semanticEvent, "state", "currentIndex", nextIndex, now),
            Patch(semanticEvent, "state", "slot0Revealed", false, now),
            Patch(semanticEvent, "state", "slot1Revealed", false, now),
            Patch(semanticEvent, "card-deck.progress", "value", (nextIndex + 1d) * 100d / total, now),
            Patch(semanticEvent, "card-deck.status", "text", $"Card {nextIndex + 1} of {total}", now)
        };

        for (var slot = 0; slot < visibleSlots; slot++)
        {
            var cardIndex = (nextIndex + slot) % total;
            patches.Add(Patch(semanticEvent, $"card-deck.text.{slot}", "text",
                ReadStoredCardText(document, cardIndex, "front"), now));
            patches.Add(Patch(semanticEvent, $"card-deck.hint.{slot}", "text", "Click to flip", now));
        }

        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed,
            delta > 0 ? "Next card." : "Previous card.",
            JsonSerializer.SerializeToElement(new { currentIndex = nextIndex }), patches));
    }

    private static JsonElement[] ReadCards(IReadOnlyDictionary<string, JsonElement> inputs)
    {
        foreach (var key in new[] { "cards", "items", "flashcards", "questions" })
        {
            if (inputs.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Array)
                return value.EnumerateArray().ToArray();
        }
        return [];
    }

    private static int ParseSlot(string componentId)
    {
        var separator = componentId.LastIndexOf('.');
        return separator >= 0 && int.TryParse(componentId[(separator + 1)..], out var slot)
            ? Math.Clamp(slot, 0, 1)
            : 0;
    }

    private static int ReadInt(GenUiDocument document, string key) =>
        document.State.TryGetValue(key, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static string ReadStoredCardText(GenUiDocument document, int cardIndex, string field)
    {
        if (!document.State.TryGetValue("cardsData", out var cardsData) || cardsData.ValueKind != JsonValueKind.Array)
            return string.Empty;
        var cards = cardsData.EnumerateArray().ToArray();
        if (cardIndex < 0 || cardIndex >= cards.Length || !cards[cardIndex].TryGetProperty(field, out var value))
            return string.Empty;
        return value.GetString() ?? string.Empty;
    }

    private static string ReadCardText(JsonElement card, string field)
    {
        var aliases = field == "front"
            ? new[] { "front", "question", "prompt", "term", "title" }
            : new[] { "back", "answer", "response", "definition", "content" };
        foreach (var alias in aliases)
        {
            if (!card.TryGetProperty(alias, out var element)) continue;
            if (element.ValueKind == JsonValueKind.String) return element.GetString() ?? string.Empty;
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("text", out var text))
                return text.GetString() ?? string.Empty;
            return element.ToString();
        }
        return string.Empty;
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
                    Props(("expressions", expressions), ("xMin", -10), ("xMax", 10), ("yMin", -2), ("yMax", 2),
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
        var expressionText = semanticEvent.Value is { ValueKind: JsonValueKind.String } value
            ? value.GetString() ?? string.Empty
            : string.Empty;
        var expressions = expressionText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (expressions.Length == 0) expressions = ["sin(x)"];
        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Completed, "Graph updated.",
            JsonSerializer.SerializeToElement(new { expressions }),
            [
                new GenUiStatePatch(Guid.NewGuid(), semanticEvent.Origin.InstanceId, GenUiPatchOperation.Replace,
                    "graph.canvas", "expressions", JsonSerializer.SerializeToElement(expressions), now),
                new GenUiStatePatch(Guid.NewGuid(), semanticEvent.Origin.InstanceId, GenUiPatchOperation.Replace,
                    "graph.status", "text", JsonSerializer.SerializeToElement($"{expressions.Length} expression(s)"), now),
                new GenUiStatePatch(Guid.NewGuid(), semanticEvent.Origin.InstanceId, GenUiPatchOperation.Replace,
                    "state", "expressions", JsonSerializer.SerializeToElement(expressions), now)
            ]));
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
