using Haven.Core;

namespace Haven.Application;

public sealed record GenUiFirstTurnBenchmarkCase(
    string Id,
    string Prompt,
    string ExpectedAppOrTemplateId,
    IReadOnlyList<string> RequiredStateKeys,
    IReadOnlyList<string> RequiredActionIds,
    IReadOnlyList<GenUiNavigationKind> RequiredRouteKinds,
    int MinimumInteractiveComponents,
    bool RequiresPersistentState);

public static class GenUiFirstTurnBenchmarkCatalog
{
    public static IReadOnlyList<GenUiFirstTurnBenchmarkCase> Cases { get; } =
    [
        Case("meal-planner", "Build me a weekly meal planner with editable meals, servings and a generated shopping list.", "meal-planner", ["selectedDay","meals","shoppingList"], ["meal.add","meal.edit","meal.remove","shopping.generate"], [GenUiNavigationKind.Root,GenUiNavigationKind.Detail,GenUiNavigationKind.Tab], 4, true),
        Case("flashcards", "Make an interactive flashcard deck with reveal, confidence rating and progress.", "card-deck", ["cards","index","confidence"], ["card.reveal","card.rate","card.next"], [GenUiNavigationKind.Root], 3, true),
        Case("assessment", "Give me a five-question revision quiz with marking, feedback and score.", "assessment", ["answers","score"], ["answer.record","assessment.next"], [GenUiNavigationKind.Root], 2, true),
        Case("project-checklist", "Create a project checklist where I can add, complete and reorder tasks.", "checklist", ["items"], ["item.add","item.toggle"], [GenUiNavigationKind.Root], 2, true),
        Case("comparison-grid", "Build a sortable and filterable comparison table for several options.", "data-grid", ["rows","filter"], ["grid.filter","grid.select"], [GenUiNavigationKind.Root], 2, false),
        Case("kpi-dashboard", "Create an interactive KPI dashboard with filters and drill-down details.", "dashboard", ["filters","selection"], ["dashboard.filter","dashboard.select"], [GenUiNavigationKind.Root,GenUiNavigationKind.Detail], 2, false),
        Case("graph-explorer", "Make a function graph explorer where I can edit expressions and inspect points.", "graph", ["expressions","viewport"], ["graph.update","graph.select"], [GenUiNavigationKind.Root], 2, true),
        Case("workflow-wizard", "Create a multi-step approval workflow with retry and completion state.", "workflow", ["step","results"], ["workflow.advance","workflow.retry"], [GenUiNavigationKind.Root,GenUiNavigationKind.WizardStep], 2, true),
        Case("calculator", "Build a calculator for evaluating an arithmetic expression and clearing the result.", "calculator", ["expression"], ["calculator.evaluate","calculator.clear"], [GenUiNavigationKind.Root], 2, false),
        Case("survey", "Create a structured survey with typed inputs, validation, reset and submit.", "structured-form", ["values"], ["form.submit","form.reset"], [GenUiNavigationKind.Root], 2, true),
        Case("research-compare", "Make a research comparison workspace with sources, excerpts and selectable evidence.", "research-results", ["sources","selection"], ["research.search","research.select"], [GenUiNavigationKind.Root,GenUiNavigationKind.Detail], 2, true),
        Case("document-workspace", "Create a report workspace with document, outline, sources and review controls.", "document-workspace", ["document","selection"], ["document.edit","outline.select"], [GenUiNavigationKind.Root,GenUiNavigationKind.Tab], 2, true),
        Case("code-workspace", "Create a coding workspace with repository tree, editor, diff and test results.", "code-workspace", ["selection","runResults"], ["file.select","tests.run"], [GenUiNavigationKind.Root,GenUiNavigationKind.Tab], 2, true),
        Case("whiteboard", "Make an editable whiteboard with tools, selection, undo and redo.", "whiteboard", ["objects","selection","history"], ["board.undo","board.redo"], [GenUiNavigationKind.Root], 2, true),
        Case("travel-itinerary", "Build a multi-day travel itinerary with editable stops and day detail pages.", "travel-itinerary", ["days","selectedDay","stops"], ["stop.add","stop.edit","day.select"], [GenUiNavigationKind.Root,GenUiNavigationKind.Detail], 3, true),
        Case("budget-tracker", "Create a budget tracker with categories, transactions and totals.", "budget-tracker", ["transactions","categories","total"], ["transaction.add","transaction.remove"], [GenUiNavigationKind.Root,GenUiNavigationKind.Tab], 3, true),
        Case("habit-tracker", "Build a weekly habit tracker with toggles, streaks and history.", "habit-tracker", ["habits","streaks","selectedWeek"], ["habit.toggle","habit.add"], [GenUiNavigationKind.Root,GenUiNavigationKind.Detail], 2, true),
        Case("event-schedule", "Create an event schedule where I can add sessions and inspect session details.", "event-schedule", ["sessions","selectedSession"], ["session.add","session.select"], [GenUiNavigationKind.Root,GenUiNavigationKind.Detail], 2, true),
        Case("crafting-inventory", "Build an inventory and crafting workspace with item placement, removal and crafted output.", "crafting-inventory", ["inventory","slots","output"], ["slot.place","slot.clear","craft.execute"], [GenUiNavigationKind.Root], 3, true),
        Case("revision-planner", "Build a revision timetable with subjects, sessions, progress and session detail.", "revision-planner", ["subjects","sessions","progress"], ["session.add","session.complete","subject.select"], [GenUiNavigationKind.Root,GenUiNavigationKind.Detail,GenUiNavigationKind.Tab], 3, true)
    ];

    private static GenUiFirstTurnBenchmarkCase Case(string id, string prompt, string app, IReadOnlyList<string> state, IReadOnlyList<string> actions, IReadOnlyList<GenUiNavigationKind> routes, int interactive, bool persistent)
        => new(id, prompt, app, state, actions, routes, interactive, persistent);
}

public static class GenUiFirstTurnBenchmarkValidator
{
    private static readonly HashSet<string> InteractiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "HavenButton", "HavenTextInput", "HavenSelect", "HavenToggle", "HavenSlider"
    };

    public static IReadOnlyList<string> Validate(GenUiFirstTurnBenchmarkCase benchmark, GenUiAppDefinition app)
    {
        ArgumentNullException.ThrowIfNull(benchmark);
        ArgumentNullException.ThrowIfNull(app);
        var errors = new List<string>();
        var semantic = GenUiSemanticValidator.ValidateAndRepair(app);
        errors.AddRange(semantic.Errors);
        var definition = semantic.Definition;
        if (!definition.AppId.Equals(benchmark.ExpectedAppOrTemplateId, StringComparison.OrdinalIgnoreCase))
            errors.Add($"Expected app/template '{benchmark.ExpectedAppOrTemplateId}', got '{definition.AppId}'.");

        var state = definition.StateSchema.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in benchmark.RequiredStateKeys)
            if (!state.Contains(key)) errors.Add($"Missing required state '{key}'.");

        var componentActions = Flatten(definition.Document.Root)
            .SelectMany(component => component.Actions.Select(action => action.ActionId));
        var typedActions = definition.Actions.Select(action => action.ActionId);
        var actionIds = componentActions.Concat(typedActions).ToHashSet(StringComparer.Ordinal);
        foreach (var action in benchmark.RequiredActionIds)
            if (!actionIds.Contains(action)) errors.Add($"Missing required action '{action}'.");

        foreach (var kind in benchmark.RequiredRouteKinds)
            if (!definition.Routes.Any(route => route.Kind == kind)) errors.Add($"Missing required navigation kind '{kind}'.");

        var interactiveCount = Flatten(definition.Document.Root).Count(component => InteractiveTypes.Contains(component.ComponentType));
        if (interactiveCount < benchmark.MinimumInteractiveComponents)
            errors.Add($"Expected at least {benchmark.MinimumInteractiveComponents} interactive controls, found {interactiveCount}.");

        if (benchmark.RequiresPersistentState && !definition.StateSchema.Any(field => field.Persistence != GenUiPersistenceScope.Transient))
            errors.Add("Expected persistent state but every state field is transient.");

        errors.AddRange(GenUiDocumentQualityValidator.Validate(definition.Document).Select(issue => issue.Message));
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<GenUiComponent> Flatten(GenUiComponent root)
    {
        yield return root;
        foreach (var child in root.Children)
            foreach (var descendant in Flatten(child)) yield return descendant;
    }
}
