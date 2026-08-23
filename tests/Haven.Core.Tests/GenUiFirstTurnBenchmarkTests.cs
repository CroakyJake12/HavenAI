using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class GenUiFirstTurnBenchmarkTests
{
    [Fact]
    public void CatalogContainsTwentyDistinctMaterialCases()
    {
        Assert.Equal(20, GenUiFirstTurnBenchmarkCatalog.Cases.Count);
        Assert.Equal(20, GenUiFirstTurnBenchmarkCatalog.Cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(20, GenUiFirstTurnBenchmarkCatalog.Cases.Select(item => item.Prompt).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(GenUiFirstTurnBenchmarkCatalog.Cases, item => item.Id == "meal-planner");
    }

    [Fact]
    public void CompleteMealPlannerPassesFirstTurnAcceptance()
    {
        var benchmark = GenUiFirstTurnBenchmarkCatalog.Cases.Single(item => item.Id == "meal-planner");
        var errors = GenUiFirstTurnBenchmarkValidator.Validate(benchmark, CreateMealPlanner());
        Assert.Empty(errors);
    }

    [Fact]
    public void MealPlannerWithoutShoppingGenerationFailsAcceptance()
    {
        var benchmark = GenUiFirstTurnBenchmarkCatalog.Cases.Single(item => item.Id == "meal-planner");
        var app = CreateMealPlanner();
        app = app with { Actions = app.Actions.Where(action => action.ActionId != "shopping.generate").ToArray() };
        var root = app.Document.Root with
        {
            Children = app.Document.Root.Children.Select(RemoveShoppingGenerateAction).ToArray()
        };
        app = app with { Document = app.Document with { Root = root } };

        var errors = GenUiFirstTurnBenchmarkValidator.Validate(benchmark, app);
        Assert.Contains(errors, error => error.Contains("shopping.generate", StringComparison.Ordinal));
    }

    private static GenUiAppDefinition CreateMealPlanner()
    {
        var instanceId = Guid.NewGuid();
        var origin = new GenUiOrigin(Guid.NewGuid(), "genui", null, instanceId);
        var day = new GenUiComponent("day.detail", "HavenCard", Empty(), [],
        [
            Text("day.title", "Monday meals"),
            Button("meal.add.button", "Add meal", "meal.add"),
            Button("meal.edit.button", "Edit meal", "meal.edit"),
            Button("meal.remove.button", "Remove meal", "meal.remove")
        ]);
        var shopping = new GenUiComponent("shopping.tab", "HavenCard", Empty(), [],
        [
            Text("shopping.title", "Shopping list"),
            Button("shopping.generate.button", "Generate list", "shopping.generate")
        ]);
        var root = new GenUiComponent("meal.root", "HavenWorkspace", Empty(), [],
        [
            new GenUiComponent("day.select", "HavenSelect", Props(("value", "Monday")),
                [new GenUiActionBinding("day.select", GenUiRouteKind.Local, "day.select", CapabilityRiskClass.Low, false)], []),
            new GenUiComponent("servings", "HavenSlider", Props(("value", 2)),
                [new GenUiActionBinding("servings.change", GenUiRouteKind.Local, "servings.change", CapabilityRiskClass.Low, false)], []),
            day,
            shopping,
            new GenUiComponent("meal.status", "HavenStatus", Props(("text", "Ready")), [], [])
        ]);
        var state = new Dictionary<string, JsonElement>
        {
            ["selectedDay"] = JsonSerializer.SerializeToElement("Monday"),
            ["meals"] = JsonSerializer.SerializeToElement(new[] { "Breakfast", "Dinner" }),
            ["shoppingList"] = JsonSerializer.SerializeToElement(Array.Empty<string>()),
            ["servings"] = JsonSerializer.SerializeToElement(2)
        };
        var document = new GenUiDocument(Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin,
            "Weekly meal planner", "genui", root, state, DateTimeOffset.UtcNow);
        return new GenUiAppDefinition("meal-planner", GenUiSemanticValidator.CurrentSchemaVersion, document,
            [
                new("selectedDay", GenUiValueType.String, GenUiPersistenceScope.Instance, true, "Monday"),
                new("meals", GenUiValueType.Array, GenUiPersistenceScope.Instance, true),
                new("shoppingList", GenUiValueType.Array, GenUiPersistenceScope.Instance, true),
                new("servings", GenUiValueType.Integer, GenUiPersistenceScope.Instance, true, 2)
            ], [], [],
            [
                new("home", "meal.root", GenUiNavigationKind.Root, null, null, true),
                new("day", "day.detail", GenUiNavigationKind.Detail, "home", null, false),
                new("shopping", "shopping.tab", GenUiNavigationKind.Tab, "home", "planner", false)
            ], GenUiGenerationPipeline.RuntimeVersion)
        {
            Actions =
            [
                Action("meal.add"), Action("meal.edit"), Action("meal.remove"), Action("shopping.generate"),
                Action("day.select"), Action("servings.change")
            ]
        };
    }

    private static GenUiActionDefinition Action(string id) => new(id, GenUiActionExecutionKind.Local, [], null, []);

    private static GenUiComponent Button(string id, string label, string action) => new(
        id, "HavenButton", Props(("label", label)),
        [new GenUiActionBinding(action, GenUiRouteKind.Local, action, CapabilityRiskClass.Low, false)], []);

    private static GenUiComponent Text(string id, string text) => new(id, "HavenText", Props(("text", text)), [], []);

    private static GenUiComponent RemoveShoppingGenerateAction(GenUiComponent component)
        => component with
        {
            Actions = component.Actions.Where(action => action.ActionId != "shopping.generate").ToArray(),
            Children = component.Children.Select(RemoveShoppingGenerateAction).ToArray()
        };

    private static IReadOnlyDictionary<string, JsonElement> Empty() => new Dictionary<string, JsonElement>();

    private static IReadOnlyDictionary<string, JsonElement> Props(params (string Key, object Value)[] values)
        => values.ToDictionary(value => value.Key, value => JsonSerializer.SerializeToElement(value.Value), StringComparer.Ordinal);
}
